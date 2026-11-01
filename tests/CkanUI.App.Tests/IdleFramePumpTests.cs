using CkanUI.App.Diagnostics;
using NUnit.Framework;

namespace CkanUI.App.Tests;

/// <summary>诊断帧泵的不变量：空闲期不许自己再排渲染帧。</summary>
/// <remarks>
/// 这条曾经是最贵的一个 bug —— RAF 循环在空闲期也按 10Hz 排帧，等于把整窗重绘次数翻一倍，
/// 而画面上什么都没有多出来。决策抽成纯函数守住：改 RequestNextFrame 先过这一关。
/// </remarks>
[TestFixture]
public sealed class IdleFramePumpTests
{
    [Test]
    public void ShouldRequestFrame_Idle_ReturnsFalse() =>
        Assert.That(PerformanceMonitor.ShouldRequestFrame(isSampling: true, hasTopLevel: true, isIdle: true), Is.False);

    [Test]
    public void ShouldRequestFrame_Active_ReturnsTrue() =>
        Assert.That(PerformanceMonitor.ShouldRequestFrame(isSampling: true, hasTopLevel: true, isIdle: false), Is.True);

    [Test]
    public void ShouldRequestFrame_NotSampling_ReturnsFalse() =>
        Assert.That(PerformanceMonitor.ShouldRequestFrame(isSampling: false, hasTopLevel: true, isIdle: false), Is.False);

    [Test]
    public void ShouldRequestFrame_NoTopLevel_ReturnsFalse() =>
        Assert.That(PerformanceMonitor.ShouldRequestFrame(isSampling: true, hasTopLevel: false, isIdle: false), Is.False);
}
