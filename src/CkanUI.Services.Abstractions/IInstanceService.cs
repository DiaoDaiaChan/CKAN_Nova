using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Services.Abstractions;

/// <summary>
/// 包装 Core 的 GameInstanceManager（02 篇 §5）。切换实例的完整序列由实现内部保证，
/// 调用方只需要 await 一个方法；事件已在 UI 线程封送。
/// </summary>
public interface IInstanceService
{
    IReadOnlyList<InstanceInfo> Instances { get; }

    InstanceInfo? Current { get; }

    /// <summary>实例列表变化（增删改）。已在 UI 线程触发。</summary>
    event Action? InstancesChanged;

    /// <summary>当前实例变化。已在 UI 线程触发。</summary>
    event Action<InstanceInfo?>? CurrentChanged;

    Task<OperationResult<IReadOnlyList<InstanceInfo>>> RefreshAsync(CancellationToken ct = default);

    /// <summary>切换实例：释放旧注册表锁 → SetCurrentInstance → 取新锁 → 重建依赖服务。</summary>
    Task<OperationResult<InstanceInfo>> SwitchAsync(string instanceName, CancellationToken ct = default);

    Task<OperationResult<InstanceInfo>> AddAsync(string path, string? name = null, CancellationToken ct = default);

    Task<OperationResult<InstanceInfo>> CloneAsync(string sourceName, string newName, CancellationToken ct = default);

    Task<OperationResult<bool>> RemoveAsync(string instanceName, CancellationToken ct = default);

    /// <summary>
    /// 重命名实例：只改配置里的实例名与自启动指向，不移动、不改写游戏目录（与 Core 的
    /// GameInstanceManager.RenameInstance 一致）。名称被占用时返回可重试失败。
    /// </summary>
    Task<OperationResult<InstanceInfo>> RenameAsync(string instanceName, string newName, CancellationToken ct = default);

    /// <summary>
    /// 游戏进程是否正在运行（12 篇 F-33：运行中安装会与游戏抢文件）。
    /// <para>
    /// 判据是"有进程的可执行文件位于该实例的游戏目录之下"，只做保守推断：探测不到就返回
    /// <c>false</c>（宁可少警告，不要误拦）。<paramref name="instanceName"/> 为空表示当前实例。
    /// </para>
    /// </summary>
    bool IsGameRunning(string instanceName = "") => false;
}
