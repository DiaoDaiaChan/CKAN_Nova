namespace CkanUI.ViewModels.Pages.Instances;

/// <summary>
/// 实例页的延时器（18 篇 §4 的 T3/T4/T5 需要「到点再释放」）。抽成接口是为了让测试用
/// 可控时钟推进时间，而不是真的等 30 秒；写法与模组页的 <c>IModsDelayScheduler</c> 保持一致。
/// </summary>
public interface IInstancesDelayScheduler
{
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

/// <summary>生产实现：<see cref="Task.Delay(TimeSpan, CancellationToken)"/>。</summary>
public sealed class TaskInstancesDelayScheduler : IInstancesDelayScheduler
{
    /// <summary>共享实例：默认属性注入用，无状态。</summary>
    public static TaskInstancesDelayScheduler Instance { get; } = new();

    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        delay <= TimeSpan.Zero ? Task.CompletedTask : Task.Delay(delay, cancellationToken);
}
