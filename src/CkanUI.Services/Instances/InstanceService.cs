using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CKAN;
using CKAN.Configuration;

namespace CkanUI.Services.Instances;

/// <summary>包装 Core 实例管理并按 02 篇 §5 执行实例切换序列。</summary>
public sealed class InstanceService : IInstanceService
{
    private readonly GameInstanceManager _manager;
    private readonly RepositoryDataManager _repositoryData;
    private readonly IDispatcher _dispatcher;
    private readonly IOperationQueue? _operations;
    private readonly IGameProcessProbe _processProbe;
    private IReadOnlyList<InstanceInfo> _instances = Array.Empty<InstanceInfo>();
    private InstanceInfo? _current;

    public InstanceService(GameInstanceManager manager, RepositoryDataManager repositoryData,
        IDispatcher dispatcher, IOperationQueue? operations = null,
        IGameProcessProbe? processProbe = null)
    {
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
        _repositoryData = repositoryData ?? throw new ArgumentNullException(nameof(repositoryData));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _operations = operations;
        _processProbe = processProbe ?? new GameProcessProbe();
        _manager.InstanceChanged += OnInstanceChanged;
        RefreshProjection();
    }

    public IReadOnlyList<InstanceInfo> Instances => _instances;
    public InstanceInfo? Current => _current;
    public event Action? InstancesChanged;
    public event Action<InstanceInfo?>? CurrentChanged;

    public Task<OperationResult<IReadOnlyList<InstanceInfo>>> RefreshAsync(CancellationToken ct = default) =>
        GuardAsync<IReadOnlyList<InstanceInfo>>(() => { ct.ThrowIfCancellationRequested(); RefreshProjection(); return _instances; });

    public Task<OperationResult<InstanceInfo>> SwitchAsync(string instanceName, CancellationToken ct = default) =>
        GuardAsync(() =>
        {
            ct.ThrowIfCancellationRequested();
            if (!_manager.Instances.TryGetValue(instanceName, out var target))
                return OperationResult<InstanceInfo>.Fail($"找不到游戏实例“{instanceName}”。");
            var previous = _manager.CurrentInstance;
            RegistryManager.DisposeAll();
            if (target.IsMaybeLocked)
                return OperationResult<InstanceInfo>.Fail($"实例“{target.Name}”已被其他 CKAN 进程锁定，无法切换。");
            _manager.SetCurrentInstance(target);
            _ = RegistryManager.Instance(target, _repositoryData);
            RefreshProjection();
            if (previous != null && !ReferenceEquals(previous, target) && _operations != null)
                foreach (var operation in _operations.Operations.Where(o => o.CanCancel && o.State is OperationState.Pending or OperationState.Running))
                    _operations.Cancel(operation.TaskId);
            return OperationResult<InstanceInfo>.Ok(_current!);
        });

    public Task<OperationResult<InstanceInfo>> AddAsync(string path, string? name = null, CancellationToken ct = default) =>
        GuardAsync(() =>
        {
            ct.ThrowIfCancellationRequested();
            var normalizedName = string.IsNullOrWhiteSpace(name) ? Path.GetFileName(Path.TrimEndingDirectorySeparator(path)) : name;
            if (string.IsNullOrWhiteSpace(normalizedName)) return OperationResult<InstanceInfo>.Fail("必须提供有效的实例名称。");
            var instance = _manager.AddInstance(path, normalizedName, _manager.User);
            if (instance == null) return OperationResult<InstanceInfo>.Fail("无法识别所选目录中的游戏实例。");
            RefreshProjection();
            return OperationResult<InstanceInfo>.Ok(Project(instance));
        });

    public Task<OperationResult<InstanceInfo>> CloneAsync(string sourceName, string newName, CancellationToken ct = default) =>
        GuardAsync(() =>
        {
            ct.ThrowIfCancellationRequested();
            if (!_manager.Instances.TryGetValue(sourceName, out var source)) return OperationResult<InstanceInfo>.Fail($"找不到源实例“{sourceName}”。");
            if (string.IsNullOrWhiteSpace(newName)) return OperationResult<InstanceInfo>.Fail("必须提供有效的新实例名称。");
            var destination = Path.Combine(Path.GetDirectoryName(source.GameDir) ?? source.GameDir, newName);
            var clone = _manager.CloneInstance(source, newName, destination);
            RefreshProjection();
            return OperationResult<InstanceInfo>.Ok(Project(clone));
        });

