using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Plugin.Saves.Tests;

// 实例 / 对话框 / 错误上报三个替身：从宿主测试工程原样复制（存档页用例只用这三个），
// 留在原工程的同名替身不能被插件测试反向引用，所以随用例一起搬一份。

/// <summary>
/// 实例页测试替身集合（07 篇：headless 可测）。
/// </summary>
/// <remarks>
/// 所有替身都只记录调用次数与参数、并允许预设返回值，绝不碰真实磁盘与真实对话框，
/// 因此可以在 CI 上稳定跑。事件都实现成真实字段事件（不是空 add/remove），好让页面的事件订阅路径也被覆盖。
/// </remarks>
internal sealed class InstancesFakeService : IInstanceService
{
    private readonly List<InstanceInfo> _items = new();

    /// <inheritdoc />
    public IReadOnlyList<InstanceInfo> Instances => _items;

    /// <inheritdoc />
    public InstanceInfo? Current { get; set; }

    public int RefreshCalls { get; private set; }

    public int SwitchCalls { get; private set; }

    public int AddCalls { get; private set; }

    public int CloneCalls { get; private set; }

    public int RemoveCalls { get; private set; }

    public int RenameCalls { get; private set; }

    public string? LastSwitchName { get; private set; }

    public string? LastAddPath { get; private set; }

    public string? LastCloneSource { get; private set; }

    public string? LastCloneName { get; private set; }

    public string? LastRemoveName { get; private set; }

    public string? LastRenameFrom { get; private set; }

    public string? LastRenameTo { get; private set; }

    /// <summary>预设的切换异常（模拟服务层意外抛出）。</summary>
    public Exception? SwitchThrows { get; set; }

    /// <summary>预设的切换结果（非 null 时不再走默认逻辑）。</summary>
    public OperationResult<InstanceInfo>? SwitchResult { get; set; }

    /// <summary>预设的克隆结果。</summary>
    public OperationResult<InstanceInfo>? CloneResult { get; set; }

    /// <summary>预设的遗忘结果。</summary>
    public OperationResult<bool>? RemoveResult { get; set; }

    /// <summary>预设的重命名结果。</summary>
    public OperationResult<InstanceInfo>? RenameResult { get; set; }

    /// <inheritdoc />
    public event Action? InstancesChanged;

    /// <inheritdoc />
    public event Action<InstanceInfo?>? CurrentChanged;

    public void Seed(params InstanceInfo[] items)
    {
        _items.Clear();
        _items.AddRange(items);
    }

    public void RaiseInstancesChanged() => InstancesChanged?.Invoke();

    public void RaiseCurrentChanged() => CurrentChanged?.Invoke(Current);

    /// <inheritdoc />
    public Task<OperationResult<IReadOnlyList<InstanceInfo>>> RefreshAsync(CancellationToken ct = default)
    {
        RefreshCalls++;
        return Task.FromResult(OperationResult<IReadOnlyList<InstanceInfo>>.Ok(_items.ToArray()));
    }

    /// <inheritdoc />
    public Task<OperationResult<InstanceInfo>> SwitchAsync(string instanceName, CancellationToken ct = default)
    {
        SwitchCalls++;
        LastSwitchName = instanceName;
        if (SwitchThrows is { } exception)
        {
            throw exception;
        }

        if (SwitchResult is { } preset)
        {
            return Task.FromResult(preset);
        }

        var target = _items.FirstOrDefault(item => string.Equals(item.Name, instanceName, StringComparison.Ordinal));
        if (target is null)
        {
            return Task.FromResult(OperationResult<InstanceInfo>.Fail($"实例「{instanceName}」不存在。"));
        }

        if (target.IsLocked)
        {
            return Task.FromResult(OperationResult<InstanceInfo>.Fail($"实例「{instanceName}」已被其他 CKAN 进程锁定，无法切换。"));
        }

        Current = target;
        CurrentChanged?.Invoke(target);
        return Task.FromResult(OperationResult<InstanceInfo>.Ok(target));
    }

    /// <inheritdoc />
    public Task<OperationResult<InstanceInfo>> AddAsync(string path, string? name = null, CancellationToken ct = default)
    {
        AddCalls++;
        LastAddPath = path;
        var info = new InstanceInfo(name ?? Path.GetFileName(path), path, "1.12.5", false, false, 0);
        _items.Add(info);
        InstancesChanged?.Invoke();
        return Task.FromResult(OperationResult<InstanceInfo>.Ok(info));
    }

