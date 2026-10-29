using System.IO.Compression;
using System.Text;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.ModConfig;

namespace CkanUI.Services.ModConfig;

/// <summary>adapter 包在磁盘上的固定布局（ADP-04）。</summary>
public static class AdapterPackageLayout
{
    /// <summary>adapter 目录里的入口文件名——AdapterCatalog 只认这个名字（&lt;根&gt;/&lt;id&gt;/adapter.json）。</summary>
    public const string AdapterEntryFileName = "adapter.json";

    /// <summary>安装清单文件名。</summary>
    public const string ManifestFileName = "installed.json";

    /// <summary>安装期的临时目录前缀（枚举时跳过，避免把半成品当 adapter）。</summary>
    public const string StagingPrefix = ".staging-";

    /// <summary>覆盖安装时旧目录的备份前缀，替换成功即删。</summary>
    public const string BackupPrefix = ".backup-";
}

/// <summary>失败结果的统一拼装：Detail 以稳定诊断码开头，方便日志/界面/用例对齐。</summary>
internal static class AdapterResults
{
    /// <summary>带值失败。</summary>
    public static OperationResult<T> Fail<T>(string code, string message, string? detail = null, bool retryable = false)
        => OperationResult<T>.Fail(message, Compose(code, detail), retryable);

    /// <summary>无值失败。</summary>
    public static OperationResult Fail(string code, string message, string? detail = null, bool retryable = false)
        => OperationResult.Fail(message, Compose(code, detail), retryable);

    private static string Compose(string code, string? detail)
        => string.IsNullOrWhiteSpace(detail) ? code : code + ": " + detail;
}

/// <summary>包内一个待落盘的文件（相对入口目录，/ 分隔）。</summary>
internal sealed record AdapterPackageFile(string RelativePath, byte[] Content);

/// <summary>解出来的包内容：入口原文 + 其余文件。</summary>
internal sealed record AdapterPackageContents(string? EntryPoint, byte[] AdapterJsonBytes, IReadOnlyList<AdapterPackageFile> Files);

