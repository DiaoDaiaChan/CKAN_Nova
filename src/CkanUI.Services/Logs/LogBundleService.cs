using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Logs;

namespace CkanUI.Services.Logs;

/// <summary>
/// 诊断包生成（14 篇 §5.1 / LOG-05）。
/// 两条硬约束：
/// 1) 只装「我们可以负责」的内容——日志、摘要、脱敏后的配置；绝不打 .sfs / GameData / .ckan / .zip（可能几十 GB 且含玩家隐私）。
/// 2) 出口反向扫描是硬门禁：zip 写完后重新打开，把每个条目当文本再扫一遍泄漏模式，
///    命中就删包报错。为什么要扫「写完的包」而不是「写之前的字符串」：
///    打包/编码环节本身可能引入差异（换行、BOM），只有扫描真实产物才叫出口校验。
/// </summary>
public sealed class LogBundleService : ILogBundleService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly ILogStore _store;
    private readonly ILogRedactor _redactor;
    private readonly IDiagnosticFactsProvider? _factsProvider;

    /// <summary>构造诊断包服务。</summary>
    public LogBundleService(ILogStore store, ILogRedactor redactor, IDiagnosticFactsProvider? factsProvider = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(redactor);
        _store = store;
        _redactor = redactor;
        _factsProvider = factsProvider;
    }

    /// <inheritdoc />
    public async Task<OperationResult<LogBundleResult>> CreateAsync(
        LogBundleRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.OutputPath))
        {
            return OperationResult<LogBundleResult>.Fail("诊断包输出路径为空");
        }

        var options = request.Options ?? LogBundleOptions.Default;
        var facts = DiagnosticFacts.Empty;
        if (_factsProvider is not null)
        {
            facts = await _factsProvider.CollectAsync(cancellationToken).ConfigureAwait(false) ?? DiagnosticFacts.Empty;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = _store.Snapshot();
        var tailLimit = options.ClampTailLines();
        var tail = new List<LogEvent>(Math.Min(tailLimit, snapshot.Count));
        for (var i = Math.Max(0, snapshot.Count - tailLimit); i < snapshot.Count; i++)
        {
            tail.Add(snapshot.Events[i]);
        }

        var errors = new List<LogEvent>();
        var warningCount = 0;
        foreach (var item in snapshot.Events)
        {
            if (LogLevels.IsError(item.Level))
            {
                errors.Add(item);
            }
            else if (LogLevels.IsWarning(item.Level))
            {
                warningCount++;
            }
        }

        var entries = new List<(string Name, string Content)>
        {
            ("summary.json", BuildSummary(facts, snapshot.Count, tail.Count, errors.Count, warningCount)),
            ("env.txt", BuildEnvironment(facts)),
            ("log.tail.txt", BuildLogText(tail)),
            ("log.errors.txt", BuildLogText(errors, includeDetail: true)),
            ("registry.summary.json", JsonSerializer.Serialize(facts.Manifest, JsonOptions)),
        };

        if (options.IncludeConfig)
        {
            entries.Add(("config.redacted.json", JsonSerializer.Serialize(_redactor.RedactConfig(facts.Config), JsonOptions)));
        }

        if (options.IncludeConsoleLog)
        {
            entries.Add(("console.log", BuildLogText(UiEvents(tail))));
        }

        // 统一脱敏（日志行里也带用户名/路径，漏一步就白做）。
        for (var i = 0; i < entries.Count; i++)
        {
            entries[i] = (entries[i].Name, _redactor.Redact(entries[i].Content));
        }

        var leakKinds = DescribeLeaks(entries.Select(static entry => entry.Content));
        if (leakKinds is not null)
        {
            return OperationResult<LogBundleResult>.Fail(
                "诊断包含敏感信息，已拒绝输出",
                leakKinds);
        }

        byte[] bytes;
        using (var buffer = new MemoryStream())
        {
            using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var (name, text) in entries)
                {
                    var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
                    using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
                    writer.Write(text);
                }
            }

            bytes = buffer.ToArray();
        }

        // 出口反向扫描：把真正要落盘的 zip 重新读一遍再验一次。
        var packagedLeaks = DescribeLeaks(ReadArchiveText(bytes));
        if (packagedLeaks is not null)
        {
            return OperationResult<LogBundleResult>.Fail(
                "诊断包出口校验未通过，已丢弃",
                packagedLeaks);
        }

        try
        {
            var fullPath = Path.GetFullPath(request.OutputPath);
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllBytes(fullPath, bytes);
            var sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            return OperationResult<LogBundleResult>.Ok(new LogBundleResult
            {
                ZipPath = fullPath,
                ByteLength = bytes.LongLength,
                TailLineCount = tail.Count,
                ErrorLineCount = errors.Count,
                Entries = [.. entries.Select(static entry => entry.Name)],
                Sha256 = sha256,
            });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return OperationResult<LogBundleResult>.Fail("写入诊断包失败", exception.Message, retryable: true);
        }
    }

    private static IEnumerable<LogEvent> UiEvents(IReadOnlyList<LogEvent> events)
    {
        foreach (var item in events)
        {
            if (item.Kind == LogSourceKind.Ui)
            {
                yield return item;
            }
        }
    }

    private static IEnumerable<string> ReadArchiveText(byte[] bytes)
    {
        using var buffer = new MemoryStream(bytes);
        using var archive = new ZipArchive(buffer, ZipArchiveMode.Read);
        foreach (var entry in archive.Entries)
        {
            using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
            yield return reader.ReadToEnd();
        }
    }

    private static string BuildLogText(IEnumerable<LogEvent> events, bool includeDetail = false)
    {
        var builder = new StringBuilder();
        foreach (var item in events)
        {
            builder.Append(LogSearchKeys.FormatLine(item)).Append('\n');
            if (includeDetail && !string.IsNullOrEmpty(item.Detail))
            {
                builder.Append(item.Detail).Append('\n');
            }
        }

        return builder.ToString();
    }

    private string? DescribeLeaks(IEnumerable<string> texts)
    {
        var counts = new Dictionary<RedactionLeakKind, int>();
        foreach (var text in texts)
        {
            foreach (var leak in _redactor.FindLeaks(text))
            {
                counts[leak.Kind] = counts.TryGetValue(leak.Kind, out var current) ? current + 1 : 1;
            }
        }

        if (counts.Count == 0)
        {
            return null;
        }

        return string.Join(
            ", ",
            counts.OrderByDescending(static pair => pair.Value).Select(static pair => pair.Key.Text() + " x" + pair.Value));
    }

    private string BuildSummary(DiagnosticFacts facts, int entryCount, int tailLines, int errorCount, int warningCount)
    {
        var summary = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var pair in facts.Summary)
        {
            summary[pair.Key] = pair.Value;
        }

        var byFamily = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var item in _store.Snapshot().Events)
        {
            if (!LogLevels.IsError(item.Level))
            {
                continue;
            }

            var key = item.Family.ToString();
            byFamily[key] = byFamily.TryGetValue(key, out var current) ? current + 1 : 1;
        }

        summary["bundle.generatedAtUtc"] = DateTime.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        summary["log.entries"] = entryCount;
        summary["log.errors"] = errorCount;
        summary["log.warnings"] = warningCount;
        summary["log.tailLines"] = tailLines;
        summary["log.errorsByFamily"] = byFamily;
        return JsonSerializer.Serialize(summary, JsonOptions);
    }

    private string BuildEnvironment(DiagnosticFacts facts)
    {
        var lines = new List<string>();
        foreach (var pair in facts.Environment.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            lines.Add(pair.Key + ": " + pair.Value);
        }

        lines.Add("runtime.os: " + RuntimeInformation.OSDescription);
        lines.Add("runtime.arch: " + RuntimeInformation.ProcessArchitecture);
        lines.Add("runtime.framework: " + RuntimeInformation.FrameworkDescription);
        lines.Add("runtime.version: " + Environment.Version);
        lines.Add("runtime.machine: " + Environment.MachineName);
        return string.Join("\n", lines) + "\n";
    }
}
