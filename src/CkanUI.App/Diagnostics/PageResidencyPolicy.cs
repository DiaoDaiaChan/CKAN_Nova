using System;

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;

namespace CkanUI.App.Diagnostics;

/// <summary>
/// 页面驻留预算的调度：在空闲回收器的 8 秒节拍上判断「用户已经静置够久」，然后把超预算的页面视图摘出舞台。
/// </summary>
/// <remarks>
/// <para>
/// 为什么挂在 <see cref="IdleMemoryReclaimer"/> 的节拍上：它是进程里唯一常驻的空闲定时器，
/// 而且已经由 <see cref="DynamicRefreshRateController"/> 在窗口挂载时启动，不必再改 MainWindow 的生命周期。
/// </para>
/// <para>
/// 为什么不能只靠 <see cref="IdleMemoryReclaimer.BeforeReclaim"/>：那条钩子只在「工作集涨够 128MB」时才走，
/// 而驻留预算要表达的是「你两分钟没动、页面也都看过了，就可以把冷页面放掉」，与该不该压堆无关。
/// </para>
/// <para>
/// 默认保留 12 页（23 页全量扫场实测 ws −65 MB）。摘掉冷页面会让再次进入那一页多一次视图重建
/// （XAML 加载 + 首屏排版，实测 40–57 ms，比首次进入还快），但不改变任何画面内容。
/// 开关是环境变量 <c>CKANUI_PAGE_RESIDENCY=&lt;保留页数&gt;</c>，设 0 关闭；
/// 静置阈值用 <c>CKANUI_PAGE_RESIDENCY_IDLE_S</c>（默认 120 秒，最小 15 秒）。
/// </para>
/// <para>
/// 预算之外还有一条「未访问预热页回收」：启动预热会预建 12 个页面树，用户始终没点开的那几页照样占内存，
/// 而预算的 keep 恰好等于预热页数、永远不会触发。这里在同一个节拍上多调一次
/// <see cref="MainWindow.TrimUnvisitedWarmPages"/>，只收「从没被切到前台」的页面，访问过的和活动页不动。
/// 阈值 <c>CKANUI_PAGE_WARM_TRIM=&lt;秒&gt;</c>（默认 240 秒，设 0 关闭）。
/// </para>
/// </remarks>
internal static class PageResidencyPolicy
{
    private const int DefaultIdleSeconds = 120;

    private const int MinimumIdleSeconds = 15;

    /// <summary>默认保留 12 页：与 MainWindow 的 WarmupOrder 等长，预热建好的页不会被预算反过来拆掉。</summary>
    private const int DefaultKeep = 12;

    /// <summary>保留的页面数（含活动页）；0 = 预算关闭。诊断与演练要读它，所以是 internal。</summary>
    internal static readonly int Keep = ReadKeep();

    /// <summary>预热页回收的等待时间：给用户留出「刚启动就会点几个页」的窗口。</summary>
    private const int DefaultWarmTrimSeconds = 240;

    private static readonly TimeSpan IdleBeforeTrim = TimeSpan.FromSeconds(ReadIdleSeconds());

    private static readonly TimeSpan WarmTrimAfter = TimeSpan.FromSeconds(ReadWarmTrimSeconds());

    private static readonly DateTime StartedUtc = DateTime.UtcNow;

    private static bool _installed;

    /// <summary>挂上空闲节拍；未启用时什么都不做（重复调用无副作用）。</summary>
    internal static void Install()
    {
        if (_installed || Keep <= 0)
        {
            return;
        }

        _installed = true;
        IdleMemoryReclaimer.IdleTick += Tick;
    }

    private static void Tick()
    {
        if (!DynamicRefreshRateController.Instance.IsIdle)
        {
            return;
        }

        if (MainWindowOf() is not { } window)
        {
            return;
        }

        if (DateTime.UtcNow - window.LastPageSwitchUtc < IdleBeforeTrim)
        {
            return;
        }

        window.TrimPageResidency(Keep);

        if (WarmTrimAfter > TimeSpan.Zero && DateTime.UtcNow - StartedUtc >= WarmTrimAfter)
        {
            // 预热页是「猜你会点」的投机成本：启动后一直没被点开的那些，静置够久就还回去。
            window.TrimUnvisitedWarmPages(Keep);
        }
    }

    private static MainWindow? MainWindowOf() =>
        Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime
            ? lifetime.MainWindow as MainWindow
            : null;

    /// <summary>未设置时为默认值；显式设成 0/负数/非数字则关闭预算（社区排障用的杀开关）。</summary>
    private static int ReadKeep()
    {
        var configured = Environment.GetEnvironmentVariable("CKANUI_PAGE_RESIDENCY");
        if (string.IsNullOrWhiteSpace(configured))
        {
            return DefaultKeep;
        }

        return int.TryParse(configured, out var keep) && keep > 0 ? keep : 0;
    }

    /// <summary>未设置为默认 240 秒；显式设成 0/负数/非数字则关闭预热页回收。</summary>
    private static int ReadWarmTrimSeconds()
    {
        var configured = Environment.GetEnvironmentVariable("CKANUI_PAGE_WARM_TRIM");
        if (string.IsNullOrWhiteSpace(configured))
        {
            return DefaultWarmTrimSeconds;
        }

        return int.TryParse(configured, out var seconds) && seconds > 0 ? seconds : 0;
    }

    private static int ReadIdleSeconds() =>
        int.TryParse(Environment.GetEnvironmentVariable("CKANUI_PAGE_RESIDENCY_IDLE_S"), out var seconds) &&
        seconds >= MinimumIdleSeconds
            ? seconds
            : DefaultIdleSeconds;
}
