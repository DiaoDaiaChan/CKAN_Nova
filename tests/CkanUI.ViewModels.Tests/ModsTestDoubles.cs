using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.ViewModels.Tests;

/// <summary>模组页测试替身（TST-01：无 I/O、无网络、可控异常）。</summary>
internal static class ModsRowFactory
{
    public static ModRowData Create(
        string identifier,
        string? name = null,
        string? author = "作者甲",
        string? installedVersion = null,
        string? latestVersion = "1.0.0",
        long downloadCount = 100,
        IReadOnlyList<string>? tags = null,
        long? sizeBytes = 1024,
        bool isCompatible = true,
        bool isAutoInstalled = false,
        bool isImplicit = false,
        ModSearchFacts? facts = null)
    {
        var installed = installedVersion is not null;
        var hasUpdate = installed
                       && latestVersion is not null
                       && global::CkanUI.ViewModels.Pages.Mods.ModsVersionComparer.IsNewer(latestVersion, installedVersion);

        return new ModRowData(
            Identifier: identifier,
            Name: name ?? identifier,
            Author: author,
            InstalledVersion: installedVersion,
            LatestVersion: latestVersion,
            DownloadCount: downloadCount,
            Tags: tags ?? Array.Empty<string>(),
            SizeBytes: sizeBytes,
            IsInstalled: installed,
            HasUpdate: hasUpdate,
            IsCompatible: isCompatible,
            IsAutoInstalled: isAutoInstalled,
            IsSelected: false,
            IsImplicit: isImplicit)
        {
            Facts = facts ?? default,
        };
    }

    public static RepositorySnapshot Repository(params ModRowData[] rows) =>
        new(rows, new[] { "default" }, DateTime.UtcNow, false);

    public static RegistrySnapshot Registry(params InstalledModuleInfo[] modules)
    {
        var map = new Dictionary<string, InstalledModuleInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var module in modules)
        {
            map[module.Identifier] = module;
        }

        return new RegistrySnapshot("测试实例", "v1.0", map, new HashSet<string>(), DateTime.UtcNow);
    }

    public static InstalledModuleInfo Installed(string identifier, string version, bool auto = false) =>
        new(identifier, version, auto, "default", DateTime.UtcNow);

    public static InstanceInfo Instance(string name = "测试实例") =>
        new(name, "C:/ksp", "1.12.5", true, false, 12);
}

/// <summary>
/// 搜索语法用例的统一入口：真实走服务层 <c>ModSearchParser</c>（内部即上游 <c>ModSearch.Parse</c>），
/// 再交给 <c>ModsQuery</c> 做条件匹配，端到端验证"解析委托模型层"。
/// </summary>
internal static class Search
{
    public static readonly global::CkanUI.Services.Search.ModSearchParser Parser = new();

    public static global::CkanUI.ViewModels.Pages.Mods.ModsQuery Parse(string? text) =>
        global::CkanUI.ViewModels.Pages.Mods.ModsQuery.FromSpec(Parser.Parse(text));
}

/// <summary>假仓库服务：可设快照、可注入异常、可统计调用次数。</summary>
internal sealed class FakeRepositoryService : IRepositoryService
{
    public RepositorySnapshot Current { get; set; } = RepositorySnapshot.Empty;

    public OperationResult<RepositorySnapshot> RefreshResult { get; set; } =
        OperationResult<RepositorySnapshot>.Ok(RepositorySnapshot.Empty);

    public OperationResult<RepositorySnapshot> LoadResult { get; set; } =
        OperationResult<RepositorySnapshot>.Ok(RepositorySnapshot.Empty);

    public Exception? RefreshException { get; set; }

    public int RefreshCount { get; private set; }

    public event Action<RepositorySnapshot>? Updated;

    public event Action<OperationProgress>? RefreshProgress;

    /// <summary>模拟后台刷新完成并推送快照。</summary>
    public void RaiseUpdated(RepositorySnapshot snapshot)
    {
        Current = snapshot;
        Updated?.Invoke(snapshot);
    }

    /// <summary>模拟进度推送（保持事件被使用，避免 CS0067）。</summary>
    public void RaiseProgress(OperationProgress progress) => RefreshProgress?.Invoke(progress);

    public Task<OperationResult<RepositorySnapshot>> LoadCachedAsync(CancellationToken ct = default) =>
        Task.FromResult(LoadResult);

    public Task<OperationResult<RepositorySnapshot>> RefreshAsync(CancellationToken ct = default)
    {
        RefreshCount++;
        if (RefreshException is not null)
        {
            throw RefreshException;
        }

        if (RefreshResult.IsSuccess && RefreshResult.Value is not null)
        {
            Current = RefreshResult.Value;
        }

        return Task.FromResult(RefreshResult);
    }
}

/// <summary>假注册表服务。</summary>
internal sealed class FakeRegistryService : IRegistryService
{
    public RegistrySnapshot Current { get; set; } = RegistrySnapshot.Empty();

    public OperationResult<RegistrySnapshot> RefreshResult { get; set; } =
        OperationResult<RegistrySnapshot>.Ok(RegistrySnapshot.Empty());

    public Exception? RefreshException { get; set; }

    public int RefreshCount { get; private set; }

