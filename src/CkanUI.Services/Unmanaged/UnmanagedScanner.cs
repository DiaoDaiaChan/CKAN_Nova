using System.Text.RegularExpressions;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Services.Unmanaged;

/// <summary>
/// 未管理内容的扫描与删除（12 篇 F-42 / UI-10）。纯文件系统实现：不引用 CKAN，
/// 「哪些文件受管」由调用方以集合传入，因此可以在 headless 测试里用临时目录完整覆盖。
/// </summary>
/// <remarks>
/// 判定规则与 Core <c>GameInstance.UnmanagedFiles</c> 对齐：GameData 下没有被注册表登记归属的文件就是未管理；
/// 目录的受管子项计数是递归统计的，计数大于 0 的目录不允许整目录删除（会连带受管文件）。
/// </remarks>
public static class UnmanagedScanner
{
    /// <summary>GameData 子目录名（与 Core <c>GameInstance</c> 一致）。</summary>
    public const string GameDataFolder = "GameData";

    /// <summary>与 Core <c>GameInstance.dllPattern</c> 相同的 DLL 标识符提取规则。</summary>
    private static readonly Regex DllPattern = new(
        @"^(?:.*/)?(?<identifier>[^.]+).*\.dll$",
        RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace | RegexOptions.Compiled);

    /// <summary>解析实例的 GameData 绝对路径。</summary>
    public static string ResolveGameData(string gameDir) => Path.Combine(gameDir, GameDataFolder);

    /// <summary>把绝对路径折算成 GameData 相对路径（正斜杠）。</summary>
    public static string ToRelative(string gameDataDir, string absolutePath) =>
        Path.GetRelativePath(gameDataDir, absolutePath).Replace('\\', '/');

    /// <summary>从文件名猜模块标识符（只对 .dll 有意义，其余返回 <c>null</c>）。</summary>
    public static string? GuessIdentifier(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return null;
        var match = DllPattern.Match(relativePath);
        return match.Success ? match.Groups["identifier"].Value : null;
    }

