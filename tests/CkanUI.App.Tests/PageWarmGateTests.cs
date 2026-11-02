using System;
using CkanUI.App;
using NUnit.Framework;

namespace CkanUI.App.Tests;

/// <summary>重页面预热的时间窗判定：窗口前台时不热，后台或长静置才热。</summary>
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
    public void FocusedWindow_LongIdle_Warms()
    {
        Assert.That(MainWindow.ShouldWarmExpensive(windowActive: true, isIdle: true, TimeSpan.FromSeconds(60)), Is.True);
    }

    [Test]
    public void FocusedWindow_LongIdleButUserStillTyping_DoesNotWarm()
    {
        // 用户一直在操作（键盘/鼠标），静置门虽然过了，但此刻插一次 1 秒冻结是纯伤害。
        Assert.That(MainWindow.ShouldWarmExpensive(windowActive: true, isIdle: false, TimeSpan.FromSeconds(600)), Is.False);
    }
}
