using System;
using CkanUI.App;
using NUnit.Framework;

namespace CkanUI.App.Tests;

/// <summary>预热的时间窗判定：只认窗口在后台，前台一律不热（docs §49）。</summary>
[TestFixture]
public class PageWarmGateTests
{
    [Test]
    public void BackgroundWindow_AlwaysAllowsExpensiveWarm()
    {
        Assert.That(MainWindow.ShouldWarmExpensive(windowActive: false, isIdle: false, TimeSpan.Zero), Is.True);
    }

    [Test]
    public void FocusedWindow_ShortIdle_DoesNotWarm()
    {
        Assert.That(MainWindow.ShouldWarmExpensive(windowActive: true, isIdle: true, TimeSpan.FromSeconds(3)), Is.False);
    }

    [Test]
    public void FocusedWindow_LongIdle_StillDoesNotWarm()
    {
        // 整页排版是 UI 线程上的原子操作，用户在前台时无论静置多久都不该突然冻结一下。
        Assert.That(MainWindow.ShouldWarmExpensive(windowActive: true, isIdle: true, TimeSpan.FromSeconds(60)), Is.False);
    }

    [Test]
    public void BackgroundWindow_IgnoresIdleAndNavigation()
    {
        Assert.That(MainWindow.ShouldWarmExpensive(windowActive: false, isIdle: true, TimeSpan.FromDays(1)), Is.True);
    }

    [Test]
    public void FocusedWindow_LongIdleButUserStillTyping_DoesNotWarm()
    {
        // 用户一直在操作（键盘/鼠标），静置门虽然过了，但此刻插一次 1 秒冻结是纯伤害。
        Assert.That(MainWindow.ShouldWarmExpensive(windowActive: true, isIdle: false, TimeSpan.FromSeconds(600)), Is.False);
    }
}
