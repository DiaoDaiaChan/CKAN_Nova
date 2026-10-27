using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CKAN;

namespace CkanUI.Services.Registry;

/// <summary>提供 Core 注册表的不可变只读投影（02 篇 §6）。</summary>
public sealed class RegistryService : IRegistryService, IDisposable
{
    private readonly GameInstanceManager _instances;
    private readonly RepositoryDataManager _repositoryData;
    private readonly IDispatcher _dispatcher;
    private readonly object _gate = new();
    private CancellationTokenSource? _pending;
    private RegistrySnapshot _current = RegistrySnapshot.Empty();
    private bool _disposed;

    public RegistryService(GameInstanceManager instances, RepositoryDataManager repositoryData, IDispatcher dispatcher)
    {
        _instances = instances ?? throw new ArgumentNullException(nameof(instances));
        _repositoryData = repositoryData ?? throw new ArgumentNullException(nameof(repositoryData));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _instances.InstanceChanged += OnInstanceChanged;
        _repositoryData.Updated += OnRepositoryUpdated;
        if (_instances.CurrentInstance is { } current) _current = CreateSnapshot(current);
    }

    public RegistrySnapshot Current => _current;
    public event Action<RegistrySnapshot>? Changed;

    public Task<OperationResult<RegistrySnapshot>> OpenAsync(InstanceInfo instance, CancellationToken ct = default) =>
        GuardAsync(() =>
        {
            ct.ThrowIfCancellationRequested();
            if (!_instances.Instances.TryGetValue(instance.Name, out var gameInstance))
                return OperationResult<RegistrySnapshot>.Fail($"找不到游戏实例“{instance.Name}”。");
            if (gameInstance.IsMaybeLocked) return OperationResult<RegistrySnapshot>.Fail($"实例“{instance.Name}”已被锁定，无法打开注册表。");
            if (!ReferenceEquals(_instances.CurrentInstance, gameInstance))
                return OperationResult<RegistrySnapshot>.Fail("只能打开当前活动实例的注册表，请先切换实例。");
            var snapshot = CreateSnapshot(gameInstance);
            Publish(snapshot);
            return OperationResult<RegistrySnapshot>.Ok(snapshot);
        });

    public Task<OperationResult<bool>> CloseAsync(CancellationToken ct = default) => GuardAsync(() =>
    {
        ct.ThrowIfCancellationRequested();
        RegistryManager.DisposeAll();
        var snapshot = RegistrySnapshot.Empty(_instances.CurrentInstance?.Name ?? string.Empty);
        Publish(snapshot);
        return OperationResult<bool>.Ok(true);
    });

    public Task<OperationResult<RegistrySnapshot>> RefreshAsync(CancellationToken ct = default) => GuardAsync(() =>
    {
        ct.ThrowIfCancellationRequested();
        var gameInstance = _instances.CurrentInstance;
        var snapshot = gameInstance == null ? RegistrySnapshot.Empty() : CreateSnapshot(gameInstance);
        Publish(snapshot);
        return OperationResult<RegistrySnapshot>.Ok(snapshot);
    });

    private RegistrySnapshot CreateSnapshot(GameInstance instance)
    {
        var registry = RegistryManager.Instance(instance, _repositoryData).registry;
        var installed = registry.InstalledModules.ToDictionary(module => module.identifier,
            module => new InstalledModuleInfo(module.identifier, module.Module.version.ToString(), module.AutoInstalled,
                null, module.InstallTime), StringComparer.Ordinal);
        var dlls = new HashSet<string>(registry.InstalledDlls, StringComparer.Ordinal);
        return new RegistrySnapshot(instance.Name, null,
            new System.Collections.ObjectModel.ReadOnlyDictionary<string, InstalledModuleInfo>(installed),
            new HashSet<string>(dlls, StringComparer.Ordinal), DateTime.UtcNow);
    }

    private void Publish(RegistrySnapshot snapshot)
    {
        _current = snapshot;
        _dispatcher.Post(() => Changed?.Invoke(snapshot));
    }

    private void OnInstanceChanged(GameInstance? _, GameInstance? current) => ScheduleRefresh(current);
    private void OnRepositoryUpdated(Repository[] _) => ScheduleRefresh(_instances.CurrentInstance);

    private void ScheduleRefresh(GameInstance? instance)
    {
        CancellationTokenSource debounce;
        lock (_gate)
        {
            if (_disposed) return;
            _pending?.Cancel();
            _pending?.Dispose();
            _pending = debounce = new CancellationTokenSource();
        }
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(50, debounce.Token).ConfigureAwait(false);
                var snapshot = instance == null ? RegistrySnapshot.Empty() : CreateSnapshot(instance);
                Publish(snapshot);
            }
            catch (OperationCanceledException) { }
            catch { /* Core 异步变化不应逃逸到事件发布线程。 */ }
        });
    }

    private static async Task<OperationResult<T>> GuardAsync<T>(Func<OperationResult<T>> action)
    {
        try { return await Task.Run(action).ConfigureAwait(false); }
        catch (OperationCanceledException ex) { return OperationResult<T>.Fail("操作已取消。", ex.Message, true); }
        catch (Exception ex) { return OperationResult<T>.Fail("注册表操作失败。", ex.ToString(), true); }
    }

    private static Task<OperationResult<T>> GuardAsync<T>(Func<T> action) => GuardAsync<T>(() => OperationResult<T>.Ok(action()));

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _pending?.Cancel();
            _pending?.Dispose();
            _pending = null;
        }
        _instances.InstanceChanged -= OnInstanceChanged;
        _repositoryData.Updated -= OnRepositoryUpdated;
    }
}
