using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CkanUI.App;
using CkanUI.Services.Abstractions;
using CkanUI.ViewModels;

namespace CkanUI.Tools.PerfProbe;

/// <summary>
/// 真实依赖注入下的换页成本探测：解析页面 VM → 激活（触发 OnActivated 刷新）→ 建视图 → 布局 → 渲染，
/// 逐段计时。与 <see cref="BenchmarkRunner.RunPageSweep"/> 的区别是这里用的是真实 DI 与真实数据源，
/// 能看到「带数据的首次进入」和「再次进入」的差别。
/// </summary>
public static class PageSwitchProbe
{
    /// <summary>跑一遍全部导航键，rounds 轮（第 1 轮是首次进入，第 2 轮是同页往返）。</summary>
    /// <param name="rounds">轮数；每轮都按注册顺序走一遍所有页面。</param>
    public static void Run(int rounds = 2, string? onlyKey = null)
    {
        HeadlessAppHost.EnsureInitialized();

        using var services = ServiceRegistration.Build(() => null);

        // 与真机一致：实例与仓库都已在启动时装配好，页面读到的是已加载的数据。
        _ = services.GetService(typeof(IInstanceService));
        HeadlessAppHost.PumpJobs();

        var host = new Window { Width = 1280, Height = 800 };
        host.Show();
        HeadlessAppHost.PumpJobs();

        var locator = new ViewLocator();
        var keys = ServiceRegistration.PageTypes.Keys
            .Where(k => onlyKey is null || string.Equals(k, onlyKey, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        PageViewModel? previous = null;

        for (var round = 1; round <= rounds; round++)
        {
            foreach (var key in keys)
            {
                if (!ServiceRegistration.PageTypes.TryGetValue(key, out var type))
                {
                    continue;
                }

                var sw = Stopwatch.StartNew();
                var vm = services.GetService(type) as PageViewModel;
                var resolveMs = sw.Elapsed.TotalMilliseconds;

                sw.Restart();
                previous?.Deactivate();
                var deactivateMs = sw.Elapsed.TotalMilliseconds;

                sw.Restart();
                vm?.Activate();
                var activateMs = sw.Elapsed.TotalMilliseconds;

                sw.Restart();
                Settle(300);
                var settleMs = sw.Elapsed.TotalMilliseconds;

                sw.Restart();
                var view = locator.Build(vm) ?? new Border();
                var viewMs = sw.Elapsed.TotalMilliseconds;

                sw.Restart();
                host.Content = view;
                HeadlessAppHost.PumpJobs();
                host.Measure(new Size(1280, 800));
                host.Arrange(new Rect(0, 0, 1280, 800));
                var layoutMs = sw.Elapsed.TotalMilliseconds;

                sw.Restart();
                Reveal(view);
                var revealMs = sw.Elapsed.TotalMilliseconds;

                sw.Restart();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(16);
                HeadlessAppHost.PumpJobs();
                var renderMs = sw.Elapsed.TotalMilliseconds;

                var nodes = view.GetVisualDescendants().Count();
                var totalMs = resolveMs + deactivateMs + activateMs + settleMs + viewMs + layoutMs + revealMs + renderMs;

                Console.WriteLine(
                    $"[PageSwitch] round={round} {key}: total={totalMs:F1}ms resolve={resolveMs:F2} deactivate={deactivateMs:F2} " +
                    $"activate={activateMs:F2} settle300={settleMs:F0} view={viewMs:F1} layout={layoutMs:F1} reveal={revealMs:F1} render={renderMs:F1} nodes={nodes}");

                previous = vm;
            }
        }

        previous?.Deactivate();
        host.Close();
        HeadlessAppHost.PumpJobs();
    }

    /// <summary>量一次真实存档的「读盘 + 解析 + 校验」成本：切回存档页以前每次都要付这一笔。</summary>
    /// <param name="rounds">重复次数。</param>
    public static void SaveValidateCost(int rounds = 3)
    {
        HeadlessAppHost.EnsureInitialized();
        using var services = ServiceRegistration.Build(() => null);
        var instances = (IInstanceService)services.GetService(typeof(IInstanceService))!;
        var saves = (ISaveFileService)services.GetService(typeof(ISaveFileService))!;
        HeadlessAppHost.PumpJobs();

        var instance = instances.Current ?? instances.Instances.FirstOrDefault();
        if (instance is null)
        {
            Console.WriteLine("[SaveValidate] no instance");
            return;
        }

        var list = saves.ListSavesAsync(instance.GameDir).GetAwaiter().GetResult();
        if (!list.IsSuccess || list.Value is null || list.Value.Count == 0)
        {
            Console.WriteLine("[SaveValidate] no saves");
            return;
        }

        var file = list.Value[0];
        Console.WriteLine($"[SaveValidate] file={Path.GetFileName(file.FilePath)} size={file.SizeBytes}");
        for (var i = 1; i <= rounds; i++)
        {
            var sw = Stopwatch.StartNew();
            var result = saves.ValidateAsync(file.FilePath).GetAwaiter().GetResult();
            Console.WriteLine($"[SaveValidate] round{i} validate={sw.Elapsed.TotalMilliseconds:F2}ms ok={result.IsSuccess}");
        }
    }

    /// <summary>打印指定页面的可视树类型直方图，用来判断节点数被谁吃掉。</summary>
    /// <param name="key">导航键。</param>
    public static void NodeHistogram(string key)
    {
        HeadlessAppHost.EnsureInitialized();
        using var services = ServiceRegistration.Build(() => null);
        _ = services.GetService(typeof(IInstanceService));
        HeadlessAppHost.PumpJobs();

        if (!ServiceRegistration.PageTypes.TryGetValue(key, out var type))
        {
            Console.WriteLine($"[NodeHist] unknown key {key}");
            return;
        }

        var vm = services.GetService(type) as PageViewModel;
        vm?.Activate();
        Settle(300);

        var view = new ViewLocator().Build(vm) ?? new Border();
        var host = new Window { Width = 1280, Height = 800, Content = view };
        host.Show();
        HeadlessAppHost.PumpJobs();
        host.Measure(new Size(1280, 800));
        host.Arrange(new Rect(0, 0, 1280, 800));
        HeadlessAppHost.PumpJobs();

        var all = view.GetVisualDescendants().ToList();
        Console.WriteLine($"[NodeHist] {key} total={all.Count}");
        foreach (var group in all.GroupBy(x => x.GetType().Name).OrderByDescending(g => g.Count()).Take(18))
        {
            Console.WriteLine($"[NodeHist]   {group.Key} x{group.Count()}");
        }

        host.Close();
        HeadlessAppHost.PumpJobs();
    }

    /// <summary>复刻 MainWindow.PlayPageReveal 的六套动效挂载 + 入场编排，量它们各自要遍历多少节点、花多少时间。</summary>
    /// <param name="root">页面根。</param>
    private static void Reveal(Control root)
    {
        CkanUI.App.Animations.BannerMotion.Attach(root);
        CkanUI.App.Animations.ViewLayerMotion.Attach(root);
        CkanUI.App.Animations.HoverLiftMotion.Attach(root);
        CkanUI.App.Animations.ScrollRevealMotion.Attach(root);
        CkanUI.App.Animations.FeedArrivalMotion.Attach(root);
        CkanUI.App.Animations.EntranceChoreographer.Play(root, includeOpacity: false);
        CkanUI.App.Animations.TextRevealChoreographer.Play(root);
    }

    /// <summary>把调度队列跑空一段时间，让激活时发起的异步加载有机会把数据灌进 VM。</summary>
    /// <param name="milliseconds">最长等待时间；固定窗口保证各页可比。</param>
    private static void Settle(int milliseconds)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < milliseconds)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }

        Dispatcher.UIThread.RunJobs();
    }
}
