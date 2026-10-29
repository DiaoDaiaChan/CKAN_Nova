using System;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Controls;

namespace CkanUI.App.Animations;

/// <summary>
/// 一次性物理反馈（第三阶段 MAT-04 / MAT-05）：校验报错的冲量轻颤、导航项的追赶位移。
/// </summary>
/// <remarks>
/// <para>两件事都靠「挂一个状态类 → 等一帧 → 摘掉」实现：挂上的瞬间过渡被 <c>x:Null</c> 禁用（瞬间到位），
/// 摘掉的瞬间弹簧过渡重新生效（从当前偏离量释放冲量）。整个衰减震荡由 SpringEasing 求解，
/// 不需要任何关键帧，也不需要每帧回调。</para>
/// <para>挂类必须发生在 UI 线程；本类不做跨线程封送，调用方（视图代码后置）本来就跑在 UI 线程上。</para>
/// </remarks>
public static class MotionFeedback
{
    /// <summary>错误态类名：挂着它的控件带危险色描边，并拥有冲量弹簧过渡。</summary>
    public const string InvalidClass = "is-invalid";

    /// <summary>冲量类名：挂着它的一帧内控件被拉偏 8px，且禁用过渡。</summary>
    public const string ImpulseClass = "shake-impulse";

    /// <summary>导航项「追赶」类名。</summary>
    public const string NavCatchUpClass = "nav-catch-up";

    /// <summary>等一帧：让「拉偏」先被渲染树记录成起点，摘类时才有得插值。</summary>
    public static readonly TimeSpan Frame = TimeSpan.FromMilliseconds(16);

    /// <summary>导航项追赶位移的持续时长（略微长于弹簧的一段冲击，之后自然归位）。</summary>
    public static readonly TimeSpan NavCatchUpHold = TimeSpan.FromMilliseconds(110);

    /// <summary>
    /// 校验不通过时让某个输入控件左右轻颤一下，并留成错误态（危险色描边）。
    /// </summary>
    /// <param name="target">出错的控件；为空时直接返回（字段级反馈是增强，不是必需）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>抖动释放后完成的任务。</returns>
    public static async Task ShakeAsync(Control? target, CancellationToken cancellationToken = default)
    {
        if (target is null)
        {
            return;
        }

        target.Classes.Add(InvalidClass);

        try
        {
            target.Classes.Add(ImpulseClass);
            await Task.Delay(Frame, cancellationToken).ConfigureAwait(true);
            target.Classes.Remove(ImpulseClass);
        }
        catch (OperationCanceledException)
        {
            // 被新一次校验打断：仍然要把冲量类摘掉，否则控件会一直偏向 8px 处。
            target.Classes.Remove(ImpulseClass);
        }
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
