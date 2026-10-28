using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using log4net;

namespace CkanUI.Services.History;

/// <summary>
/// 安装历史的实现（12 篇 F-44 / 13 篇 UI-23）：读取、追加、"从历史重装"。
/// </summary>
/// <remarks>
/// <para>
/// 重装不自己拼安装逻辑：一律 <see cref="IChangeSetService.BuildAsync"/> 求变更集
/// （由 Core 的 RelationshipResolver 带出依赖与冲突），再交给 <see cref="IInstallerService.ApplyAsync"/>。
/// 与 <c>ModPackService</c> 的编排一致；有冲突时**不**调用 ApplyAsync。
/// </para>
/// <para>
/// 快照里"现在还装着吗"由本类读 <see cref="IRegistryService.Current"/> 现算，
/// 落盘的只有标识与版本——这样历史文件不会因为注册表变化而失真。
/// </para>
/// </remarks>
public sealed class InstallHistoryService : IInstallHistoryService, IInstallHistoryMaintenance
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(InstallHistoryService));

    private readonly InstallHistoryRecorder _recorder;
    private readonly IRegistryService _registry;
    private readonly IChangeSetService _changeSets;
    private readonly IInstallerService _installer;

    public InstallHistoryService(
        InstallHistoryRecorder recorder,
        IRegistryService registry,
        IChangeSetService changeSets,
        IInstallerService installer)
    {
        _recorder = recorder ?? throw new ArgumentNullException(nameof(recorder));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _changeSets = changeSets ?? throw new ArgumentNullException(nameof(changeSets));
        _installer = installer ?? throw new ArgumentNullException(nameof(installer));
    }

    /// <summary>便捷构造：内部自建 <see cref="InstallHistoryRecorder"/>。</summary>
    public InstallHistoryService(
        IInstanceService instances,
        IRegistryService registry,
        IChangeSetService changeSets,
        IInstallerService installer,
        int maxLines = InstallHistoryStore.DefaultMaxLines,
        Func<DateTimeOffset>? clock = null)
        : this(new InstallHistoryRecorder(instances, maxLines, clock), registry, changeSets, installer)
    {
    }

    /// <inheritdoc />
    public bool HasActiveInstance => _recorder.FilePath is not null;

    /// <inheritdoc />
    public string? InstanceName => _recorder.InstanceName;

    /// <inheritdoc />
    public Task<OperationResult<IReadOnlyList<InstallHistoryEntry>>> LoadAsync(
        int? limit = null,
        CancellationToken ct = default)
    {
        if (ct.IsCancellationRequested)
        {
            return Task.FromResult(OperationResult<IReadOnlyList<InstallHistoryEntry>>.Fail(
                "操作已取消。",
                retryable: true));
        }

        var store = _recorder.TryOpenStore();
        if (store is null)
        {
            return Task.FromResult(OperationResult<IReadOnlyList<InstallHistoryEntry>>.Fail(
                "当前没有可用的游戏实例，无法读取安装历史。"));
        }

        try
        {
            IReadOnlyList<InstallHistoryEntry> entries = Compose(store.Read());
            if (limit is > 0 && entries.Count > limit.Value)
            {
                entries = entries.Take(limit.Value).ToList();
            }

            return Task.FromResult(OperationResult<IReadOnlyList<InstallHistoryEntry>>.Ok(entries));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("读取安装历史失败。", ex);
            return Task.FromResult(OperationResult<IReadOnlyList<InstallHistoryEntry>>.Fail(
                "读取安装历史失败，请检查实例目录的读权限后重试。",
                ex.Message,
                retryable: true));
        }
    }

    /// <inheritdoc />
    public Task<OperationResult<int>> DeleteAsync(DateTimeOffset timestamp, CancellationToken ct = default)
    {
        if (ct.IsCancellationRequested)
        {
            return Task.FromResult(OperationResult<int>.Fail("操作已取消。", retryable: true));
        }

        // 删盘是同步小文件操作，用 Task.FromResult 包一层就够，不额外起线程。
        return Task.FromResult(_recorder.DeleteAt(timestamp));
    }

    /// <inheritdoc />
    public Task<OperationResult<int>> ClearAsync(CancellationToken ct = default)
    {
        if (ct.IsCancellationRequested)
        {
            return Task.FromResult(OperationResult<int>.Fail("操作已取消。", retryable: true));
        }

        return Task.FromResult(_recorder.Clear());
    }

    /// <inheritdoc />
    public async Task<OperationResult<InstallHistoryRestorePlan>> PreviewRestoreAsync(
        InstallHistoryRestoreRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            var prepared = await PrepareAsync(request, ct).ConfigureAwait(false);
            return prepared.IsSuccess
                ? OperationResult<InstallHistoryRestorePlan>.Ok(prepared.Value!.Plan)
                : OperationResult<InstallHistoryRestorePlan>.Fail(
                    prepared.Message ?? "无法计算重装变更集。",
                    prepared.Detail,
                    prepared.Retryable);
        }
        catch (OperationCanceledException)
        {
            return OperationResult<InstallHistoryRestorePlan>.Fail("操作已取消。", retryable: true);
        }
        catch (Exception ex)
        {
            Log.Error("计算重装变更集时异常。", ex);
            return OperationResult<InstallHistoryRestorePlan>.Fail(
                "计算重装变更集时出现异常，请重试。",
                ex.Message,
                retryable: true);
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<InstallHistoryRestoreResult>> RestoreAsync(
        InstallHistoryRestoreRequest request,
        IProgress<OperationProgress>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            var prepared = await PrepareAsync(request, ct).ConfigureAwait(false);
            if (!prepared.IsSuccess)
            {
                return OperationResult<InstallHistoryRestoreResult>.Fail(
                    prepared.Message ?? "无法计算重装变更集。",
                    prepared.Detail,
                    prepared.Retryable);
            }

            var preparation = prepared.Value!;
            var plan = preparation.Plan;
            if (plan.HasConflicts)
            {
                return OperationResult<InstallHistoryRestoreResult>.Fail(
                    "重装目标存在依赖冲突，已中止：" + string.Join("；", plan.Conflicts),
                    retryable: true);
            }

            if (plan.IsEmpty || preparation.ChangeSet is null)
            {
                return OperationResult<InstallHistoryRestoreResult>.Ok(new InstallHistoryRestoreResult(
                    plan,
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    Array.Empty<string>()));
            }

            OperationResult<InstallReport> applied;
            using (InstallHistoryScope.Suppress())
            {
                applied = await _installer
                    .ApplyAsync(preparation.ChangeSet, progress, ct)
                    .ConfigureAwait(false);
            }

            if (!applied.IsSuccess || applied.Value is null)
            {
                return OperationResult<InstallHistoryRestoreResult>.Fail(
                    applied.Message ?? "重装失败。",
                    applied.Detail,
                    applied.Retryable);
            }

            var report = applied.Value;
            var result = new InstallHistoryRestoreResult(
                plan,
                report.Installed,
                report.Removed,
                report.Failed,
                report.Warnings);

            RecordRestore(plan, report);
            return OperationResult<InstallHistoryRestoreResult>.Ok(result);
        }
        catch (OperationCanceledException)
        {
            return OperationResult<InstallHistoryRestoreResult>.Fail("操作已取消。", retryable: true);
        }
        catch (Exception ex)
        {
            Log.Error("从历史重装时异常。", ex);
            return OperationResult<InstallHistoryRestoreResult>.Fail(
                "重装过程出现异常，请重试。",
                ex.Message,
                retryable: true);
        }
    }

    /// <inheritdoc />
    public Task<OperationResult<InstallHistoryEntry>> RecordAsync(
        InstallHistoryRecordRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (ct.IsCancellationRequested)
        {
            return Task.FromResult(OperationResult<InstallHistoryEntry>.Fail(
                "操作已取消。",
                retryable: true));
        }

        var modules = request.InstalledModules ?? CurrentInstalledModules();
        return Task.FromResult(_recorder.Record(request, modules));
    }

    /// <summary>重装成功后自己记一条，标成 <see cref="InstallHistoryOperation.Restore"/>。</summary>
    private void RecordRestore(InstallHistoryRestorePlan plan, InstallReport report)
    {
        var failed = new HashSet<string>(report.Failed, StringComparer.Ordinal);
        var changes = plan.Items
            .Select(item => new InstallHistoryChange(
                item.Identifier,
                item.Action,
                item.FromVersion,
                item.ToVersion,
                !failed.Contains(item.Identifier)))
            .ToList();

        var outcome = report.Failed.Count == 0 && report.Warnings.Count == 0
            ? InstallHistoryOutcome.Succeeded
            : report.Installed.Count == 0 && report.Removed.Count == 0
                ? InstallHistoryOutcome.Failed
                : InstallHistoryOutcome.PartiallyFailed;

        var message = $"从 {plan.Timestamp.ToLocalTime():yyyy-MM-dd HH:mm:ss} 的历史快照重装。";
        // RecordAsync 内部是同步落盘，这里直接等它完成即可（不引入额外 async 状态机）。
        var recorded = RecordAsync(
            new InstallHistoryRecordRequest(InstallHistoryOperation.Restore, changes, outcome, message),
            CancellationToken.None).GetAwaiter().GetResult();

        if (!recorded.IsSuccess)
        {
            Log.Warn($"重装成功但写入安装历史失败：{recorded.Message}");
        }
    }

    /// <summary>求解重装的变更集；返回计划与原始变更集（执行时才需要后者）。</summary>
    private async Task<OperationResult<RestorePreparation>> PrepareAsync(
        InstallHistoryRestoreRequest request,
        CancellationToken ct)
    {
        var store = _recorder.TryOpenStore();
        if (store is null)
        {
            return OperationResult<RestorePreparation>.Fail("当前没有可用的游戏实例，无法从历史重装。");
        }

        var entries = Compose(store.Read());
        var target = entries.FirstOrDefault(entry => entry.Snapshot is not null
            && entry.Snapshot.TakenAt == request.Timestamp);
        if (target?.Snapshot is null)
        {
            return OperationResult<RestorePreparation>.Fail("历史里找不到该时间点的已装集合快照，无法重装。");
        }

        var snapshot = target.Snapshot;
        var wanted = request.Identifiers is { Count: > 0 } filter
            ? snapshot.Modules.Where(module => filter.Contains(module.Identifier, StringComparer.Ordinal)).ToList()
            : snapshot.Modules.ToList();
        if (wanted.Count == 0)
        {
            return OperationResult<RestorePreparation>.Fail("所选历史快照里没有可重装的模组。");
        }

        var installed = CurrentInstalledModules();
        var requests = new List<ChangeRequest>();
        foreach (var module in wanted)
        {
            var current = installed
                .FirstOrDefault(item => string.Equals(item.Identifier, module.Identifier, StringComparison.Ordinal));
            if (current is not null
                && string.Equals(current.Version, module.Version, StringComparison.Ordinal))
            {
                // 已经是这个版本，不算一次变更（"将被变更的模组数量"要诚实）。
                continue;
            }

            requests.Add(new ChangeRequest(module.Identifier, ChangeAction.Install, module.Version));
        }

        if (request.RemoveExtras)
        {
            var wantedIds = new HashSet<string>(wanted.Select(module => module.Identifier), StringComparer.Ordinal);
            foreach (var item in installed)
            {
                if (!wantedIds.Contains(item.Identifier))
                {
                    requests.Add(new ChangeRequest(item.Identifier, ChangeAction.Remove));
                }
            }
        }

        if (requests.Count == 0)
        {
            return OperationResult<RestorePreparation>.Ok(new RestorePreparation(
                new InstallHistoryRestorePlan(
                    request.Timestamp,
                    Array.Empty<ChangeItemDto>(),
                    Array.Empty<string>(),
                    0),
                null));
        }

        var built = await _changeSets.BuildAsync(requests, ct).ConfigureAwait(false);
        if (!built.IsSuccess || built.Value is null)
        {
            return OperationResult<RestorePreparation>.Fail(
                built.Message ?? "无法计算重装变更集。",
                built.Detail,
                built.Retryable);
        }

        var changeSet = built.Value;
        var plan = new InstallHistoryRestorePlan(
            request.Timestamp,
            changeSet.Items,
            changeSet.Conflicts,
            changeSet.EstimatedDownloadBytes);
        return OperationResult<RestorePreparation>.Ok(new RestorePreparation(plan, changeSet));
    }

    /// <summary>把落盘行拼成页面要的条目（新的在前），并给每条挂上它自己的快照。</summary>
    private List<InstallHistoryEntry> Compose(IReadOnlyList<InstallHistoryLine> lines)
    {
        var installed = CurrentInstalledModules()
            .ToDictionary(item => item.Identifier, item => item.Version, StringComparer.Ordinal);

        var snapshots = new Dictionary<DateTimeOffset, InstallHistoryLine>();
        foreach (var line in lines)
        {
            if (line.IsSnapshot)
            {
                snapshots[line.At] = line; // 同一时间戳以最后一行为准
            }
        }

        var entries = new List<InstallHistoryEntry>();
        foreach (var line in lines)
        {
            if (line.IsSnapshot)
            {
                continue;
            }

            var entry = new InstallHistoryEntry(
                line.At,
                line.Instance,
                line.Operation,
                line.Outcome,
                line.Changes,
                line.Message);

            if (snapshots.TryGetValue(line.At, out var snapshot))
            {
                entry = entry with { Snapshot = Describe(snapshot, installed) };
            }

            entries.Add(entry);
        }

        entries.Sort((left, right) => right.Timestamp.CompareTo(left.Timestamp));
        return entries;
    }

    private static InstallHistorySnapshot Describe(
        InstallHistoryLine line,
        IReadOnlyDictionary<string, string>? installed = null)
        => new(line.At, line.Instance, Describe(line.Modules, installed));

    private static IReadOnlyList<InstallHistoryModule> Describe(
        IReadOnlyList<InstalledModuleVersion> modules,
        IReadOnlyDictionary<string, string>? installed = null)
    {
        var result = new List<InstallHistoryModule>(modules.Count);
        foreach (var module in modules)
        {
            var isInstalled = installed is not null && installed.ContainsKey(module.Identifier);
            var currentVersion = installed is not null
                && installed.TryGetValue(module.Identifier, out var version)
                    ? version
                    : null;
            result.Add(new InstallHistoryModule(module.Identifier, module.Version, isInstalled, currentVersion));
        }

        return result;
    }

    /// <summary>当前注册表的已装集合（标识 + 版本），按标识排序以便稳定显示。</summary>
    private IReadOnlyList<InstalledModuleVersion> CurrentInstalledModules()
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

    /// <summary>求解结果 + 原始变更集（执行装包时要用未裁剪的那份）。</summary>
    private sealed record RestorePreparation(InstallHistoryRestorePlan Plan, ChangeSetPreview? ChangeSet);
}
