using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Services.Cleanup;

/// <summary>
/// 「哪些目录算卸载残留」的可测来源（12 篇 F-43）。
/// <para>
/// 判定逻辑（卸载结果报上来的目录、目录内容启发式、未来可能的注册表线索）在这层收口，
/// <see cref="ResidualCleanupService"/> 只负责安全校验与落盘。这样测试可以塞一个假来源，
/// 不必为了"能列出候选"去复活 CKAN 的卸载流程。
/// </para>
/// </summary>
public interface IResidualDirectorySource
{
    /// <summary>
    /// 返回候选残留目录（相对 <c>&lt;GameDir&gt;/GameData</c>、正斜杠）。实现不做安全校验——
    /// 受管内容/符号链接/越权路径一律由 <see cref="IResidualCleanupService"/> 拒绝。
    /// </summary>
    /// <param name="instance">目标实例。</param>
    OperationResult<IReadOnlyList<string>> GetCandidates(InstanceInfo instance);
}
