using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Services.Unmanaged;

namespace CkanUI.Services.Cleanup;

/// <summary>
/// 卸载残留目录的列出/预览/删除（12 篇 F-43 / 06 篇 UI-22）。
/// </summary>
/// <remarks>
/// 安全边界（硬约束，任何一条不满足就跳过该目录，绝不放宽）：
/// <list type="number">
/// <item>只允许删 <c>&lt;GameDir&gt;/GameData</c> 之下的目录：路径归一化拒绝盘符、绝对路径、<c>..</c>，落盘前再用
/// <see cref="Path.GetFullPath(string)"/> 复核一次前缀。</item>
/// <item>目录本身或目录内任何一层是符号链接/目录联接 → 跳过（删链接可能连带删掉链接目标）。</item>
/// <item>目录内含有受管文件（<see cref="IManagedPathProvider"/>）→ 跳过，让用户回模组页卸载。</item>
/// <item>删除前再校验一次（TOCTOU）：预览到删除之间用户可能装了新模组。</item>
/// </list>
/// 单个目录被跳过不影响同批其它目录——残留清理是"能删多少删多少"，与未管理页"整批拒绝"的语义不同，
/// 因此跳过项一律计入 <see cref="ResidualCleanupReport.SkippedCount"/> 让界面如实汇报。
/// </remarks>
public sealed class ResidualCleanupService : IResidualCleanupService
{
    /// <summary>每个目录最多展示几个示例文件名（够用户认出这是哪个模组的残留）。</summary>
    private const int SampleFileLimit = 5;

    private const string MissingReason = "目录已不存在，可能已被其它程序删除。";

    private readonly IResidualDirectorySource _source;
    private readonly IManagedPathProvider _managedPaths;
    private readonly IErrorReporter _errors;

