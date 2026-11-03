using CkanUI.App;
using CkanUI.App.Tests.Automation;
using NUnit.Framework;

namespace CkanUI.App.Tests;

/// <summary>
/// 置顶光标覆盖窗口的接线约束：没启用时必须回落到主窗口内的光标层，
/// 窗口还没显示时启用必须是安全空操作。真实窗口下的"覆盖 PopupRoot"由 Win32 探针单独验证。
/// </summary>
[TestFixture]
public sealed class CursorOverlayTests
{
    [Test]
    public void ActiveCursor_FallsBackToTheInWindowLayerBeforeOverlayIsEnabled()
    {
        HeadlessAppHost.EnsureInitialized();

        var window = new MainWindow();
        window.Show();
        HeadlessAppHost.PumpJobs();
        try
        {
            Assert.That(window.IsCursorOverlayEnabled, Is.False);
            Assert.That(window.ActiveCursor, Is.SameAs(window.CursorPointer));
            Assert.That(window.ActiveCursor, Is.Not.Null, "主窗口必须挂了光标层，否则光标完全不画");
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    [Test]
    public void EnableCursorOverlay_IsHiddenSafeNoOpBeforeTheWindowIsVisible()
    {
        HeadlessAppHost.EnsureInitialized();

        var window = new MainWindow();
        Assert.DoesNotThrow(() => window.EnableCursorOverlay());
        Assert.That(window.IsCursorOverlayEnabled, Is.False, "窗口没显示前不该建覆盖窗口");
        Assert.That(window.ActiveCursor, Is.SameAs(window.CursorPointer));
    }
}
