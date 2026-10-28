using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Services.Unmanaged;

/// <summary>
/// 未管理文件服务实现（12 篇 F-42 / UI-10）：受管集合来自 <see cref="IManagedPathProvider"/>，
/// 扫描与删除逻辑在纯文件系统的 <see cref="UnmanagedScanner"/>，两者都可在 headless 测试下跑。
/// </summary>
public sealed class UnmanagedService : IUnmanagedService
{
    private static readonly IReadOnlySet<string> EmptySet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlyDictionary<string, string> EmptyDlls =
        new Dictionary<string, string>(StringComparer.Ordinal);

    private readonly IManagedPathProvider _managed;
    private readonly IManagedFileRegistrar _registrar;
    private readonly IErrorReporter _errors;

    public UnmanagedService(IManagedPathProvider managed, IManagedFileRegistrar registrar, IErrorReporter errors)
    {
        _managed = managed ?? throw new ArgumentNullException(nameof(managed));
        _registrar = registrar ?? throw new ArgumentNullException(nameof(registrar));
        _errors = errors ?? throw new ArgumentNullException(nameof(errors));
    }

    /// <inheritdoc />
    public Task<OperationResult<IReadOnlyList<UnmanagedFileInfo>>> ScanAsync(
        InstanceInfo instance, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(instance);
        return GuardAsync(() => Scan(instance, ct), "扫描未管理文件");
    }

    /// <inheritdoc />
    public Task<OperationResult<IReadOnlyList<string>>> DeleteAsync(
        InstanceInfo instance, IReadOnlyCollection<string> relativePaths,
        bool dryRun = true, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(instance);
        return GuardAsync(() => Delete(instance, relativePaths, dryRun, ct), "删除未管理文件");
    }

    /// <inheritdoc />
    public Task<OperationResult<UnmanagedAdoptResult>> AdoptAsync(
        InstanceInfo instance, IReadOnlyCollection<string> relativePaths,
        bool dryRun = false, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(instance);
        return GuardAsync(() => Adopt(instance, relativePaths, dryRun, ct), "接管未管理文件");
    }

    private OperationResult<IReadOnlyList<UnmanagedFileInfo>> Scan(InstanceInfo instance, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var managed = _managed.GetManagedPaths(instance);
        if (!managed.IsSuccess)
        {
            return OperationResult<IReadOnlyList<UnmanagedFileInfo>>.Fail(
                managed.Message ?? "读取注册表失败，无法判断哪些内容受管。",
                managed.Detail,
                managed.Retryable);
        }

        return UnmanagedScanner.Scan(
            UnmanagedScanner.ResolveGameData(instance.GameDir),
            managed.Value ?? EmptySet);
    }

    private OperationResult<UnmanagedAdoptResult> Adopt(
        InstanceInfo instance, IReadOnlyCollection<string> relativePaths, bool dryRun, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var managed = _managed.GetManagedPaths(instance);
        if (!managed.IsSuccess)
        {
            return OperationResult<UnmanagedAdoptResult>.Fail(
                managed.Message ?? "读取注册表失败，无法判断哪些内容受管。",
                managed.Detail,
                managed.Retryable);
        }

        var registered = _registrar.GetInstalledDlls(instance);
        if (!registered.IsSuccess)
        {
            return OperationResult<UnmanagedAdoptResult>.Fail(
                registered.Message ?? "读取注册表失败，无法判断已登记的手工安装 DLL。",
                registered.Detail,
                registered.Retryable);
        }

        var plan = UnmanagedAdopter.Plan(
            UnmanagedScanner.ResolveGameData(instance.GameDir),
            managed.Value ?? EmptySet,
            registered.Value ?? EmptyDlls,
            relativePaths,
            _registrar.NormalizeIdentifier);

        if (!plan.IsSuccess || plan.Value is null)
        {
            return OperationResult<UnmanagedAdoptResult>.Fail(
                plan.Message ?? "接管未管理文件失败。", plan.Detail, plan.Retryable);
        }

        var (items, additions) = plan.Value;
        if (additions.Count == 0 || dryRun)
        {
            return OperationResult<UnmanagedAdoptResult>.Ok(new UnmanagedAdoptResult(items));
        }

        var merge = _registrar.MergeDlls(instance, additions);
        if (!merge.IsSuccess)
        {
            return OperationResult<UnmanagedAdoptResult>.Fail(
                merge.Message ?? "写入注册表失败。", merge.Detail, merge.Retryable);
        }

        var outcome = merge.Value ?? new DllMergeResult(false, null);
        var backups = outcome.BackupPath is { Length: > 0 } backup
            ? new[] { backup }
            : Array.Empty<string>();
        return OperationResult<UnmanagedAdoptResult>.Ok(
            new UnmanagedAdoptResult(items, backups, Applied: outcome.Changed));
    }

    private OperationResult<IReadOnlyList<string>> Delete(
        InstanceInfo instance, IReadOnlyCollection<string> relativePaths, bool dryRun, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var managed = _managed.GetManagedPaths(instance);
        if (!managed.IsSuccess)
        {
            return OperationResult<IReadOnlyList<string>>.Fail(
                managed.Message ?? "读取注册表失败，无法判断哪些内容受管。",
                managed.Detail,
                managed.Retryable);
        }

        return UnmanagedScanner.Delete(
            UnmanagedScanner.ResolveGameData(instance.GameDir),
            managed.Value ?? EmptySet,
            relativePaths,
            dryRun);
    }

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
            return OperationResult<T>.Fail("未管理文件操作失败。", ex.ToString(), retryable: true);
        }
    }
}
