using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CkanUI.App.Controls;

namespace CkanUI.Tools.PerfProbe;

/// <summary>
/// 内存与空转占用探针：补 PERF-01 门禁没覆盖的两项——<b>驻留内存</b>与<b>空闲 CPU</b>。
/// </summary>
/// <remarks>
/// <para>
/// 现有 PERF-01 四项门禁只量时间（首帧、可交互、滚动帧时间），量不到"挂机时吃多少内存、烧多少 CPU"。
/// 本探针按三个观测点取数：初始化后、3,000 模组首屏呈现后、滚动 300 帧后，分别记录托管堆、
/// 进程工作集、线程数、Gen2 次数；再单开一个装着 <see cref="StarfieldBackground"/> 的窗口做 2 秒空转采样。
/// </para>
/// <para>
/// 空转采样分两档，对应两种真实成本：
/// <b>调度档</b>（只推进 DispatcherTimer + 排版，<c>UseHeadlessDrawing=true</c> 下不起光栅化）反映动画时钟与布局开销；
/// <b>光栅档</b>（每帧把窗口渲染进 <see cref="RenderTargetBitmap"/>）把 Skia 真实绘制成本压进来，
/// 更接近桌面端开着一个星空背景挂机的代价。
/// </para>
/// </remarks>
public static class MemoryProbe
{
    private const int IdleSampleMs = 2000;
    private const int AllocationSampleFrames = 300;

    /// <summary>一次内存/空转画像的全部观测值。</summary>
    public sealed record Result(
        long ManagedInitBytes,
        long WorkingSetInitBytes,
        int ThreadsInit,
        long ManagedInteractiveBytes,
        long WorkingSetInteractiveBytes,
        int ThreadsInteractive,
        int Gen2Interactive,
        long ManagedAfterScrollBytes,
        long AllocatedDuringScrollBytes,
        double AllocatedPerFrameBytes,
        double IdleCpuNoRasterPercent,
        double IdleFrameCostNoRasterMs,
        double IdleCpuWithRasterPercent,
        double IdleFrameCostWithRasterMs,
        long PeakWorkingSetBytes,
        int TotalFramesPumped);

    /// <summary>执行完整画像测量。会在无头宿主里自己建窗口，调用前必须先初始化 Avalonia。</summary>
    public static Result Run()
    {
        var proc = Process.GetCurrentProcess();
        proc.Refresh();
        ForceCollect();
        proc.Refresh();

        var managedInit = GC.GetTotalMemory(true);
        var wsInit = proc.WorkingSet64;
        var threadsInit = proc.Threads.Count;

        // 1) 3,000 模组首屏：与 PERF-01 第二项同一套装配，量呈现后的驻留
        var (_, _, view, vm, window) = BenchmarkRunner.RunColdStartToInteractive();
        ForceCollect();
        proc.Refresh();

        var managedInteractive = GC.GetTotalMemory(true);
        var wsInteractive = proc.WorkingSet64;
        var threadsInteractive = proc.Threads.Count;
        var gen2Interactive = GC.CollectionCount(2);

        // 2) 滚动 300 帧的累计分配（进程级精确值，不含测量自身的小对象也可忽略）
        var allocated = ScrollAndMeasureAllocations(view, vm, window);
        ForceCollect();
        var managedAfterScroll = GC.GetTotalMemory(true);

        window.Close();
        HeadlessAppHost.PumpJobs();

        // 3) 空转采样：只装一个可见的星空背景窗口
        var (cpuNoRaster, frameCostNoRaster, framesNoRaster) = MeasureIdleCpu(renderEachFrame: false);
        var (cpuWithRaster, frameCostWithRaster, framesWithRaster) = MeasureIdleCpu(renderEachFrame: true);

        proc.Refresh();
        return new Result(
            managedInit,
            wsInit,
            threadsInit,
            managedInteractive,
            wsInteractive,
            threadsInteractive,
            gen2Interactive,
            managedAfterScroll,
            allocated,
            allocated / (double)AllocationSampleFrames,
            cpuNoRaster,
            frameCostNoRaster,
            cpuWithRaster,
            frameCostWithRaster,
            proc.PeakWorkingSet64,
            framesNoRaster + framesWithRaster);
    }

    private static void ForceCollect()
    {
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
    }

    private static long ScrollAndMeasureAllocations(object? view, object? _, Window window)
    {
        if (view is not DataGrid && view is Control control)
        {
            // view 是 ModsPageView，真正的 DataGrid 在它里面
            var grid = control.FindControl<DataGrid>("ModsGrid");
            if (grid is null) return 0;

            var scrollMethod = typeof(DataGrid)
                .GetMethods(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == "ScrollSlotIntoView" && m.GetParameters().Length == 2);
            if (scrollMethod is null) return 0;

            var rowCount = grid.ItemsSource is System.Collections.ICollection c ? c.Count : 3000;
            if (rowCount <= 0) return 0;

            var before = GC.GetTotalAllocatedBytes(precise: true);
            var index = 0;
            for (var frame = 0; frame < AllocationSampleFrames; frame++)
            {
                index = (index + 10) % rowCount;
                scrollMethod.Invoke(grid, new object[] { index, false });
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(16);
            }

            return GC.GetTotalAllocatedBytes(precise: true) - before;
        }

        return 0;
    }

    private static (double CpuPercent, double FrameCostMs, int Frames) MeasureIdleCpu(bool renderEachFrame)
    {
        var starfield = new StarfieldBackground { IsVisible = true };
        var window = new Window
        {
            Width = 1280,
            Height = 800,
            Content = new Border
            {
                Background = Brushes.Black,
                Child = starfield
            }
        };
        window.Show();
        HeadlessAppHost.PumpJobs();
        window.Measure(new Size(1280, 800));
        window.Arrange(new Rect(0, 0, 1280, 800));
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(16);
        HeadlessAppHost.PumpJobs();

        // 预热，避免首批 JIT/字形加载算进采样（也把首次 GC/JIT 的尾账甩在采样窗口之外）
        for (var warm = 0; warm < 60; warm++)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(16);
            HeadlessAppHost.PumpJobs();
        }