/// <summary>
/// adapter 包的读取与校验（ADP-04）：判容器形态、解 zip、挡 zip-slip、挡体积/条目数超限。
/// </summary>
/// <remarks>全部失败都返回 <see cref="OperationResult{T}"/>；这里只做内存里的校验，不碰磁盘。</remarks>
internal static class AdapterPackageReader
{
    /// <summary>判断容器形态：先看内容特征（{ 开头的 JSON、PK 开头的 zip），再看扩展名兜底。</summary>
    /// <param name="location">来源路径或 URL（只看扩展名）。</param>
    /// <param name="payload">包内容。</param>
    public static AdapterPackageFormat DetectFormat(string location, ReadOnlySpan<byte> payload)
    {
        var start = 0;
        if (payload.Length >= 3 && payload[0] == 0xEF && payload[1] == 0xBB && payload[2] == 0xBF)
        {
            start = 3;
        }

        while (start < payload.Length && payload[start] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')
        {
            start++;
        }

        if (start < payload.Length && payload[start] == (byte)'{')
        {
            return AdapterPackageFormat.Json;
        }

        if (payload.Length >= 4 && payload[0] == (byte)'P' && payload[1] == (byte)'K' && payload[2] is 3 or 5 or 7 && payload[3] is 4 or 6 or 8)
        {
            return AdapterPackageFormat.Zip;
        }

        return location.TrimEnd().EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
            ? AdapterPackageFormat.Zip
            : AdapterPackageFormat.Json;
    }

    /// <summary>解 zip 包：整包校验，任何越界条目都整包拒绝（不做"跳过这一条"的宽容）。</summary>
    /// <param name="payload">zip 原始字节。</param>
    /// <param name="declaredEntryPoint">调用方声明的入口（可空 = 自动找唯一的 adapter.json）。</param>
    /// <param name="limits">体积/条目数上限。</param>
    public static OperationResult<AdapterPackageContents> ReadZip(byte[] payload, string? declaredEntryPoint, AdapterInstallerLimits limits)
    {
        ZipArchive archive;
        try
        {
            archive = new ZipArchive(new MemoryStream(payload, writable: false), ZipArchiveMode.Read);
        }
        catch (InvalidDataException ex)
        {
            return AdapterResults.Fail<AdapterPackageContents>("adapter-zip-invalid", $"不是合法的 zip 包：{ex.Message}");
        }

        using (archive)
        {
            if (archive.Entries.Count == 0)
            {
                return AdapterResults.Fail<AdapterPackageContents>("adapter-zip-empty", "zip 包里没有任何条目。");
            }

            if (archive.Entries.Count > limits.MaxEntryCount)
            {
                return AdapterResults.Fail<AdapterPackageContents>(
                    "adapter-zip-too-many-entries",
                    $"zip 条目数 {archive.Entries.Count} 超过上限 {limits.MaxEntryCount}。");
            }

            var plans = new List<ZipPlan>(archive.Entries.Count);
            var declaredTotal = 0L;
            foreach (var entry in archive.Entries)
            {
                if (entry.Name.Length == 0)
                {
                    continue;
                }

                var normalized = NormalizeEntryName(entry.FullName);
                if (normalized is null)
                {
                    return AdapterResults.Fail<AdapterPackageContents>(
                        "adapter-zip-slip",
                        $"zip 条目「{entry.FullName}」是绝对路径或含 ..，整包拒绝安装。",
                        entry.FullName);
                }

                if (entry.Length > limits.MaxExtractedBytes - declaredTotal)
                {
                    return AdapterResults.Fail<AdapterPackageContents>(
                        "adapter-zip-too-large",
                        $"zip 解压后体积超过上限 {limits.MaxExtractedBytes} 字节（条目「{entry.FullName}」已越界）。",
                        entry.FullName);
                }

                declaredTotal += entry.Length;
                plans.Add(new ZipPlan(normalized, entry));
            }

            var entryPoint = SelectEntryPoint(plans, declaredEntryPoint);
            if (!entryPoint.IsSuccess)
            {
                return OperationResult<AdapterPackageContents>.Fail(entryPoint.Message ?? "找不到 zip 包里的入口。", entryPoint.Detail, entryPoint.Retryable);
            }

            var (entryPointPath, entryPointEntry) = entryPoint.Value;
            var adapterJsonBytes = ReadEntry(entryPointEntry, limits.MaxAdapterJsonBytes);
            if (adapterJsonBytes is null)
            {
                return AdapterResults.Fail<AdapterPackageContents>(
                    "adapter-package-too-large",
                    $"入口「{entryPointPath}」超过 {limits.MaxAdapterJsonBytes} 字节上限。",
                    entryPointPath);
            }

            var prefix = GetDirectoryPrefix(entryPointPath);
            var files = new List<AdapterPackageFile>(plans.Count);
            var extracted = 0L;
            foreach (var plan in plans)
            {
                if (ReferenceEquals(plan.Entry, entryPointEntry))
                {
                    continue;
                }

                string relative;
                if (prefix.Length == 0)
                {
                    relative = plan.RelativePath;
                }
                else if (plan.RelativePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    relative = plan.RelativePath[prefix.Length..];
                }
                else
                {
                    return AdapterResults.Fail<AdapterPackageContents>(
                        "adapter-zip-entry-outside-root",
                        $"zip 条目「{plan.RelativePath}」不在入口目录「{prefix}」下，整包拒绝安装。",
                        plan.RelativePath);
                }

                if (relative.Length == 0)
                {
                    continue;
                }

                var content = ReadEntry(plan.Entry, limits.MaxExtractedBytes - extracted);
                if (content is null)
                {
                    return AdapterResults.Fail<AdapterPackageContents>(
                        "adapter-zip-too-large",
                        $"zip 解压后体积超过上限 {limits.MaxExtractedBytes} 字节。",
                        plan.RelativePath);
                }

                extracted += content.Length;
                files.Add(new AdapterPackageFile(relative, content));
            }

            return OperationResult<AdapterPackageContents>.Ok(new AdapterPackageContents(entryPointPath, adapterJsonBytes, files));
        }
    }

    /// <summary>按 UTF-8 解码并跳过 BOM。</summary>
    /// <param name="bytes">原文。</param>
    public static string DecodeUtf8(byte[] bytes)
    {
        var span = bytes.AsSpan();
        if (span.Length >= 3 && span[0] == 0xEF && span[1] == 0xBB && span[2] == 0xBF)
        {
            span = span[3..];
        }

        return Encoding.UTF8.GetString(span);
    }

    /// <summary>把 zip 条目名归一成相对路径；任何可能逃出目标目录的写法都返回 null。</summary>
    /// <param name="raw">条目原始名。</param>
    public static string? NormalizeEntryName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var text = raw.Replace('\\', '/').Trim();
        if (text.Length == 0 || text[0] == '/')
        {
            return null;
        }

        if (text.Length >= 2 && text[1] == ':')
        {
            return null;
        }

        var kept = new List<string>();
        foreach (var segment in text.Split('/'))
        {
            if (segment.Length == 0 || string.Equals(segment, ".", StringComparison.Ordinal))
            {
                continue;
            }

            if (string.Equals(segment, "..", StringComparison.Ordinal) || segment.Contains(':'))
            {
                return null;
            }

            kept.Add(segment);
        }

        return kept.Count == 0 ? null : string.Join('/', kept);
    }

