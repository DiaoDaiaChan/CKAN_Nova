using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Avalonia.Controls;
using Avalonia.Threading;
using CkanUI.ViewModels;

namespace CkanUI.App.Diagnostics;

/// <summary>
/// 真机换页计时探针：设了环境变量 <c>CKANUI_NAV_TRACE=&lt;文件路径&gt;</c> 时，首帧后按顺序走一遍所有导航键，
/// 逐页记录「点下去 → 页面对象就位 → 布局落地 → 第一帧上屏」以及期间的帧数与最大帧间隔。
/// <para>
/// 为什么要在真机上量：无头探针（tools/perf-probe 的 --pages-real）只量 Measure/Arrange，
/// 走的是 UseHeadlessDrawing，既不经过合成器也不含换页动效，和用户看到的「切 tab 卡一下」不是一回事。
/// 这里量的是同一条真实路径：真实 DI、真实实例数据、ANGLE/D3D11 合成、真实窗口。
/// </para>
/// </summary>
internal static class NavigationProbe
{
    /// <summary>相邻两页之间的静置时间，让动画与队列收尾。</summary>
    private const int GapMilliseconds = 900;

    /// <summary>等首帧的兜底上限；超时记 -1，说明这一页没有产生新帧。</summary>
    private const int FrameTimeoutMilliseconds = 600;

    /// <summary>首帧后先静置这么久再开始扫，避开启动期的排队。</summary>
    private const int WarmupMilliseconds = 2500;

    /// <summary>采样前静置时长可由 CKANUI_NAV_WARMUP_MS 覆盖（给空闲预热留时间用）。</summary>
    private static int WarmupMs() => int.TryParse(Environment.GetEnvironmentVariable("CKANUI_NAV_WARMUP_MS"), out var ms) && ms > 0 ? ms : WarmupMilliseconds;

    /// <summary>按环境变量挂上探针；没设就什么都不做。</summary>
    /// <param name="window">主窗口。</param>
    public static void Attach(MainWindow window)
    {
        var path = Environment.GetEnvironmentVariable("CKANUI_NAV_TRACE");
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        window.Opened += async (_, _) =>
        {
            try
            {
                var watchMs = WatchMs();
                if (watchMs > 0)
                {
                    _ = WatchAsync(window, path, watchMs);
                }

                await Task.Delay(WarmupMs());
                await RunAsync(window, path);
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                TryWrite(path, $"[nav] failed {ex.GetType().Name}: {ex.Message}");
            }
        };
    }

    private static int WatchMs() => int.TryParse(Environment.GetEnvironmentVariable("CKANUI_NAV_WATCH_MS"), out var ms) && ms > 0 ? ms : 0;

    /// <summary>首帧起持续采样帧间隔，用来确认空闲预热有没有在启动后造成肉眼可见的卡顿。</summary>
    private static async Task WatchAsync(TopLevel window, string path, int durationMs)
    {
        var frames = new List<double>();
        var sw = Stopwatch.StartNew();
        var stop = false;

        void OnFrame(TimeSpan _)
        {
            frames.Add(sw.Elapsed.TotalMilliseconds);
            if (!stop)
            {
                window.RequestAnimationFrame(OnFrame);
            }
        }

        window.RequestAnimationFrame(OnFrame);
        await Task.Delay(durationMs).ConfigureAwait(true);
        stop = true;

        var gaps = new List<(double At, double Gap)>();
        for (var i = 1; i < frames.Count; i++)
        {
            gaps.Add((frames[i], frames[i] - frames[i - 1]));
        }

        var over50 = gaps.Count(g => g.Gap > 50);
        var worst = gaps.OrderByDescending(g => g.Gap).Take(5).Select(g => $"t={g.At:F0}ms gap={g.Gap:F0}ms");
        TryWrite(path, $"[nav] watch frames={frames.Count} over50={over50} maxGap={gaps.Select(g => g.Gap).DefaultIfEmpty(0).Max():F1} top={string.Join(", ", worst)}");
    }

