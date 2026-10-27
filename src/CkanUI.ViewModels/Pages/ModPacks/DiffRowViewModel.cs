using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.ViewModels.Pages.ModPacks;

/// <summary>实例对比表格的一行：标识、差异类型与两侧版本。</summary>
/// <param name="Identifier">模组标识。</param>
/// <param name="Kind">差异类型。</param>
/// <param name="LeftVersion">左侧实例的版本（仅右侧有时为 null）。</param>
/// <param name="RightVersion">右侧实例的版本（仅左侧有时为 null）。</param>
public sealed record DiffRowViewModel(string Identifier, InstanceDiffKind Kind, string? LeftVersion, string? RightVersion)
{
    /// <summary>差异类型的中文标签。</summary>
    public string KindText => ModPackStateText.Label(Kind);

    /// <summary>左侧版本的显示文本。</summary>
    public string LeftVersionText => string.IsNullOrWhiteSpace(LeftVersion) ? "—" : LeftVersion!;

    /// <summary>右侧版本的显示文本。</summary>
    public string RightVersionText => string.IsNullOrWhiteSpace(RightVersion) ? "—" : RightVersion!;

    /// <summary>一行可读的版本对比文案。</summary>
    public string VersionsText => Kind switch
    {
        InstanceDiffKind.VersionDiffers => LeftVersionText + " → " + RightVersionText,
        InstanceDiffKind.OnlyInLeft => "左侧 " + LeftVersionText,
        InstanceDiffKind.OnlyInRight => "右侧 " + RightVersionText,
        _ => "—"
    };

    /// <summary>是否是版本不同（而不是只在一边存在）。</summary>
    public bool IsVersionDiff => Kind == InstanceDiffKind.VersionDiffers;

    /// <summary>把服务层的差异条目投影成表格行。</summary>
    /// <param name="item">服务层差异条目。</param>
    /// <returns>表格行。</returns>
    public static DiffRowViewModel From(InstanceDiffItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return new DiffRowViewModel(item.Identifier, item.Kind, item.LeftVersion, item.RightVersion);
    }
}
