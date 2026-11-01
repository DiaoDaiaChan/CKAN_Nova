namespace CkanUI.Services.Abstractions.Assets;

/// <summary>载具分类，对应 KSP 的 Ships 子目录。</summary>
public enum CraftKind
{
    /// <summary>火箭（<c>Ships/VAB</c>）。</summary>
    Vab,

    /// <summary>飞机（<c>Ships/SPH</c>）。</summary>
    Sph,

    /// <summary>子装配（<c>Ships/Subassemblies</c>）。</summary>
    Subassembly,
}

/// <summary>一个载具（<c>.craft</c>）的描述。</summary>
/// <param name="Name">不带扩展名的名字。</param>
/// <param name="FilePath">.craft 绝对路径。</param>
/// <param name="Kind">分类。</param>
/// <param name="ThumbnailPath">缩略图绝对路径；没有则为 null。</param>
/// <param name="Version">.loadmeta 里记的 KSP 版本。</param>
/// <param name="PartCount">零件数。</param>
/// <param name="StageCount">级数。</param>
/// <param name="SizeBytes">文件字节数。</param>
/// <param name="ModifiedAt">最后修改时间。</param>
public sealed record CraftAsset(
    string Name,
    string FilePath,
    CraftKind Kind,
    string? ThumbnailPath,
    string? Version,
    int? PartCount,
    int? StageCount,
    long SizeBytes,
    DateTimeOffset ModifiedAt);

/// <summary>一张旗帜。</summary>
/// <param name="Name">不带扩展名的文件名。</param>
/// <param name="FilePath">图片绝对路径。</param>
/// <param name="ConfigPath">KSP 口径的配置值，例如 <c>Squad/Flags/default</c>。</param>
/// <param name="PackName">来源包（GameData 下的第一段目录）。</param>
/// <param name="SizeBytes">文件字节数。</param>
public sealed record FlagAsset(
    string Name,
    string FilePath,
    string ConfigPath,
    string PackName,
    long SizeBytes);

/// <summary>一张加载图。</summary>
/// <param name="FileName">含扩展名的文件名。</param>
/// <param name="FilePath">图片绝对路径。</param>
/// <param name="Folder">所在目录。</param>
/// <param name="SizeBytes">文件字节数。</param>
/// <param name="ModifiedAt">最后修改时间。</param>
public sealed record LoadingImageAsset(
    string FileName,
    string FilePath,
    string Folder,
    long SizeBytes,
    DateTimeOffset ModifiedAt);