    /// <summary>创建服务。</summary>
    /// <param name="source">候选残留目录的来源（生产环境是 <see cref="HeuristicResidualDirectorySource"/>）。</param>
    /// <param name="managedPaths">受管文件清单（与未管理页共用，保证"受管"只有一种口径）。</param>
    /// <param name="errors">异常上报。</param>
    public ResidualCleanupService(
        IResidualDirectorySource source,
        IManagedPathProvider managedPaths,
        IErrorReporter errors)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(managedPaths);
        ArgumentNullException.ThrowIfNull(errors);
        _source = source;
        _managedPaths = managedPaths;
        _errors = errors;
    }

    /// <inheritdoc />
    public Task<OperationResult<IReadOnlyList<ResidualDirectoryInfo>>> ListAsync(
        InstanceInfo instance,
        IReadOnlyCollection<string>? reportedByUninstall = null,
        CancellationToken ct = default)
        => RunAsync(
            "ResidualCleanupService.ListAsync",
            "列出残留目录失败。",
            () => List(instance, reportedByUninstall),
            ct);

    /// <inheritdoc />
    public Task<OperationResult<IReadOnlyList<ResidualDirectoryInfo>>> PreviewAsync(
        InstanceInfo instance,
        IReadOnlyCollection<string> relativePaths,
        CancellationToken ct = default)
        => RunAsync(
            "ResidualCleanupService.PreviewAsync",
            "预览残留目录失败。",
            () => Preview(instance, relativePaths),
            ct);

    /// <inheritdoc />
    public Task<OperationResult<ResidualCleanupReport>> DeleteAsync(
        InstanceInfo instance,
        IReadOnlyCollection<string> relativePaths,
        CancellationToken ct = default)
        => RunAsync(
            "ResidualCleanupService.DeleteAsync",
            "删除残留目录失败。",
            () => Delete(instance, relativePaths),
            ct);

    private OperationResult<IReadOnlyList<ResidualDirectoryInfo>> List(
        InstanceInfo instance, IReadOnlyCollection<string>? reportedByUninstall)
    {
        var context = Open(instance);
        if (!context.IsSuccess)
        {
            return OperationResult<IReadOnlyList<ResidualDirectoryInfo>>.Fail(
                context.Message ?? "无法读取实例目录。", context.Detail, context.Retryable);
        }

        var (gameData, managed) = context.Value;

        var candidates = new List<string>();
        AddCandidates(candidates, reportedByUninstall);

        var fromSource = _source.GetCandidates(instance);
        if (!fromSource.IsSuccess)
        {
            return OperationResult<IReadOnlyList<ResidualDirectoryInfo>>.Fail(
                fromSource.Message ?? "无法枚举残留目录。", fromSource.Detail, fromSource.Retryable);
        }

        AddCandidates(candidates, fromSource.Value);

        var infos = new List<ResidualDirectoryInfo>(candidates.Count);
        foreach (var relative in candidates)
        {
            var info = Describe(gameData, relative, managed);
            // 已经不存在的目录没什么可清理的，直接不列（预览里才会如实显示）。
            if (info.BlockReason == MissingReason)
            {
                continue;
            }

            infos.Add(info);
        }

        infos.Sort((left, right) => string.Compare(left.RelativePath, right.RelativePath, StringComparison.OrdinalIgnoreCase));
        return OperationResult<IReadOnlyList<ResidualDirectoryInfo>>.Ok(infos);
    }

    private OperationResult<IReadOnlyList<ResidualDirectoryInfo>> Preview(
        InstanceInfo instance, IReadOnlyCollection<string> relativePaths)
    {
        if (relativePaths is null || relativePaths.Count == 0)
        {
            return OperationResult<IReadOnlyList<ResidualDirectoryInfo>>.Fail(
                "没有勾选要删除的目录。", null, retryable: false);
        }

        var context = Open(instance);
        if (!context.IsSuccess)
        {
            return OperationResult<IReadOnlyList<ResidualDirectoryInfo>>.Fail(
                context.Message ?? "无法读取实例目录。", context.Detail, context.Retryable);
        }

        var (gameData, managed) = context.Value;

        var normalized = NormalizeAll(relativePaths);
        if (!normalized.IsSuccess || normalized.Value is null)
        {
            return OperationResult<IReadOnlyList<ResidualDirectoryInfo>>.Fail(
                normalized.Message ?? "路径不合法。", normalized.Detail, normalized.Retryable);
        }

        var infos = new List<ResidualDirectoryInfo>(normalized.Value.Count);
        foreach (var relative in normalized.Value)
        {
            infos.Add(Describe(gameData, relative, managed));
        }

        return OperationResult<IReadOnlyList<ResidualDirectoryInfo>>.Ok(infos);
    }

    private OperationResult<ResidualCleanupReport> Delete(
        InstanceInfo instance, IReadOnlyCollection<string> relativePaths)
    {
        if (relativePaths is null || relativePaths.Count == 0)
        {
            return OperationResult<ResidualCleanupReport>.Fail("没有勾选要删除的目录。", null, retryable: false);
        }

        var context = Open(instance);
        if (!context.IsSuccess)
        {
            return OperationResult<ResidualCleanupReport>.Fail(
                context.Message ?? "无法读取实例目录。", context.Detail, context.Retryable);
        }

        var (gameData, managed) = context.Value;

        var normalized = NormalizeAll(relativePaths);
        if (!normalized.IsSuccess || normalized.Value is null)
        {
            return OperationResult<ResidualCleanupReport>.Fail(
                normalized.Message ?? "路径不合法。", normalized.Detail, normalized.Retryable);
        }

        var deleted = new List<string>();
        var skipped = new List<string>();
        var failed = new List<string>();
        var freed = 0L;

        foreach (var relative in normalized.Value)
        {
            // 删除前再校验一次：预览到删除之间用户可能又装了什么（TOCTOU）。
            var info = Describe(gameData, relative, managed);
            if (!info.IsDeletable)
            {
                skipped.Add(relative);
                continue;
            }

            var absolute = AbsoluteOf(gameData, relative);
            if (!IsUnderRoot(gameData, absolute))
            {
                skipped.Add(relative);
                continue;
            }

            try
            {
                Directory.Delete(absolute, recursive: true);
                deleted.Add(relative);
                freed += info.SizeBytes;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _errors.Report(ex, "ResidualCleanupService.Delete", $"删除残留目录失败：{relative}");
                failed.Add(relative);
            }
        }

        var report = new ResidualCleanupReport(deleted.Count, skipped.Count, failed.Count)
        {
            Deleted = deleted,
            Skipped = skipped,
            Failed = failed,
            FreedBytes = freed,
        };

        if (failed.Count > 0)
        {
            return OperationResult<ResidualCleanupReport>.Fail(
                $"有 {failed.Count} 个目录没能删除：{string.Join("、", failed)}",
                "可能被其它程序占用或权限不足，关闭占用后可以重试。",
                retryable: true);
        }

        return OperationResult<ResidualCleanupReport>.Ok(report);
    }

    /// <summary>解析 GameData 与受管清单；两者任一不可用就整体失败。</summary>
    private OperationResult<(string GameData, IReadOnlySet<string> Managed)> Open(InstanceInfo instance)
    {
        if (instance is null)
        {
            return OperationResult<(string GameData, IReadOnlySet<string> Managed)>.Fail(
                "没有选定游戏实例。", null, retryable: false);
        }

        var gameData = UnmanagedScanner.ResolveGameData(instance.GameDir);
        if (!Directory.Exists(gameData))
        {
            return OperationResult<(string GameData, IReadOnlySet<string> Managed)>.Fail(
                $"找不到 GameData 目录：{gameData}",
                "实例路径可能已被移动或删除，请先到实例页校正。",
                retryable: false);
        }

        var managed = _managedPaths.GetManagedPaths(instance);
        if (!managed.IsSuccess || managed.Value is null)
        {
            return OperationResult<(string GameData, IReadOnlySet<string> Managed)>.Fail(
                managed.Message ?? "无法读取受管文件清单。",
                managed.Detail ?? "受管清单不可用时不允许删除任何目录。",
                retryable: managed.Retryable);
        }

        return OperationResult<(string GameData, IReadOnlySet<string> Managed)>.Ok((gameData, managed.Value));
    }

    /// <summary>统计一个目录并给出可删除性判断（不落盘）。</summary>
    private static ResidualDirectoryInfo Describe(string gameData, string relative, IReadOnlySet<string> managed)
    {
        var absolute = AbsoluteOf(gameData, relative);
        if (!Directory.Exists(absolute))
        {
            return new ResidualDirectoryInfo(relative, 0, 0, Array.Empty<string>()) { BlockReason = MissingReason };
        }

        var files = new List<string>();
        var size = 0L;
        try
        {
            foreach (var file in Directory.EnumerateFiles(absolute, "*", SearchOption.AllDirectories))
            {
                files.Add(UnmanagedScanner.ToRelative(absolute, file));
                size += SafeLength(file);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ResidualDirectoryInfo(relative, 0, 0, Array.Empty<string>())
            {
                BlockReason = $"无法读取目录内容：{ex.Message}",
            };
        }

        files.Sort(StringComparer.OrdinalIgnoreCase);
        var samples = files.Take(SampleFileLimit).ToArray();
        return new ResidualDirectoryInfo(relative, files.Count, size, samples)
        {
            BlockReason = BlockReasonFor(gameData, absolute, relative, managed),
        };
    }

    private static string? BlockReasonFor(
        string gameData, string absolute, string relative, IReadOnlySet<string> managed)
    {
        if (!IsUnderRoot(gameData, absolute))
        {
            return "目录不在 GameData 之下，已拒绝。";
        }

        if (IsSymlink(absolute))
        {
            return "这是符号链接，删它会连带删掉链接目标，已跳过。";
        }

        if (ContainsSymlink(absolute))
        {
            return "目录内含有符号链接，已跳过。";
        }

        if (IsManagedOrContainsManaged(relative, managed))
        {
            return "目录内含有受管文件，请到模组页卸载对应模组。";
        }

        return null;
    }

    private static void AddCandidates(List<string> target, IReadOnlyCollection<string>? source)
    {
        if (source is null)
        {
            return;
        }

        foreach (var raw in source)
        {
            var relative = Normalize(raw);
            if (relative is null)
            {
                // 来源里的脏路径不报错，直接忽略：列出阶段不该因为一条坏数据就整页失败。
                continue;
            }

            if (!target.Contains(relative, StringComparer.OrdinalIgnoreCase))
            {
                target.Add(relative);
            }
        }
    }

    private static OperationResult<IReadOnlyList<string>> NormalizeAll(IReadOnlyCollection<string> rawPaths)
    {
        var normalized = new List<string>(rawPaths.Count);
        foreach (var raw in rawPaths)
        {
            var relative = Normalize(raw);
            if (relative is null)
            {
                return OperationResult<IReadOnlyList<string>>.Fail(
                    $"路径不合法：{raw}",
                    "只允许操作 GameData 内的相对路径。",
                    retryable: false);
            }

            if (!normalized.Contains(relative, StringComparer.OrdinalIgnoreCase))
            {
                normalized.Add(relative);
            }
        }

        return OperationResult<IReadOnlyList<string>>.Ok(normalized);
    }

    /// <summary>归一化相对路径；绝对路径、带盘符或含 <c>..</c> 的一律拒绝（与未管理页同一套规则）。</summary>
    private static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var trimmed = raw.Trim().Replace('\\', '/').Trim('/');
        if (trimmed.Length == 0)
        {
            return null;
        }

        if (trimmed.Contains(':', StringComparison.Ordinal))
        {
            return null;
        }

        if (Path.IsPathRooted(trimmed))
        {
            return null;
        }

        foreach (var segment in trimmed.Split('/'))
        {
            if (segment.Length == 0 || segment is "." or "..")
            {
                return null;
            }
        }

        return trimmed;
    }

    private static string AbsoluteOf(string gameData, string relative)
        => Path.Combine(gameData, relative.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>复核绝对路径确实落在 GameData 之下（防 <c>..</c> 与符号链接之外的意外逃逸）。</summary>
    private static bool IsUnderRoot(string gameData, string absolute)
    {
        var root = Path.GetFullPath(gameData)
                       .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                   + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(absolute);
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSymlink(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 读不到属性时按"不安全"处理：宁可跳过也不冒险删。
            return true;
        }
    }

    private static bool ContainsSymlink(string directory)
    {
        try
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory, "*", SearchOption.AllDirectories))
            {
                if ((File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0)
                {
                    return true;
                }
            }

            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static bool IsManagedOrContainsManaged(string relative, IReadOnlySet<string> managed)
    {
        if (managed.Contains(relative))
        {
            return true;
        }

        var prefix = relative + "/";
        foreach (var path in managed)
        {
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static long SafeLength(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private async Task<OperationResult<T>> RunAsync<T>(
        string context, string userMessage, Func<OperationResult<T>> work, CancellationToken ct)
    {
        try
        {
            return await Task.Run(work, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return OperationResult<T>.Fail("操作已取消。", null, retryable: false);
        }
        catch (Exception ex)
        {
            _errors.Report(ex, context, userMessage);
            return OperationResult<T>.Fail(userMessage, ex.Message, retryable: true);
        }
    }
}
