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

    private readonly IManagedPathProvider _managed;
    private readonly IErrorReporter _errors;

    public UnmanagedService(IManagedPathProvider managed, IErrorReporter errors)
    {
        _managed = managed ?? throw new ArgumentNullException(nameof(managed));
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
    /// <remarks>接管（写回注册表）属于 INT-09 的后续迭代；当前只支持查看与删除，明确返回不可重试失败。</remarks>
    public Task<OperationResult<IReadOnlyList<string>>> AdoptAsync(
        InstanceInfo instance, IReadOnlyCollection<string> relativePaths, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(instance);
        return Task.FromResult(OperationResult<IReadOnlyList<string>>.Fail(
            "尚未支持把未管理文件接管为受管安装。",
            "本次迭代只提供扫描、定位与删除；接管能力随 INT-09 补齐。",
            retryable: false));
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
