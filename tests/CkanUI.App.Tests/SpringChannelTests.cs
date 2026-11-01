using System;

using CkanUI.App.Animations;

using NUnit.Framework;

namespace CkanUI.App.Tests;

/// <summary>
/// 弹簧通道的物理判据：打断连续性、过冲、收敛、冲量峰值、帧率无关、hold 排队。
/// </summary>
[TestFixture]
public sealed class SpringChannelTests
{
    private const double Frame = 1.0 / 60.0;

    [Test]
    public void Retarget_KeepsPositionAndVelocity()
    {
        var channel = new SpringChannel(0, SpringSpec.Glide, 0.001, 0.001);
        for (var i = 0; i < 6; i++)
        {
            channel.Advance(Frame);
        }

        var position = channel.Value;
        var velocity = channel.Velocity;
        channel.Retarget(300);

        Assert.That(channel.Value, Is.EqualTo(position).Within(1e-9), "换目标不能瞬移");
        Assert.That(channel.Velocity, Is.EqualTo(velocity).Within(1e-9), "换目标不能清速度");
    }

    [Test]
    public void InterruptedMotion_HasNoPerFrameTeleport()
    {
        var channel = new SpringChannel(0, SpringSpec.Glide, 0.001, 0.001);
        channel.Retarget(400);
        for (var i = 0; i < 8; i++)
        {
            channel.Advance(Frame);
        }

        channel.Retarget(0);
        var gap = Math.Abs(channel.Value - channel.Target);
        var previous = channel.Value;
        var maxStep = 0d;
        var moving = 0;

        for (var i = 0; i < 40; i++)
        {
            if (!channel.Advance(Frame))
            {
                break;
            }

            var step = Math.Abs(channel.Value - previous);
            maxStep = Math.Max(maxStep, step);
            previous = channel.Value;
            if (step > 0.01)
            {
                moving++;
            }
        }

        // 每帧步长的绝对值随速度变化，判跳变要看它相对"待补距离"的比例。
        Assert.That(maxStep, Is.LessThan(gap * 0.25), "中断后不能出现瞬移式步长");
        Assert.That(moving, Is.GreaterThan(10), "打断后必须还在动，不能直接冻结");
    }

    [Test]
    public void Glide_DoesNotOvershoot()
    {
        var channel = new SpringChannel(0, SpringSpec.Glide, 0.001, 0.001);
        channel.Retarget(300);
        var peak = 0d;
        for (var i = 0; i < 240; i++)
        {
            channel.Advance(Frame);
            peak = Math.Max(peak, channel.Value);
        }

        Assert.That(peak, Is.LessThanOrEqualTo(300.5), "临界阻尼档不允许过冲");
        Assert.That(channel.Value, Is.EqualTo(300).Within(0.01));
    }

    [Test]
    public void Snappy_OvershootsThenSettles()
    {
        var channel = new SpringChannel(0, SpringSpec.Snappy, 0.001, 0.001);
        channel.Retarget(300);
        var peak = 0d;
        for (var i = 0; i < 240; i++)
        {
            channel.Advance(Frame);
            peak = Math.Max(peak, channel.Value);
        }

        Assert.That(peak, Is.InRange(305d, 315d), "回弹档要有约 3% 的过冲");
        Assert.That(channel.Value, Is.EqualTo(300).Within(0.01));
    }

    [Test]
    public void Motion_SettlesWithinHalfASecond()
    {
        var channel = new SpringChannel(0, SpringSpec.Glide, 0.25, 4);
        channel.Retarget(200);
        var elapsed = 0d;
        while (channel.IsMoving && elapsed < 2)
        {
            channel.Advance(Frame);
            elapsed += Frame;
        }

        Assert.That(channel.IsMoving, Is.False);
        Assert.That(elapsed, Is.LessThan(0.5), "落位时间要短于半秒");
        Assert.That(channel.Value, Is.EqualTo(200).Within(1e-9));
    }

    [Test]
    public void Kick_PeakMatchesRequestedAmplitude()
    {
        var channel = new SpringChannel(0, SpringSpec.Impulse, 0.001, 0.001);
        channel.Kick(8);
        var peak = 0d;
        for (var i = 0; i < 240; i++)
        {
            channel.Advance(Frame);
            peak = Math.Max(peak, Math.Abs(channel.Value));
        }

        Assert.That(peak, Is.EqualTo(8).Within(0.4), "冲量峰值要等于设计幅度");
        Assert.That(channel.Value, Is.EqualTo(0).Within(0.01));
    }

    [Test]
    public void RepeatedKick_AccumulatesVelocityInsteadOfRestarting()
    {
        var channel = new SpringChannel(0, SpringSpec.Impulse, 0.001, 0.001);
        channel.Kick(8);
        channel.Advance(Frame);
        var first = channel.Velocity;
        channel.Kick(8);

        Assert.That(channel.Velocity, Is.GreaterThan(first), "连击要在当前速度上叠加");
    }

    [Test]
    public void SnapTo_StopsImmediately()
    {
        var channel = new SpringChannel(0, SpringSpec.Glide, 0.001, 0.001);
        channel.Retarget(120);
        channel.Advance(Frame);
        channel.SnapTo(50);

        Assert.That(channel.Value, Is.EqualTo(50).Within(1e-9));
        Assert.That(channel.Velocity, Is.EqualTo(0).Within(1e-9));
        Assert.That(channel.IsMoving, Is.False);
    }

    [Test]
    public void Advance_IsRefreshRateIndependent()
    {
        var at60 = new SpringChannel(0, SpringSpec.Snappy, 0.001, 0.001);
        var at240 = new SpringChannel(0, SpringSpec.Snappy, 0.001, 0.001);
        at60.Retarget(240);
        at240.Retarget(240);

        for (var i = 0; i < 240; i++)
        {
            at240.Advance(1.0 / 240.0);
        }

        for (var i = 0; i < 60; i++)
        {
            at60.Advance(1.0 / 60.0);
        }

        Assert.That(at60.Value, Is.EqualTo(at240.Value).Within(1e-6), "同一时刻的位置不能取决于刷新率");
    }

    [Test]
    public void Hold_DelaysDepartureWithoutATimer()
    {
        var channel = new SpringChannel(0, SpringSpec.Glide, 0.001, 0.001);
        channel.Retarget(100, 0.05);
        for (var i = 0; i < 3; i++)
        {
            channel.Advance(Frame);
        }

        Assert.That(channel.Value, Is.EqualTo(0).Within(1e-9), "排队期间不能提前出发");
        Assert.That(channel.Velocity, Is.EqualTo(0).Within(1e-9));
        Assert.That(channel.IsHeld, Is.True);
        Assert.That(channel.IsMoving, Is.True, "排队也算在运动，帧泵不能停");

        for (var i = 0; i < 4; i++)
        {
            channel.Advance(Frame);
        }

        Assert.That(channel.IsHeld, Is.False);
        Assert.That(channel.Value, Is.GreaterThan(0), "排队结束后必须真的出发");
    }

    [Test]
    public void RetargetWithoutHold_ClearsTheQueue()
    {
        var channel = new SpringChannel(0, SpringSpec.Glide, 0.001, 0.001);
        channel.Retarget(100, 0.2);
        channel.Advance(Frame);
        channel.Retarget(0);
        channel.Advance(Frame);

        Assert.That(channel.IsHeld, Is.False);
        Assert.That(channel.Value, Is.EqualTo(0).Within(1e-9));
    }
}
