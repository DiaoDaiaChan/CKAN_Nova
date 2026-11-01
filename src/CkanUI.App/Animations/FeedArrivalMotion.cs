using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Runtime.CompilerServices;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace CkanUI.App.Animations;

/// <summary>
/// 数据到达入场：列表在换页之后才拿到数据时，行分批浮上来，而不是整块"啪"地出现。
/// </summary>
/// <remarks>
/// <para>页面入场只编排换页那一刻已经存在的元素；真实页面的列表通常是异步加载的，
/// 入场演完、数据才到，行是硬出现的。这里订阅 <see cref="ItemsControl"/> 的数据源增删，
/// 数据一次到齐就按 10 行一批、24 ms 间隔错峰入场。</para>
/// <para>只演新实现出来的容器：容器演过就记进 <see cref="Seen"/>，后续筛选/追加不会让整页重演。</para>
/// </remarks>
public static class FeedArrivalMotion
{
    /// <summary>一次到达最多编排多少行（超出的直接显示）。</summary>
    public const int MaxItems = 10;

    /// <summary>行间间隔（完整档，毫秒）。</summary>
    public const int StepMilliseconds = 24;

    /// <summary>起点下移距离（完整档，像素）。</summary>
    public const double OffsetPixels = 14d;

    /// <summary>起点下移距离（精简档，像素）。</summary>
    public const double ReducedOffsetPixels = 4d;

    private const int MaxDepth = 10;

    private const int MaxVisited = 2400;

    private static readonly ConditionalWeakTable<ItemsControl, State> States = new();

    private static readonly ConditionalWeakTable<Control, object> Seen = new();

    /// <summary>给 <paramref name="root"/> 下的每个数据项容器挂上数据到达入场；返回本次新增接管数量。</summary>
    public static int Attach(Visual? root)
    {
        if (root is null || MotionBudget.IsCollapsed)
        {
            return 0;
        }

        var count = 0;
        foreach (var itemsControl in Enumerate<ItemsControl>(root))
        {
            if (itemsControl is ComboBox || States.TryGetValue(itemsControl, out _))
            {
                continue;
            }

            State? state = null;
            state = new State(() => Schedule(itemsControl, state!));
            States.Add(itemsControl, state);
            itemsControl.PropertyChanged += (_, e) =>
            {
                if (e.Property == ItemsControl.ItemsSourceProperty)
                {
                    Rebind(itemsControl, state);
                }
            };
            Rebind(itemsControl, state);
            count++;
        }

        return count;
    }

    private static void Rebind(ItemsControl itemsControl, State state)
    {
        if (state.Source is INotifyCollectionChanged previous)
        {
            previous.CollectionChanged -= state.Handler;
        }

        state.Source = itemsControl.ItemsSource as INotifyCollectionChanged;
        if (state.Source is null)
        {
            return;
        }

        state.Source.CollectionChanged += state.Handler;
    }

    private static void Schedule(ItemsControl itemsControl, State state)
    {
        if (state.Pending)
        {
            return;
        }

        state.Pending = true;
        state.Retries = 0;
        Dispatcher.UIThread.Post(() => Drain(itemsControl, state), DispatcherPriority.Loaded);
    }

    private static void Drain(ItemsControl itemsControl, State state)
    {
        state.Pending = false;
        if (MotionBudget.IsCollapsed || !itemsControl.IsEffectivelyVisible)
        {
            return;
        }

        var played = Play(itemsControl);
        if (played == 0 && state.Retries == 0 && itemsControl.ItemCount > 0)
        {
            // 数据先到、容器后实现：等一次布局再来。
            state.Retries++;
            state.Pending = true;
            Dispatcher.UIThread.Post(() => Drain(itemsControl, state), DispatcherPriority.Loaded);
        }
    }

    /// <summary>对当前已经实现出来的行跑一遍错峰入场，返回编排数量。</summary>
    public static int Play(ItemsControl itemsControl)
    {
        if (MotionBudget.IsCollapsed)
        {
            return 0;
        }

        var full = MotionBudget.AllowsPageChoreography;
        var offset = MotionBudget.Amplitude(full ? OffsetPixels : ReducedOffsetPixels);
        var step = full ? StepMilliseconds / 1000d : 0d;
        var index = 0;

        for (var i = 0; i < itemsControl.ItemCount && index < MaxItems; i++)
        {
            if (itemsControl.ContainerFromIndex(i) is not Control container || !container.IsVisible)
            {
                continue;
            }

            if (Seen.TryGetValue(container, out _))
            {
                continue;
            }

            Seen.Add(container, container);
            var rest = container.Opacity > 0d ? container.Opacity : 1d;
            var spring = TransformSpring.For(container, SpringSpec.Glide);
            spring.MoveTo(y: offset, opacity: 0d, snap: true);
            spring.MoveTo(y: 0d, opacity: rest, hold: index * step);
            index++;
        }

        return index;
    }

    private static IEnumerable<T> Enumerate<T>(Visual root)
        where T : Visual
    {
        var queue = new Queue<(Visual Node, int Depth)>();
        queue.Enqueue((root, 0));
        var visited = 0;

        while (queue.Count > 0 && visited < MaxVisited)
        {
            var (node, depth) = queue.Dequeue();
            visited++;

            if (node is T match)
            {
                yield return match;
            }

            if (depth >= MaxDepth)
            {
                continue;
            }

            foreach (var child in node.GetVisualChildren())
            {
                queue.Enqueue((child, depth + 1));
            }
        }
    }

    private sealed class State
    {
        public INotifyCollectionChanged? Source;

        public bool Pending;

        public int Retries;

        public NotifyCollectionChangedEventHandler Handler { get; }

        public State(Action onChanged)
        {
            Handler = (_, _) => onChanged();
        }
    }
}