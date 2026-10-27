using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Services.Abstractions;

/// <summary>
/// 跨实例去重服务（规格 F-41 / UI-21 / INT-09）：扫描多个游戏实例 GameData 里内容相同的文件，
/// 算出可释放空间，并把重复副本改成硬链接共享同一份数据。
/// </summary>
/// <remarks>
/// 安全边界（实现必须逐条满足，详见 docs/design/dedup.md）：
/// ① 只动 GameData 之内的普通文件；② 跳过符号链接/重解析点；
/// ③ 只有同一卷内的文件才能硬链接；④ 至少 2 个副本才成组；
/// ⑤ 默认跳过注册表受管文件（改动它们会破坏卸载语义）；
/// ⑥ 默认 dry-run，只有显式 <c>DryRun = false</c> 才写盘。
/// 跨卷、权限不足等一律降级成可读的跳过原因，不抛异常。
/// </remarks>
public interface IInstanceDedupService
{
    /// <summary>扫描指定实例（实例名集合为空表示全部实例）的 GameData，按内容哈希分组。</summary>
    Task<OperationResult<DedupScanReport>> ScanAsync(DedupScanRequest request, CancellationToken ct = default);

    /// <summary>执行去重（默认预演）：把每组除保留副本外的文件改成指向保留副本的硬链接。</summary>
    Task<OperationResult<DedupExecuteReport>> ExecuteAsync(DedupExecuteRequest request, CancellationToken ct = default);
}
