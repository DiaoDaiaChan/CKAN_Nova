using System;
using System.Collections.Generic;
using System.Threading;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Services.ChangeSets;

namespace CkanUI.Services.Tests;

/// <summary>
/// 离线 fake：直接给定解析结果或抛出指定异常，用于验证 ChangeSetService 的
/// 排序、体积汇总、无实例语义与异常上报，不必造真实仓库索引。
/// </summary>
public sealed class ChangeSetFakePlanner : IChangeSetPlanner
{
    /// <summary>是否已选中实例。</summary>
    public bool HasActiveInstance { get; set; } = true;

    /// <summary>要返回的解析结果，为 null 时返回空结果。</summary>
    public ChangeResolution? Resolution { get; set; }

    /// <summary>非 null 时 Plan 直接抛出该异常。</summary>
    public Exception? Failure { get; set; }

    /// <summary>Plan 被调用的次数。</summary>
    public int PlanCalls { get; private set; }

    /// <summary>最后一次调用是否要求计算推荐项。</summary>
    public bool LastIncludeRecommendations { get; private set; }

    /// <inheritdoc />
    public OperationResult<ChangeResolution> Plan(
        IReadOnlyCollection<ChangeRequest> requests,
        bool includeRecommendations,
        CancellationToken ct)
    {
        PlanCalls++;
        LastIncludeRecommendations = includeRecommendations;

        if (Failure is not null)
        {
            throw Failure;
        }

        return OperationResult<ChangeResolution>.Ok(
            Resolution ?? new ChangeResolution(
                Array.Empty<ResolvedChangeRequest>(),
                Array.Empty<ResolvedModule>(),
                Array.Empty<string>(),
                Array.Empty<string>()));
    }
}
