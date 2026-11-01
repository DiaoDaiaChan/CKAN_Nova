using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CkanUI.App.Diagnostics;
using CkanUI.App.Views;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Pages;

namespace CkanUI.Tools.PerfProbe;

/// <summary>
/// PERF-01 门禁各项基准测试执行器。
/// </summary>
public static class BenchmarkRunner
{
    /// <summary>
    /// 冷启动与首帧渲染基准：从无头运行时初始化到首个窗口与组件完成排版绘制的时间。
    /// PERF-01 目标：冷启动到首帧 <= 1.5s (1500ms)。
    /// </summary>
    public static (double DurationMs, bool Passed) RunColdStartToFirstFrame()
    {
        var sw = Stopwatch.StartNew();

        HeadlessAppHost.EnsureInitialized();

        var window = new Window
        {
            Width = 1280,
            Height = 800,
            Content = new Border { Width = 1280, Height = 800 }
        };
        window.Show();
        HeadlessAppHost.PumpJobs();
        window.Measure(new Size(1280, 800));
        window.Arrange(new Rect(0, 0, 1280, 800));
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(16);
        HeadlessAppHost.PumpJobs();

        sw.Stop();
        window.Close();
        HeadlessAppHost.PumpJobs();

        var elapsedMs = sw.Elapsed.TotalMilliseconds;
        return (elapsedMs, elapsedMs <= 1500.0);
    }

    /// <summary>
    /// 冷启动到可交互基准：加载 3,000 个模组元数据并完成 ModsPageView DataGrid 首屏布局与呈现。
    /// PERF-01 目标：冷启动到可交互（3,000 mods） <= 3.0s (3000ms)。
    /// </summary>
    /// <param name="modCount">生成多少条模组；用于按行数扫首屏成本（默认 3,000 = PERF-01 门禁口径）。</param>
    public static (double DurationMs, bool Passed, ModsPageView View, ModsPageViewModel ViewModel, Window Window) RunColdStartToInteractive(int modCount = 3000)
    {
        var sw = Stopwatch.StartNew();
        var phases = new List<(string Name, double Ms)>();

        HeadlessAppHost.EnsureInitialized();
        phases.Add(("headless-init", sw.Elapsed.TotalMilliseconds));

        // 1. 生成 3,000 模组数据集
        var (mods, installed) = MockModDataGenerator.Generate(modCount);
        phases.Add(($"mock-data-{modCount}", sw.Elapsed.TotalMilliseconds));
        var instance = new InstanceInfo("KSP-PerfInstance", "D:/Games/KSP_Perf", "1.12.5", true, false, mods.Count);

        var repoSvc = new MockRepositoryService(mods);
        var regSvc = new MockRegistryService(instance.Name, installed);
        var instSvc = new MockInstanceService(instance);
        var errReporter = new MockErrorReporter();

        // 2. 构造 ViewModel 并加载快照
        var vm = new ModsPageViewModel(repoSvc, regSvc, instSvc, errReporter);
        vm.ReloadFromSnapshots();
        phases.Add(("vm-reload", sw.Elapsed.TotalMilliseconds));

        // 3. 构造 ModsPageView 并挂载至窗口排版
        var view = new ModsPageView
        {
            DataContext = vm,
            Width = 1280,
            Height = 800
        };

        phases.Add(("view-new", sw.Elapsed.TotalMilliseconds));

        var window = new Window
        {
            Width = 1280,
            Height = 800,
            Content = view
        };
        phases.Add(("window-new", sw.Elapsed.TotalMilliseconds));

        window.Show();
        phases.Add(("window-show", sw.Elapsed.TotalMilliseconds));

        HeadlessAppHost.PumpJobs();
        window.Measure(new Size(1280, 800));
        window.Arrange(new Rect(0, 0, 1280, 800));
        phases.Add(("measure-arrange", sw.Elapsed.TotalMilliseconds));

        AvaloniaHeadlessPlatform.ForceRenderTimerTick(16);
        HeadlessAppHost.PumpJobs();
        phases.Add(("first-render", sw.Elapsed.TotalMilliseconds));

        sw.Stop();
        var elapsedMs = sw.Elapsed.TotalMilliseconds;

        Console.WriteLine($"[InteractivePhases] {string.Join(" | ", PhaseDeltas(phases))}");

        // 第二次同构窗口：用来区分「每视图都要付的成本」和「进程内一次性成本（JIT / 样式解析缓存）」。
        var secondSw = Stopwatch.StartNew();
        var secondView = new ModsPageView { DataContext = vm, Width = 1280, Height = 800 };
        var secondWindow = new Window { Width = 1280, Height = 800, Content = secondView };
        secondWindow.Show();
        HeadlessAppHost.PumpJobs();
        secondWindow.Measure(new Size(1280, 800));
        secondWindow.Arrange(new Rect(0, 0, 1280, 800));
        secondWindow.Close();
        HeadlessAppHost.PumpJobs();
        Console.WriteLine($"[InteractivePhases2] second-window-total={secondSw.Elapsed.TotalMilliseconds:F1}ms");

        // 控件树规模：首屏成本若随控件数走，就要先看是谁在堆节点。
        var descendants = view.GetVisualDescendants().ToList();
        var byType = descendants
            .GroupBy(d => d.GetType().Name)
            .OrderByDescending(g => g.Count())
            .Take(12)
            .Select(g => $"{g.Key}x{g.Count()}");
        Console.WriteLine($"[VisualTree] total={descendants.Count} | {string.Join(" ", byType)}");

        // 728 个 Rectangle 是谁生的：取前 3 个，打印它们的祖先链（上限 6 层）。
        foreach (var rect in descendants.Where(d => d.GetType().Name == "Rectangle").Take(3))
        {
            var chain = new List<string>();
            Avalonia.Visual? cursor = rect;
            for (var depth = 0; depth < 6 && cursor is not null; depth++)
            {
                chain.Add($"{cursor.GetType().Name}({(cursor as StyledElement)?.Name ?? "-"})");
                cursor = cursor.GetVisualParent();
            }

            Console.WriteLine($"[VisualTree] rect-chain: {string.Join(" < ", chain)}");
        }

        return (elapsedMs, elapsedMs <= 3000.0, view, vm, window);
    }

