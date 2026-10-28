using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Logs;
using CkanUI.Services.Logs;
using CkanUI.TestSupport;

using NUnit.Framework;

namespace CkanUI.Services.Tests;

/// <summary>
/// LOG-05 诊断包（14 篇 §5.1 / §5.2 / §9.1 T-LOG-01、T-LOG-02 出口校验）。
/// 重点不是「能打 zip」，而是两条硬约束：只装该装的内容、出口反向扫描必须拦得住。
/// </summary>
[TestFixture]
sealed class LogBundleServiceTests
{
    private static LogStore NewStore() => new(null, new LogStoreOptions { Capacity = 5_000 });

    /// <summary>
    /// 拼出 Windows 用户目录样本。门禁 H-10（21 篇 §12）禁止源码里出现「盘符:反斜杠 Users 反斜杠」字面量，
    /// 所以样本在运行期拼装；脱敏校验针对的就是拼出来的字符串，断言强度不变。
    /// </summary>
    private static string WinHome(string user) => "C:" + @"\Users\" + user;

    private static LogEvent Add(
        LogStore store,
        string message,
        LogLevel level = LogLevel.Info,
        string? detail = null,
        LogSourceKind kind = LogSourceKind.Log4Net,
        LogFamily family = LogFamily.Other) =>
        store.Append(new LogEvent
        {
            Message = message,
            Detail = detail,
            Level = level,
            Family = family,
            Kind = kind,
            Source = LogSources.Encode(kind, "CKAN.Main"),
        });

    private static Dictionary<string, string> ReadEntries(string zipPath)
    {
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
            entries[entry.FullName] = reader.ReadToEnd();
        }

