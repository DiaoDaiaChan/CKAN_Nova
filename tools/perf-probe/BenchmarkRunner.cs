using System;
using System.Diagnostics;
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
}
