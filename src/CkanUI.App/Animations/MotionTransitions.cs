using System.Collections.Generic;
using System.Reflection;

using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;

namespace CkanUI.App.Animations;

/// <summary>
/// 接管一个控件之前只摘掉会和逐帧写入打架的那几条过渡，其余过渡原样保留。
/// </summary>
/// <remarks>
/// <para>直接 `control.Transitions = null` 下手最省事，但它会连背景色/前景色的淡入一起废掉——
/// 悬停时按钮底色不再渐变，只剩弹簧在动，观感反而更"硬"。这里按属性精确摘除。</para>
/// <para>必须摘的原因：Avalonia 的过渡在**动画优先级**上写属性，高于代码写的本地值。只要
/// `RenderTransform` 的过渡还在跑（90–360 ms），它每帧都会把插值出来的 `TransformOperations` 盖在我们的
/// `TransformGroup` 上，弹簧要么看不见、要么和过渡各写一半——这就是"有时候弹得起来、有时候只动一点"的根因。</para>
/// <para>`ITransition.Property` 在 12.1.4 上不对外公开，只能反射读；一次接管读一次，不进逐帧路径。</para>
/// </remarks>
public static class MotionTransitions
{
    private static readonly PropertyInfo? PropertyMember =
        typeof(TransitionBase).GetProperty(
            "Property",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    /// <summary>摘掉 `RenderTransform` 的过渡；<paramref name="includeOpacity"/> 为真时连 `Opacity` 一起摘。</summary>
    /// <param name="control">即将被逐帧驱动的控件。</param>
    /// <param name="includeOpacity">该模块是否也在逐帧写 `Opacity`（写了就必须摘，否则淡入会被再平滑一次）。</param>
    public static void Strip(Control control, bool includeOpacity)
    {
        if (control.Transitions is not { Count: > 0 } transitions)
        {
            return;
        }

        var kept = new List<ITransition>(transitions.Count);
        var removed = false;

        foreach (var transition in transitions)
        {
            var property = PropertyOf(transition);

            if (property == Visual.RenderTransformProperty
                || (includeOpacity && property == Visual.OpacityProperty))
            {
                removed = true;
                continue;
            }

            kept.Add(transition);
        }

        if (!removed)
        {
            return;
        }

        if (kept.Count == 0)
        {
            control.Transitions = null;
            return;
        }

        var replacement = new Transitions();
        foreach (var transition in kept)
        {
            replacement.Add(transition);
        }

        control.Transitions = replacement;
    }

    private static AvaloniaProperty? PropertyOf(ITransition transition) =>
        PropertyMember?.GetValue(transition) as AvaloniaProperty;
}