    /// <summary>把累计时间戳换算成每段耗时文本，便于定位首屏那 1 秒多花在哪一段。</summary>
    private static IEnumerable<string> PhaseDeltas(List<(string Name, double Ms)> phases)
    {
        var previous = 0d;
        foreach (var (name, ms) in phases)
        {
            yield return $"{name}={ms - previous:F1}ms";
            previous = ms;
        }
    }

    /// <summary>
    /// 首屏样式成本的拆解实验：同样 1,000 个叶子控件，只换控件类型/文本语种，
    /// 用来判断 0.35ms/控件 的成本到底落在「样式解析」还是「字体排版」上。
    /// </summary>
    public static void RunLeafCostMicroBenchmark()
    {
        HeadlessAppHost.EnsureInitialized();

        var cases = new (string Name, Func<Control> Make)[]
        {
            ("1000-border", () => new Border { Width = 10, Height = 10 }),
            ("1000-textblock-ascii", () => new TextBlock { Text = "mod-name-1.2.3" }),
            ("1000-textblock-cjk", () => new TextBlock { Text = "模组名称 1.2.3" }),
        };

        foreach (var (name, make) in cases)
        {
            var panel = new StackPanel();
            for (var i = 0; i < 1000; i++)
            {
                panel.Children.Add(make());
            }

            var sw = Stopwatch.StartNew();
            var window = new Window { Width = 1280, Height = 800, Content = panel };
            window.Show();
            HeadlessAppHost.PumpJobs();
            window.Measure(new Size(1280, 800));
            window.Arrange(new Rect(0, 0, 1280, 800));
            sw.Stop();
            Console.WriteLine($"[LeafCost] {name}={sw.Elapsed.TotalMilliseconds:F1}ms");
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    /// <summary>
    /// 3,000 模组连续滚动帧采样基准：在已呈现的 DataGrid 上持续驱动滚动与渲染并记录帧时间。
    /// PERF-01 目标：
    /// - 3,000 mod scroll p50 frame time <= 8ms
    /// - 3,000 mod scroll p99 frame time <= 16.7ms (60 FPS)
    /// </summary>
    public static (PerformanceSnapshot Snapshot, bool PassedP50, bool PassedP99) Run3000ModScrollBenchmark(
        ModsPageView view,
        ModsPageViewModel vm,
        Window window,
        double durationSeconds = 10.0,
        bool simulateBusyBackground = false)
    {
        var buffer = new PerformanceTelemetryBuffer(1000);
        var dataGrid = view.FindControl<DataGrid>("ModsGrid");
        var distinctDescendants = dataGrid?.GetVisualDescendants().Select(x => x.GetType().Name).Distinct().ToList() ?? new List<string>();
        Console.WriteLine($"[ScrollBenchmark] DataGrid found: {dataGrid != null}, Distinct Descendants: {string.Join(", ", distinctDescendants)}");

        var totalSw = Stopwatch.StartNew();

        var totalFrames = (int)(durationSeconds * 60); // 10秒采样 600 帧
        if (totalFrames < 60) totalFrames = 60;

        var rowCount = vm.Rows.Count;
        var scrollIndex = 0;

        // 如果启用后台并发任务（模拟模组下载/安装或后台 I/O 活跃）
        CancellationTokenSource? busyCts = null;
        Task? busyTask = null;
        if (simulateBusyBackground)
        {
            busyCts = new CancellationTokenSource();
            busyTask = Task.Run(async () =>
            {
                var token = busyCts.Token;
                var rand = new Random();
                var chunk = new byte[64 * 1024];
                while (!token.IsCancellationRequested)
                {
                    rand.NextBytes(chunk);
                    await Task.Delay(15, token).ConfigureAwait(false);
                }
            });
        }

        try
        {
            for (var frame = 0; frame < totalFrames; frame++)
            {
                var frameSw = Stopwatch.StartNew();

                scrollIndex = (scrollIndex + 10) % rowCount;
                var scrollMethod = typeof(DataGrid).GetMethods(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == "ScrollSlotIntoView" && m.GetParameters().Length == 2);
                if (dataGrid != null && scrollMethod != null)
                {
                    scrollMethod.Invoke(dataGrid, new object[] { scrollIndex, false });
                }

                // 驱动 Headless 平台渲染计时器与排版绘制
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(16);

                frameSw.Stop();
                var frameTimeMs = frameSw.Elapsed.TotalMilliseconds;

                // 记录采样
                buffer.RecordFrame(frameTimeMs, 0.5, totalSw.ElapsedMilliseconds);
            }
        }
        finally
        {
            if (busyCts != null)
            {
                busyCts.Cancel();
                try { busyTask?.Wait(1000); } catch { }
                busyCts.Dispose();
            }
        }

        var snapshot = buffer.TakeSnapshot();
        var passedP50 = snapshot.P50FrameMs <= 8.0;
        var passedP99 = simulateBusyBackground ? (snapshot.P99FrameMs <= 33.0) : (snapshot.P99FrameMs <= 16.7);

        return (snapshot, passedP50, passedP99);
    }

    /// <summary>
    /// 搜索框逐键成本：模拟真实打字（每键间隔 = 防抖窗口 + 20ms），
    /// 分别量「键按下那一瞬的同步开销」和「防抖到点后整表重投影的开销」。
    /// </summary>
    /// <param name="modCount">模组条数，默认与 PERF-01 门禁一致。</param>
    /// <param name="keystrokes">打多少个键。</param>
    public static void RunSearchKeystrokeBenchmark(int modCount = 3000, int keystrokes = 12)
    {
        HeadlessAppHost.EnsureInitialized();
        var (mods, installed) = MockModDataGenerator.Generate(modCount);
        var instance = new InstanceInfo("KSP-PerfInstance", "D:/Games/KSP_Perf", "1.12.5", true, false, mods.Count);
        var vm = new ModsPageViewModel(
            new MockRepositoryService(mods),
            new MockRegistryService(instance.Name, installed),
            new MockInstanceService(instance),
            new MockErrorReporter());
        vm.ReloadFromSnapshots();

        var view = new ModsPageView { DataContext = vm, Width = 1280, Height = 800 };
        var window = new Window { Width = 1280, Height = 800, Content = view };
        window.Show();
        HeadlessAppHost.PumpJobs();
        window.Measure(new Size(1280, 800));
        window.Arrange(new Rect(0, 0, 1280, 800));
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(16);
        HeadlessAppHost.PumpJobs();

        // 选中第一行：逐键路径里最贵的那条（详情快照 + 已装标识扫描）参与进来。
        vm.SelectedRow = vm.Rows.Count > 0 ? vm.Rows[0] : null;
        HeadlessAppHost.PumpJobs();

        // 冷路径先热身一次，避免把 JIT 算进第一个键。
        vm.SearchText = "r";
        Thread.Sleep(ModsPageViewModel.SearchDebounceMilliseconds + 20);
        HeadlessAppHost.PumpJobs();

        const string Keyword = "rendering";
        var keystrokeMs = new List<double>();
        for (var i = 1; i <= Math.Min(keystrokes, Keyword.Length); i++)
        {
            var text = Keyword[..i];
            var keySw = Stopwatch.StartNew();
            vm.SearchText = text;
            keySw.Stop();
            keystrokeMs.Add(keySw.Elapsed.TotalMilliseconds);

            Thread.Sleep(ModsPageViewModel.SearchDebounceMilliseconds + 20);
            HeadlessAppHost.PumpJobs();
        }

        var applyMs = new List<double>();
        for (var i = 0; i < 5; i++)
        {
            var applySw = Stopwatch.StartNew();
            vm.ApplyFilter();
            applySw.Stop();
            applyMs.Add(applySw.Elapsed.TotalMilliseconds);
        }

        var keySorted = keystrokeMs.OrderBy(v => v).ToList();
        var applySorted = applyMs.OrderBy(v => v).ToList();
        Console.WriteLine(
            $"[SearchKeystroke] keys={keystrokeMs.Count} sync-p50={Percentile(keySorted, 0.50):F2}ms"
            + $" sync-max={keySorted[^1]:F2}ms"
            + $" | filter-p50={Percentile(applySorted, 0.50):F2}ms"
            + $" filter-max={applySorted[^1]:F2}ms"
            + $" | visible={vm.VisibleCount} total={vm.TotalCount}");
    }


    /// <summary>
    /// 行选中成本：点击列表行要刷新右栏详情（含「已装标识」扫描与快照重建），
    /// 这里逐行切换选中，量这次点击同步付出的时间。
    /// </summary>
    /// <param name="modCount">模组条数。</param>
    /// <param name="clicks">切换多少次选中。</param>
    public static void RunRowSelectionBenchmark(int modCount = 3000, int clicks = 20)
    {
        HeadlessAppHost.EnsureInitialized();
        var (mods, installed) = MockModDataGenerator.Generate(modCount);
        var instance = new InstanceInfo("KSP-PerfInstance", "D:/Games/KSP_Perf", "1.12.5", true, false, mods.Count);
        var vm = new ModsPageViewModel(
            new MockRepositoryService(mods),
            new MockRegistryService(instance.Name, installed),
            new MockInstanceService(instance),
            new MockErrorReporter());
        vm.ReloadFromSnapshots();

        var view = new ModsPageView { DataContext = vm, Width = 1280, Height = 800 };
        var window = new Window { Width = 1280, Height = 800, Content = view };
        window.Show();
        HeadlessAppHost.PumpJobs();
        window.Measure(new Size(1280, 800));
        window.Arrange(new Rect(0, 0, 1280, 800));
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(16);
        HeadlessAppHost.PumpJobs();

        // 热身一次，别把 JIT 算进第一次点击。
        vm.SelectedRow = vm.Rows.Count > 0 ? vm.Rows[0] : null;
        HeadlessAppHost.PumpJobs();

        var clickMs = new List<double>();
        for (var i = 0; i < clicks && vm.Rows.Count > 0; i++)
        {
            var target = vm.Rows[(i * 37) % vm.Rows.Count];
            var clickSw = Stopwatch.StartNew();
            vm.SelectedRow = target;
            clickSw.Stop();
            clickMs.Add(clickSw.Elapsed.TotalMilliseconds);
            HeadlessAppHost.PumpJobs();
        }

        var sorted = clickMs.OrderBy(v => v).ToList();
        Console.WriteLine(
            $"[RowSelection] clicks={sorted.Count} p50={Percentile(sorted, 0.50):F2}ms"
            + $" p90={Percentile(sorted, 0.90):F2}ms max={sorted[^1]:F2}ms");
    }
    /// <summary>
    /// 仓库索引（约 38 MB 的 JSON）解析成本基准。这是「模组列表什么时候出来」的固定地板：
    /// 解析发生在 CKAN Core 内部（<c>external/CKAN/Core/Repositories/RepositoryData.FromJson</c>），
    /// 本项目改不动，所以必须先量清楚它占多少，再决定投影段（<c>RepositoryService.BuildSnapshot</c>）值不值得优化。
    /// </summary>
    public static void RunRepoIndexBenchmark(string path, int reps = 3)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
        {
            Console.WriteLine($"[RepoIndex] 找不到仓库索引文件：{path}");
            return;
        }

        Console.WriteLine($"[RepoIndex] file={path} size={info.Length / 1048576.0:F1}MB reps={reps}");
        for (var i = 0; i < reps; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var allocBefore = GC.GetTotalAllocatedBytes(precise: true);
            var sw = Stopwatch.StartNew();
            var data = CKAN.RepositoryData.FromJson(path, null);
            sw.Stop();
            var allocMb = (GC.GetTotalAllocatedBytes(precise: true) - allocBefore) / 1048576.0;
            MeasureProjection(data);

            Console.WriteLine(
                $"[RepoIndex] run {i + 1}: parse={sw.Elapsed.TotalMilliseconds:F0}ms" +
                $" modules={data?.AvailableModules?.Count ?? 0}" +
                $" downloadCounts={data?.DownloadCounts?.Count ?? 0}" +
                $" alloc={allocMb:F1}MB");
        }
    }

    /// <summary>
    /// 量「逐模组投影」里 <c>AvailableModule.Latest</c> 的成本：旧实现每个模块查两次（取 latest 一次、判兼容再一次），
    /// 这里分别量「每模块一次」和「每模块两次」，用来给 docs/ops/perf-baseline.md §17 的去重结论提供数字。
    /// </summary>
    private static void MeasureProjection(CKAN.RepositoryData? data)
    {
        if (data?.AvailableModules is not { Count: > 0 } modules) return;

        var criteria = new CKAN.Versioning.GameVersionCriteria(new CKAN.Versioning.GameVersion(1, 12, 5));
        var tolerance = new CKAN.Configuration.StabilityToleranceConfig(
            Path.Combine(Path.GetTempPath(), "ckan-ui-perf-probe-stability.json"));

        for (var pass = 1; pass <= 2; pass++)
        {
            var sw = Stopwatch.StartNew();
            var hits = 0;
            foreach (var available in modules.Values)
            {
                var compatible = available.Latest(tolerance, criteria);
                var latest = compatible ?? available.Latest(tolerance);
                if (latest is null) continue;
                // pass 2 复现旧实现的第二次 Latest（判 compatible），用来量重复查询的代价。
                if (pass == 2 && available.Latest(tolerance, criteria) is not null) hits++;
                _ = latest.identifier;
            }
            sw.Stop();
            Console.WriteLine(
                $"[RepoIndex] projection pass{pass}: {sw.Elapsed.TotalMilliseconds:F0}ms modules={modules.Count} duplicates={hits}");
        }
    }

    private static double Percentile(List<double> sorted, double fraction)
    {
        if (sorted.Count == 0)
        {
            return 0;
        }

        var index = (int)Math.Clamp(Math.Round(fraction * (sorted.Count - 1)), 0, sorted.Count - 1);
        return sorted[index];
    }
}

