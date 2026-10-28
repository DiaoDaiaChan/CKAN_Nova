using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Diagnostics;

namespace CkanUI.App.Tests.Automation;

/// <summary>
/// 单个交互动作的执行结果报告。
/// </summary>
public sealed record ActionResult(
    DiscoveredControlInfo Target,
    bool Succeeded,
    bool WasExecutable,
    TimeSpan Elapsed,
    string? ErrorMessage,
    Exception? Exception);

/// <summary>
/// 无头交互执行引擎：在无头环境下触发控件的 Click 或 Command 操作，
/// 校验执行安全性、无异常抛出并排空 UI 调度队列。
/// </summary>
public static class InteractiveActionExecutor
{
    public static ActionResult Execute(DiscoveredControlInfo target)
    {
        ArgumentNullException.ThrowIfNull(target);

        var sw = Stopwatch.StartNew();
        var wasExecutable = target.IsEnabled && target.CanExecute;

        // 如果控件当前被禁用或不可执行，正常记录并不强行触发异常
        if (!wasExecutable)
        {
            sw.Stop();
            return new ActionResult(
                Target: target,
                Succeeded: true,
                WasExecutable: false,
                Elapsed: sw.Elapsed,
                ErrorMessage: null,
                Exception: null);
        }

        try
        {
            if (target.Command is not null)
            {
                // 优先走 Command 链路
                target.Command.Execute(target.CommandParameter);
            }
            else if (target.Control is Button btn)
            {
                // 模拟点击事件路由
                btn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            else if (target.Control is MenuItem mi)
            {
                mi.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            }

            // 排空异步和 UI 调度任务，确保任务不挂起
            HeadlessAppHost.PumpJobs();

            sw.Stop();
            return new ActionResult(
                Target: target,
                Succeeded: true,
                WasExecutable: true,
                Elapsed: sw.Elapsed,
                ErrorMessage: null,
                Exception: null);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ActionResult(
                Target: target,
                Succeeded: false,
                WasExecutable: true,
                Elapsed: sw.Elapsed,
                ErrorMessage: ex.Message,
                Exception: ex);
        }
    }

    /// <summary>
    /// 批量执行一组发现的交互项，并生成结构化执行报表。
    /// </summary>
    public static IReadOnlyList<ActionResult> ExecuteBatch(IEnumerable<DiscoveredControlInfo> targets)
    {
        var results = new List<ActionResult>();
        foreach (var target in targets)
        {
            results.Add(Execute(target));
        }
        return results;
    }
}
