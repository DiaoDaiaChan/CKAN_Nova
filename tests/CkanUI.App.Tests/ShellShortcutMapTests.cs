using Avalonia.Input;
using CkanUI.App.Input;
using NUnit.Framework;

namespace CkanUI.App.Tests;

/// <summary>
/// 外壳快捷键映射表（UI-12）：派发逻辑在 MainWindow 里，没法单测；这里锁住「哪个键 = 哪个语义」。
/// 新增键位先改这张表，再改 UI-12 的文档，避免文档与实现各说各话。
/// </summary>
[TestFixture]
public sealed class ShellShortcutMapTests
{
    [TestCase(Key.Tab, KeyModifiers.Control, ShellShortcutKind.NextTab)]
    [TestCase(Key.Tab, KeyModifiers.Control | KeyModifiers.Shift, ShellShortcutKind.PreviousTab)]
    [TestCase(Key.PageDown, KeyModifiers.Control, ShellShortcutKind.NextTab)]
    [TestCase(Key.PageUp, KeyModifiers.Control, ShellShortcutKind.PreviousTab)]
    [TestCase(Key.F, KeyModifiers.Control, ShellShortcutKind.FocusSearch)]
    [TestCase(Key.OemComma, KeyModifiers.Control, ShellShortcutKind.OpenSettings)]
    [TestCase(Key.Down, KeyModifiers.Control | KeyModifiers.Alt, ShellShortcutKind.NextRail)]
    [TestCase(Key.Up, KeyModifiers.Control | KeyModifiers.Alt, ShellShortcutKind.PreviousRail)]
    public void ControlShortcuts_MapToTheirSemanticAction(Key key, KeyModifiers modifiers, ShellShortcutKind expected)
    {
        var shortcut = ShellShortcutMap.Match(key, modifiers);

        Assert.That(shortcut.Kind, Is.EqualTo(expected));
        Assert.That(shortcut.Index, Is.EqualTo(0), "只有 Ctrl+数字才带下标。");
    }

    [TestCase(Key.D1, 1)]
    [TestCase(Key.D4, 4)]
    [TestCase(Key.D9, 9)]
    [TestCase(Key.NumPad2, 2)]
    public void ControlDigit_JumpsToThatSidebarEntry(Key key, int expected)
    {
        var shortcut = ShellShortcutMap.Match(key, KeyModifiers.Control);

        Assert.That(shortcut.Kind, Is.EqualTo(ShellShortcutKind.JumpRail));
        Assert.That(shortcut.Index, Is.EqualTo(expected));
    }

    [TestCase(Key.Tab, KeyModifiers.None)]
    [TestCase(Key.F, KeyModifiers.None)]
    [TestCase(Key.D1, KeyModifiers.None)]
    [TestCase(Key.K, KeyModifiers.Control)]
    [TestCase(Key.F, KeyModifiers.Control | KeyModifiers.Shift)]
    [TestCase(Key.Down, KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift)]
    public void OtherCombinations_AreNotShellShortcuts(Key key, KeyModifiers modifiers)
    {
        Assert.That(ShellShortcutMap.Match(key, modifiers).IsHit, Is.False);
    }
}
