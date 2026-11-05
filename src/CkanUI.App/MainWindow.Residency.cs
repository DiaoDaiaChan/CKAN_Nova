using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using CkanUI.App.Diagnostics;
using CkanUI.ViewModels;
using CkanUI.ViewModels.Pages.Instances;

namespace CkanUI.App;

/// <summary>
/// 页面驻留预算：把长时间没被看过的页面视图摘出舞台、丢掉视图缓存，让这部分内存真的回到托管堆。
/// </summary>
/// <remarks>
/// <para>
/// 宿主的换页模型是「视图只建一次、之后常驻 <c>PageStage</c>，换页只切可见性」（见 ShowPage 的说明），
/// 换来的是换页零重建，代价是每个页面树都带着自己的样式实例、排版缓存和资源表达式活在可视树里。
/// 实测（空实例、32 键逐页扫）每页边际成本 ≈ 14MB 工作集 / 8.7MB 活堆，32 页就是四百多 MB。
/// </para>
/// <para>
/// 这里不去改那条换页路径（它正被另一条并行任务改动），只是给它加一个**旁路**：
/// 摘除动作严格照抄 <c>PruneRemovedPageViews</c> 的三步曲（_pageViews / _pageViewKeys / PageStage.Children），
/// 外加丢掉 <see cref="ViewLocator"/> 的视图缓存。被摘掉的页面下次进入会走 ShowPage 的「没建过」分支，
/// 于是自动重建视图、重新注入 TextPrompt，行为与首次进入一致（代价是那一次进入要重新加载 XAML）。
/// </para>
/// <para>
/// 什么时候跑：由 <see cref="PageResidencyPolicy"/> 挂在空闲回收器的节拍上，只在「用户已经空闲、
/// 且距上次换页超过阈值」时动手，活跃换页期间完全不碰，因此不影响换页流畅度。
/// </para>
/// </remarks>
public sealed partial class MainWindow
{
    /// <summary>页面视图的最近使用序号：越大越新，只用于挑「最久没看」的那几页。</summary>
    private readonly Dictionary<Control, long> _pageTouches = new(ReferenceEqualityComparer.Instance);

    /// <summary>真的被用户看到过的页面视图。预热建出来的那些从不进这里，是「未访问」判定的唯一依据。</summary>
    private readonly HashSet<Control> _pageVisited = new(ReferenceEqualityComparer.Instance);

    private long _pageTouchSeq;

    /// <summary>上一次换页的时刻（由 ShowPage 维护），驻留预算用它判断「用户已经静置多久」。</summary>
    internal DateTime LastPageSwitchUtc => _lastNavigationUtc;

    /// <summary>当前保活表里的页面视图数（诊断用）。</summary>
    internal int PageViewCount => _pageViews.Count;

    /// <summary>配置的保留页数；0 表示驻留预算没开（诊断用）。</summary>
    internal int PageResidencyKeep => PageResidencyPolicy.Keep;