    private static async Task RunAsync(MainWindow window, string path)
    {
        if (window.DataContext is not ShellViewModel shell)
        {
            TryWrite(path, "[nav] no shell DataContext");
            return;
        }

        var only = Environment.GetEnvironmentVariable("CKANUI_NAV_ONLY");
        var keys = shell.NavItems
            .Select(item => item.Key)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(key => string.IsNullOrWhiteSpace(only) || string.Equals(key, only, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        TryWrite(path, $"[nav] start keys={keys.Length}");

        for (var round = 1; round <= 2; round++)
        foreach (var key in keys)
        {
            var frames = new List<double>();
            var stop = false;
            var sw = Stopwatch.StartNew();

            void OnFrame(TimeSpan _)
            {
                frames.Add(sw.Elapsed.TotalMilliseconds);
                if (!stop)
                {
                    window.RequestAnimationFrame(OnFrame);
                }
            }

            window.RequestAnimationFrame(OnFrame);

            var assign = await NavigateAsync(shell, key, sw).ConfigureAwait(true);

            var layout = await WaitForLayoutAsync(sw).ConfigureAwait(true);
            var paint = await WaitForFrameAsync(window, sw).ConfigureAwait(true);
            stop = true;

            var maxGap = 0d;
            for (var i = 1; i < frames.Count; i++)
            {
                maxGap = Math.Max(maxGap, frames[i] - frames[i - 1]);
            }

            TryWrite(
                path,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"[nav] r{round} {key} assign={assign:F1}ms layout={layout:F1}ms paint={paint:F1}ms frames={frames.Count} maxGap={maxGap:F1}ms"));

            await Task.Delay(GapMilliseconds);
        }

        TryWrite(path, "[nav] done");
    }

    /// <summary>发出导航并等到 ShellViewModel.CurrentPage 换到新页面。</summary>
    private static async Task<double> NavigateAsync(ShellViewModel shell, string key, Stopwatch sw)
    {
        var tcs = new TaskCompletionSource<double>(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (string.Equals(e.PropertyName, nameof(ShellViewModel.CurrentPage), StringComparison.Ordinal))
            {
                tcs.TrySetResult(sw.Elapsed.TotalMilliseconds);
            }
        }

        shell.PropertyChanged += OnChanged;
        try
        {
            shell.NavigateCommand.Execute(key);
            var finished = await Task.WhenAny(tcs.Task, Task.Delay(3000)).ConfigureAwait(true);
            return ReferenceEquals(finished, tcs.Task) ? await tcs.Task.ConfigureAwait(true) : -1d;
        }
        finally
        {
            shell.PropertyChanged -= OnChanged;
        }
    }

    /// <summary>布局落地的时刻：<see cref="DispatcherPriority.Loaded"/> 回调在布局之后跑。</summary>
    private static Task<double> WaitForLayoutAsync(Stopwatch sw)
    {
        var tcs = new TaskCompletionSource<double>(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(
            () => tcs.TrySetResult(sw.Elapsed.TotalMilliseconds),
            DispatcherPriority.Loaded);
        return tcs.Task;
    }

    /// <summary>布局之后的第一帧；静止页面可能不再出帧，超时记 -1。</summary>
    private static async Task<double> WaitForFrameAsync(TopLevel window, Stopwatch sw)
    {
        var tcs = new TaskCompletionSource<double>(TaskCreationOptions.RunContinuationsAsynchronously);
        window.RequestAnimationFrame(_ => tcs.TrySetResult(sw.Elapsed.TotalMilliseconds));

        var finished = await Task.WhenAny(tcs.Task, Task.Delay(FrameTimeoutMilliseconds)).ConfigureAwait(true);
        return ReferenceEquals(finished, tcs.Task) ? await tcs.Task.ConfigureAwait(true) : -1d;
    }

    private static void TryWrite(string path, string line)
    {
        try
        {
            File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // 探针写不进日志不该影响应用本身。
        }
    }
}
