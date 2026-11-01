using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace CkanUI.App.Animations;

/// <summary>
/// 迟到的控件也要被接管：给根节点挂一个节流的 `LayoutUpdated` 补扫，把各动效模块的 `Attach` 反复跑一遍。
/// </summary>
/// <remarks>
/// <para>所有 `Attach` 都是幂等的（每个控件各自登记在 `ConditionalWeakTable` 里），所以补扫只会捡到新控件，
/// 不会给老控件重复订阅。换页那一拍只扫一次是不够的：页面模板、数据列表、浮层、空态与数据态互斥切换
/// 都可能在之后才把控件挂上树，只赌第一拍就会"有的页面有动画、有的没有"。</para>
/// <para>节流 4 Hz 而不是逐次布局回调：这里是覆盖兜底，不是逐帧动画；滚动时 `LayoutUpdated` 一帧能来好几次，
/// 每次全树扫一遍会拖慢滚动本身（§5.9.18 同一个理由）。</para>
/// </remarks>
internal static class MotionScan
{
    /// <summary>补扫频率（每秒次数）。</summary>
    internal const double RescanHz = 4d;

    private static readonly ConditionalWeakTable<Visual, Submission> Submissions = new();

    /// <summary>登记一个补扫动作；同一根节点 + 同一 <paramref name="key"/> 只登记一次。</summary>
    /// <param name="root">要观察的根（通常是 `PageHost` 或对话框）。</param>
    /// <param name="key">模块名，用来去重。</param>
    /// <param name="scan">补扫动作，通常是"再调一次自己的 Attach"。</param>
    internal static void Subscribe(Visual? root, string key, Action scan)
    {
        if (root is not Layoutable layoutable || scan is null)
        {
            return;
        }

        var submission = Submissions.GetValue(layoutable, static node => new Submission((Layoutable)node));
        submission.Add(key, scan);
    }

    private sealed class Submission
    {
        private readonly HashSet<string> _keys = new(StringComparer.Ordinal);

        private readonly List<Action> _scans = [];

        private readonly long _interval = (long)(Stopwatch.Frequency / RescanHz);

        private long _last;

        internal Submission(Layoutable root) => root.LayoutUpdated += OnLayoutUpdated;

        internal void Add(string key, Action scan)
        {
            if (_keys.Add(key))
            {
                _scans.Add(scan);
            }
        }

        private void OnLayoutUpdated(object? sender, EventArgs e)
        {
            var now = Stopwatch.GetTimestamp();
            if (now - _last < _interval)
            {
                return;
            }

            _last = now;
            foreach (var scan in _scans)
            {
                scan();
            }
        }
    }
}
