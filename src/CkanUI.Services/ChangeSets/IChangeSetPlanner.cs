using System.Collections.Generic;
using System.Threading;
using CKAN;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Services.ChangeSets;

/// <summary>
/// 一条用户请求在 Core 侧解析后的结果。
/// <see cref="Target"/> 为 null 表示该请求没能在仓库索引里解析出可安装版本，
/// 原因由规划器写入 <see cref="ChangeResolution.Warnings"/>。
/// </summary>
/// <param name="Identifier">模组标识。</param>
/// <param name="Action">用户请求的动作。</param>
/// <param name="Origin">变更来源。</param>
/// <param name="Installed">当前已安装版本，未安装为 null。</param>
/// <param name="Target">目标版本，卸载或无法解析时为 null。</param>
/// <param name="DownloadBytes">预计需要下载的字节数，命中下载缓存或无需下载时为 0。</param>
public sealed record ResolvedChangeRequest(
    string Identifier,
    ChangeAction Action,
    ChangeOrigin Origin,
    CkanModule? Installed,
    CkanModule? Target,
    long DownloadBytes);

/// <summary>依赖闭包或推荐项里的一项，动作由服务层按已安装版本与目标版本比较得出。</summary>
/// <param name="Module">目标模组。</param>
/// <param name="Installed">当前已安装版本，未安装为 null。</param>
/// <param name="Origin">变更来源。</param>
/// <param name="DownloadBytes">预计需要下载的字节数。</param>
public sealed record ResolvedModule(
    CkanModule Module,
    CkanModule? Installed,
    ChangeOrigin Origin,
    long DownloadBytes);

/// <summary>
/// Core 计算的原始产出，只含实体与冲突/警告文本，不含排序与体积汇总（由服务层负责）。
/// </summary>
/// <param name="Requests">用户显式请求解析结果。</param>
/// <param name="ExtraModules">依赖闭包与推荐项带来的额外模组。</param>
/// <param name="Conflicts">冲突描述，每条一行，已含模组标识。</param>
/// <param name="Warnings">降级为提示的说明。</param>
public sealed record ChangeResolution(
    IReadOnlyList<ResolvedChangeRequest> Requests,
    IReadOnlyList<ResolvedModule> ExtraModules,
    IReadOnlyList<string> Conflicts,
    IReadOnlyList<string> Warnings)
{
    /// <summary>
    /// 与 <see cref="Conflicts"/> 一一对应的结构化冲突（B0/B2）。以 <c>init</c> 属性追加，
    /// 位置参数保持不变——既有构造点（含测试 fake）一个都不用改。
    /// </summary>
    public IReadOnlyList<ConflictEntry> ConflictEntries { get; init; } = Array.Empty<ConflictEntry>();
}

/// <summary>
/// 变更集计算里唯一触达 CKAN Core 的一层（INT-07，见 02-core-integration.md）。
/// 抽出接口是为了让 <see cref="ChangeSetService"/> 的排序、体积汇总与失败语义可以离线测试，
/// 而不必在测试里造出真实的仓库索引与游戏实例。
/// </summary>
public interface IChangeSetPlanner
{
    /// <summary>当前是否已选中游戏实例。</summary>
    bool HasActiveInstance { get; }

    /// <summary>同步计算一次变更集；异常直接向外抛，由调用方统一转成失败结果。</summary>
    /// <param name="requests">用户请求集合。</param>
    /// <param name="includeRecommendations">是否计算推荐项。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>解析结果；无活动实例时返回可重试的失败。</returns>
    OperationResult<ChangeResolution> Plan(
        IReadOnlyCollection<ChangeRequest> requests,
        bool includeRecommendations,
        CancellationToken ct);
}
