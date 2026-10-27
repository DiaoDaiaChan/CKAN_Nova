using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Pages.Unmanaged;

namespace CkanUI.ViewModels.Tests;

/// <summary>未管理文件页测试替身：服务、实例、错误上报、对话框、宿主启动器。</summary>
internal sealed class UnmanagedFakeService : IUnmanagedService
{
    public List<UnmanagedFileInfo> Entries { get; } = new();

    public OperationResult<IReadOnlyList<UnmanagedFileInfo>>? ScanOverride { get; set; }

    public OperationResult<IReadOnlyList<string>>? DeleteOverride { get; set; }

    public Func<IReadOnlyCollection<string>, bool, OperationResult<IReadOnlyList<string>>>? DeleteHandler { get; set; }

    public int ScanCount { get; private set; }

    public List<UnmanagedDeleteCall> Deletes { get; } = new();

    public int AdoptCount { get; private set; }

    public Task<OperationResult<IReadOnlyList<UnmanagedFileInfo>>> ScanAsync(
        InstanceInfo instance, CancellationToken ct = default)
    {
        ScanCount++;
        return Task.FromResult(
            ScanOverride ?? OperationResult<IReadOnlyList<UnmanagedFileInfo>>.Ok(Entries.ToArray()));
    }

    public Task<OperationResult<IReadOnlyList<string>>> AdoptAsync(
        InstanceInfo instance, IReadOnlyCollection<string> relativePaths, CancellationToken ct = default)
    {
        AdoptCount++;
        return Task.FromResult(OperationResult<IReadOnlyList<string>>.Fail("尚未支持把未管理文件接管为受管安装。"));
    }

    public Task<OperationResult<IReadOnlyList<string>>> DeleteAsync(
        InstanceInfo instance,
        IReadOnlyCollection<string> relativePaths,
        bool dryRun = true,
        CancellationToken ct = default)
    {
        Deletes.Add(new UnmanagedDeleteCall(relativePaths.ToArray(), dryRun));
        if (DeleteHandler is { } handler)
        {
            return Task.FromResult(handler(relativePaths, dryRun));
        }

        return Task.FromResult(
            DeleteOverride ?? OperationResult<IReadOnlyList<string>>.Ok(relativePaths.ToArray()));
    }
}

/// <summary>一次删除调用（含 dry-run 标记）。</summary>
internal sealed record UnmanagedDeleteCall(IReadOnlyList<string> Paths, bool DryRun);

internal sealed class UnmanagedFakeInstanceService : IInstanceService
{
    private readonly List<InstanceInfo> _instances = new();

    public IReadOnlyList<InstanceInfo> Instances => _instances;

    public InstanceInfo? Current { get; set; }

    public event Action? InstancesChanged;

    public event Action<InstanceInfo?>? CurrentChanged;

    public int RefreshCount { get; private set; }

    public void Add(InstanceInfo instance) => _instances.Add(instance);

    public void RaiseCurrentChanged(InstanceInfo? instance)
    {
        Current = instance;
        CurrentChanged?.Invoke(instance);
    }

    public void RaiseInstancesChanged() => InstancesChanged?.Invoke();

    public Task<OperationResult<IReadOnlyList<InstanceInfo>>> RefreshAsync(CancellationToken ct = default)
    {
        RefreshCount++;
        return Task.FromResult(OperationResult<IReadOnlyList<InstanceInfo>>.Ok(_instances.ToArray()));
    }

    public Task<OperationResult<InstanceInfo>> SwitchAsync(string instanceName, CancellationToken ct = default) =>
        Task.FromResult(OperationResult<InstanceInfo>.Fail("测试替身不支持切换实例。"));

    public Task<OperationResult<InstanceInfo>> AddAsync(
        string path, string? name = null, CancellationToken ct = default) =>
        Task.FromResult(OperationResult<InstanceInfo>.Fail("测试替身不支持添加实例。"));

    public Task<OperationResult<InstanceInfo>> CloneAsync(
        string sourceName, string newName, CancellationToken ct = default) =>
        Task.FromResult(OperationResult<InstanceInfo>.Fail("测试替身不支持克隆实例。"));

    public Task<OperationResult<bool>> RemoveAsync(string instanceName, CancellationToken ct = default) =>
        Task.FromResult(OperationResult<bool>.Fail("测试替身不支持移除实例。"));

    public Task<OperationResult<InstanceInfo>> RenameAsync(
        string instanceName, string newName, CancellationToken ct = default) =>
        Task.FromResult(OperationResult<InstanceInfo>.Fail("测试替身不支持重命名实例。"));
}

internal sealed class UnmanagedFakeErrorReporter : IErrorReporter
{
    public event Action<ErrorEntry>? Reported;

    public List<ErrorEntry> Entries { get; } = new();

    public IReadOnlyList<ErrorEntry> Recent => Entries;

    public void Report(Exception exception, string context, string? userMessage = null)
    {
        var entry = new ErrorEntry(
            DateTime.UtcNow, context, userMessage ?? exception.Message, exception.ToString(), exception);
        Entries.Add(entry);
        Reported?.Invoke(entry);
    }

    public void Report(string context, string userMessage, string? detail = null)
    {
        var entry = new ErrorEntry(DateTime.UtcNow, context, userMessage, detail, null);
        Entries.Add(entry);
        Reported?.Invoke(entry);
    }
}

internal sealed class UnmanagedFakeDialogService : IDialogService
{
    public Queue<bool> ConfirmResults { get; } = new();

    public bool ConfirmResult { get; set; }

    public int ConfirmCount { get; private set; }

    public List<string> Titles { get; } = new();

    public string? LastMessage { get; private set; }

    public string? LastDetail { get; private set; }

    public Task<bool> ConfirmAsync(string title, string message, string? detail = null)
    {
        ConfirmCount++;
        Titles.Add(title);
        LastMessage = message;
        LastDetail = detail;
        return Task.FromResult(ConfirmResults.Count > 0 ? ConfirmResults.Dequeue() : ConfirmResult);
    }

    public Task<int> SelectAsync(string title, string message, IReadOnlyList<string> options, int defaultIndex = 0) =>
        Task.FromResult(defaultIndex);

    public Task ShowMessageAsync(string title, string message) => Task.CompletedTask;

    public Task ShowErrorAsync(string title, string message, string? detail = null) => Task.CompletedTask;

    public Task<OperationResult<bool>> RunWithProgressAsync(
        string title, Func<IProgress<double>, CancellationToken, Task> work) =>
        Task.FromResult(OperationResult<bool>.Ok(true));
}

internal sealed class UnmanagedFakeShellLauncher : IUnmanagedShellLauncher
{
    public OperationResult? Result { get; set; }

    public string? LastPath { get; private set; }

    public int Calls { get; private set; }

    public Task<OperationResult> ShowInFolderAsync(string absolutePath)
    {
        Calls++;
        LastPath = absolutePath;
        return Task.FromResult(Result ?? OperationResult.Ok());
    }
}
