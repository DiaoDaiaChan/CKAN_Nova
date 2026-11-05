using CkanUI.App;
using NUnit.Framework;

namespace CkanUI.App.Tests;

/// <summary>
/// 弹层宿主契约：弹层必须嵌进窗口自身的 Overlay 层（<c>Win32PlatformOptions.OverlayPopups</c>）。
/// 一旦退回「每个弹层开一个顶层子窗口」，Windows 的 Z 序会让子窗口恒在主窗口之上：
/// 窗口内任何图层——包括 ZIndex = int.MaxValue 的自绘光标层——都压不住它，光标就会被下拉列表、
/// 右键菜单、Flyout 挡住。这条契约是光标永远在最前的唯一保障，改平台选项前先看这里。
/// </summary>
[TestFixture]
public sealed class PopupHostingTests
{
    [Test]
    public void Popups_AreHostedInsideTheWindowOverlayLayer()
    {
        Assert.That(Program.CreateWin32PlatformOptions().OverlayPopups, Is.True,
            "弹层必须嵌进窗口 Overlay 层，否则弹层子窗口会压在自绘光标之上");
    }
}