    /// <summary>扫描 GameData：返回未管理的文件与目录（扁平列表，目录项带受管子项计数）。</summary>
    public static OperationResult<IReadOnlyList<UnmanagedFileInfo>> Scan(
        string gameDataDir, IReadOnlySet<string> managedPaths)
    {
        ArgumentNullException.ThrowIfNull(managedPaths);
        if (string.IsNullOrWhiteSpace(gameDataDir))
        {
            return OperationResult<IReadOnlyList<UnmanagedFileInfo>>.Fail("实例没有有效的游戏目录。", null, false);
        }

        if (!Directory.Exists(gameDataDir))
        {
            return OperationResult<IReadOnlyList<UnmanagedFileInfo>>.Fail(
                $"找不到 GameData 目录：{gameDataDir}",
                "实例路径可能已被移动或删除，请先到实例页校正。",
                retryable: false);
        }

        var directories = new Dictionary<string, DirectoryStat>(StringComparer.OrdinalIgnoreCase);
        var files = new List<UnmanagedFileInfo>();
        try
        {
            foreach (var file in Directory.EnumerateFiles(gameDataDir, "*", SearchOption.AllDirectories))
            {
                var relative = ToRelative(gameDataDir, file);
                if (relative.Length == 0) continue;

                var isManaged = managedPaths.Contains(relative);
                Accumulate(directories, ParentOf(relative), isManaged);
                if (isManaged) continue;

                var size = SafeLength(file);
                AddSize(directories, ParentOf(relative), size);
                files.Add(new UnmanagedFileInfo(relative, size, false, GuessIdentifier(relative))
                {
                    UnmanagedChildCount = 1,
                    LastWriteTimeUtc = SafeWriteTime(file),
                });
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult<IReadOnlyList<UnmanagedFileInfo>>.Fail("扫描 GameData 失败。", ex.Message, retryable: true);
        }

        var result = new List<UnmanagedFileInfo>(directories.Count + files.Count);
        foreach (var (relative, stat) in directories)
        {
            // 根目录自身不出现在列表里；完全没有未管理内容的目录也不需要展示。
            if (relative.Length == 0 || stat.UnmanagedCount == 0) continue;
            result.Add(new UnmanagedFileInfo(relative, stat.SizeBytes, true, null)
            {
                ManagedChildCount = stat.ManagedCount,
                UnmanagedChildCount = stat.UnmanagedCount,
                LastWriteTimeUtc = SafeWriteTime(Path.Combine(gameDataDir, relative)),
            });
        }

        result.AddRange(files);
        result.Sort(Compare);
        return OperationResult<IReadOnlyList<UnmanagedFileInfo>>.Ok(result);
    }

    /// <summary>
    /// 删除未管理内容。<paramref name="dryRun"/> 为 <c>true</c> 时只校验并返回将被删除的路径，绝不落盘。
    /// 选中项里只要含受管路径（或含受管子项的目录），整批拒绝、一个都不删。
    /// </summary>
    public static OperationResult<IReadOnlyList<string>> Delete(
        string gameDataDir, IReadOnlySet<string> managedPaths,
        IReadOnlyCollection<string> relativePaths, bool dryRun)
    {
        ArgumentNullException.ThrowIfNull(managedPaths);
        if (relativePaths is null || relativePaths.Count == 0)
        {
            return OperationResult<IReadOnlyList<string>>.Fail("没有选中要删除的内容。", null, retryable: false);
        }

        if (!Directory.Exists(gameDataDir))
        {
            return OperationResult<IReadOnlyList<string>>.Fail(
                $"找不到 GameData 目录：{gameDataDir}",
                "实例路径可能已被移动或删除，请先到实例页校正。",
                retryable: false);
        }

        var normalized = new List<string>(relativePaths.Count);
        foreach (var raw in relativePaths)
        {
            var relative = Normalize(raw);
            if (relative is null)
            {
                return OperationResult<IReadOnlyList<string>>.Fail(
                    $"路径不合法：{raw}",
                    "只允许删除 GameData 内的相对路径。",
                    retryable: false);
            }

            if (!normalized.Contains(relative, StringComparer.OrdinalIgnoreCase)) normalized.Add(relative);
        }

        var refused = normalized.Where(path => IsManagedOrContainsManaged(path, managedPaths)).ToArray();
        if (refused.Length > 0)
        {
            return OperationResult<IReadOnlyList<string>>.Fail(
                $"选中项包含受管内容，已拒绝删除：{string.Join("、", refused)}",
                "受管文件请到模组页卸载，本页只处理未管理内容。",
                retryable: false);
        }

        if (dryRun) return OperationResult<IReadOnlyList<string>>.Ok(normalized);

        foreach (var relative in normalized)
        {
            var absolute = Path.Combine(gameDataDir, relative.Replace('/', Path.DirectorySeparatorChar));
            try
            {
                if (Directory.Exists(absolute))
                {
                    Directory.Delete(absolute, recursive: true);
                }
                else if (File.Exists(absolute))
                {
                    File.Delete(absolute);
                }
                else
                {
                    return OperationResult<IReadOnlyList<string>>.Fail(
                        $"找不到要删除的内容：{relative}",
                        "可能已被其它程序删除，请刷新后重试。",
                        retryable: true);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return OperationResult<IReadOnlyList<string>>.Fail(
                    $"删除失败：{relative}", ex.Message, retryable: true);
            }
        }

        return OperationResult<IReadOnlyList<string>>.Ok(normalized);
    }

    private static int Compare(UnmanagedFileInfo left, UnmanagedFileInfo right)
    {
        var byPath = string.Compare(left.RelativePath, right.RelativePath, StringComparison.OrdinalIgnoreCase);
        if (byPath != 0) return byPath;
        if (left.IsDirectory == right.IsDirectory) return 0;
        return left.IsDirectory ? -1 : 1;
    }

    private static string ParentOf(string relative)
    {
        var index = relative.LastIndexOf('/');
        return index < 0 ? string.Empty : relative[..index];
    }

    private static void Accumulate(
        Dictionary<string, DirectoryStat> directories, string relativeDir, bool managed)
    {
        var current = relativeDir;
        while (true)
        {
            if (!directories.TryGetValue(current, out var stat))
            {
                stat = new DirectoryStat();
                directories[current] = stat;
            }

            if (managed) stat.ManagedCount++;
            else stat.UnmanagedCount++;

            if (current.Length == 0) return;
            current = ParentOf(current);
        }
    }

    private static void AddSize(Dictionary<string, DirectoryStat> directories, string relativeDir, long size)
    {
        var current = relativeDir;
        while (true)
        {
            if (directories.TryGetValue(current, out var stat)) stat.SizeBytes += size;
            if (current.Length == 0) return;
            current = ParentOf(current);
        }
    }

    private static bool IsManagedOrContainsManaged(string relative, IReadOnlySet<string> managedPaths)
    {
        if (managedPaths.Contains(relative)) return true;
        var prefix = relative + "/";
        foreach (var path in managedPaths)
        {
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    /// <summary>归一化相对路径；绝对路径、带盘符或含 <c>..</c> 的一律拒绝。</summary>
    private static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var trimmed = raw.Trim().Replace('\\', '/').Trim('/');
        if (trimmed.Length == 0) return null;
        if (trimmed.Contains(':', StringComparison.Ordinal)) return null;
        if (Path.IsPathRooted(trimmed)) return null;
        foreach (var segment in trimmed.Split('/'))
        {
            if (segment.Length == 0 || segment is "." or "..") return null;
        }

        return trimmed;
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

    private static DateTime? SafeWriteTime(string path)
    {
        try
        {
            return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : Directory.GetLastWriteTimeUtc(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private sealed class DirectoryStat
    {
        public int ManagedCount { get; set; }

        public int UnmanagedCount { get; set; }

        public long SizeBytes { get; set; }
    }
}
