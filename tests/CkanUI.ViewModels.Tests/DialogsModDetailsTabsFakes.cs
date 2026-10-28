using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Navigation;
using CkanUI.ViewModels.Pages;

namespace CkanUI.ViewModels.Tests;

/// <summary>
/// 详情面板四页签用例的替身集合（与 <c>ChangesTestDoubles</c> 分开，避免并行任务改同一个文件）。
/// </summary>
internal sealed class ModTabsFakeChangeSetService : IChangeSetService
{
    /// <summary>下一次 <see cref="BuildAsync"/> 的返回结果（默认空预览）。</summary>
    public OperationResult<ChangeSetPreview> NextResult { get; set; } =
        OperationResult<ChangeSetPreview>.Ok(new ChangeSetPreview([], [], 0, []));

    /// <summary>非 null 时 <see cref="BuildAsync"/> 抛该异常，用于验证异常路径。</summary>
    public Exception? ThrowOnBuild { get; set; }

    /// <summary>非 null 时 <see cref="BuildAsync"/> 等待它完成，用于验证忙碌态禁用按钮。</summary>
    public TaskCompletionSource<bool>? Gate { get; set; }

    /// <summary>收到的请求批次（每批一次 <see cref="BuildAsync"/>）。</summary>
    public List<IReadOnlyCollection<ChangeRequest>> ReceivedRequests { get; } = new();

    /// <inheritdoc />
    public async Task<OperationResult<ChangeSetPreview>> BuildAsync(
        IReadOnlyCollection<ChangeRequest> requests,
        CancellationToken ct = default)
    {
        ReceivedRequests.Add(requests);

        if (Gate is not null)
        {
            await Gate.Task.ConfigureAwait(true);
        }

        if (ThrowOnBuild is not null)
        {
            throw ThrowOnBuild;
        }

        return NextResult;
    }

    /// <inheritdoc />
    public OperationResult<ChangeSetPreview> BuildLocal(IReadOnlyCollection<ChangeRequest> requests) => NextResult;
}

/// <summary>变更集落点替身：记录被灌进来的请求批次。</summary>
internal sealed class ModTabsFakeChangeRequestSink : IChangeRequestSink
{
    /// <summary>最近一次灌入的请求（null 表示被清空）。</summary>
    public IReadOnlyCollection<ChangeRequest>? LastRequests { get; private set; }

    /// <summary><see cref="SetRequests"/> 被调用的次数。</summary>
    public int SetCount { get; private set; }

    /// <inheritdoc />
    public event Action? RequestsApplied
    {
        add { }
        remove { }
    }

    /// <inheritdoc />
    public void SetRequests(IReadOnlyCollection<ChangeRequest>? requests)
    {
        LastRequests = requests;
        SetCount++;
    }
}

/// <summary>导航服务替身：记录目标页键与参数，可配置返回 false 或抛异常。</summary>
internal sealed class ModTabsFakeNavigationService : INavigationService
{
    /// <summary>导航返回值（默认成功）。</summary>
    public bool Result { get; set; } = true;

    /// <summary>非 null 时 <see cref="NavigateAsync"/> 抛该异常。</summary>
    public Exception? ThrowOnNavigate { get; set; }

    /// <summary>收到的导航调用（页键, 参数）。</summary>
    public List<(string Key, object? Parameter)> Calls { get; } = new();

    /// <inheritdoc />
    public object? CurrentPage => null;

    /// <inheritdoc />
    public string CurrentKey { get; private set; } = string.Empty;

    /// <inheritdoc />
    public event Action<object?, string>? Navigated;

    /// <inheritdoc />
    public Task<bool> NavigateAsync(string pageKey, object? parameter = null, CancellationToken ct = default)
    {
        Calls.Add((pageKey, parameter));

        if (ThrowOnNavigate is not null)
        {
            throw ThrowOnNavigate;
        }

        CurrentKey = pageKey;
        Navigated?.Invoke(this, pageKey);
        return Task.FromResult(Result);
    }
}
