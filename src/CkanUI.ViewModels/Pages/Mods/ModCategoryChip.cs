using CommunityToolkit.Mvvm.Input;

namespace CkanUI.ViewModels.Pages.Mods;

/// <summary>
/// 模组主分类筛选胶囊（8 大核心分类系统的 UI 开关）。
/// </summary>
public sealed record ModCategoryChip(
    string Id,
    string DisplayTitle,
    string NameZh,
    string Icon,
    int ModCount,
    bool IsActive,
    IRelayCommand ToggleCommand)
{
    /// <summary>完整按钮展示文案，包含数量统计：🎨 视觉与音效美化 (614)</summary>
    public string ButtonText => ModCount > 0 ? $"{DisplayTitle} ({ModCount})" : DisplayTitle;

    /// <summary>无障碍读屏状态提示</summary>
    public string StateText => IsActive ? $"已选中分类：{NameZh}" : $"未选中分类：{NameZh}";
}