    /// <inheritdoc />
    public Task<OperationResult<InstanceInfo>> CloneAsync(string sourceName, string newName, CancellationToken ct = default)
    {
        CloneCalls++;
        LastCloneSource = sourceName;
        LastCloneName = newName;
        if (CloneResult is { } preset)
        {
            return Task.FromResult(preset);
        }

        var info = new InstanceInfo(newName, "D:/clone", null, false, false, 0);
        _items.Add(info);
        InstancesChanged?.Invoke();
        return Task.FromResult(OperationResult<InstanceInfo>.Ok(info));
    }

    /// <inheritdoc />
    public Task<OperationResult<InstanceInfo>> RenameAsync(string instanceName, string newName, CancellationToken ct = default)
    {
        RenameCalls++;
        LastRenameFrom = instanceName;
        LastRenameTo = newName;
        if (RenameResult is { } preset)
        {
            return Task.FromResult(preset);
        }

        var index = _items.FindIndex(item => string.Equals(item.Name, instanceName, StringComparison.Ordinal));
        if (index < 0)
        {
            return Task.FromResult(OperationResult<InstanceInfo>.Fail("找不到实例"));
        }

        var info = _items[index] with { Name = newName };
        _items[index] = info;
        InstancesChanged?.Invoke();
        return Task.FromResult(OperationResult<InstanceInfo>.Ok(info));
    }

    /// <inheritdoc />
    public Task<OperationResult<bool>> RemoveAsync(string instanceName, CancellationToken ct = default)
    {
        RemoveCalls++;
        LastRemoveName = instanceName;
        if (RemoveResult is { } preset)
        {
            return Task.FromResult(preset);
        }

        var removed = _items.RemoveAll(item => string.Equals(item.Name, instanceName, StringComparison.Ordinal)) > 0;
        InstancesChanged?.Invoke();
        return Task.FromResult(OperationResult<bool>.Ok(removed));
    }
}

/// <summary>对话框替身：记录确认/选择调用，返回可预设的结果。</summary>
internal sealed class InstancesFakeDialogs : IDialogService
{
    public bool ConfirmResult { get; set; } = true;

    public int SelectResult { get; set; }

    public Exception? ConfirmThrows { get; set; }

    public List<string> ConfirmTitles { get; } = new();

    public List<string> ConfirmMessages { get; } = new();

    public int ConfirmCalls => ConfirmTitles.Count;

    public int SelectCalls { get; private set; }

    /// <summary>每次选择对话框收到的候选项（按调用顺序），用于断言"弹窗里到底给了什么"。</summary>
    public List<IReadOnlyList<string>> SelectOptions { get; } = new();

    /// <summary>每次选择对话框的标题。</summary>
    public List<string> SelectTitles { get; } = new();

    /// <inheritdoc />
    public Task<bool> ConfirmAsync(string title, string message, string? detail = null)
    {
        ConfirmTitles.Add(title);
        ConfirmMessages.Add(message);
        if (ConfirmThrows is { } exception)
        {
            return Task.FromException<bool>(exception);
        }

        return Task.FromResult(ConfirmResult);
    }

    /// <inheritdoc />
    public Task<int> SelectAsync(string title, string message, IReadOnlyList<string> options, int defaultIndex = 0)
    {
        SelectCalls++;
        SelectTitles.Add(title);
        SelectOptions.Add(options);
        return Task.FromResult(SelectResult);
    }

    /// <inheritdoc />
    public Task ShowMessageAsync(string title, string message) => Task.CompletedTask;

    /// <inheritdoc />
    public Task ShowErrorAsync(string title, string message, string? detail = null) => Task.CompletedTask;

    /// <inheritdoc />
    public Task<OperationResult<bool>> RunWithProgressAsync(
        string title,
        Func<IProgress<double>, CancellationToken, Task> work) => Task.FromResult(OperationResult<bool>.Ok(true));
}

/// <summary>错误上报器替身：统计上报次数与最后一次上下文。</summary>
internal sealed class InstancesFakeErrorReporter : IErrorReporter
{
    /// <inheritdoc />
    public event Action<ErrorEntry>? Reported
    {
        add { }
        remove { }
    }

    /// <inheritdoc />
    public IReadOnlyList<ErrorEntry> Recent => Array.Empty<ErrorEntry>();

    public int ReportCount { get; private set; }

    public string? LastContext { get; private set; }

    /// <inheritdoc />
    public void Report(Exception exception, string context, string? userMessage = null)
    {
        ReportCount++;
        LastContext = context;
    }

    /// <inheritdoc />
    public void Report(string context, string userMessage, string? detail = null)
    {
        ReportCount++;
        LastContext = context;
    }
}
