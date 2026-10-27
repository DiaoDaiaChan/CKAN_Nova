using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Services.Abstractions;

/// <summary>
/// 卸载残留目录的列出/预览/删除（12 篇 F-43 / 06 篇 UI-22）。
/// <para>
/// 与 <see cref="IUnmanagedService"/> 的分工：那边处理"GameData 里谁都不认识的未管理内容"，
/// 这边处理"卸载之后剩下的配置残留目录"。安全边界同样是硬约束——
/// 只允许删 <c>&lt;GameDir&gt;/GameData</c> 下的目录、拒绝符号链接、拒绝含受管文件的目录，
/// 并且在真正落盘前再校验一次（TOCTOU）。
/// </para>
/// </summary>
public interface IResidualCleanupService
{
    /// <summary>
    /// 列出候选残留目录（目录内容启发式 + 卸载结果里报上来的目录），带文件数、字节数与示例文件名。
    /// </summary>
    /// <param name="instance">目标实例。</param>
    /// <param name="reportedByUninstall">卸载时后端报上来的残留目录（相对 GameData）；可为 <c>null</c>。</param>
    /// <param name="ct">取消标记。</param>
    Task<OperationResult<IReadOnlyList<ResidualDirectoryInfo>>> ListAsync(
        InstanceInfo instance,
        IReadOnlyCollection<string>? reportedByUninstall = null,
        CancellationToken ct = default);

    /// <summary>
    /// 预览"删掉这些目录会发生什么"：逐个给出文件数、字节数、示例文件与不可删除的原因，绝不落盘。
    /// </summary>
    /// <param name="instance">目标实例。</param>
    /// <param name="relativePaths">相对 GameData 的目录路径集合。</param>
    /// <param name="ct">取消标记。</param>
    Task<OperationResult<IReadOnlyList<ResidualDirectoryInfo>>> PreviewAsync(
        InstanceInfo instance,
        IReadOnlyCollection<string> relativePaths,
        CancellationToken ct = default);

    /// <summary>
    /// 删除勾选的残留目录。受管内容/符号链接/越权路径一律跳过并计入
    /// <see cref="ResidualCleanupReport.SkippedCount"/>；真的删不动时返回可重试的失败。
    /// </summary>
    /// <param name="instance">目标实例。</param>
    /// <param name="relativePaths">相对 GameData 的目录路径集合。</param>
    /// <param name="ct">取消标记。</param>
    Task<OperationResult<ResidualCleanupReport>> DeleteAsync(
        InstanceInfo instance,
        IReadOnlyCollection<string> relativePaths,
        CancellationToken ct = default);
}