    private static OperationResult<(string Path, ZipArchiveEntry Entry)> SelectEntryPoint(List<ZipPlan> plans, string? declaredEntryPoint)
    {
        if (!string.IsNullOrWhiteSpace(declaredEntryPoint))
        {
            var wanted = NormalizeEntryName(declaredEntryPoint);
            if (wanted is null)
            {
                return AdapterResults.Fail<(string, ZipArchiveEntry)>("adapter-entry-invalid", $"声明的入口「{declaredEntryPoint}」不是合法的包内相对路径。");
            }

            foreach (var plan in plans)
            {
                if (string.Equals(plan.RelativePath, wanted, StringComparison.OrdinalIgnoreCase))
                {
                    return OperationResult<(string, ZipArchiveEntry)>.Ok((plan.RelativePath, plan.Entry));
                }
            }

            return AdapterResults.Fail<(string, ZipArchiveEntry)>("adapter-zip-entry-missing", $"zip 包里找不到声明的入口「{wanted}」。", wanted);
        }

        var candidates = new List<ZipPlan>();
        foreach (var plan in plans)
        {
            if (string.Equals(GetFileName(plan.RelativePath), AdapterPackageLayout.AdapterEntryFileName, StringComparison.OrdinalIgnoreCase))
            {
                candidates.Add(plan);
            }
        }

        if (candidates.Count == 0)
        {
            return AdapterResults.Fail<(string, ZipArchiveEntry)>(
                "adapter-zip-no-adapter-json",
                $"zip 包里没有 {AdapterPackageLayout.AdapterEntryFileName}，无法确定入口。");
        }

        if (candidates.Count > 1)
        {
            var names = new List<string>(candidates.Count);
            foreach (var candidate in candidates)
            {
                names.Add(candidate.RelativePath);
            }

            return AdapterResults.Fail<(string, ZipArchiveEntry)>(
                "adapter-zip-ambiguous-adapter-json",
                $"zip 包里有 {candidates.Count} 个 {AdapterPackageLayout.AdapterEntryFileName}（{string.Join("、", names)}），请显式指定入口。");
        }

        return OperationResult<(string, ZipArchiveEntry)>.Ok((candidates[0].RelativePath, candidates[0].Entry));
    }

    private static byte[]? ReadEntry(ZipArchiveEntry entry, long limit)
    {
        if (limit <= 0)
        {
            return null;
        }

        using var stream = entry.Open();
        using var buffer = new MemoryStream();
        var chunk = new byte[16384];
        var total = 0L;
        int read;
        while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
        {
            total += read;
            if (total > limit)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static string GetFileName(string relativePath)
    {
        var index = relativePath.LastIndexOf('/');
        return index < 0 ? relativePath : relativePath[(index + 1)..];
    }

    private static string GetDirectoryPrefix(string relativePath)
    {
        var index = relativePath.LastIndexOf('/');
        return index < 0 ? string.Empty : relativePath[..(index + 1)];
    }

    private readonly record struct ZipPlan(string RelativePath, ZipArchiveEntry Entry);
}
