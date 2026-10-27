namespace CkanUI.ViewModels.Pages.Mods;

/// <summary>
/// 搜索防抖的延时来源（VM-01）。抽成接口的唯一目的是让防抖逻辑可 headless 测试：
/// 生产实现用 <see cref="TaskModsDelayScheduler"/>（真实 `Task.Delay`），测试用即时完成的假实现。
/// </summary>
public interface IModsDelayScheduler
{
    /// <summary>等待指定时长；被取消时抛 <see cref="OperationCanceledException"/>。</summary>
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

/// <summary>生产用延时实现。</summary>
public sealed class TaskModsDelayScheduler : IModsDelayScheduler
{
    /// <summary>共享实例（无可变状态）。</summary>
    public static TaskModsDelayScheduler Instance { get; } = new();

    /// <inheritdoc />
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        delay <= TimeSpan.Zero ? Task.CompletedTask : Task.Delay(delay, cancellationToken);
}
