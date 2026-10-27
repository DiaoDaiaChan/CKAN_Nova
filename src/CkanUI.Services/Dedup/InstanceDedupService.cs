using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Services.Unmanaged;

namespace CkanUI.Services.Dedup;

/// <summary>
/// 跨实例去重服务（F-41 / UI-21 / INT-09）：实例解析走 <see cref="IInstanceService"/>，
/// 受管清单走 <see cref="IManagedPathProvider"/>（与未管理文件页同一来源），
/// 扫描与执行分别是纯文件系统的 <see cref="DedupScanner"/> 与 <see cref="DedupExecutor"/>。
/// </summary>
public sealed class InstanceDedupService : IInstanceDedupService
{
    private const string ScanContext = "跨实例去重扫描";
    private const string ExecuteContext = "跨实例去重执行";

    private readonly IInstanceService _instances;
    private readonly IManagedPathProvider _managed;
    private readonly IDedupLinker _linker;
    private readonly IErrorReporter _errors;

    public InstanceDedupService(
        IInstanceService instances,
        IManagedPathProvider managed,
        IDedupLinker linker,
        IErrorReporter errors)
    {
        _instances = instances ?? throw new ArgumentNullException(nameof(instances));
        _managed = managed ?? throw new ArgumentNullException(nameof(managed));
        _linker = linker ?? throw new ArgumentNullException(nameof(linker));
        _errors = errors ?? throw new ArgumentNullException(nameof(errors));
    }

    /// <inheritdoc />
    public Task<OperationResult<DedupScanReport>> ScanAsync(DedupScanRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return GuardAsync(() => Scan(request, ct), ScanContext);
    }

    /// <inheritdoc />
    public Task<OperationResult<DedupExecuteReport>> ExecuteAsync(DedupExecuteRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return GuardAsync(() => Execute(request, ct), ExecuteContext);
    }

    private OperationResult<DedupScanReport> Scan(DedupScanRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var support = _linker.Support;
        var targets = ResolveInstances(request.InstanceNames);
        if (targets.Count == 0)
        {
            return OperationResult<DedupScanReport>.Ok(DedupScanReport.Empty(support));
        }

        var skipped = new List<DedupSkipInfo>();
        var tally = new DedupScanTally();
        var candidates = new List<DedupCandidateFile>();

        foreach (var instance in targets)
        {
            ct.ThrowIfCancellationRequested();
            var managed = _managed.GetManagedPaths(instance);
            if (!managed.IsSuccess || managed.Value is null)
            {
                // 拿不到受管清单就无法保证「不改受管文件」，整实例跳过而不是冒险。
                tally.Skip(DedupSkipKind.Unreadable, new DedupSkipInfo(
                    instance.Name,
                    string.Empty,
                    DedupSkipKind.Unreadable,
                    $"读不到受管文件清单，已跳过该实例：{managed.Message ?? "未知原因"}"), skipped);
                continue;
            }

            var context = new DedupInstanceContext(
                instance.Name,
                DedupInstanceContext.ResolveGameData(instance.GameDir),
                managed.Value);

            candidates.AddRange(DedupScanner.Enumerate(
                context, request.MinimumFileSizeBytes, request.IncludeManagedFiles, tally, skipped, ct));
        }

        var groups = DedupScanner.Group(candidates, _linker, tally, ct);
        var report = new DedupScanReport(
            targets.Select(instance => instance.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray(),
            groups,
            skipped,
            tally.ScannedFiles,
            candidates.Count,
            tally.SmallFiles,
            tally.ManagedFiles,
            tally.LinkFiles,
            tally.UnreadableFiles,
            tally.SkippedCount,
            support);

        return OperationResult<DedupScanReport>.Ok(report);
    }

    private OperationResult<DedupExecuteReport> Execute(DedupExecuteRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var support = _linker.Support;
        if (support != DedupPlatformSupport.Supported)
        {
            // 交给执行器统一产出「不支持」结果（不写盘、不抛）。
            return OperationResult<DedupExecuteReport>.Ok(
                new DedupExecutor(_linker).Execute(request, new Dictionary<string, DedupInstanceContext>(StringComparer.Ordinal), ct));
        }

        if (request.Groups.Count == 0)
        {
            return OperationResult<DedupExecuteReport>.Ok(DedupExecuteReport.Empty(request.DryRun, support));
        }

        var contexts = new Dictionary<string, DedupInstanceContext>(StringComparer.Ordinal);
        foreach (var name in request.Groups
                     .SelectMany(group => group.Files)
                     .Select(file => file.InstanceName)
                     .Distinct(StringComparer.Ordinal))
        {
            var instance = FindInstance(name);
            if (instance is null)
            {
                continue;
            }

            var managed = _managed.GetManagedPaths(instance);
            if (!managed.IsSuccess || managed.Value is null)
            {
                continue;
            }

            contexts[name] = new DedupInstanceContext(
                instance.Name,
                DedupInstanceContext.ResolveGameData(instance.GameDir),
                managed.Value);
        }

        var report = new DedupExecutor(_linker).Execute(request, contexts, ct);
        return OperationResult<DedupExecuteReport>.Ok(report);
    }

    private List<InstanceInfo> ResolveInstances(IReadOnlyList<string>? names)
    {
        var all = _instances.Instances;
        if (names is null || names.Count == 0)
        {
            return all.ToList();
        }

        var wanted = new HashSet<string>(names.Where(name => !string.IsNullOrWhiteSpace(name)), StringComparer.Ordinal);
        return all.Where(instance => wanted.Contains(instance.Name)).ToList();
    }

    private InstanceInfo? FindInstance(string name) =>
        _instances.Instances.FirstOrDefault(instance => string.Equals(instance.Name, name, StringComparison.Ordinal));

    private async Task<OperationResult<T>> GuardAsync<T>(Func<OperationResult<T>> action, string context)
    {
        try
        {
            return await Task.Run(action).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex)
        {
            _errors.Report(ex, context, "操作已取消。");
            return OperationResult<T>.Fail("操作已取消。", ex.Message, retryable: true);
        }
        catch (Exception ex)
        {
            _errors.Report(ex, context);
            return OperationResult<T>.Fail("去重操作失败。", ex.ToString(), retryable: true);
        }
    }
}
