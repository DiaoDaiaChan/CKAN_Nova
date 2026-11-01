using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Tools.PerfProbe;

public sealed class MockRepositoryService : IRepositoryService
{
    public RepositorySnapshot Current { get; private set; }

    public MockRepositoryService(IReadOnlyList<ModRowData> mods)
    {
        Current = new RepositorySnapshot(
            Available: mods,
            RepositoryIds: ["default-ksp-repo"],
            UpdatedAt: DateTime.UtcNow,
            IsFromCache: false);
    }

    public event Action<RepositorySnapshot>? Updated;
    public event Action<OperationProgress>? RefreshProgress;

    public void SetMods(IReadOnlyList<ModRowData> mods)
    {
        Current = new RepositorySnapshot(
            Available: mods,
            RepositoryIds: ["default-ksp-repo"],
            UpdatedAt: DateTime.UtcNow,
            IsFromCache: false);
        Updated?.Invoke(Current);
    }

    public Task<OperationResult<RepositorySnapshot>> LoadCachedAsync(CancellationToken ct = default) =>
        Task.FromResult(OperationResult<RepositorySnapshot>.Ok(Current));

    public Task<OperationResult<RepositorySnapshot>> RefreshAsync(CancellationToken ct = default)
    {
        RefreshProgress?.Invoke(new OperationProgress("mock-refresh", "refresh", "Refreshing mock repo", null, 100.0, false, OperationState.Succeeded));
        Updated?.Invoke(Current);
        return Task.FromResult(OperationResult<RepositorySnapshot>.Ok(Current));
    }
}

public sealed class MockRegistryService : IRegistryService
{
    public RegistrySnapshot Current { get; private set; }

    public MockRegistryService(string instanceName, IReadOnlyDictionary<string, InstalledModuleInfo> installed)
    {
        Current = new RegistrySnapshot(
            InstanceName: instanceName,
            RegistryVersion: "v1.32",
            Installed: installed,
            Dlls: new HashSet<string>(),
            TakenAt: DateTime.UtcNow);
    }

    public event Action<RegistrySnapshot>? Changed;

    public void SetInstalled(string instanceName, IReadOnlyDictionary<string, InstalledModuleInfo> installed)
    {
        Current = new RegistrySnapshot(
            InstanceName: instanceName,
            RegistryVersion: "v1.32",
            Installed: installed,
            Dlls: new HashSet<string>(),
            TakenAt: DateTime.UtcNow);
        Changed?.Invoke(Current);
    }

    public Task<OperationResult<RegistrySnapshot>> OpenAsync(InstanceInfo instance, CancellationToken ct = default) =>
        Task.FromResult(OperationResult<RegistrySnapshot>.Ok(Current));

    public Task<OperationResult<bool>> CloseAsync(CancellationToken ct = default) =>
        Task.FromResult(OperationResult<bool>.Ok(true));

    public Task<OperationResult<RegistrySnapshot>> RefreshAsync(CancellationToken ct = default)
    {
        Changed?.Invoke(Current);
        return Task.FromResult(OperationResult<RegistrySnapshot>.Ok(Current));
    }
}

public sealed class MockInstanceService : IInstanceService
{
    private readonly List<InstanceInfo> _instances = new();
    public IReadOnlyList<InstanceInfo> Instances => _instances;
    public InstanceInfo? Current { get; private set; }

    public MockInstanceService(InstanceInfo currentInstance)
    {
        _instances.Add(currentInstance);
        Current = currentInstance;
    }

    public event Action? InstancesChanged { add { } remove { } }
    public event Action<InstanceInfo?>? CurrentChanged;

    public Task<OperationResult<IReadOnlyList<InstanceInfo>>> RefreshAsync(CancellationToken ct = default) =>
        Task.FromResult(OperationResult<IReadOnlyList<InstanceInfo>>.Ok(_instances));

    public Task<OperationResult<InstanceInfo>> SwitchAsync(string instanceName, CancellationToken ct = default)
    {
        var found = _instances.Find(i => i.Name == instanceName);
        if (found != null)
        {
            Current = found;
            CurrentChanged?.Invoke(Current);
            return Task.FromResult(OperationResult<InstanceInfo>.Ok(found));
        }
        return Task.FromResult(OperationResult<InstanceInfo>.Fail("Not found"));
    }

    public Task<OperationResult<InstanceInfo>> AddAsync(string path, string? name = null, CancellationToken ct = default) =>
        Task.FromResult(OperationResult<InstanceInfo>.Fail("Not supported"));

    public Task<OperationResult<InstanceInfo>> CloneAsync(string sourceName, string newName, CancellationToken ct = default) =>
        Task.FromResult(OperationResult<InstanceInfo>.Fail("Not supported"));

    public Task<OperationResult<bool>> RemoveAsync(string instanceName, CancellationToken ct = default) =>
        Task.FromResult(OperationResult<bool>.Fail("Not supported"));

    public Task<OperationResult<InstanceInfo>> RenameAsync(string instanceName, string newName, CancellationToken ct = default) =>
        Task.FromResult(OperationResult<InstanceInfo>.Fail("Not supported"));
}

public sealed class MockErrorReporter : IErrorReporter
{
    private readonly List<ErrorEntry> _recent = new();
    public IReadOnlyList<ErrorEntry> Recent => _recent;

    public event Action<ErrorEntry>? Reported;

    public void Report(Exception ex, string context, string? userMessage = null)
    {
        var entry = new ErrorEntry(DateTime.UtcNow, context, userMessage ?? ex.Message, ex.ToString(), ex);
        _recent.Add(entry);
        Reported?.Invoke(entry);
        Console.Error.WriteLine($"[MockErrorReporter] {context}: {userMessage} ({ex.Message})");
    }

    public void Report(string context, string userMessage, string? detail = null)
    {
        var entry = new ErrorEntry(DateTime.UtcNow, context, userMessage, detail, null);
        _recent.Add(entry);
        Reported?.Invoke(entry);
        Console.Error.WriteLine($"[MockErrorReporter] {context}: {userMessage}");
    }
}
