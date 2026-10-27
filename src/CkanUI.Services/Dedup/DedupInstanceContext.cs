namespace CkanUI.Services.Dedup;

/// <summary>
/// 一个参与去重的实例在扫描/执行期需要的全部上下文（GameData 根 + 受管文件集合）。
/// </summary>
/// <remarks>受管集合口径与 <c>IManagedPathProvider</c> 一致：相对 <c>&lt;GameDir&gt;/GameData</c> 的正斜杠路径、忽略大小写。</remarks>
public sealed record DedupInstanceContext(string InstanceName, string GameDataDir, IReadOnlySet<string> ManagedPaths)
{
    /// <summary>实例的 GameData 目录（与未管理文件页同一口径）。</summary>
    public static string ResolveGameData(string gameDir) => Path.Combine(gameDir, "GameData");

    /// <summary>绝对路径是否落在 GameData 之内（安全边界①）。</summary>
    public bool IsInsideGameData(string absolutePath)
    {
        if (string.IsNullOrWhiteSpace(absolutePath))
        {
            return false;
        }

        string root;
        string full;
        try
        {
            root = Path.GetFullPath(GameDataDir);
            full = Path.GetFullPath(absolutePath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        if (!root.EndsWith(Path.DirectorySeparatorChar))
        {
            root += Path.DirectorySeparatorChar;
        }

        return full.StartsWith(root, DedupVolume.Comparison);
    }

    /// <summary>绝对路径转成相对 GameData 的正斜杠路径。</summary>
    public string ToRelative(string absolutePath)
    {
        var relative = Path.GetRelativePath(GameDataDir, absolutePath);
        return relative.Replace('\\', '/');
    }

    /// <summary>相对路径是否受管。</summary>
    public bool IsManaged(string relativePath) => ManagedPaths.Contains(relativePath);
}
