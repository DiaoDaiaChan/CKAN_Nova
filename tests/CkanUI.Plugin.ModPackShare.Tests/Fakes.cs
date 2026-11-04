using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Services.Abstractions.Profiles;

namespace CkanUI.Plugin.ModPackShare.Tests;

/// <summary>
/// 配置档服务的可控替身。
/// 记录每一次调用的参数（用例要断言「CreateAsync 收到的是包名而不是文件名」「SetLaunch 传的是 null 还是名字」），
/// 并且可以在不抛异常的前提下让任意一步失败——真实服务的失败就是 <see cref="OperationResult"/>，
/// 页面必须学会翻译它而不是被异常冲掉。
/// </summary>
internal sealed class FakeInstanceProfileService : IInstanceProfileService
{
    private readonly List<InstanceProfileInfo> _profiles = [];

    /// <summary>ListAsync 的返回值；为 null 时按 <see cref="Profiles"/> 成功返回。</summary>
    public OperationResult<IReadOnlyList<InstanceProfileInfo>>? ListResult { get; set; }

    /// <summary>CreateAsync 的返回值；为 null 时成功创建并返回一个目录落在 <see cref="ProfileRoot"/> 下的档。</summary>
    public OperationResult<InstanceProfileInfo>? CreateResult { get; set; }

    /// <summary>SwitchAsync 的返回值；为 null 时成功。</summary>
    public OperationResult<InstanceProfileSwitchResult>? SwitchResult { get; set; }

    /// <summary>DeleteAsync 的返回值；为 null 时成功。</summary>
    public OperationResult<bool>? DeleteResult { get; set; }

    /// <summary>SetLaunchProfileAsync 的返回值；为 null 时成功。</summary>
    public OperationResult<bool>? SetLaunchResult { get; set; }

    /// <summary>当前记录的启动配置档（SetLaunchProfileAsync 会改它，模拟宿主的持久化）。</summary>
    public string? LaunchProfile { get; set; }

    /// <summary>新建配置档的父目录；用例把它指到临时目录，页面复制 modpack.ckan 时就落在那里。</summary>
    public string ProfileRoot { get; set; } = Path.Combine(Path.GetTempPath(), "modpackshare-tests");

    /// <summary>CreateAsync 收到的参数（实例名、档名、adoptCurrent、source）。</summary>
    public List<(string InstanceName, string ProfileName, bool AdoptCurrent, string? Source)> CreateCalls { get; } = [];

    /// <summary>SwitchAsync 收到的参数。</summary>
    public List<(string InstanceName, string ProfileName)> SwitchCalls { get; } = [];

    /// <summary>DeleteAsync 收到的参数。</summary>
    public List<(string InstanceName, string ProfileName)> DeleteCalls { get; } = [];

    /// <summary>SetLaunchProfileAsync 收到的参数（第二个可能是 null，表示清除）。</summary>
    public List<(string InstanceName, string? ProfileName)> SetLaunchCalls { get; } = [];

    /// <summary>ListAsync 被调用的次数——用来验证「切换/删除后确实刷新了列表」。</summary>
    public int ListCallCount { get; private set; }

    public IReadOnlyList<InstanceProfileInfo> Profiles => _profiles;

    public void Seed(params InstanceProfileInfo[] profiles) => _profiles.AddRange(profiles);

    public Task<OperationResult<IReadOnlyList<InstanceProfileInfo>>> ListAsync(
        string instanceName,
        CancellationToken ct = default)
    {
        ListCallCount++;
        return Task.FromResult(ListResult
            ?? OperationResult<IReadOnlyList<InstanceProfileInfo>>.Ok(_profiles.ToArray()));
    }

    public Task<OperationResult<InstanceProfileInfo>> CreateAsync(
        string instanceName,
        string profileName,
        bool adoptCurrent = false,
        string? source = null,
        CancellationToken ct = default)
    {
        CreateCalls.Add((instanceName, profileName, adoptCurrent, source));
        if (CreateResult is not null)
        {
            return Task.FromResult(CreateResult);
        }

        var directory = Path.Combine(ProfileRoot, profileName);
        var info = new InstanceProfileInfo(
            profileName,
            instanceName,
            IsActive: false,
            directory,
            Path.Combine(directory, "GameData"),
            SizeBytes: 0,
            FileCount: 0,
            LastUsedUtc: null)
        {
            Source = source,
        };

        _profiles.Add(info);
        return Task.FromResult(OperationResult<InstanceProfileInfo>.Ok(info));
    }

    public Task<OperationResult<InstanceProfileSwitchResult>> SwitchAsync(
        string instanceName,
        string profileName,
        CancellationToken ct = default)
    {
        SwitchCalls.Add((instanceName, profileName));
        if (SwitchResult is not null)
        {
            return Task.FromResult(SwitchResult);
        }

        // 模拟宿主的「换出换入」：原生效档变回普通档，目标档变生效。
        var previous = _profiles.FirstOrDefault(p => p.IsActive)?.Name;
        for (var i = 0; i < _profiles.Count; i++)
        {
            _profiles[i] = _profiles[i] with { IsActive = _profiles[i].Name == profileName };
        }

        return Task.FromResult(OperationResult<InstanceProfileSwitchResult>.Ok(
            new InstanceProfileSwitchResult(instanceName, previous, profileName, AdoptedLiveState: false)));
    }

