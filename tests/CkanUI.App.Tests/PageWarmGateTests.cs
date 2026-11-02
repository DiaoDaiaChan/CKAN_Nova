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
        Assert.That(MainWindow.ShouldWarmExpensive(windowActive: false, TimeSpan.Zero), Is.True);
    }

    [Test]
    public void FocusedWindow_ShortIdle_DoesNotWarm()
    {
        Assert.That(MainWindow.ShouldWarmExpensive(windowActive: true, TimeSpan.FromSeconds(3)), Is.False);
    }

    [Test]
    public void FocusedWindow_LongIdle_Warms()
    {
        Assert.That(MainWindow.ShouldWarmExpensive(windowActive: true, TimeSpan.FromSeconds(60)), Is.True);
    }
}
