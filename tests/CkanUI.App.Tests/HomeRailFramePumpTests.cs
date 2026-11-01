using CkanUI.App.Views;
using NUnit.Framework;

namespace CkanUI.App.Tests;

/// <summary>
/// 首页胶片带定时器的运行不变量：被观看时照旧 60 fps，没人看的时候必须停。
/// </summary>
[TestFixture]
public class HomeRailFramePumpTests
{
    [Test]
    public void AttachedVisibleAndActive_Runs()
        => Assert.That(HomePageView.ShouldRunRail(attached: true, isVisible: true, windowIsActive: true), Is.True);

    [Test]
    public void Detached_Stops()
        => Assert.That(HomePageView.ShouldRunRail(attached: false, isVisible: true, windowIsActive: true), Is.False);

    [Test]
    public void Invisible_Stops()
        => Assert.That(HomePageView.ShouldRunRail(attached: true, isVisible: false, windowIsActive: true), Is.False);

    [Test]
    public void WindowInactive_Stops()
        => Assert.That(HomePageView.ShouldRunRail(attached: true, isVisible: true, windowIsActive: false), Is.False);

    [Test]
    public void UnknownWindowState_KeepsRunning()
        => Assert.That(HomePageView.ShouldRunRail(attached: true, isVisible: true, windowIsActive: null), Is.True);
}