        var proc = Process.GetCurrentProcess();
        // 预热后若仍在后台跑 GC/JIT 尾账，先静置 300ms 再取基准，否则空转 CPU 会被启动尾账抬高
        Thread.Sleep(300);
        proc.Refresh();
        var cpuStart = proc.TotalProcessorTime;
        var wallSw = Stopwatch.StartNew();
        var frames = 0;
        var frameSw = new Stopwatch();
        double frameCostSum = 0;

        using var rtb = renderEachFrame ? new RenderTargetBitmap(new PixelSize(1280, 800)) : null;

        while (wallSw.ElapsedMilliseconds < IdleSampleMs)
        {
            frameSw.Restart();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(16);
            HeadlessAppHost.PumpJobs();
            if (rtb is not null)
            {
                rtb.Render(window);
            }

            frameSw.Stop();
            frameCostSum += frameSw.Elapsed.TotalMilliseconds;
            frames++;

            // 无头渲染不会真的等 16ms，这里按目标帧间隔让出，才测得到"挂机 2 秒"的真实占比
            var sleepMs = 16 - (int)frameSw.Elapsed.TotalMilliseconds;
            if (sleepMs > 0) Thread.Sleep(sleepMs);
        }

        wallSw.Stop();
        proc.Refresh();
        var cpuDelta = proc.TotalProcessorTime - cpuStart;

        window.Close();
        HeadlessAppHost.PumpJobs();

        var cpuPercent = wallSw.Elapsed.TotalMilliseconds <= 0
            ? 0
            : cpuDelta.TotalMilliseconds / wallSw.Elapsed.TotalMilliseconds * 100.0;
        var frameCostMs = frames == 0 ? 0 : frameCostSum / frames;
        return (cpuPercent, frameCostMs, frames);
    }

    /// <summary>把画像写成 Markdown 表格片段（供 perf-report.md 与 stdout 共用）。</summary>
    public static string ToMarkdown(Result r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## 2. 内存与空转占用画像（本探针新增）");
        sb.AppendLine();
        sb.AppendLine("| 观测点 | 托管堆 | 进程工作集 | 线程数 | Gen2 |");
        sb.AppendLine("|---|---|---|---|---|");
        sb.AppendLine($"| 初始化后 | {Mb(r.ManagedInitBytes)} | {Mb(r.WorkingSetInitBytes)} | {r.ThreadsInit} | — |");
        sb.AppendLine($"| 3,000 模组首屏后 | {Mb(r.ManagedInteractiveBytes)} | {Mb(r.WorkingSetInteractiveBytes)} | {r.ThreadsInteractive} | {r.Gen2Interactive} |");
        sb.AppendLine($"| 滚动 {AllocationSampleFrames} 帧后 | {Mb(r.ManagedAfterScrollBytes)} | — | — | — |");
        sb.AppendLine();
        sb.AppendLine($"- 滚动累计分配: {r.AllocatedDuringScrollBytes / 1024.0:F0} KB / {AllocationSampleFrames} 帧 = **{r.AllocatedPerFrameBytes:F0} B/帧**");
        sb.AppendLine($"- 空转 CPU（调度档，不含光栅化）: {r.IdleCpuNoRasterPercent:F2}% 单核 / 帧成本 {r.IdleFrameCostNoRasterMs:F3}ms");
        sb.AppendLine($"- 空转 CPU（光栅档，每帧 Skia 渲染 1280×800）: {r.IdleCpuWithRasterPercent:F2}% 单核 / 帧成本 {r.IdleFrameCostWithRasterMs:F3}ms");
        sb.AppendLine($"- 进程峰值工作集: {Mb(r.PeakWorkingSetBytes)}");
        return sb.ToString();
    }

    private static string Mb(long bytes) => $"{bytes / 1024.0 / 1024.0:F1} MB";

    /// <summary>写出 JSON 快照，便于优化前后 diff。</summary>
    public static string WriteJson(Result r, string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var payload = new
        {
            measuredAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            runtime = $".NET {Environment.Version} / Avalonia 12 Headless",
            managedInitMb = Round(r.ManagedInitBytes),
            workingSetInitMb = Round(r.WorkingSetInitBytes),
            threadsInit = r.ThreadsInit,
            managedInteractiveMb = Round(r.ManagedInteractiveBytes),
            workingSetInteractiveMb = Round(r.WorkingSetInteractiveBytes),
            threadsInteractive = r.ThreadsInteractive,
            gen2Interactive = r.Gen2Interactive,
            managedAfterScrollMb = Round(r.ManagedAfterScrollBytes),
            scrollAllocatedKb = Math.Round(r.AllocatedDuringScrollBytes / 1024.0, 1),
            allocatedPerFrameBytes = Math.Round(r.AllocatedPerFrameBytes, 1),
            idleCpuNoRasterPercent = Math.Round(r.IdleCpuNoRasterPercent, 2),
            idleFrameCostNoRasterMs = Math.Round(r.IdleFrameCostNoRasterMs, 3),
            idleCpuWithRasterPercent = Math.Round(r.IdleCpuWithRasterPercent, 2),
            idleFrameCostWithRasterMs = Math.Round(r.IdleFrameCostWithRasterMs, 3),
            peakWorkingSetMb = Round(r.PeakWorkingSetBytes)
        };

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
        return path;
    }

    private static double Round(long bytes) => Math.Round(bytes / 1024.0 / 1024.0, 1);
}