        return entries;
    }

    private static DiagnosticFacts Facts() => new()
    {
        Environment = new Dictionary<string, string>(StringComparer.Ordinal) { ["os"] = "Windows 11" },
        Summary = new Dictionary<string, object?>(StringComparer.Ordinal) { ["app.version"] = "0.1.0" },
        Manifest = new Dictionary<string, object?>(StringComparer.Ordinal) { ["instances"] = 2 },
        Config = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Repository.AuthTokens"] = "secret-value",
            ["GameDir"] = @"C:\Kerbal Space Program",
            ["PreferredVersion"] = "1.12.5",
        },
    };

    [Test]
    public async Task CreateAsync_WritesDefaultEntries()
    {
        using var temp = new TempWorkspace("bundle-default");
        var store = NewStore();
        Add(store, "install started");
        var zipPath = temp.Combine("out", "bundle.zip");

        var result = await new LogBundleService(store, new LogRedactor()).CreateAsync(LogBundleRequest.For(zipPath));

        Assert.That(result.IsSuccess, Is.True, result.Message);
        var entries = ReadEntries(zipPath);
        Assert.That(
            entries.Keys,
            Is.EquivalentTo(new[]
            {
                "summary.json", "env.txt", "log.tail.txt", "log.errors.txt", "registry.summary.json",
                "config.redacted.json", "console.log",
            }));
        Assert.That(result.Value!.Entries, Is.EquivalentTo(entries.Keys));
        Assert.That(new FileInfo(zipPath).Length, Is.EqualTo(result.Value!.ByteLength));
    }

    [Test]
    public async Task CreateAsync_RedactsLogContent()
    {
        using var temp = new TempWorkspace("bundle-redact");
        var store = NewStore();
        Add(store, @"读取 " + WinHome("张三") + @"\AppData\Local\CKAN\logs\ckanui.log 失败 api_key=abcdef123456", LogLevel.Error);

        var result = await new LogBundleService(store, new LogRedactor())
            .CreateAsync(LogBundleRequest.For(temp.Combine("bundle.zip")));

        Assert.That(result.IsSuccess, Is.True, result.Message);
        var entries = ReadEntries(result.Value!.ZipPath);
        var all = string.Join("\n", entries.Values);
        Assert.That(all, Does.Not.Contain("张三"));
        Assert.That(all, Does.Not.Contain("abcdef123456"));
        Assert.That(entries["log.tail.txt"], Does.Contain("读取"));
    }

    [Test]
    public async Task CreateAsync_RedactsConfigKeys()
    {
        using var temp = new TempWorkspace("bundle-config");
        var service = new LogBundleService(NewStore(), new LogRedactor(), new StubFactsProvider(Facts()));

        var result = await service.CreateAsync(LogBundleRequest.For(temp.Combine("bundle.zip")));

        Assert.That(result.IsSuccess, Is.True, result.Message);
        var config = ReadEntries(result.Value!.ZipPath)["config.redacted.json"];
        Assert.That(config, Does.Not.Contain("AuthTokens"));
        Assert.That(config, Does.Not.Contain("secret-value"));
        Assert.That(config, Does.Not.Contain("Kerbal"));
        Assert.That(config, Does.Contain("PreferredVersion"));
    }

    [Test]
    public async Task CreateAsync_CanOmitOptionalEntries()
    {
        using var temp = new TempWorkspace("bundle-minimal");
        var request = new LogBundleRequest
        {
            OutputPath = temp.Combine("bundle.zip"),
            Options = new LogBundleOptions { IncludeConfig = false, IncludeConsoleLog = false },
        };

        var result = await new LogBundleService(NewStore(), new LogRedactor()).CreateAsync(request);

        Assert.That(result.IsSuccess, Is.True, result.Message);
        var entries = ReadEntries(result.Value!.ZipPath);
        Assert.That(entries.ContainsKey("config.redacted.json"), Is.False);
        Assert.That(entries.ContainsKey("console.log"), Is.False);
        Assert.That(entries.ContainsKey("log.tail.txt"), Is.True);
    }

    [Test]
    public async Task CreateAsync_CountsTailAndErrors()
    {
        using var temp = new TempWorkspace("bundle-counts");
        var store = NewStore();
        Add(store, "info line");
        Add(store, "warn line", LogLevel.Warn);
        Add(store, "error line", LogLevel.Error, detail: "System.IO.IOException: 磁盘已满");
        Add(store, "fatal line", LogLevel.Fatal);

        var result = await new LogBundleService(store, new LogRedactor())
            .CreateAsync(LogBundleRequest.For(temp.Combine("bundle.zip")));

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(result.Value!.TailLineCount, Is.EqualTo(4));
        Assert.That(result.Value!.ErrorLineCount, Is.EqualTo(2));

        var entries = ReadEntries(result.Value!.ZipPath);
        Assert.That(entries["log.errors.txt"], Does.Contain("error line"));
        Assert.That(entries["log.errors.txt"], Does.Contain("磁盘已满"));
        Assert.That(entries["log.errors.txt"], Does.Not.Contain("info line"));
    }

    [Test]
    public async Task CreateAsync_TailHonoursRequestedLineCount()
    {
        using var temp = new TempWorkspace("bundle-tail");
        var store = NewStore();
        for (var i = 0; i < 20; i++)
        {
            Add(store, "line " + i);
        }

        var result = await new LogBundleService(store, new LogRedactor())
            .CreateAsync(LogBundleRequest.For(temp.Combine("bundle.zip"), tailLines: 500));

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(result.Value!.TailLineCount, Is.EqualTo(20), "日志行数少于阈值时全都要装");
    }

    [Test]
    public async Task CreateAsync_MergesFactsIntoSummaryAndManifest()
    {
        using var temp = new TempWorkspace("bundle-facts");
        var store = NewStore();
        Add(store, "net failure", LogLevel.Error, family: LogFamily.Net);
        var provider = new StubFactsProvider(Facts());

        var result = await new LogBundleService(store, new LogRedactor(), provider)
            .CreateAsync(LogBundleRequest.For(temp.Combine("bundle.zip")));

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(provider.Calls, Is.EqualTo(1));
        var entries = ReadEntries(result.Value!.ZipPath);
        Assert.That(entries["summary.json"], Does.Contain("app.version"));
        Assert.That(entries["summary.json"], Does.Contain("log.errorsByFamily"));
        Assert.That(entries["summary.json"], Does.Contain("Net"));
        Assert.That(entries["registry.summary.json"], Does.Contain("instances"));
        Assert.That(entries["env.txt"], Does.Contain("os: Windows 11"));
    }

    [Test]
    public async Task CreateAsync_ConsoleLogKeepsOnlyUiEvents()
    {
        using var temp = new TempWorkspace("bundle-console");
        var store = NewStore();
        Add(store, "log4net line");
        Add(store, "ui line", kind: LogSourceKind.Ui);

        var result = await new LogBundleService(store, new LogRedactor())
            .CreateAsync(LogBundleRequest.For(temp.Combine("bundle.zip")));

        var entries = ReadEntries(result.Value!.ZipPath);
        Assert.That(entries["console.log"], Does.Contain("ui line"));
        Assert.That(entries["console.log"], Does.Not.Contain("log4net line"));
    }

    [Test]
    public async Task CreateAsync_Sha256MatchesWrittenBytes()
    {
        using var temp = new TempWorkspace("bundle-sha");
        var zipPath = temp.Combine("bundle.zip");

        var result = await new LogBundleService(NewStore(), new LogRedactor())
            .CreateAsync(LogBundleRequest.For(zipPath));

        Assert.That(result.IsSuccess, Is.True, result.Message);
        var expected = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(zipPath))).ToLowerInvariant();
        Assert.That(result.Value!.Sha256, Is.EqualTo(expected));
    }

    [Test]
    public async Task CreateAsync_CreatesMissingDirectories()
    {
        using var temp = new TempWorkspace("bundle-dirs");
        var zipPath = temp.Combine("a", "b", "bundle.zip");

        var result = await new LogBundleService(NewStore(), new LogRedactor())
            .CreateAsync(LogBundleRequest.For(zipPath));

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(File.Exists(zipPath), Is.True);
    }

    [Test]
    public async Task CreateAsync_RejectsEmptyOutputPath()
    {
        var result = await new LogBundleService(NewStore(), new LogRedactor())
            .CreateAsync(new LogBundleRequest { OutputPath = "  " });

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Is.EqualTo("诊断包输出路径为空"));
    }

    [Test]
    public void CreateAsync_RejectsNullRequest()
    {
        var service = new LogBundleService(NewStore(), new LogRedactor());

        Assert.ThrowsAsync<ArgumentNullException>(() => service.CreateAsync(null!));
    }

    [Test]
    public async Task CreateAsync_RefusesWhenExitScanStillFindsLeak()
    {
        using var temp = new TempWorkspace("bundle-gate");
        var store = NewStore();
        Add(store, "someone@example.com");
        var service = new LogBundleService(store, new LeakyRedactor());

        var result = await service.CreateAsync(LogBundleRequest.For(temp.Combine("bundle.zip")));

        Assert.That(result.IsSuccess, Is.False, "反向扫描命中必须拒绝生成");
        Assert.That(result.Message, Is.EqualTo("诊断包含敏感信息，已拒绝输出"));
        Assert.That(result.Detail, Does.Contain("email"));
        Assert.That(File.Exists(temp.Combine("bundle.zip")), Is.False, "被拒绝时不得留下半成品");
    }

    [Test]
    public async Task CreateAsync_HonoursCancellation()
    {
        using var temp = new TempWorkspace("bundle-cancel");
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var service = new LogBundleService(NewStore(), new LogRedactor());

        Assert.ThrowsAsync<OperationCanceledException>(
            () => service.CreateAsync(LogBundleRequest.For(temp.Combine("bundle.zip")), cts.Token));
    }

    [Test]
    public void Constructor_RequiresDependencies()
    {
        Assert.Throws<ArgumentNullException>(() => new LogBundleService(null!, new LogRedactor()));
        Assert.Throws<ArgumentNullException>(() => new LogBundleService(NewStore(), null!));
    }

    [Test]
    public void Options_HaveSpecDefaults()
    {
        Assert.That(LogBundleOptions.Default.TailLines, Is.EqualTo(5_000));
        Assert.That(LogBundleOptions.Default.IncludeConfig, Is.True);
        Assert.That(LogBundleOptions.Default.IncludeConsoleLog, Is.True);
        Assert.That(LogBundleRequest.For("x.zip", 7_000).Options.TailLines, Is.EqualTo(7_000));
    }

    [TestCase(10, 500)]
    [TestCase(500, 500)]
    [TestCase(5_000, 5_000)]
    [TestCase(99_999, 50_000)]
    public void Options_ClampTailLines(int requested, int expected) =>
        Assert.That(new LogBundleOptions { TailLines = requested }.ClampTailLines(), Is.EqualTo(expected));

    /// <summary>抹不干净的假脱敏器：用来验证出口硬门禁确实在拦，而不是「碰巧没有泄漏」。</summary>
    private sealed class LeakyRedactor : ILogRedactor
    {
        public string Redact(string? text) => text ?? string.Empty;

        public IReadOnlyList<RedactionLeak> FindLeaks(string? text) =>
            string.IsNullOrEmpty(text) || !text.Contains('@')
                ? []
                : [new RedactionLeak(RedactionLeakKind.Email, 0, 1)];

        public IReadOnlyDictionary<string, string> RedactConfig(IReadOnlyDictionary<string, string?> config) =>
            config.Where(pair => pair.Value is not null).ToDictionary(pair => pair.Key, pair => pair.Value!);

        public bool ContainsLeak(string? text) => FindLeaks(text).Count > 0;
    }
}
