using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Services.Abstractions;

/// <summary>
/// 启动游戏之后的分层资源释放（18 篇 §3/§4/§7）。Core 没有「清缓存」API，
/// 所以大头只能靠「丢仓库快照 + <c>RegistryManager.DisposeAll()</c>」拿到，
/// 而这一步是可逆的：<see cref="RestoreAsync"/> 用
/// <see cref="IRegistryService.OpenAsync"/> 与 <see cref="IRepositoryService.LoadCachedAsync"/> 重新打开。
/// </summary>
/// <remarks>
/// <para>契约只描述「释放哪些层、成败如何」，不含时机：什么时候调用由实例页的启动编排
/// 按 18 篇 §4 的 T3/T4/T5 决定，服务本身同步执行、可 headless 测试。</para>
/// <para>每层独立成败：某层抛异常只记 Warn 并继续下一层，绝不让一层失败挡住后面的大头；
/// 失败信息全部收在 <see cref="ResourceReleaseReport"/> 里，本服务不通过 <c>IErrorReporter</c> 弹错。</para>
/// </remarks>
public interface IResourceReleaseService
{
    /// <summary>进度事件的固定任务 id（<see cref="OperationProgress.TaskId"/>）。</summary>
    const string ProgressTaskId = "resource-release";

    /// <summary>
    /// 当前是否处于「已释放」状态（L2 执行成功且尚未 <see cref="RestoreAsync"/>）。
    /// 消费方据此决定要不要提示用户「数据已卸载，切回列表会重新读取」。
    /// </summary>
    bool IsReleased { get; }

    /// <summary>一次释放结束（含失败与取消）后触发，携带完整报告。可在任意线程触发。</summary>
    event Action<ResourceReleaseReport>? Released;

    /// <summary>
    /// 按档位执行分层释放：保守 = L0+L1，均衡 = L0–L2，激进 = L0–L4，关闭 = 什么都不做。
    /// 同一次启动内重复调用是幂等的（已成功的层跳过），因此调用方可以在 T3/T4/T5 分三次推进同一档位。
    /// 取消时停止后续层并返回 <see cref="ResourceReleaseReport.Cancelled"/> 为 true 的报告，不抛异常。
    /// </summary>
    Task<OperationResult<ResourceReleaseReport>> ReleaseAsync(
        ResourceReleaseTier tier,
        IProgress<OperationProgress>? progress = null,
        CancellationToken ct = default);

    /// <summary>
    /// 恢复被 L2 释放掉的数据：重新打开注册表锁并按本地缓存重建仓库快照（37.9MB 级读取，R-59）。
    /// 未释放时是空操作；失败返回可重试的 <see cref="OperationResult"/>，调用方必须给出「重试 / 手动刷新」的出口。
    /// </summary>
    Task<OperationResult> RestoreAsync(CancellationToken ct = default);
}
