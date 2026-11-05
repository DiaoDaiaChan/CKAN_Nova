using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using log4net;

namespace CkanUI.Services.History;

/// <summary>
/// 装饰真安装器：每次 <see cref="ApplyAsync"/> 结束后落一条安装历史（12 篇 F-44 "每次安装/卸载/升级一条"）。
/// </summary>
/// <remarks>
/// <para>
/// 选装饰器而不是改 <c>InstallerService</c>：安装流水线只有一个入口
/// （<see cref="IInstallerService.ApplyAsync"/>），包一层就能覆盖所有调用点——页面、ModPack 编排、
/// 未来的重装；而 <c>InstallerService</c> 保持不知道"历史"这件事。
/// 接线只需 DI 里把 <see cref="IInstallerService"/> 指到本类（见设计文档）。
/// </para>
/// <para>
/// 失败也记：用户要能在历史里看见"那一次装挂了"。记历史本身的异常只记日志，绝不能
/// 影响安装结果——历史是附属品。
/// </para>
/// </remarks>
public sealed class HistoryRecordingInstaller : IInstallerService
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(HistoryRecordingInstaller));

    private readonly IInstallerService _inner;
    private readonly InstallHistoryRecorder _recorder;
    private readonly IRegistryService _registry;

    public HistoryRecordingInstaller(
        IInstallerService inner,
        InstallHistoryRecorder recorder,
        IRegistryService registry)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _recorder = recorder ?? throw new ArgumentNullException(nameof(recorder));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    /// <inheritdoc />
    public async Task<OperationResult<InstallReport>> ApplyAsync(
        ChangeSetPreview changeSet,
        IProgress<OperationProgress>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(changeSet);

        var applied = await _inner.ApplyAsync(changeSet, progress, ct).ConfigureAwait(false);

        // "从历史重装"自己记账（操作类型是 Restore），别为同一次操作落两条。
        if (InstallHistoryScope.IsSuppressed)
        {
            return applied;
        }

        try
        {
            Record(changeSet, applied);
        }
        catch (Exception ex)
        {
            Log.Warn("写入安装历史失败（不影响本次安装结果）。", ex);
        }

        return applied;
    }

    private void Record(ChangeSetPreview changeSet, OperationResult<InstallReport> applied)
    {
        var report = applied.Value;
        var failed = report is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : new HashSet<string>(report.Failed, StringComparer.Ordinal);

        // B1：警告不是失败。只有失败项（或整体失败）才让这次记录落到 PartiallyFailed/Failed。
        var succeededAll = applied.IsSuccess && report is not null && report.IsFullSuccess;
        var changes = changeSet.Items
            .Select(item => new InstallHistoryChange(
                item.Identifier,
                item.Action,
                item.FromVersion,
                item.ToVersion,
                // 整体失败时任何一项都不能算成功（失败清单可能只列了首个错误）。
                applied.IsSuccess && report is not null && !failed.Contains(item.Identifier)))
            .ToList();

        var outcome = applied switch
        {
            { IsSuccess: true } when succeededAll => InstallHistoryOutcome.Succeeded,
            { IsSuccess: true } => InstallHistoryOutcome.PartiallyFailed,
            _ => InstallHistoryOutcome.Failed,
        };

        var message = applied.IsSuccess
            ? null
            : applied.Message ?? "安装失败。";

        var recorded = _recorder.Record(
            new InstallHistoryRecordRequest(
                InferOperation(changeSet),
                changes,
                outcome,
                message),
            InstalledModules());

        if (!recorded.IsSuccess)
        {
            Log.Warn($"写入安装历史失败：{recorded.Message}");
        }
    }

    /// <summary>整份变更集算哪一类操作：全是卸载 → 卸载；含新装 → 安装；其余按升级/替换。</summary>
    private static InstallHistoryOperation InferOperation(ChangeSetPreview changeSet)
    {
        if (changeSet.Items.Count == 0)
        {
            return InstallHistoryOperation.Install;
        }

        var actions = changeSet.Items.Select(item => item.Action).ToList();
        if (actions.All(action => action == ChangeAction.Remove))
        {
            return InstallHistoryOperation.Remove;
        }

        if (actions.All(action => action == ChangeAction.Downgrade))
        {
            return InstallHistoryOperation.Downgrade;
        }

        if (actions.All(action => action is ChangeAction.Upgrade or ChangeAction.Downgrade))
        {
            return InstallHistoryOperation.Upgrade;
        }

        if (actions.Any(action => action == ChangeAction.Replace))
        {
            return InstallHistoryOperation.Replace;
        }

        return actions.Any(action => action == ChangeAction.Install)
            ? InstallHistoryOperation.Install
            : InstallHistoryOperation.Upgrade;
    }

    /// <summary>安装结束后注册表已被刷新；读到的就是"这次操作之后"的已装集合。</summary>
    private IReadOnlyList<InstalledModuleVersion> InstalledModules()
    {
        var installed = _registry.Current.Installed;
        if (installed.Count == 0)
        {
            return Array.Empty<InstalledModuleVersion>();
        }

        return installed
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new InstalledModuleVersion(pair.Key, pair.Value.Version))
            .ToList();
    }
}
