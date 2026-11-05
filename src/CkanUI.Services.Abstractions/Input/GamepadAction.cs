namespace CkanUI.Services.Abstractions.Input;

/// <summary>
/// 手柄手势被状态机翻译成的语义动作。视图模型与外壳只认这一层，
/// 换手柄、加体感、换成键盘映射都不用改派发方。
/// </summary>
public enum GamepadAction
{
    /// <summary>无动作（占位，派发方应跳过）。</summary>
    None = 0,

    /// <summary>焦点上移。</summary>
    FocusUp,

    /// <summary>焦点下移。</summary>
    FocusDown,

    /// <summary>焦点左移。</summary>
    FocusLeft,

    /// <summary>焦点右移。</summary>
    FocusRight,

    /// <summary>激活当前焦点（等价 Enter / 点击）。</summary>
    Accept,

    /// <summary>取消 / 返回（等价 Esc 或后退）。</summary>
    Cancel,

    /// <summary>上一个页签。</summary>
    PreviousTab,

    /// <summary>下一个页签。</summary>
    NextTab,

    /// <summary>打开命令面板。</summary>
    TogglePalette,

    /// <summary>打开快捷键目录。</summary>
    ShowShortcuts,

    /// <summary>向上翻一屏。</summary>
    ScrollUp,

    /// <summary>向下翻一屏。</summary>
    ScrollDown,

    /// <summary>折叠 / 展开侧栏。</summary>
    ToggleSidebar,
}
