namespace CkanUI.Services.Abstractions;

/// <summary>
/// 启动/关闭编排（02 篇 §1 启动序列 ①–⑧、§5 关闭序列）。
/// 实现在 Services 层（唯一允许接触 CKAN 的地方），App 只负责在启动最早期调用它。
/// </summary>
public interface IAppBootstrapper
{
    /// <summary>按 02 篇 §1 顺序初始化 Core 并建立 UI 侧快照；失败返回失败结果。</summary>
    Task<OperationResult> StartupAsync(CancellationToken cancellationToken = default);

    /// <summary>按 02 篇 §5 顺序释放：取消进行中操作 → RegistryManager.DisposeAll() → 停止后台任务。</summary>
    Task ShutdownAsync(CancellationToken cancellationToken = default);
}
