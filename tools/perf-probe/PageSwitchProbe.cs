using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
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

    /// <summary>量缩略图解码成本：同一批图片按不同目标宽度解码，看宽度对耗时的影响。</summary>
    /// <param name="dir">图片目录。</param>
    /// <param name="rounds">每个宽度重复次数。</param>
    public static void ThumbCost(string dir, int rounds = 3)
    {
        var files = Directory.GetFiles(dir, "*.png").OrderBy(f => f, StringComparer.Ordinal).Take(6).ToArray();
        if (files.Length == 0)
        {
            Console.WriteLine($"[ThumbCost] no png in {dir}");
            return;
        }

        HeadlessAppHost.EnsureInitialized();
        foreach (var width in new[] { 640, 256, 128, 96 })
        {
            var total = 0d;
            for (var round = 1; round <= rounds; round++)
            {
                var sw = Stopwatch.StartNew();
                foreach (var file in files)
                {
                    // 与 Avalonia 的 DecodeToWidth 同构：解码 + 高质量缩放，都是字节级真活。
                    using var src = SkiaSharp.SKBitmap.Decode(file);
                    var info = new SkiaSharp.SKImageInfo(width, Math.Max(1, src.Height * width / src.Width));
                    using var dst = src.Resize(info, new SkiaSharp.SKSamplingOptions(SkiaSharp.SKCubicResampler.Mitchell))
                                    ?? throw new InvalidOperationException("resize failed");
                    _ = dst.ByteCount;
                }

                var ms = sw.Elapsed.TotalMilliseconds;
                total += ms;
                Console.WriteLine($"[ThumbCost] width={width} round{round} files={files.Length} total={ms:F1}ms per={ms / files.Length:F2}ms");
            }

            Console.WriteLine($"[ThumbCost] width={width} avg={total / rounds:F1}ms");
        }
    }

    /// <summary>走真实转换器量「按不同目标宽度解码」的耗时（每个文件只用一次，保证命中不到缓存）。</summary>
    /// <param name="dir">图片目录。</param>
    public static void ThumbConvertCost(string dir)
    {
        HeadlessAppHost.EnsureInitialized();
        var files = Directory.GetFiles(dir, "*.png").OrderBy(f => f, StringComparer.Ordinal).Take(6).ToArray();
        if (files.Length == 0)
        {
            Console.WriteLine($"[ThumbConvert] no png in {dir}");
            return;
        }

        var converter = new CkanUI.App.Converters.PathToThumbnailConverter();
        foreach (var width in new[] { "640", "96" })
        {
            var total = 0d;
            foreach (var file in files)
            {
                var sw = Stopwatch.StartNew();
                var bitmap = converter.Convert(file, typeof(object), width, System.Globalization.CultureInfo.InvariantCulture)
                    as Avalonia.Media.Imaging.Bitmap;
                var ms = sw.Elapsed.TotalMilliseconds;
                total += ms;
                Console.WriteLine(
                    $"[ThumbConvert] width={width} {Path.GetFileName(file)} {ms:F2}ms pixels={(bitmap is null ? "null" : bitmap.PixelSize.Width + "x" + bitmap.PixelSize.Height)}");
            }

            Console.WriteLine($"[ThumbConvert] width={width} total={total:F1}ms per={total / files.Length:F2}ms");
        }
    }

    /// <summary>逐个 TabControl 切子标签，量「切一次」的布局 + 渲染成本。</summary>
    /// <param name="rounds">每个标签页切几轮（第 1 轮含首次模板实例化）。</param>
    /// <param name="onlyKey">只测某个导航键。</param>
    public static void TabSweep(int rounds = 2, string? onlyKey = null)
    {
        HeadlessAppHost.EnsureInitialized();

        using var services = ServiceRegistration.Build(() => null);
        _ = services.GetService(typeof(IInstanceService));
        HeadlessAppHost.PumpJobs();

        var host = new Window { Width = 1280, Height = 800 };
        host.Show();
        HeadlessAppHost.PumpJobs();

        var locator = new ViewLocator();
        foreach (var key in ServiceRegistration.PageTypes.Keys
                     .Where(k => onlyKey is null || string.Equals(k, onlyKey, StringComparison.OrdinalIgnoreCase)))
        {
            if (!ServiceRegistration.PageTypes.TryGetValue(key, out var type))
            {
                continue;
            }

            var vm = services.GetService(type) as PageViewModel;
            vm?.Activate();
            Settle(150);

            var view = locator.Build(vm) ?? new Border();
            host.Content = view;
            HeadlessAppHost.PumpJobs();
            host.Measure(new Size(1280, 800));
            host.Arrange(new Rect(0, 0, 1280, 800));
            HeadlessAppHost.PumpJobs();

            var tabs = view.GetVisualDescendants().OfType<TabControl>().ToList();
            Console.WriteLine($"[Tabs] key={key} tabControls={tabs.Count}");

            for (var t = 0; t < tabs.Count; t++)
            {
                var tab = tabs[t];
                var count = tab.ItemCount;
                Console.WriteLine($"[Tabs]   control{t} items={count} selected={tab.SelectedIndex}");

                for (var round = 1; round <= rounds; round++)
                {
                    for (var i = 0; i < count; i++)
                    {
                        var sw = Stopwatch.StartNew();
                        tab.SelectedIndex = i;
                        HeadlessAppHost.PumpJobs();
                        host.Measure(new Size(1280, 800));
                        host.Arrange(new Rect(0, 0, 1280, 800));
                        var layoutMs = sw.Elapsed.TotalMilliseconds;

                        sw.Restart();
                        AvaloniaHeadlessPlatform.ForceRenderTimerTick(16);
                        HeadlessAppHost.PumpJobs();
                        var renderMs = sw.Elapsed.TotalMilliseconds;

                        Console.WriteLine(
                            $"[Tabs]     round={round} item{i}: layout={layoutMs:F1}ms render={renderMs:F1}ms nodes={view.GetVisualDescendants().Count()}");
                    }
                }

                tab.SelectedIndex = 0;
            }

            vm?.Deactivate();
            host.Content = null;
            HeadlessAppHost.PumpJobs();
            _ = tabs;
        }

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

    /// <summary>量化「换掉整表 ItemsSource」相对「原地不动」多花的布局时间（行容器重建成本）。</summary>
    /// <param name="rows">列表行数。</param>
    /// <param name="rounds">每组采样次数。</param>
    public static void ListRebindCost(int rows = 200, int rounds = 5)
    {
        HeadlessAppHost.EnsureInitialized();

        var template = new FuncDataTemplate<ProbeRow>((item, _) => new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Children =
            {
                new TextBlock { Text = item.Name, Margin = new Thickness(4, 2) },
                new TextBlock { Text = item.Size, Margin = new Thickness(4, 2) },
            },
        }, supportsRecycling: true);

        var list = new ListBox { ItemTemplate = template, Height = 600, Width = 460 };
        var host = new Window { Width = 1280, Height = 800, Content = list };
        host.Show();
        HeadlessAppHost.PumpJobs();

        static ProbeRow[] Build(int count) =>
            Enumerable.Range(0, count).Select(i => new ProbeRow($"slot {i}", $"{i * 8} KB")).ToArray();

        static double Layout(Window window)
        {
            var sw = Stopwatch.StartNew();
            window.InvalidateMeasure();
            window.Measure(new Size(window.Width, window.Height));
            window.Arrange(new Rect(0, 0, window.Width, window.Height));
            // 光调 Measure/Arrange 在已落地的窗口上会短路：显式把布局队列跑空，量到的才是真实成本。
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Loaded);
            return sw.Elapsed.TotalMilliseconds;
        }

        list.ItemsSource = Build(rows);
        Layout(host);
        Settle(200);

        var rebind = new double[rounds];
        var steady = new double[rounds];
        for (var i = 0; i < rounds; i++)
        {
            list.ItemsSource = Build(rows);
            rebind[i] = Layout(host);
            steady[i] = Layout(host);
        }

        Array.Sort(rebind);
        Array.Sort(steady);
        var rebindP50 = rebind[rounds / 2];
        var steadyP50 = steady[rounds / 2];
        Console.WriteLine($"[ListRebind] rows={rows} replaceP50={rebindP50:F2}ms replaceMin={rebind[0]:F2}ms steadyP50={steadyP50:F2}ms delta={rebindP50 - steadyP50:F2}ms");

        host.Close();
        HeadlessAppHost.PumpJobs();
    }

    /// <summary>列表行探针：两个字段，够触发文本测量。</summary>
    private sealed record ProbeRow(string Name, string Size);

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
