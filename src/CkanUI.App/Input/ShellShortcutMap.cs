using Avalonia.Input;

namespace CkanUI.App.Input;

/// <summary>外壳级快捷键的语义动作（不含按键本身，方便单测与后续改键）。</summary>
public enum ShellShortcutKind
{
    /// <summary>没有命中任何外壳快捷键。</summary>
    None = 0,

    /// <summary>切到下一个页签（分节内的 tab）。</summary>
    NextTab,

    /// <summary>切到上一个页签。</summary>
    PreviousTab,

    /// <summary>聚焦当前页的搜索框。</summary>
    FocusSearch,

    /// <summary>跳到设置页。</summary>
    OpenSettings,

    /// <summary>侧栏下一个可导航项。</summary>
    NextRail,

    /// <summary>侧栏上一个可导航项。</summary>
    PreviousRail,

    /// <summary>侧栏第 N 项（1 起）。</summary>
    JumpRail,
}

/// <summary>一次快捷键命中结果：动作 + 可选下标（只有 <see cref="ShellShortcutKind.JumpRail"/> 用）。</summary>
/// <param name="Kind">命中的动作。</param>
/// <param name="Index">目标下标，1 起；其他动作为 0。</param>
public readonly record struct ShellShortcut(ShellShortcutKind Kind, int Index = 0)
{
    /// <summary>未命中。</summary>
    public static ShellShortcut None => default;

    /// <summary>是否命中。</summary>
    public bool IsHit => Kind != ShellShortcutKind.None;
}

/// <summary>
/// 外壳快捷键映射表：把 <see cref="Key"/> + <see cref="KeyModifiers"/> 翻译成语义动作。
/// </summary>
/// <remarks>
/// <para>抽成纯函数是为了可测——<c>MainWindow</c> 里的键处理没法单测，映射表可以。</para>
/// <para>键位与 <c>docs/plan/03-ui-spec.md</c> §9 不冲突：那里定义的 Ctrl+F / Ctrl+R / Ctrl+Enter 属于页面级，
/// 本表只声明 Ctrl+F 的「聚焦搜索」语义，由外壳在冒泡阶段兜底（页面先上手就轮不到外壳）。</para>
/// </remarks>
public static class ShellShortcutMap
{
    /// <summary>翻译一次按键；未命中返回 <see cref="ShellShortcut.None"/>。</summary>
    public static ShellShortcut Match(Key key, KeyModifiers modifiers)
    {
        // 外壳快捷键一律要 Ctrl：不带修饰键的按键留给输入框和页面自己，免得敲字母就切页签。
        if ((modifiers & KeyModifiers.Control) == 0)
        {
            return ShellShortcut.None;
        }

        var shift = (modifiers & KeyModifiers.Shift) != 0;

        if ((modifiers & KeyModifiers.Alt) != 0)
        {
            // Alt+Ctrl+方向键给侧栏翻页；Alt 系其余组合不归外壳管。
            if (shift)
            {
                return ShellShortcut.None;
            }

            return key switch
            {
                Key.Up => new ShellShortcut(ShellShortcutKind.PreviousRail),
                Key.Down => new ShellShortcut(ShellShortcutKind.NextRail),
                _ => ShellShortcut.None,
            };
        }

        switch (key)
        {
            case Key.Tab:
                return new ShellShortcut(shift ? ShellShortcutKind.PreviousTab : ShellShortcutKind.NextTab);
            case Key.PageUp:
                return new ShellShortcut(ShellShortcutKind.PreviousTab);
            case Key.PageDown:
                return new ShellShortcut(ShellShortcutKind.NextTab);
            case Key.F when !shift:
                return new ShellShortcut(ShellShortcutKind.FocusSearch);
            case Key.OemComma when !shift:
                return new ShellShortcut(ShellShortcutKind.OpenSettings);
        }

        if (shift)
        {
            return ShellShortcut.None;
        }

        var railIndex = RailIndexOf(key);
        return railIndex > 0
            ? new ShellShortcut(ShellShortcutKind.JumpRail, railIndex)
            : ShellShortcut.None;
    }

    /// <summary>Ctrl+1..Ctrl+9 / Ctrl+小键盘 1..9 → 侧栏第 N 项；显式列全，不依赖枚举值连续。</summary>
    private static int RailIndexOf(Key key) => key switch
    {
        Key.D1 or Key.NumPad1 => 1,
        Key.D2 or Key.NumPad2 => 2,
        Key.D3 or Key.NumPad3 => 3,
        Key.D4 or Key.NumPad4 => 4,
        Key.D5 or Key.NumPad5 => 5,
        Key.D6 or Key.NumPad6 => 6,
        Key.D7 or Key.NumPad7 => 7,
        Key.D8 or Key.NumPad8 => 8,
        Key.D9 or Key.NumPad9 => 9,
        _ => 0,
    };
}
