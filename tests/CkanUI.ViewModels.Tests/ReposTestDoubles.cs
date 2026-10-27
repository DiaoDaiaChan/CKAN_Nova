using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.ViewModels.Tests;

/// <summary>仓库页测试替身：可控返回、记录调用参数、可主动触发服务层事件。</summary>
public sealed class ReposFakeRepositoryService : IRepositoryService
{
    private Action<RepositorySnapshot>? _updated;
    private Action<OperationProgress>? _progress;
    private Action<IReadOnlyList<RepositoryEntryInfo>>? _repositoriesChanged;

    /// <summary>当前投影给 UI 的仓库行。</summary>
    public List<RepositoryEntryInfo> Rows { get; set; } = new();

    /// <summary>编辑类操作（新增/移除/移动/恢复默认）的返回值；为 null 时返回当前行的成功结果。</summary>
    public OperationResult<IReadOnlyList<RepositoryEntryInfo>>? EditResult { get; set; }

    /// <summary>整表刷新的返回值；为 null 时返回成功。</summary>
    public OperationResult<RepositorySnapshot>? RefreshAllResult { get; set; }

    /// <summary>单仓库刷新的返回值；为 null 时返回成功。</summary>
    public OperationResult<IReadOnlyList<RepositoryEntryInfo>>? RefreshOneResult { get; set; }

    /// <summary>编辑类操作抛出的异常（用于验证错误上报）。</summary>
    public Exception? EditException { get; set; }

    public int LoadCount { get; private set; }

    public int RefreshAllCount { get; private set; }

    public RepositoryEditRequest? LastAdded { get; private set; }

    public string? LastRemovedName { get; private set; }

    public string? LastMovedName { get; private set; }

    public int LastMoveDelta { get; private set; }

    public string? LastRefreshedName { get; private set; }

    public int RestoreCount { get; private set; }

    public RepositorySnapshot Current { get; private set; } = RepositorySnapshot.Empty;

    public event Action<RepositorySnapshot>? Updated
    {
        add => _updated += value;
        remove => _updated -= value;
    }

    public event Action<OperationProgress>? RefreshProgress
    {
        add => _progress += value;
        remove => _progress -= value;
    }

    public IReadOnlyList<RepositoryEntryInfo> Repositories => Rows;

    public event Action<IReadOnlyList<RepositoryEntryInfo>>? RepositoriesChanged
    {
        add => _repositoriesChanged += value;
        remove => _repositoriesChanged -= value;
    }

    public int RepositoriesChangedSubscribers => _repositoriesChanged?.GetInvocationList().Length ?? 0;

    public int ProgressSubscribers => _progress?.GetInvocationList().Length ?? 0;

    /// <summary>模拟服务层广播仓库列表变化（已封送到 UI 线程之后的效果）。</summary>
    public void RaiseRepositoriesChanged(IReadOnlyList<RepositoryEntryInfo> rows)
    {
        Rows = rows.ToList();
        _repositoriesChanged?.Invoke(rows);
    }

    /// <summary>模拟服务层广播刷新进度。</summary>
    public void RaiseProgress(OperationProgress progress) => _progress?.Invoke(progress);

    public Task<OperationResult<RepositorySnapshot>> LoadCachedAsync(CancellationToken ct = default)
        => Task.FromResult(OperationResult<RepositorySnapshot>.Ok(Current));

    public Task<OperationResult<RepositorySnapshot>> RefreshAsync(CancellationToken ct = default)
    {
        RefreshAllCount++;
        return Task.FromResult(RefreshAllResult ?? OperationResult<RepositorySnapshot>.Ok(Current));
    }

    public Task<OperationResult<IReadOnlyList<RepositoryEntryInfo>>> LoadRepositoriesAsync(
        CancellationToken ct = default)
    {
        LoadCount++;
        return Task.FromResult(EditResult ?? Ok());
    }

    public Task<OperationResult<IReadOnlyList<RepositoryEntryInfo>>> AddRepositoryAsync(
        RepositoryEditRequest request, CancellationToken ct = default)
    {
        LastAdded = request;
        if (EditException is not null)
        {
            return Task.FromException<OperationResult<IReadOnlyList<RepositoryEntryInfo>>>(EditException);
        }

        return Task.FromResult(EditResult ?? Ok());
    }

    public Task<OperationResult<IReadOnlyList<RepositoryEntryInfo>>> RemoveRepositoryAsync(
        string name, CancellationToken ct = default)
    {
        LastRemovedName = name;
        return Task.FromResult(EditResult ?? Ok());
    }

    public Task<OperationResult<IReadOnlyList<RepositoryEntryInfo>>> MoveRepositoryAsync(
        string name, int delta, CancellationToken ct = default)
    {
        LastMovedName = name;
        LastMoveDelta = delta;
        return Task.FromResult(EditResult ?? Ok());
    }

    public Task<OperationResult<IReadOnlyList<RepositoryEntryInfo>>> RestoreDefaultRepositoriesAsync(
        CancellationToken ct = default)
    {
        RestoreCount++;
        return Task.FromResult(EditResult ?? Ok());
    }

    public Task<OperationResult<IReadOnlyList<RepositoryEntryInfo>>> RefreshRepositoryAsync(
        string name, CancellationToken ct = default)
    {
        LastRefreshedName = name;
        return Task.FromResult(RefreshOneResult ?? Ok());
    }

    private OperationResult<IReadOnlyList<RepositoryEntryInfo>> Ok()
        => OperationResult<IReadOnlyList<RepositoryEntryInfo>>.Ok(Rows.ToArray());
}

/// <summary>记录上报条目的 IErrorReporter 替身。</summary>
public sealed class ReposFakeErrorReporter : IErrorReporter
{
    private Action<ErrorEntry>? _reported;

    public List<ErrorEntry> Entries { get; } = new();

    public IReadOnlyList<ErrorEntry> Recent => Entries;

    public event Action<ErrorEntry>? Reported
    {
        add => _reported += value;
        remove => _reported -= value;
    }

    public void Report(Exception exception, string context, string? userMessage = null)
        => Record(new ErrorEntry(DateTime.UtcNow, context, userMessage ?? exception.Message,
                                 exception.Message, exception));

    public void Report(string context, string userMessage, string? detail = null)
        => Record(new ErrorEntry(DateTime.UtcNow, context, userMessage, detail, null));

    private void Record(ErrorEntry entry)
    {
        Entries.Add(entry);
        _reported?.Invoke(entry);
    }
}

/// <summary>可预设确认结果的 IDialogService 替身。</summary>
public sealed class ReposFakeDialogService : IDialogService
{
    public bool ConfirmResult { get; set; } = true;

    public int ConfirmCount { get; private set; }

    public string? LastTitle { get; private set; }

    public string? LastMessage { get; private set; }

    public Task<bool> ConfirmAsync(string title, string message, string? detail = null)
    {
        ConfirmCount++;
        LastTitle = title;
        LastMessage = message;
        return Task.FromResult(ConfirmResult);
    }

    public Task<int> SelectAsync(string title, string message, IReadOnlyList<string> options,
                                 int defaultIndex = 0)
        => Task.FromResult(defaultIndex);

    public Task ShowMessageAsync(string title, string message) => Task.CompletedTask;

    public Task ShowErrorAsync(string title, string message, string? detail = null) => Task.CompletedTask;

    public Task<OperationResult<bool>> RunWithProgressAsync(
        string title, Func<IProgress<double>, CancellationToken, Task> work)
        => Task.FromResult(OperationResult<bool>.Ok(true));
}
