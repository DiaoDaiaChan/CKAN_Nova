using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CKAN;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Services.ChangeSets;

/// <summary>
/// INT-07：把用户勾选解析成可预览的变更集（<see cref="ChangeSetPreview"/>）。
/// Core 计算全部委托给 <see cref="IChangeSetPlanner"/>，本类只负责排序、下载体积汇总、
/// 中文提示与失败语义（见 02-core-integration.md、04-mvvm-and-models.md、05-threading.md）。
/// </summary>
public sealed class ChangeSetService : IChangeSetService
{
    private const string Context = "ChangeSetService";
    private const string BuildFailedMessage = "计算变更集失败，请查看日志后重试。";
    private const string LocalFailedMessage = "生成变更预览失败，请查看日志后重试。";

    private readonly IChangeSetPlanner _planner;
    private readonly IErrorReporter? _errors;

    /// <summary>创建服务。</summary>
    /// <param name="planner">Core 侧的变更集规划器。</param>
    /// <param name="errors">错误上报器，可缺省。</param>
    public ChangeSetService(IChangeSetPlanner planner, IErrorReporter? errors = null)
    {
        _planner = planner ?? throw new ArgumentNullException(nameof(planner));
        _errors = errors;
    }

    /// <inheritdoc />
    public async Task<OperationResult<ChangeSetPreview>> BuildAsync(
        IReadOnlyCollection<ChangeRequest> requests,
        CancellationToken ct = default)
    {
        try
        {
            // Core 全是同步 API，放到线程池上算，避免阻塞 UI 线程（05-threading.md）
            return await Task.Run(() => Build(requests, includeRecommendations: true, ct), ct)
                             .ConfigureAwait(false);
        }
        catch (OperationCanceledException ex)
        {
            return OperationResult<ChangeSetPreview>.Fail("操作已取消。", ex.Message, retryable: true);
        }
        catch (Exception ex)
        {
            _errors?.Report(ex, Context + ".BuildAsync", BuildFailedMessage);
            return OperationResult<ChangeSetPreview>.Fail(BuildFailedMessage, ex.Message, retryable: false);
        }
    }

    /// <inheritdoc />
    public OperationResult<ChangeSetPreview> BuildLocal(IReadOnlyCollection<ChangeRequest> requests)
    {
        try
        {
            // 勾选时的即时反馈：不跑推荐项，无实例时返回空集而不是失败
            return Build(requests, includeRecommendations: false, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _errors?.Report(ex, Context + ".BuildLocal", LocalFailedMessage);
            return OperationResult<ChangeSetPreview>.Fail(LocalFailedMessage, ex.Message, retryable: false);
        }
    }

    /// <summary>两种入口共用的同步实现。</summary>
    private OperationResult<ChangeSetPreview> Build(
        IReadOnlyCollection<ChangeRequest> requests,
        bool includeRecommendations,
        CancellationToken ct)
    {
        var empty = EmptyPreview();

        if (requests is null || requests.Count == 0)
        {
            return OperationResult<ChangeSetPreview>.Ok(empty);
        }

        if (!_planner.HasActiveInstance)
        {
            // 快速预览允许在无实例时返回空集；完整计算必须让调用方知道原因
            return includeRecommendations
                       ? OperationResult<ChangeSetPreview>.Fail(
                             CoreChangeSetPlanner.NoInstanceMessage, null, retryable: true)
                       : OperationResult<ChangeSetPreview>.Ok(empty);
        }

        var planned = _planner.Plan(requests, includeRecommendations, ct);
        if (!planned.IsSuccess || planned.Value is null)
        {
            return OperationResult<ChangeSetPreview>.Fail(
                planned.Message ?? BuildFailedMessage, planned.Detail, planned.Retryable);
        }

        return OperationResult<ChangeSetPreview>.Ok(ToPreview(planned.Value));
    }

    /// <summary>把 Core 侧实体整理成稳定的预览：按（来源优先级, 标识）排序并汇总下载体积。</summary>
    private static ChangeSetPreview ToPreview(ChangeResolution resolution)
    {
        var items = new List<ChangeItemDto>();
        var warnings = new List<string>(resolution.Warnings);

        foreach (var request in resolution.Requests)
        {
            if (request.Target is null && request.Action != ChangeAction.Remove)
            {
                continue;
            }

            items.Add(CreateItem(request.Identifier, request.Installed, request.Target,
                                 request.Action, request.Origin, request.DownloadBytes));
        }

        foreach (var extra in resolution.ExtraModules)
        {
            items.Add(CreateItem(extra.Module.identifier, extra.Installed, extra.Module,
                                 DescribeAction(extra.Installed, extra.Module), extra.Origin,
                                 extra.DownloadBytes));
        }

        // 同一标识只保留优先级最高的一条，排序键用枚举声明顺序（UserRequested 最靠前）
        var ordered = items
                      .OrderBy(i => (int)i.Origin)
                      .ThenBy(i => i.Identifier, StringComparer.Ordinal)
                      .GroupBy(i => i.Identifier, StringComparer.Ordinal)
                      .Select(g => g.First())
                      .ToList();

        var conflicts = resolution.Conflicts
                                  .Where(c => !string.IsNullOrWhiteSpace(c))
                                  .Distinct(StringComparer.Ordinal)
                                  .ToList();

        var totalBytes = ordered.Sum(i => i.DownloadBytes ?? 0L);

        return new ChangeSetPreview(ordered, conflicts, totalBytes, warnings);
    }

    /// <summary>构造单条变更项；卸载项没有目标版本也没有下载体积。</summary>
    private static ChangeItemDto CreateItem(
        string identifier,
        CkanModule? installed,
        CkanModule? target,
        ChangeAction action,
        ChangeOrigin origin,
        long downloadBytes)
        => new ChangeItemDto(
            identifier,
            DisplayName(target, installed, identifier),
            action,
            installed?.version.ToString(),
            target?.version.ToString(),
            origin,
            action == ChangeAction.Remove ? null : downloadBytes);

    /// <summary>按已安装版本与目标版本比较得出动作。</summary>
    private static ChangeAction DescribeAction(CkanModule? installed, CkanModule target)
    {
        if (installed is null)
        {
            return ChangeAction.Install;
        }

        if (installed.version == target.version)
        {
            // 理论上已被规划器过滤，兜底按升级处理避免出现非法动作
            return ChangeAction.Upgrade;
        }

        return installed.version < target.version ? ChangeAction.Upgrade : ChangeAction.Downgrade;
    }

    /// <summary>优先用仓库里的显示名，退回到已安装名称，最后用标识。</summary>
    private static string DisplayName(CkanModule? target, CkanModule? installed, string identifier)
    {
        if (!string.IsNullOrWhiteSpace(target?.name))
        {
            return target!.name;
        }

        if (!string.IsNullOrWhiteSpace(installed?.name))
        {
            return installed!.name;
        }

        return identifier;
    }

    private static ChangeSetPreview EmptyPreview()
        => new ChangeSetPreview(
            Array.Empty<ChangeItemDto>(), Array.Empty<string>(), 0L, Array.Empty<string>());
}
