using System.Collections.Generic;
using System.Runtime.CompilerServices;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;

namespace CkanUI.App.Animations;

/// <summary>
/// 开关类控件的选中反馈：勾上时轻微放大再弹回，让"已选"这件事有手感。
/// </summary>
/// <remarks>
/// 只订阅 <see cref="ToggleButton.IsCheckedProperty"/>，不改控件模板；缩放走
/// <see cref="TransformSpring"/>，与按压反馈共用同一个弹簧实例，因此连点时
/// 目标值变化不会把动画打回原点。
/// </remarks>
public static class TogglePopMotion
{
    /// <summary>弹起的峰值缩放。</summary>
    public const double PopScale = 1.035d;

    private const int MaxDepth = 12;

    private const int MaxVisited = 3000;

    private static readonly ConditionalWeakTable<Control, object> Tracked = new();

    /// <summary>接管 <paramref name="root"/> 下所有 <see cref="ToggleButton"/> 的选中反馈；返回新接管数量。</summary>
    public static int Attach(Visual? root)
    {
        if (root is null || MotionBudget.IsCollapsed)
        {
            return 0;
        }

        var attached = 0;
        foreach (var toggle in Enumerate<ToggleButton>(root))
        {
            if (Tracked.TryGetValue(toggle, out _))
            {
                continue;
            }

            Tracked.Add(toggle, new object());
            toggle.PropertyChanged += OnTogglePropertyChanged;
            attached++;
        }

        return attached;
    }

    /// <summary>对单个开关播放一次弹起；关闭档直接返回。</summary>
    public static void Pop(Control control)
    {
        if (MotionBudget.IsCollapsed)
        {
            return;
        }

        // 冲量而不是"先设目标再设回来"：两次 MoveTo 在同一拍相遇时目标等于当前值，
        // 弹簧会判定已静止，观感上什么都没发生。
        TransformSpring.For(control, SpringSpec.Snappy).KickScale(MotionBudget.Amplitude(PopScale - 1d));
    }

    private static void OnTogglePropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (sender is not Control control
            || args.Property != ToggleButton.IsCheckedProperty
            || args.NewValue is not true)
        {
            return;
        }

        Pop(control);
    }

    private static IEnumerable<TControl> Enumerate<TControl>(Visual root)
        where TControl : Control
    {
        var queue = new Queue<Visual>();
        queue.Enqueue(root);
        var visited = 0;

        for (var depth = 0; depth < MaxDepth && queue.Count > 0; depth++)
        {
            var count = queue.Count;
            for (var index = 0; index < count; index++)
            {
                var node = queue.Dequeue();
                if (node is TControl match)
                {
                    yield return match;
                }

                foreach (var child in node.GetVisualChildren())
                {
                    if (++visited > MaxVisited)
                    {
                        yield break;
                    }

                    queue.Enqueue(child);
                }
            }
        }
    }
}