    public event Action<RegistrySnapshot>? Changed;

    /// <summary>模拟注册表变化。</summary>
    public void RaiseChanged(RegistrySnapshot snapshot)
    {
        Current = snapshot;
        Changed?.Invoke(snapshot);
    }

    public Task<OperationResult<RegistrySnapshot>> OpenAsync(InstanceInfo instance, CancellationToken ct = default) =>
        Task.FromResult(OperationResult<RegistrySnapshot>.Ok(Current));

    public Task<OperationResult<bool>> CloseAsync(CancellationToken ct = default) =>
        Task.FromResult(OperationResult<bool>.Ok(true));

    public Task<OperationResult<RegistrySnapshot>> RefreshAsync(CancellationToken ct = default)
    {
        RefreshCount++;
        if (RefreshException is not null)
        {
            throw RefreshException;
        }

        if (RefreshResult.IsSuccess && RefreshResult.Value is not null)
        {
            Current = RefreshResult.Value;
        }

        return Task.FromResult(RefreshResult);
    }
}

/// <summary>假实例服务。</summary>
internal sealed class FakeInstanceService : IInstanceService
{
    public IReadOnlyList<InstanceInfo> Instances { get; set; } = Array.Empty<InstanceInfo>();

    public InstanceInfo? Current { get; set; }
    /// <summary>F-33：测试里直接设定游戏是否在跑，不用真起进程。</summary>
    public bool GameRunning { get; set; }

    public int IsGameRunningCalls { get; private set; }

    public bool IsGameRunning(string instanceName = "")
    {
        IsGameRunningCalls++;
        return GameRunning;
    }

    public event Action? InstancesChanged;

    public event Action<InstanceInfo?>? CurrentChanged;

    /// <summary>模拟切换当前实例。</summary>
    public void RaiseCurrentChanged(InstanceInfo? instance)
    {
        Current = instance;
        CurrentChanged?.Invoke(instance);
    }

    /// <summary>模拟实例列表变化。</summary>
    public void RaiseInstancesChanged() => InstancesChanged?.Invoke();

    public Task<OperationResult<IReadOnlyList<InstanceInfo>>> RefreshAsync(CancellationToken ct = default) =>
        Task.FromResult(OperationResult<IReadOnlyList<InstanceInfo>>.Ok(Instances));

    public Task<OperationResult<InstanceInfo>> SwitchAsync(string instanceName, CancellationToken ct = default) =>
        Task.FromResult(OperationResult<InstanceInfo>.Fail("测试不切换实例"));

    public Task<OperationResult<InstanceInfo>> AddAsync(string path, string? name = null, CancellationToken ct = default) =>
        Task.FromResult(OperationResult<InstanceInfo>.Fail("测试不添加实例"));

    public Task<OperationResult<InstanceInfo>> CloneAsync(string sourceName, string newName, CancellationToken ct = default) =>
        Task.FromResult(OperationResult<InstanceInfo>.Fail("测试不克隆实例"));

    public Task<OperationResult<bool>> RemoveAsync(string instanceName, CancellationToken ct = default) =>
        Task.FromResult(OperationResult<bool>.Ok(true));

    public Task<OperationResult<InstanceInfo>> RenameAsync(string instanceName, string newName, CancellationToken ct = default) =>
        Task.FromResult(OperationResult<InstanceInfo>.Fail("测试不重命名实例"));
}

/// <summary>记录型错误上报器。</summary>
internal sealed class RecordingErrorReporter : IErrorReporter
{
    private readonly List<ErrorEntry> _entries = new();

    public event Action<ErrorEntry>? Reported;

    public IReadOnlyList<ErrorEntry> Recent => _entries;

    public void Report(Exception exception, string context, string? userMessage = null)
    {
        var entry = new ErrorEntry(DateTime.UtcNow, context, userMessage ?? context, exception.Message, exception);
        _entries.Add(entry);
        Reported?.Invoke(entry);
    }

    public void Report(string context, string userMessage, string? detail = null)
    {
        var entry = new ErrorEntry(DateTime.UtcNow, context, userMessage, detail, null);
        _entries.Add(entry);
        Reported?.Invoke(entry);
    }
}

/// <summary>
/// 可控延时（防抖测试核心）：记录每次请求的时长，只有测试显式 <see cref="ReleaseAll"/> 后才完成，
/// 从而能断言"连续输入期间不重建列表、只落最后一次"。
/// </summary>
internal sealed class ControlledDelayScheduler : global::CkanUI.ViewModels.Pages.Mods.IModsDelayScheduler
{
    private readonly List<TaskCompletionSource> _pending = new();

    public List<TimeSpan> Requested { get; } = new();

    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        Requested.Add(delay);
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
        _pending.Add(tcs);
        return tcs.Task;
    }

    /// <summary>放行所有等待中的延时。</summary>
    public void ReleaseAll()
    {
        foreach (var tcs in _pending)
        {
            tcs.TrySetResult();
        }

        _pending.Clear();
    }
}

/// <summary>即时完成的延时（非防抖用例用它让搜索立刻生效）。</summary>
internal sealed class ImmediateDelayScheduler : global::CkanUI.ViewModels.Pages.Mods.IModsDelayScheduler
{
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.CompletedTask;
}