    public Task<OperationResult<bool>> DeleteAsync(
        string instanceName,
        string profileName,
        CancellationToken ct = default)
    {
        DeleteCalls.Add((instanceName, profileName));
        if (DeleteResult is not null)
        {
            return Task.FromResult(DeleteResult);
        }

        _profiles.RemoveAll(p => p.Name == profileName);
        return Task.FromResult(OperationResult<bool>.Ok(true));
    }

    public Task<OperationResult<string?>> GetLaunchProfileAsync(
        string instanceName,
        CancellationToken ct = default)
    {
        // 和真实服务一致：启动档是持久化的独立状态；没显式设置时回落到 Seed 里标了 IsLaunchProfile 的档，
        // 否则用例只写 isLaunch: true 就永远读不到启动配置。
        var name = LaunchProfile
            ?? _profiles.FirstOrDefault(p => p.IsLaunchProfile)?.Name;
        return Task.FromResult(OperationResult<string?>.Ok(name));
    }

    public Task<OperationResult<bool>> SetLaunchProfileAsync(
        string instanceName,
        string? profileName,
        CancellationToken ct = default)
    {
        SetLaunchCalls.Add((instanceName, profileName));
        if (SetLaunchResult is not null)
        {
            return Task.FromResult(SetLaunchResult);
        }

        LaunchProfile = profileName;
        for (var i = 0; i < _profiles.Count; i++)
        {
            _profiles[i] = _profiles[i] with { IsLaunchProfile = _profiles[i].Name == profileName };
        }

        return Task.FromResult(OperationResult<bool>.Ok(true));
    }

    public Task<OperationResult<bool>> ApplyLaunchProfileAsync(
        string instanceName,
        CancellationToken ct = default)
        => throw new NotSupportedException("页面不应在切换页面里替宿主应用启动配置档。");
}

/// <summary>
/// 实例服务替身：只提供 Current / Instances / 两个事件 / IsGameRunning。
/// 其余方法一律抛 <see cref="NotSupportedException"/>——页面若偷偷切实例，用例当场红。
/// </summary>
internal sealed class FakeInstanceService : IInstanceService
{
    public IReadOnlyList<InstanceInfo> Instances { get; set; } = [];

    public InstanceInfo? Current { get; set; }

    /// <summary>IsGameRunning 的返回值（页面据此拦截写操作）。</summary>
    public bool GameRunning { get; set; }

    public event Action? InstancesChanged;

    public event Action<InstanceInfo?>? CurrentChanged;

    public void RaiseCurrentChanged(InstanceInfo? current)
    {
        Current = current;
        CurrentChanged?.Invoke(current);
    }

    public void RaiseInstancesChanged() => InstancesChanged?.Invoke();

    public bool IsGameRunning(string instanceName = "") => GameRunning;

    public Task<OperationResult<IReadOnlyList<InstanceInfo>>> RefreshAsync(CancellationToken ct = default)
        => throw new NotSupportedException();

    public Task<OperationResult<InstanceInfo>> SwitchAsync(string instanceName, CancellationToken ct = default)
        => throw new NotSupportedException();

    public Task<OperationResult<InstanceInfo>> AddAsync(string path, string? name = null, CancellationToken ct = default)
        => throw new NotSupportedException();

    public Task<OperationResult<InstanceInfo>> CloneAsync(string sourceName, string newName, CancellationToken ct = default)
        => throw new NotSupportedException();

    public Task<OperationResult<bool>> RemoveAsync(string instanceName, CancellationToken ct = default)
        => throw new NotSupportedException();

    public Task<OperationResult<InstanceInfo>> RenameAsync(string instanceName, string newName, CancellationToken ct = default)
        => throw new NotSupportedException();
}

/// <summary>
/// 整合包服务替身。<see cref="IModPackService"/> 全部成员都有 DIM 默认实现，
/// 所以这里只需要覆写 <see cref="LoadEditorAsync"/>：导入流程只用到它来取包名与模组数。
/// </summary>
internal sealed class FakeModPackService : IModPackService
{
    /// <summary>LoadEditorAsync 的返回值；为 null 时按 <see cref="Document"/> 成功返回。</summary>
    public OperationResult<ModPackEditorDocument>? LoadResult { get; set; }

    /// <summary>LoadEditorAsync 成功时返回的文档。</summary>
    public ModPackEditorDocument? Document { get; set; }

    /// <summary>LoadEditorAsync 收到的路径。</summary>
    public List<string> LoadCalls { get; } = [];

    public Task<OperationResult<ModPackEditorDocument>> LoadEditorAsync(
        string path,
        CancellationToken ct = default)
    {
        LoadCalls.Add(path);
        if (LoadResult is not null)
        {
            return Task.FromResult(LoadResult);
        }

        return Task.FromResult(OperationResult<ModPackEditorDocument>.Ok(
            Document ?? new ModPackEditorDocument { FilePath = path, Name = "整合包" }));
    }
}
