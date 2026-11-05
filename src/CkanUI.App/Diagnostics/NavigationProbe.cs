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

    /// <summary>看门狗当前归属的页面键：卡顿记在「当时正在看哪一页」上。</summary>
    private static volatile string _stallKey = "startup";

    /// <summary>看门狗当前归属的阶段：进入/稳定窗/滚动/点命令/页间空档，用来区分「切过去卡」和「停着也卡」。</summary>
    private static volatile string _stallPhase = "startup";

    private static readonly List<(string Key, double Lag, double At)> StallLog = new();

    private static readonly object StallLock = new();

    private static CancellationTokenSource? _stallCts;

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
    /// <summary>本页窗口内的 Gen2 回收次数：用来区分「页面自己在算」和「一次 GC 停顿」。</summary>
    private static string GcDelta(int before) => $" gen2={GC.CollectionCount(2) - before}";

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

        MemoryTimeline.Start(path);

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
                StartStallWatch();
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

    /// <summary>
    /// UI 线程卡顿看门狗：后台线程每 15ms 往 UI 线程投一次 <see cref="Dispatcher.UIThread.Post(Action)"/>，
    /// 量「投递 → 真正执行」的时间差。差值就是 UI 线程被同步计算占住的时长。
    /// </summary>
    /// <remarks>
    /// 为什么需要它：命令体快不等于不卡——真正的同步重活常发生在 <c>await</c> 之后的续体里
    /// （把结果投影成行、建树、排序），那段代码既不在命令里，也不在布局里，帧探针只能看到「掉帧」，
    /// 看不出是谁占的。看门狗给的是「哪一页、卡多久、什么时刻」，能和 [nav] 行对上。
    /// 只采超过 50ms 的停顿：低于这个数是正常的一帧。
    /// </remarks>
    private static void StartStallWatch()
    {
        if (Environment.GetEnvironmentVariable("CKANUI_NAV_STALLS") != "1" || _stallCts is not null)
        {
            return;
        }

        var cts = new CancellationTokenSource();
        _stallCts = cts;
        var clock = Stopwatch.StartNew();
        var thread = new Thread(() =>
        {
            while (!cts.IsCancellationRequested)
            {
                var posted = clock.Elapsed.TotalMilliseconds;
                // 归属必须在投递时定：等它回来再读，页面早换了，卡顿会记到冤枉的页上。
                var owner = $"{_stallKey}/{_stallPhase}";
                var completion = new TaskCompletionSource<double>(TaskCreationOptions.RunContinuationsAsynchronously);
                Dispatcher.UIThread.Post(() => completion.TrySetResult(clock.Elapsed.TotalMilliseconds - posted));
                if (completion.Task.Wait(3000) && completion.Task.Result > 50d)
                {
                    lock (StallLock)
                    {
                        StallLog.Add((owner, completion.Task.Result, posted));
                    }
                }

                Thread.Sleep(15);
            }
        })
        {
            IsBackground = true,
            Name = "ckanui-ui-stall-watch",
        };
        thread.Start();
    }

    /// <summary>停掉看门狗并写出汇总：总数、超 200ms 的条数、最长的 15 条、按页取最大值。</summary>
    private static void StopStallWatch(string path)
    {
        if (_stallCts is null)
        {
            return;
        }

        _stallCts.Cancel();
        _stallCts.Dispose();
        _stallCts = null;

        List<(string Key, double Lag, double At)> snapshot;
        lock (StallLock)
        {
            snapshot = StallLog.ToList();
            StallLog.Clear();
        }

        if (snapshot.Count == 0)
        {
            TryWrite(path, "[stall] count=0");
            return;
        }

        var top = string.Join(
            " | ",
            snapshot.OrderByDescending(item => item.Lag).Take(15).Select(item => $"t={item.At:F0}ms key={item.Key} lag={item.Lag:F0}ms"));
        TryWrite(path, $"[stall] count={snapshot.Count} over200={snapshot.Count(item => item.Lag > 200)} top={top}");

        var byKey = string.Join(
            " ",
            snapshot
                .GroupBy(item => item.Key)
                .Select(group => $"{group.Key}={group.Max(item => item.Lag):F0}ms/{group.Count()}")
                .OrderByDescending(item => item));
        TryWrite(path, $"[stall] byKey {byKey}");
    }

    private static async Task RunAsync(MainWindow window, string path)
    {
        if (window.DataContext is not ShellViewModel shell)
        {
            TryWrite(path, "[nav] no shell DataContext");
            return;
        }

        var only = Environment.GetEnvironmentVariable("CKANUI_NAV_ONLY");

        // CKANUI_NAV_KEYS=a,b,c：只走这几页。逐页扫全场时某些页在空实例下会崩（见 --pages 的教训），
        // 要量「每页边际成本」这类斜率就得能掐掉几页，又不至于像 CKANUI_NAV_ONLY 那样只能走一页。
        var subset = Environment.GetEnvironmentVariable("CKANUI_NAV_KEYS")?
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var keys = shell.NavItems
            .Select(item => item.Key)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(key => string.IsNullOrWhiteSpace(only) || string.Equals(key, only, StringComparison.OrdinalIgnoreCase))
            .Where(key => subset is null || subset.Contains(key))
            .ToArray();
        TryWrite(path, $"[nav] start keys={keys.Length}");
        ReportInstance(path);
        // 启动编排仍在后台跑（9.8MB 注册表解析要几秒），此时进页面只会量到空壳，先等实例真空上。
        await WaitForInstanceAsync(path);

        var previousKey = string.Empty;
        var rounds = NavRounds();
        for (var round = 1; round <= rounds; round++)
        foreach (var key in keys)
        {
            // 同一页面重复导航会被 Shell 当成 no-op（CurrentPage 不变），
            // 那样量到的只是探针自己的 3 秒超时。先跳到一个别的页面再回来。
            if (string.Equals(previousKey, key, StringComparison.Ordinal))
            {
                shell.NavigateCommand.Execute(keys.FirstOrDefault(item => !string.Equals(item, key, StringComparison.Ordinal)) ?? "home");
                await Task.Delay(250).ConfigureAwait(true);
            }

            previousKey = key;
            _stallKey = key;
            _stallPhase = "enter";
            var gen2Before = GC.CollectionCount(2);
            var pauseBefore = GC.GetTotalPauseDuration();
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
            _stallPhase = "window0";
            var windowMs = FrameWindowMs();
            await Task.Delay(windowMs / 3).ConfigureAwait(true);
            _stallPhase = "window1";
            await Task.Delay(windowMs / 3).ConfigureAwait(true);
            _stallPhase = "window2";
            await Task.Delay(windowMs - (2 * (windowMs / 3))).ConfigureAwait(true);
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
                    $"[nav] r{round} {key} assign={assign:F1}ms layout={layout:F1}ms paint={paint:F1}ms frames={frames.Count} maxGap={maxGap:F1}ms p50={p50:F1}ms p95={p95:F1}ms dropped={dropped} nodes={ActiveNodeCount(window)} worstAt={worstEnd:F0}ms{MemorySuffix()}{GcDelta(gen2Before)} pause={(GC.GetTotalPauseDuration() - pauseBefore).TotalMilliseconds:F0}ms"));

            if (round == 1 && Environment.GetEnvironmentVariable("CKANUI_NAV_CLICKS") == "1")
            {
                _stallPhase = "click";
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
                _stallPhase = "scroll";
                await ScrollWindowAsync(window, path, round, key).ConfigureAwait(true);
            }

            _stallPhase = "gap";
            await Task.Delay(GapMilliseconds);

            // 每轮走完打一条汇总：连续多轮遍历时，这一行就是内存泄漏的曲线。
            if (string.Equals(key, keys[^1], StringComparison.Ordinal))
            {
                TryWrite(path, $"[round] {round} keys={keys.Length} cycles={GC.CollectionCount(0)} gen2={GC.CollectionCount(2)}{MemorySuffix()}");
                ReportInstance(path);
            }
        }

        if (Environment.GetEnvironmentVariable("CKANUI_NAV_STYLES") == "1")
        {
            ReportStyles(window, path);
        }

        if (Environment.GetEnvironmentVariable("CKANUI_NAV_IDLE_CHECK") == "1")
        {
            // 走完全部页面后静置：让空闲回收器（8 秒轮询 + 12 秒最早回收）有机会跑一轮，看稳态能压到哪。
            await Task.Delay(25_000).ConfigureAwait(true);
            TryWrite(path, $"[nav] idle-after-sweep{MemorySuffix()}");
        }

        if (Environment.GetEnvironmentVariable("CKANUI_NAV_RESIDENCY_DRILL") == "1")
        {
            await ResidencyDrillAsync(window, shell, keys, path).ConfigureAwait(true);
        }

        StopStallWatch(path);
        TryWrite(path, "[nav] done");
    }

    /// <summary>
    /// 驻留预算演练：等预算把非活动页摘掉，再逐页回访，量「被摘过的页」重新进场的代价。
    /// 摘掉的是整棵视图树，回来时要重新加载 XAML 再排首屏——这段代价不量出来，预算就只能靠猜。
    /// </summary>
    private static async Task ResidencyDrillAsync(MainWindow window, ShellViewModel shell, string[] keys, string path)
    {
        if (window.PageResidencyKeep <= 0)
        {
            TryWrite(path, "[drill] skip policy-off");
            return;
        }

        // 摘除只发生在「空闲 + 距上次换页超过阈值」之后；空闲检查已经静置过一段，这里最多再等几十秒。
        var deadline = DateTime.UtcNow.AddSeconds(45);
        while (DateTime.UtcNow < deadline && window.PageViewCount > window.PageResidencyKeep)
        {
            await Task.Delay(1000).ConfigureAwait(true);
        }

        var evicted = keys.Where(key => !window.IsResidentKey(key)).Take(3).ToArray();
        TryWrite(path, $"[drill] evicted={evicted.Length} views={window.PageViewCount} keep={window.PageResidencyKeep} keys={string.Join(',', evicted)}");
        if (evicted.Length == 0)
        {
            return;
        }

        foreach (var key in evicted)
        {
            var sw = Stopwatch.StartNew();
            var assign = await NavigateAsync(shell, key, sw).ConfigureAwait(true);
            var layout = await WaitForLayoutAsync(sw).ConfigureAwait(true);
            var paint = await WaitForFrameAsync(window, sw).ConfigureAwait(true);
            TryWrite(path, $"[drill] {key} assign={assign.ToString("F1", CultureInfo.InvariantCulture)}ms layout={layout.ToString("F1", CultureInfo.InvariantCulture)}ms paint={paint.ToString("F1", CultureInfo.InvariantCulture)}ms{MemorySuffix()}");
        }

        // 再回访第一页：它此刻已经常驻，这一次是「纯切换」——冷重建与热切换的差额才是预算真正的代价。
        var warm = evicted[0];
        var warmWatch = Stopwatch.StartNew();
        var warmAssign = await NavigateAsync(shell, warm, warmWatch).ConfigureAwait(true);
        var warmLayout = await WaitForLayoutAsync(warmWatch).ConfigureAwait(true);
        var warmPaint = await WaitForFrameAsync(window, warmWatch).ConfigureAwait(true);
        TryWrite(path, $"[drill] {warm} warm assign={warmAssign.ToString("F1", CultureInfo.InvariantCulture)}ms layout={warmLayout.ToString("F1", CultureInfo.InvariantCulture)}ms paint={warmPaint.ToString("F1", CultureInfo.InvariantCulture)}ms{MemorySuffix()}");
    }

    /// <summary>
    /// 安全可点的命令白名单：只点「重新取数」这一类幂等命令，安装/卸载/应用/启动一律不点。
    /// </summary>
    private static int InstalledCount()
    {
        try
        {
            var registry = App.Services?.GetService(typeof(CkanUI.Services.Abstractions.IRegistryService))
                as CkanUI.Services.Abstractions.IRegistryService;
            return registry?.Current.Installed.Count ?? 0;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private static int AvailableCount()
    {
        try
        {
            var repositories = App.Services?.GetService(typeof(CkanUI.Services.Abstractions.IRepositoryService))
                as CkanUI.Services.Abstractions.IRepositoryService;
            return repositories?.Current.Available.Count ?? 0;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    /// <summary>等启动编排把注册表/仓库索引真正读上来；超时也继续，但会在日志里留证。</summary>
    private static async Task WaitForInstanceAsync(string path)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(30))
        {
            if (InstalledCount() > 0)
            {
                TryWrite(path, string.Create(CultureInfo.InvariantCulture, $"[inst] ready after={watch.ElapsedMilliseconds}ms installed={InstalledCount()}"));
                return;
            }

            if (AvailableCount() > 0)
            {
                TryWrite(path, string.Create(CultureInfo.InvariantCulture, $"[inst] repo-ready after={watch.ElapsedMilliseconds}ms available={AvailableCount()} installed={InstalledCount()}"));
                return;
            }

            await Task.Delay(250).ConfigureAwait(false);
        }

        TryWrite(path, "[inst] not-ready after=30000ms");
    }

    /// <summary>打印本次进程实际挂上的实例与注册表规模，避免把「实例没接上」误读成页面性能问题。</summary>
    private static void ReportInstance(string path)
    {
        try
        {
            var services = App.Services;
            var instances = services?.GetService(typeof(CkanUI.Services.Abstractions.IInstanceService))
                as CkanUI.Services.Abstractions.IInstanceService;
            var registry = services?.GetService(typeof(CkanUI.Services.Abstractions.IRegistryService))
                as CkanUI.Services.Abstractions.IRegistryService;
            var name = instances?.Current?.Name ?? "-";
            var instancePath = instances?.Current?.GameDir ?? "-";
            var installed = registry?.Current.Installed.Count ?? -1;
            var errors = services?.GetService(typeof(CkanUI.Services.Abstractions.IErrorReporter))
                as CkanUI.Services.Abstractions.IErrorReporter;
            var lastError = errors?.Recent.Count > 0
                ? errors.Recent[^1].Context + ":" + errors.Recent[^1].UserMessage
                : "-";
            TryWrite(path, string.Create(CultureInfo.InvariantCulture, $"[inst] name={name} path={instancePath} installed={installed} errors={errors?.Recent.Count ?? -1} last={lastError}"));
        }
        catch (Exception ex)
        {
            TryWrite(path, $"[inst] unavailable reason={ex.GetType().Name}");
        }
    }

    /// <summary>页面 VM 上常见列表的行数，用来区分「页面空着」和「列表没虚拟化好」。</summary>
    private static string Chatty(object? pageVm)
    {
        if (pageVm is null)
        {
            return "-";
        }

        var property = pageVm.GetType().GetProperty("Rows") ?? pageVm.GetType().GetProperty("Entries");
        var rows = property?.GetValue(pageVm) is System.Collections.ICollection collection
            ? collection.Count.ToString(CultureInfo.InvariantCulture)
            : "-";
        var all = pageVm.GetType()
            .GetField("_all", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?.GetValue(pageVm) is System.Collections.ICollection everything
            ? everything.Count.ToString(CultureInfo.InvariantCulture)
            : "-";
        return rows + "/" + all;
    }

    private static int NavRounds()
    {
        return int.TryParse(Environment.GetEnvironmentVariable("CKANUI_NAV_ROUNDS"), out var rounds) && rounds > 0 ? rounds : 2;
    }

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

        var executable = 0;
        var blocked = 0;
        foreach (var property in page.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length > 0 || !SafeCommandPattern.IsMatch(property.Name))
            {
                continue;
            }

            if (property.GetValue(page) is not ICommand command)
            {
                continue;
            }

            if (!command.CanExecute(null))
            {
                // 需要先选中一行才能点：如实记数，别让「0 条点击」看起来像没覆盖。
                blocked++;
                continue;
            }

            executable++;

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

        if (executable == 0)
        {
            TryWrite(path, $"[click] {key} none-executable blocked={blocked}");
        }
    }

    /// <summary>
    /// 清点页面上的按钮：哪些挂了命令（可安全点）、哪些是代码后置事件（不点，只报名字）。
    /// </summary>
    /// <remarks>
    /// 只点 VM 上的白名单命令会漏掉一类按钮：<c>Click</c> 在 code-behind 里接的按钮（窗口镶边、工作台工具条等），
    /// 它们在 VM 上根本没有 ICommand 属性，遍历命令永远看不见。这里按可视树清点，把覆盖率和盲区一起写进日志。
    /// </remarks>
    /// <summary>统计每个页面自带的样式表规模：视图内的 StyleInclude 会随页面进树重复参与样式匹配。</summary>
    private static void ReportStyles(TopLevel window, string path)
    {
        if (window is not MainWindow { } main)
        {
            return;
        }

        var stage = main.FindControl<Panel>("PageStage");
        if (stage is null)
        {
            TryWrite(path, "[styles] skip reason=no-stage");
            return;
        }

        var app = Application.Current?.Styles.Count ?? 0;
        var rows = new List<(string Name, int Styles, int Nodes)>(stage.Children.Count);
        foreach (var child in stage.Children)
        {
            var name = child.GetType().Name;
            rows.Add((name, child.Styles.Count, CountVisuals(child)));
        }

        var total = rows.Sum(row => row.Styles);
        var top = rows.OrderByDescending(row => row.Styles).Take(6)
            .Select(row => row.Name + "=" + row.Styles);
        TryWrite(
            path,
            string.Create(
                CultureInfo.InvariantCulture,
                $"[styles] app={app} pages={rows.Count} total={total} top={string.Join(',', top)}"));
    }

    private static int CountVisuals(Visual visual)
    {
        var count = 1;
        foreach (var child in visual.GetVisualChildren())
        {
            count += CountVisuals(child);
        }

        return count;
    }

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
    private static ScrollViewer? PickScroller(Visual scope)
    {
        // 优先找指针位置下面那个真正能滚的容器：DataGrid 等控件把 ScrollViewer 藏在模板里，
        // 只按「哪个 Extent 最大」挑会挑到外层不可滚的装饰容器，于是出现 reason=no-scroller。
        if (scope is Visual visual && TopLevel.GetTopLevel(visual) is { } top)
        {
            var center = new Point(top.Bounds.Width / 2, top.Bounds.Height / 2);
            if (top.GetVisualsAt(center).FirstOrDefault() is { } hit)
            {
                for (Visual? node = hit; node is not null; node = node.GetVisualParent())
                {
                    if (node is ScrollViewer { } viewer && viewer.Extent.Height - viewer.Viewport.Height > 40)
                    {
                        return viewer;
                    }
                }
            }
        }

        return scope.GetVisualDescendants()
            .OfType<ScrollViewer>()
            .Where(viewer => viewer.Extent.Height - viewer.Viewport.Height > 40)
            .OrderByDescending(viewer => viewer.Extent.Height - viewer.Viewport.Height)
            .FirstOrDefault();
    }

    private static async Task ScrollWindowAsync(TopLevel window, string path, int round, string key)
    {
        if (window is not Visual root)
        {
            return;
        }

        var scope = window is MainWindow { ActivePageView: { } active } ? active : root;

        var scroll = PickScroller(scope);
        if (scroll is null && !ReferenceEquals(scope, root))
        {
            scroll = PickScroller(root);
        }

        if (scroll is null)
        {
            var detail = string.Join(", ", scope.GetVisualDescendants().OfType<ScrollViewer>()
                .Take(6)
                .Select(viewer => $"{(string.IsNullOrEmpty(viewer.Name) ? "?" : viewer.Name)}:{viewer.Extent.Height:F0}/{viewer.Viewport.Height:F0}"));
            var seen = scope.GetVisualDescendants().OfType<ScrollViewer>().Count();
            var rows = Chatty(scope.DataContext);
            var scopeInfo = $"{scope.GetType().Name} visuals={scope.GetVisualDescendants().Count()} grids={scope.GetVisualDescendants().OfType<DataGrid>().Count()} rows={rows}";
            TryWrite(path, string.Create(CultureInfo.InvariantCulture, $"[scroll] skip {key} reason=no-scroller scrollers={seen} scope={scopeInfo} detail=[{detail}]"));
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
