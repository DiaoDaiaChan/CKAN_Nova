using System;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Controls;

namespace CkanUI.App.Animations;

/// <summary>
/// 一次性物理反馈：校验报错的冲量轻颤、导航项的追赶位移。
/// </summary>
/// <remarks>
/// <para>两件事都交给 <see cref="TransformSpring"/> 的冲量通道：给弹簧一记初速度，
/// 之后由同一套帧驱动积分衰减。挂类 → 等一帧 → 摘类那套做法依赖"下一帧一定被渲染"，
/// 丢帧就会把冲量吞掉；而且打断时上一次的位移会被瞬间掐断。</para>
/// <para>冲量在当前位置叠加：连续触发时振幅会累加，不会把上一记打断。</para>
/// </remarks>
public static class MotionFeedback
{
    /// <summary>错误态类名：挂着它的控件带危险色描边，并拥有冲量弹簧过渡。</summary>
    public const string InvalidClass = "is-invalid";

    /// <summary>冲量类名：样式骨骼保留（<c>Transitions={x:Null}</c>），视觉不再由它驱动。</summary>
    public const string ImpulseClass = "shake-impulse";

    /// <summary>导航项「追赶」类名。</summary>
    public const string NavCatchUpClass = "nav-catch-up";

    /// <summary>导航项被选中时的追赶位移幅度（像素）。</summary>
    public const double NavPulseAmplitude = 6d;

    /// <summary>校验失败时的横向抖动幅度（像素）。</summary>
    public const double ShakeAmplitude = 10d;

    /// <summary>
    /// 导航项被选中时的追赶位移冲量（弹簧积分物理驱动）。
    /// </summary>
    public static void PulseNav(Control? target)
    {
        if (target is null || MotionBudget.IsCollapsed)
        {
            return;
        }

        TransformSpring.For(target, SpringSpec.Snappy).Kick(MotionBudget.Amplitude(NavPulseAmplitude));
    }

    /// <summary>
    /// 校验不通过时让某个输入控件左右轻颤一下，并留成错误态（危险色描边）。
    /// </summary>
    /// <param name="target">出错的控件；为空时直接返回（字段级反馈是增强，不是必需）。</param>
    /// <param name="cancellationToken">取消令牌；冲量本身是瞬时的，取消不会撤回已经给出的速度。</param>
    /// <returns>已完成的任务（保留异步签名，调用方无需改动）。</returns>
    public static Task ShakeAsync(Control? target, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (target is null)
        {
            return Task.CompletedTask;
        }

        target.Classes.Add(InvalidClass);

        if (!MotionBudget.IsCollapsed)
        {
            TransformSpring.For(target, SpringSpec.Impulse).Kick(MotionBudget.Amplitude(ShakeAmplitude));
        }

        return Task.CompletedTask;
    }

    /// <summary>清掉错误态（校验通过后调用）。</summary>
    /// <param name="target">要复位的控件。</param>
    public static void ClearInvalid(Control? target)
    {
        if (target is null)
        {
            return;
        }

        target.Classes.Remove(InvalidClass);
        target.Classes.Remove(ImpulseClass);
    }

}
