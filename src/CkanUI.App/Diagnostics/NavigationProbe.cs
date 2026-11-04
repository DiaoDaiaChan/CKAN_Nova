using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows.Input;
using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
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
    /// <summary>当前活动页面视图的可视节点数：用来判断慢是因为节点膨胀还是别的原因。</summary>
    private static int ActiveNodeCount(TopLevel window)
        => window is MainWindow { ActivePageView: { } active }
            ? System.Linq.Enumerable.Count(Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(active))
            : 0;

    /// <summary>相邻两页之间的静置时间，让动画与队列收尾。</summary>
    private const int GapMilliseconds = 900;

    /// <summary>等首帧的兜底上限；超时记 -1，说明这一页没有产生新帧。</summary>
    private const int FrameTimeoutMilliseconds = 600;

    /// <summary>首帧后先静置这么久再开始扫，避开启动期的排队。</summary>
    private const int WarmupMilliseconds = 2500;

    /// <summary>首帧后的掉帧采样窗口，可由 CKANUI_NAV_FRAME_MS 覆盖。</summary>
    private static int FrameWindowMs() => int.TryParse(Environment.GetEnvironmentVariable("CKANUI_NAV_FRAME_MS"), out var ms) && ms >= 0 ? ms : 1500;

    private static double Percentile(List<double> values, double p)
    {
        if (values.Count == 0)
        {
            return 0d;
        }

        var sorted = values.OrderBy(v => v).ToArray();
        var index = (int)Math.Round((sorted.Length - 1) * p, MidpointRounding.AwayFromZero);
        return sorted[Math.Clamp(index, 0, sorted.Length - 1)];
    }

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

                // 只采启动帧率时可以跳过遍历（预热/启动封面本身才是被观测量）。
                if (Environment.GetEnvironmentVariable("CKANUI_NAV_SKIP_SWEEP") == "1")
                {
                    return;
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

            // 首帧之后继续采一段窗口：用户感知的「掉帧」不只是切过去卡一下，
            // 还有页面稳定后每隔几帧卡一下（滚动、图表重绘、定时器刷新）。
            await Task.Delay(FrameWindowMs()).ConfigureAwait(true);
            stop = true;

            var gaps = new List<double>(frames.Count);
            for (var i = 1; i < frames.Count; i++)
            {
                gaps.Add(frames[i] - frames[i - 1]);
            }

            var maxGap = gaps.Count == 0 ? 0d : gaps.Max();
            var worstEnd = frames.Count < 2
                ? 0d
                : frames[Enumerable.Range(1, frames.Count - 1).MaxBy(i => frames[i] - frames[i - 1])];
            var p50 = Percentile(gaps, 0.50);
            var p95 = Percentile(gaps, 0.95);
            var dropped = gaps.Count(gap => gap > 16.7);

            TryWrite(
                path,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"[nav] r{round} {key} assign={assign:F1}ms layout={layout:F1}ms paint={paint:F1}ms frames={frames.Count} maxGap={maxGap:F1}ms p50={p50:F1}ms p95={p95:F1}ms dropped={dropped} nodes={ActiveNodeCount(window)} worstAt={worstEnd:F0}ms{MemorySuffix()}"));

            if (round == 1 && Environment.GetEnvironmentVariable("CKANUI_NAV_CLICKS") == "1")
            {
                await ClickCommandsAsync(window, shell, path, key).ConfigureAwait(true);
            }

            if (round == 1 && Environment.GetEnvironmentVariable("CKANUI_NAV_BUTTONS") == "1")
            {
                ReportButtons(window, path, key);
            }

            if (round == 1 && Environment.GetEnvironmentVariable("CKANUI_NAV_TABS") == "1")
            {
                await SwitchTabsAsync(window, path, key).ConfigureAwait(true);
            }

            if (Environment.GetEnvironmentVariable("CKANUI_NAV_SCROLL") == "1")
            {
                await ScrollWindowAsync(window, path, round, key).ConfigureAwait(true);
            }

            await Task.Delay(GapMilliseconds);
        }

        if (Environment.GetEnvironmentVariable("CKANUI_NAV_IDLE_CHECK") == "1")
        {
            // 走完全部页面后静置：让空闲回收器（8 秒轮询 + 12 秒最早回收）有机会跑一轮，看稳态能压到哪。
            await Task.Delay(25_000).ConfigureAwait(true);
            TryWrite(path, $"[nav] idle-after-sweep{MemorySuffix()}");
        }

        TryWrite(path, "[nav] done");
    }

    /// <summary>
    /// 安全可点的命令白名单：只点「重新取数」这一类幂等命令，安装/卸载/应用/启动一律不点。
    /// </summary>
    private static readonly Regex SafeCommandPattern = new(
        "^(Refresh|Reload|Recompute|Recheck|Rescan|Validate|Copy)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// 逐个点页面上的安全命令，记录「点到命令体跑完」与「UI 线程重新能响应」各花多久。
    /// </summary>
    /// <remarks>
    /// 用户问的是「点一下按钮会不会卡」：命令体同步执行的时间就是冻结时间，
    /// 命令里若开了后台任务、只是等它回来，<c>free</c> 也会立刻回落——两个数字合起来
    /// 才能区分「同步计算卡住 UI」和「异步加载让页面慢慢长出来」。
    /// </remarks>
    private static async Task ClickCommandsAsync(TopLevel window, ShellViewModel shell, string path, string key)
    {
        var page = shell.CurrentPage;
        if (page is null)
        {
            TryWrite(path, $"[click] {key} no-page");
            return;
        }

        foreach (var property in page.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length > 0 || !SafeCommandPattern.IsMatch(property.Name))
            {
                continue;
            }

            if (property.GetValue(page) is not ICommand command || !command.CanExecute(null))
            {
                continue;
            }

            var watch = Stopwatch.StartNew();
            try
            {
                command.Execute(null);
            }
            catch (Exception ex)
            {
                TryWrite(path, $"[click] {key} {property.Name} threw={ex.GetType().Name}");
                continue;
            }

            var sync = watch.Elapsed.TotalMilliseconds;
            await WaitForLayoutAsync(watch).ConfigureAwait(true);
            var free = watch.Elapsed.TotalMilliseconds;
            var (frames, maxGap, over50) = await SampleFramesAsync(window, 1200).ConfigureAwait(true);
            TryWrite(
                path,
                string.Create(CultureInfo.InvariantCulture, $"[click] {key} {property.Name} sync={sync:F1}ms free={free:F1}ms frames={frames} maxGap={maxGap:F1}ms over50={over50}"));
            await Task.Delay(120).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// 清点页面上的按钮：哪些挂了命令（可安全点）、哪些是代码后置事件（不点，只报名字）。
    /// </summary>
    /// <remarks>
    /// 只点 VM 上的白名单命令会漏掉一类按钮：<c>Click</c> 在 code-behind 里接的按钮（窗口镶边、工作台工具条等），
    /// 它们在 VM 上根本没有 ICommand 属性，遍历命令永远看不见。这里按可视树清点，把覆盖率和盲区一起写进日志。
    /// </remarks>
    private static void ReportButtons(TopLevel window, string path, string key)
    {
        if (window is not MainWindow { ActivePageView: { } page })
        {
            TryWrite(path, $"[btn] {key} no-page");
            return;
        }

        var buttons = page.GetVisualDescendants().OfType<Button>().ToArray();
        var withCommand = buttons.Count(b => b.Command is not null);
        var named = buttons
            .Where(b => b.Command is null)
            .Select(b => string.IsNullOrEmpty(b.Name) ? b.Content?.ToString()?.Trim() : b.Name)
            .Where(n => !string.IsNullOrEmpty(n))
            .Select(n => n!.Length > 24 ? n[..24] : n!)
            .Distinct(StringComparer.Ordinal)
            .Take(8);

        TryWrite(
            path,
            string.Create(
                CultureInfo.InvariantCulture,
                $"[btn] {key} total={buttons.Length} command={withCommand} codebehind={buttons.Length - withCommand} [{string.Join(" | ", named)}]"));
    }

    /// <summary>
    /// 逐个切页面内部的页签（TabControl），记录「设定选中 → 布局落地」与随后 600ms 的帧窗口。
    /// </summary>
    /// <remarks>
    /// 用户说的「点 tab 掉帧」多数不在命令体，而在切换后整块内容第一次 Measure/Arrange：
    /// 页签内容是懒建的，切过去才建视图、才做投影，同步部分就落在 UI 线程上。
    /// 这里只改 SelectedIndex，不触发任何安装/卸载类命令。
    /// </remarks>
    private static async Task SwitchTabsAsync(TopLevel window, string path, string key)
    {
        if (window is not MainWindow { ActivePageView: { } page })
        {
            TryWrite(path, $"[tab] {key} no-page");
            return;
        }

        var tabs = page.GetVisualDescendants().OfType<TabControl>().Where(t => t.ItemCount > 1).ToArray();
        if (tabs.Length == 0)
        {
            TryWrite(path, $"[tab] {key} none");
            return;
        }

        for (var t = 0; t < tabs.Length; t++)
        {
            var tab = tabs[t];
            var original = tab.SelectedIndex;
            for (var i = 0; i < tab.ItemCount; i++)
            {
                if (i == tab.SelectedIndex)
                {
                    continue;
                }

                var watch = Stopwatch.StartNew();
                tab.SelectedIndex = i;
                await WaitForLayoutAsync(watch).ConfigureAwait(true);
                var free = watch.Elapsed.TotalMilliseconds;
                var (frames, maxGap, over50) = await SampleFramesAsync(window, 600).ConfigureAwait(true);
                TryWrite(
                    path,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"[tab] {key} #{t}:{i} free={free:F1}ms frames={frames} maxGap={maxGap:F1}ms over50={over50} nodes={ActiveNodeCount(window)}"));
                await Task.Delay(100).ConfigureAwait(true);
            }

            if (original >= 0 && original < tab.ItemCount)
            {
                tab.SelectedIndex = original;
            }
        }
    }

    /// <summary>点完命令后持续采帧：命令体返回不代表不卡，异步加载回来时照样会掉帧。</summary>
    private static async Task<(int Frames, double MaxGap, int Over50)> SampleFramesAsync(TopLevel window, int durationMs)
    {
        var frames = new List<double>();
        var watch = Stopwatch.StartNew();
        var stop = false;

        void OnFrame(TimeSpan _)
        {
            frames.Add(watch.Elapsed.TotalMilliseconds);
            if (!stop)
            {
                window.RequestAnimationFrame(OnFrame);
            }
        }

        window.RequestAnimationFrame(OnFrame);
        await Task.Delay(durationMs).ConfigureAwait(true);
        stop = true;

        var maxGap = 0d;
        var over50 = 0;
        for (var i = 1; i < frames.Count; i++)
        {
            var gap = frames[i] - frames[i - 1];
            maxGap = Math.Max(maxGap, gap);
            if (gap > 50)
            {
                over50++;
            }
        }

        return (frames.Count, maxGap, over50);
    }

    /// <summary>
    /// 每个页面切完后的驻留量：工作集 + 托管堆。托管堆走阻塞式完整回收，量的是「这个页面
    /// 现在真的占着多少」，不是还没回收的垃圾；代价是每次采样停顿几十毫秒，所以只在
    /// CKANUI_NAV_MEM=1 时才算。
    /// </summary>
    private static string MemorySuffix()
    {
        if (Environment.GetEnvironmentVariable("CKANUI_NAV_MEM") != "1")
        {
            return string.Empty;
        }

        var managed = GC.GetTotalMemory(forceFullCollection: true) / (1024d * 1024d);
        var working = Environment.WorkingSet / (1024d * 1024d);
        using var process = Process.GetCurrentProcess();
        // private = 已提交但不一定常驻的部分（JIT、显卡驱动映射、Skia 位图）；ws - heap 的缺口主要落在这里。
        var priv = process.PrivateMemorySize64 / (1024d * 1024d);
        return string.Create(CultureInfo.InvariantCulture, $" ws={working:F0}MB private={priv:F0}MB heap={managed:F0}MB");
    }

    /// <summary>滚动主力列表 60 帧，量滚动期间的帧间隔（用户感知的「滚起来卡」）。</summary>
    private static async Task ScrollWindowAsync(TopLevel window, string path, int round, string key)
    {
        if (window is not Visual root)
        {
            return;
        }

        var scope = window is MainWindow { ActivePageView: { } active } ? active : root;

        var scroll = scope.GetVisualDescendants()
            .OfType<ScrollViewer>()
            .Where(viewer => viewer.IsVisible && viewer.Extent.Height - viewer.Viewport.Height > 40)
            .OrderByDescending(viewer => viewer.Extent.Height - viewer.Viewport.Height)
            .FirstOrDefault();
        if (scroll is null)
        {
            return;
        }

        const int Frames = 60;
        const double Step = 24d;
        var gaps = new List<double>(Frames);
        var watch = Stopwatch.StartNew();
        var last = 0d;
        for (var i = 0; i < Frames; i++)
        {
            var max = Math.Max(0d, scroll.Extent.Height - scroll.Viewport.Height);
            scroll.Offset = new Vector(scroll.Offset.X, Math.Min(scroll.Offset.Y + Step, max));
            await NextFrameAsync(window, watch).ConfigureAwait(true);
            var now = watch.Elapsed.TotalMilliseconds;
            if (i > 0)
            {
                gaps.Add(now - last);
            }

            last = now;
        }

        TryWrite(
            path,
            string.Create(
                CultureInfo.InvariantCulture,
                $"[scroll] r{round} {key} n={gaps.Count} p50={Percentile(gaps, 0.50):F1}ms p95={Percentile(gaps, 0.95):F1}ms max={(gaps.Count == 0 ? 0d : gaps.Max()):F1}ms dropped={gaps.Count(gap => gap > 16.7)} extent={scroll.Extent.Height:F0} viewport={scroll.Viewport.Height:F0}"));
    }

    private static async Task NextFrameAsync(TopLevel window, Stopwatch watch)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        window.RequestAnimationFrame(_ => tcs.TrySetResult());
        await Task.WhenAny(tcs.Task, Task.Delay(500)).ConfigureAwait(true);
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
