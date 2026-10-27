namespace CkanUI.Services.Dedup;

/// <summary>
/// 文件实体标识（Windows：卷序列号 + 文件索引，等价于 Unix 的 dev + inode）。
/// 同一标识意味着两个路径指向**同一份数据**（硬链接）。
/// </summary>
public readonly record struct DedupFileIdentity(ulong VolumeSerial, ulong FileIndex)
{
    /// <summary>读不到标识时的占位值。</summary>
    public static DedupFileIdentity Unknown => default;

    /// <summary>是否读到了真实标识（0/0 视为未知）。</summary>
    public bool IsKnown => VolumeSerial != 0 || FileIndex != 0;

    /// <summary>两个路径是否指向同一份数据；任一侧未知时返回 false（无法确认就不当作已链接）。</summary>
    public bool SameAs(DedupFileIdentity other) =>
        IsKnown && other.IsKnown && VolumeSerial == other.VolumeSerial && FileIndex == other.FileIndex;
}
