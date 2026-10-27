using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.ViewModels.Pages.ModPacks;

/// <summary>把导入预览的条目状态与实例差异类型翻译成中文文案（界面与摘要共用一份口径）。</summary>
public static class ModPackStateText
{
    /// <summary>条目状态的中文标签。</summary>
    /// <param name="state">条目状态。</param>
    /// <returns>中文标签。</returns>
    public static string Label(ModPackEntryState state) => state switch
    {
        ModPackEntryState.Install => "将安装",
        ModPackEntryState.Upgrade => "将升级",
        ModPackEntryState.Downgrade => "将降级",
        ModPackEntryState.SameVersion => "已安装",
        ModPackEntryState.Missing => "找不到",
        ModPackEntryState.Conflict => "有冲突",
        _ => "未知"
    };

    /// <summary>实例差异类型的中文标签。</summary>
    /// <param name="kind">差异类型。</param>
    /// <returns>中文标签。</returns>
    public static string Label(InstanceDiffKind kind) => kind switch
    {
        InstanceDiffKind.OnlyInLeft => "仅左侧有",
        InstanceDiffKind.OnlyInRight => "仅右侧有",
        InstanceDiffKind.VersionDiffers => "版本不同",
        _ => "未知"
    };
}
