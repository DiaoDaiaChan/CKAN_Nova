namespace CkanUI.Services.Unmanaged;

/// <summary>
/// 注册表 <c>installed_dlls</c> 的纯函数部分（12 篇 F-42 / INT-09）：合并语义与快照文件名。
/// 不引用 CKAN，便于 headless 覆盖（上游 <c>Registry.SetDlls</c> 是整体替换，合并必须由调用方做）。
/// </summary>
public static class ManagedDllMerge
{
    /// <summary>快照文件名的前缀（与 <c>registry.json</c> 同目录）。</summary>
    public const string SnapshotPrefix = "registry.json.bak-";

    /// <summary>
    /// 把 <paramref name="additions"/> 合并到 <paramref name="existing"/> 之上（同键以新值为准）。
    /// 已有条目按原样保留——注册表里的路径是上游写的，改写它会让 <c>FileOwner</c> 查不到。
    /// </summary>
    public static IReadOnlyDictionary<string, string> Merge(
        IReadOnlyDictionary<string, string>? existing,
        IReadOnlyDictionary<string, string>? additions)
    {
        var merged = new Dictionary<string, string>(StringComparer.Ordinal);
        if (existing is not null)
        {
            foreach (var (identifier, path) in existing)
            {
                if (string.IsNullOrWhiteSpace(identifier) || string.IsNullOrWhiteSpace(path)) continue;
                merged[identifier] = path;
            }
        }

        if (additions is not null)
        {
            foreach (var (identifier, path) in additions)
            {
                if (string.IsNullOrWhiteSpace(identifier) || string.IsNullOrWhiteSpace(path)) continue;
                merged[identifier] = path;
            }
        }

        return merged;
    }

    /// <summary>生成写前快照的文件名（与 <c>registry.json</c> 同目录，秒级时间戳）。</summary>
    public static string SnapshotFileName(DateTime timestamp)
        => SnapshotPrefix + timestamp.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
}
