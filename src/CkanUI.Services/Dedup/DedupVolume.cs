namespace CkanUI.Services.Dedup;

/// <summary>
/// 卷判定（F-41 安全边界③：只有同卷的两个文件才能做硬链接，跨卷 CreateHardLink 必然失败）。
/// </summary>
/// <remarks>
/// 口径：取所有已挂载卷（<see cref="DriveInfo.GetDrives"/>）中「路径前缀最长」的那个作为卷标识。
/// Windows 上等价于盘符（<c>C:</c>）；Unix 上能区分 <c>/</c> 与 <c>/mnt/x</c> 这类独立挂载点。
/// 已知缺口：Windows 的「文件夹挂载卷」与 Unix bind mount 无法区分（见 docs/design/dedup.md）。
/// </remarks>
public static class DedupVolume
{
    /// <summary>Windows 用忽略大小写比较，Unix 用序号比较。</summary>
    public static StringComparison Comparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>解析路径所属卷的标识（形如 <c>C:</c> 或 <c>/mnt/x</c>）。</summary>
    public static string Resolve(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return "unknown";
        }

        var best = Path.GetPathRoot(full) ?? full;
        var bestLength = -1;

        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                var root = drive.RootDirectory.FullName;
                if (root.Length <= bestLength || !IsUnder(full, root))
                {
                    continue;
                }

                best = root;
                bestLength = root.Length;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 卷枚举失败就退化成路径根（同盘符仍然能判定相等），不抛给界面。
        }

        return Normalize(best);
    }

    /// <summary>两个路径是否在同一卷（不能硬链接时返回 false，调用方据此给跳过原因）。</summary>
    public static bool SameVolume(string left, string right) =>
        string.Equals(Resolve(left), Resolve(right), Comparison);

    private static bool IsUnder(string full, string root)
    {
        if (full.StartsWith(root, Comparison))
        {
            return true;
        }

        // 根目录带分隔符时上面就够了；不带分隔符（例如 "/" 之外的边界情况）再补一次边界判断。
        var trimmed = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return trimmed.Length > 0
            && full.StartsWith(trimmed + Path.DirectorySeparatorChar, Comparison);
    }

    private static string Normalize(string root) =>
        root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) is { Length: > 0 } trimmed
            ? trimmed
            : root;
}
