namespace CkanUI.Services.Abstractions;

/// <summary>
/// 安装历史的维护操作：删掉某一次操作、清空整个历史（12 篇 F-44）。
/// </summary>
/// <remarks>
/// <para>
/// 与 <see cref="IInstallHistoryService"/> 分开的理由与游戏时长相同：读历史与改历史是两种权限，
/// 页面只需知道「这个服务能不能改」，而 <see cref="IInstallHistoryService"/> 属共享契约不可改。
/// </para>
/// <para>
/// 成员全部带默认实现（DIM）：拿不到维护能力时页面隐藏删除/清空入口，而不是让按钮点了没反应。
/// </para>
/// <para>
/// 两个方法都返回「实际删掉的行数」：一次操作在 JSONL 里是条目行 + 快照行两行，
/// 页面靠这个数判断到底删没删到东西（用户可能对着已经过期的列表点了删除）。
/// </para>
/// </remarks>
public interface IInstallHistoryMaintenance
{
    /// <summary>删除时间戳为 <paramref name="timestamp"/> 的那次操作（连同它的快照行）；返回删掉的行数。</summary>
    Task<OperationResult<int>> DeleteAsync(DateTimeOffset timestamp, CancellationToken ct = default) =>
        Task.FromResult(OperationResult<int>.Fail("当前安装历史服务不支持删除。"));

    /// <summary>清空整个安装历史；返回清掉的行数。</summary>
    Task<OperationResult<int>> ClearAsync(CancellationToken ct = default) =>
        Task.FromResult(OperationResult<int>.Fail("当前安装历史服务不支持清空。"));
}