    /// <summary>某个导航键对应的页面视图此刻是否还在舞台上（诊断用：演练要挑真被摘掉的那些页）。</summary>
    internal bool IsResidentKey(string key)
    {
        foreach (var value in _pageViewKeys.Values)
        {
            if (string.Equals(value, key, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 按最近使用把超预算的页面视图摘出舞台。
    /// </summary>
    /// <param name="keep">保留的页面数（含当前活动页）；小于 1 按 1 处理。</param>
    /// <returns>本次摘掉的页面数。</returns>
    internal int TrimPageResidency(int keep)
    {
        if (keep < 1)
        {
            keep = 1;
        }

        if (PageStage is null || _pageViews.Count == 0)
        {
            return 0;
        }

        TrackStageViews();

        if (_pageViews.Count <= keep)
        {
            return 0;
        }

        var active = _activePageView;
        var victims = _pageViews
            .Where(pair => !ReferenceEquals(pair.Value, active))
            .OrderBy(pair => _pageTouches.TryGetValue(pair.Value, out var touch) ? touch : 0L)
            .Take(_pageViews.Count - keep)
            .ToList();

        foreach (var victim in victims)
        {
            EvictPageView(victim.Key, victim.Value);
        }

        if (victims.Count > 0)
        {
            // 刚摘下来的控件此刻还是垃圾不是空闲内存：顺手压一次，用户此刻本来就闲着，压缩看不见。
            IdleMemoryReclaimer.Instance.ReclaimNow();
            TraceResidency(victims.Count, keep);
        }

        return victims.Count;
    }

    /// <summary>
    /// 收掉启动预热预先建好、但用户始终没点开过的页面视图。
    /// </summary>
    /// <remarks>
    /// 预热（<c>WarmupOrder</c> 12 项）在启动时就把一批页面树挂进舞台，用户只停在一个页面时它们照样占着样式实例与排版缓存；
    /// 而 <see cref="TrimPageResidency"/> 只在「页面数超过 keep」时才动手，keep=12 正好等于预热页数，于是这批页永远不会被收。
    /// 这里按「访问过与否」单独收：没被访问过的先走，访问过的和活动页一个不动。
    /// </remarks>
    /// <param name="minimumResident">最低保留页数；访问过的页数超过它时，以访问过的页数为准。</param>
    /// <returns>本次摘掉的页面数。</returns>
    internal int TrimUnvisitedWarmPages(int minimumResident)
    {
        if (PageStage is null || _pageViews.Count == 0)
        {
            return 0;
        }

        TrackStageViews();

        if (minimumResident < 1)
        {
            minimumResident = 1;
        }

        var active = _activePageView;
        var visited = _pageViews.Count(pair => _pageVisited.Contains(pair.Value));
        var budget = Math.Max(minimumResident, visited);
        var excess = _pageViews.Count - budget;
        if (excess <= 0)
        {
            return 0;
        }

        var victims = _pageViews
            .Where(pair => !ReferenceEquals(pair.Value, active) && !_pageVisited.Contains(pair.Value))
            .OrderBy(pair => PageStage.Children.IndexOf(pair.Value))
            .Take(excess)
            .ToList();

        foreach (var victim in victims)
        {
            EvictPageView(victim.Key, victim.Value);
        }

        if (victims.Count > 0)
        {
            IdleMemoryReclaimer.Instance.ReclaimNow();
            TraceResidency(victims.Count, budget, "warm");
        }

        return victims.Count;
    }

    /// <summary>把一页视图摘出舞台并断掉所有指向它的引用（不含 VM：VM 是单例，仍由外壳持有）。</summary>
    private void EvictPageView(object page, Control view)
    {
        view.PropertyChanged -= OnPageViewPropertyChanged;

        _pageViews.Remove(page);
        _pageViewKeys.Remove(page);
        _pageTouches.Remove(view);
        _pageVisited.Remove(view);

        PageStage?.Children.Remove(view);
        if (ReferenceEquals(_activePageView, view))
        {
            _activePageView = null;
        }

        // 视图缓存不丢，控件仍被缓存表钉住：摘了也白摘。
        ViewLocator.Discard(page);

        // 视图释放了，VM 里给视图缓存的位图还在：单例 VM 活着，那部分只能靠页面自己放。
        if (page is IPageViewEvictionAware evictionAware)
        {
            evictionAware.OnViewEvicted();
        }

        // 文本输入实现握着视图当 owner：视图要换了，旧实现必须松手，下次进入由 AttachPageServices 重新注入。
        if (page is ITextPromptSink sink)
        {
            sink.TextPrompt = null;
        }
    }

    /// <summary>给还留在舞台上的页面视图挂上「被切到前台」的监听，用来记录最近使用次序。</summary>
    private void TrackStageViews()
    {
        if (PageStage is null)
        {
            return;
        }

        foreach (var child in PageStage.Children)
        {
            if (_pageTouches.ContainsKey(child))
            {
                continue;
            }

            _pageTouches[child] = ++_pageTouchSeq;
            child.PropertyChanged += OnPageViewPropertyChanged;
        }
    }

    /// <summary>ShowPage 把某一页的不透明度设成 1，就是「用户正在看这一页」的信号。</summary>
    private void OnPageViewPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (sender is not Control view || e.Property != Visual.OpacityProperty || view.Opacity <= 0d)
        {
            return;
        }

        _pageTouches[view] = ++_pageTouchSeq;

        // 只有真的被切到过不透明度 1 的页才算「用户访问过」：预热的那些永远停在 0。
        _pageVisited.Add(view);
    }

    /// <summary>临时诊断：把「摘了哪些页、还剩几页」记到 CKANUI_NAV_TRACE 指定的文件里。</summary>
    private void TraceResidency(int evicted, int keep, string kind = "")
    {
        if (Environment.GetEnvironmentVariable("CKANUI_NAV_TRACE") is not { Length: > 0 } trace)
        {
            return;
        }

        try
        {
            using var process = System.Diagnostics.Process.GetCurrentProcess();
            File.AppendAllText(
                trace,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"[residency]{(kind.Length == 0 ? string.Empty : " " + kind)} evicted={evicted} keep={keep} views={_pageViews.Count} stage={PageStage?.Children.Count} ws={process.WorkingSet64 / (1024 * 1024)}MB\n"));
        }
        catch (IOException)
        {
            // 诊断输出失败不影响界面。
        }
    }
}