    public Task<OperationResult<InstanceInfo>> RenameAsync(string instanceName, string newName, CancellationToken ct = default) =>
        GuardAsync(() =>
        {
            ct.ThrowIfCancellationRequested();
            var trimmed = newName?.Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) return OperationResult<InstanceInfo>.Fail("必须提供有效的实例名称。");
            if (!_manager.Instances.TryGetValue(instanceName, out var instance)) return OperationResult<InstanceInfo>.Fail($"找不到游戏实例“{instanceName}”。");
            if (string.Equals(instanceName, trimmed, StringComparison.Ordinal))
            {
                RefreshProjection();
                return OperationResult<InstanceInfo>.Ok(Project(instance));
            }
            if (_manager.HasInstance(trimmed)) return OperationResult<InstanceInfo>.Fail($"实例名称“{trimmed}”已被占用，请换一个名字。", null, true);
            try
            {
                _manager.RenameInstance(instanceName, trimmed);
            }
            catch (InstanceNameTakenKraken)
            {
                return OperationResult<InstanceInfo>.Fail($"实例名称“{trimmed}”已被占用，请换一个名字。", null, true);
            }
            // 重命名只动配置里的名字；自启动指向若仍写旧名，会变成指向不存在实例的死链。
            if (string.Equals(_manager.Configuration.AutoStartInstance, instanceName, StringComparison.Ordinal))
                _manager.SetAutoStart(trimmed);
            RefreshProjection();
            return OperationResult<InstanceInfo>.Ok(Project(instance));
        });

    public Task<OperationResult<bool>> RemoveAsync(string instanceName, CancellationToken ct = default) =>
        GuardAsync(() =>
        {
            ct.ThrowIfCancellationRequested();
            if (_manager.CurrentInstance?.Name == instanceName) return OperationResult<bool>.Fail("无法移除当前正在使用的实例，请先切换到其他实例。");
            if (!_manager.Instances.ContainsKey(instanceName)) return OperationResult<bool>.Fail($"找不到游戏实例“{instanceName}”。");
            _manager.RemoveInstance(instanceName);
            RefreshProjection();
            return OperationResult<bool>.Ok(true);
        });

    public bool IsGameRunning(string instanceName = "")
    {
        var name = string.IsNullOrWhiteSpace(instanceName)
            ? _manager.CurrentInstance?.Name
            : instanceName;
        if (string.IsNullOrWhiteSpace(name)) return false;
        if (!_manager.Instances.TryGetValue(name, out var instance)) return false;

        foreach (var path in _processProbe.GetProcessImagePaths())
        {
            if (GameProcessProbe.IsInsideGameDir(path, instance.GameDir))
            {
                return true;
            }
        }

        return false;
    }

    private void OnInstanceChanged(GameInstance? _, GameInstance? current) => _dispatcher.Post(() =>
    {
        RefreshProjection();
        CurrentChanged?.Invoke(_current);
    });

    private void RefreshProjection()
    {
        var entries = _manager.Instances.Values.Select(Project).ToArray();
        _instances = Array.AsReadOnly(entries);
        _current = _manager.CurrentInstance is { } current ? Project(current) : null;
        _dispatcher.Post(() => InstancesChanged?.Invoke());
    }

    private InstanceInfo Project(GameInstance gameInstance)
    {
        var isDefault = string.Equals(_manager.Configuration.AutoStartInstance, gameInstance.Name, StringComparison.Ordinal);
        int? moduleCount = null;
        if (_manager.CurrentInstance != null && ReferenceEquals(_manager.CurrentInstance, gameInstance))
        {
            try { moduleCount = RegistryManager.Instance(gameInstance, _repositoryData).registry.InstalledModules.Count; }
            catch { /* 实例投影不应因注册表损坏而阻断枚举。 */ }
        }
        return new InstanceInfo(gameInstance.Name, gameInstance.GameDir, gameInstance.Version()?.ToString(),
            isDefault, gameInstance.IsMaybeLocked, moduleCount);
    }

    private static async Task<OperationResult<T>> GuardAsync<T>(Func<OperationResult<T>> action)
    {
        try { return await Task.Run(action).ConfigureAwait(false); }
        catch (OperationCanceledException ex) { return OperationResult<T>.Fail("操作已取消。", ex.Message, true); }
        catch (Exception ex) { return OperationResult<T>.Fail("实例操作失败。", ex.ToString(), true); }
    }

    private static async Task<OperationResult<T>> GuardAsync<T>(Func<OperationResult<T>> action, bool _ = true) => await GuardAsync(action).ConfigureAwait(false);
    private static Task<OperationResult<T>> GuardAsync<T>(Func<T> action) => GuardAsync<T>(() => OperationResult<T>.Ok(action()));
}
