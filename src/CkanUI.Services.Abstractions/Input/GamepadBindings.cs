namespace CkanUI.Services.Abstractions.Input;

/// <summary>一行手柄说明：输入名（A / LB / 十字键…）与它触发的动作，都是本地化键。</summary>
/// <param name="InputKey">输入名的本地化键（如 <c>Ui_Pad_Input_A</c>）。</param>
/// <param name="ActionKey">动作名的本地化键（如 <c>Ui_Pad_Action_Accept</c>）。</param>
public readonly record struct GamepadBinding(string InputKey, string ActionKey);

/// <summary>
/// 手柄键位表：快捷键目录里「手柄」一节的唯一来源。
/// </summary>
/// <remarks>
/// 放在契约层而不是视图模型层，是为了让「目录里写的手柄键位」与
/// <c>GamepadNavigator</c> 真正派发的动作同源——改了映射只改这一处，目录不会撒谎。
/// </remarks>
public static class GamepadBindings
{
    /// <summary>全部手柄键位（按易用度排序：方向 → 确认 → 取消 → 面板 → 页签 → 翻页 → 侧栏）。</summary>
    public static IReadOnlyList<GamepadBinding> All { get; } =
    [
        new("Ui_Pad_Input_Direction", "Ui_Pad_Action_MoveFocus"),
        new("Ui_Pad_Input_A", "Ui_Pad_Action_Accept"),
        new("Ui_Pad_Input_B", "Ui_Pad_Action_Cancel"),
        new("Ui_Pad_Input_X", "Ui_Pad_Action_CommandPalette"),
        new("Ui_Pad_Input_Y", "Ui_Pad_Action_Shortcuts"),
        new("Ui_Pad_Input_Bumpers", "Ui_Pad_Action_SwitchTab"),
        new("Ui_Pad_Input_Triggers", "Ui_Pad_Action_Scroll"),
        new("Ui_Pad_Input_Back", "Ui_Pad_Action_ToggleSidebar"),
    ];
}
