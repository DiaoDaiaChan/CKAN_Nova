using CkanUI.Services.Abstractions.Dtos;
using CommunityToolkit.Mvvm.Input;

namespace CkanUI.ViewModels.Pages.Mods;

/// <summary>
/// 筛选区里的一枚标签组开关（12 篇 F-24）。每个开关自带命令，视图里不需要向上找 DataContext，
/// 也就不需要 XAML 转换器或编译绑定。
/// </summary>
public sealed record ModsLabelChip(
    string Name,
    string DisplayText,
    string ColorHex,
    bool HasColor,
    bool IsActive,
    IRelayCommand ToggleCommand)
{
    /// <summary>由标签组快照 + 当前选中状态生成一枚开关。</summary>
    public static ModsLabelChip For(LabelInfo info, bool active, IRelayCommand toggle)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(toggle);

        var text = info.ModuleCount > 0 ? $"{info.Name}（{info.ModuleCount}）" : info.Name;
        return new ModsLabelChip(info.Name, text, info.ColorHex ?? string.Empty, info.HasColor, active, toggle);
    }

    /// <summary>选中态提示（无障碍与测试断言用，不只靠颜色/边框表达状态）。</summary>
    public string StateText => IsActive ? "已筛选" : "未筛选";
}
