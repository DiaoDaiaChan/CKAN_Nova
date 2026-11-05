using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.ViewModels.Tests;

/// <summary>变更集服务替身：可配置返回结果 / 抛异常，并记录调用次数与入参（04 篇 §2 / INT-07 用例共用）。</summary>
internal sealed class ChangesFakeChangeSetService : IChangeSetService
{
    /// <summary>下一次 <see cref="BuildAsync"/> 的返回结果；为 null 时返回 <see cref="DefaultResult"/>。</summary>
    public OperationResult<ChangeSetPreview>? NextResult { get; set; }

    /// <summary><see cref="NextResult"/> 为 null 时的默认成功结果。</summary>
    public OperationResult<ChangeSetPreview> DefaultResult { get; set; } =
        OperationResult<ChangeSetPreview>.Ok(ChangesFakes.EmptyPreview);

    /// <summary>非 null 时 <see cref="BuildAsync"/> 抛出该异常。</summary>
    public Exception? ThrowOnBuild { get; set; }

    /// <summary><see cref="BuildAsync"/> 被调用次数。</summary>
    public int BuildCount { get; private set; }

    /// <summary><see cref="BuildLocal"/> 被调用次数。</summary>
    public int BuildLocalCount { get; private set; }

    /// <summary>每次调用收到的请求集合快照。</summary>
    public List<IReadOnlyCollection<ChangeRequest>> ReceivedRequests { get; } = new();

    /// <inheritdoc />
    public Task<OperationResult<ChangeSetPreview>> BuildAsync(
        IReadOnlyCollection<ChangeRequest> requests,
        CancellationToken ct = default)
    {
        BuildCount++;
        ReceivedRequests.Add(requests);
        if (ThrowOnBuild is { } error)
        {
            throw error;
        }

        return Task.FromResult(NextResult ?? DefaultResult);
    }

    /// <inheritdoc />
    public OperationResult<ChangeSetPreview> BuildLocal(IReadOnlyCollection<ChangeRequest> requests)
    {
        BuildLocalCount++;
        ReceivedRequests.Add(requests);
        return NextResult ?? DefaultResult;
    }
}

/// <summary>安装器替身：记录收到的变更集，可选挂起等待取消（03 篇 §4.1 取消语义用例）。</summary>
internal sealed class ChangesFakeInstallerService : IInstallerService
{
    /// <summary><see cref="ApplyAsync"/> 的返回结果。</summary>
    public OperationResult<InstallReport> Result { get; set; } =
        OperationResult<InstallReport>.Ok(ChangesFakes.SuccessReport());

    /// <summary>为 true 时一直挂起直到令牌取消（用于取消用例）。</summary>
    public bool WaitForCancellation { get; set; }

    /// <summary>非 null 时 <see cref="ApplyAsync"/> 抛出该异常（用于异常收口用例）。</summary>
    public Exception? ThrowOnApply { get; set; }

    /// <summary>按次返回的结果队列：用于「第一次要求用户选提供者、选完第二次成功」这类多轮闭环。</summary>
    public Queue<OperationResult<InstallReport>> Results { get; } = new();

    /// <summary>执行期要回放的进度序列（用于断言阶段徽标与百分比落点）。</summary>
    public IReadOnlyList<OperationProgress> ProgressToReport { get; set; } = Array.Empty<OperationProgress>();

    /// <summary>非 null 时在回放完进度后挂起，便于断言"执行中"的中间态。</summary>
    public TaskCompletionSource? HangAfterProgress { get; set; }

    /// <summary><see cref="ApplyAsync"/> 被调用次数。</summary>
    public int ApplyCount { get; private set; }

    /// <summary>最后一次收到的变更集。</summary>
    public ChangeSetPreview? Received { get; private set; }

    /// <inheritdoc />
    public async Task<OperationResult<InstallReport>> ApplyAsync(
        ChangeSetPreview changeSet,
        IProgress<OperationProgress>? progress = null,
        CancellationToken ct = default)
    {
        ApplyCount++;
        Received = changeSet;
        if (ThrowOnApply is { } error)
        {
            await Task.Yield();
            throw error;
        }

        if (WaitForCancellation)
        {
            await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
        }
        else
        {
            await Task.Yield();
        }

        foreach (var item in ProgressToReport)
        {
            progress?.Report(item);
        }

        if (HangAfterProgress is { } gate)
        {
            await gate.Task.ConfigureAwait(false);
        }

        return Results.Count > 0 ? Results.Dequeue() : Result;
    }
}

/// <summary>
/// 错误上报替身：统计上报次数，并记录 <see cref="Reported"/> 的订阅者数量，
/// 用于断言 <c>Deactivate</c> 之后订阅确实被清掉（04 篇 §2 / 05 篇）。
/// </summary>
internal sealed class ChangesCountingErrorReporter : IErrorReporter
{
    private Action<ErrorEntry>? _handlers;

    /// <inheritdoc />
    public event Action<ErrorEntry>? Reported
    {
        add => _handlers += value;
        remove => _handlers -= value;
    }

    /// <summary>当前订阅者数量。</summary>
    public int HandlerCount => _handlers?.GetInvocationList().Length ?? 0;

    /// <summary>异常类上报次数。</summary>
    public int ExceptionReports { get; private set; }

    /// <summary>非异常类上报次数。</summary>
    public int MessageReports { get; private set; }

    /// <summary>全部上报记录。</summary>
    public List<ErrorEntry> Entries { get; } = new();

    /// <inheritdoc />
    public IReadOnlyList<ErrorEntry> Recent => Entries;

    /// <inheritdoc />
    public void Report(Exception exception, string context, string? userMessage = null)
    {
        ExceptionReports++;
        Record(new ErrorEntry(DateTime.UtcNow, context, userMessage ?? "发生错误", exception.Message, exception));
    }

    /// <inheritdoc />
    public void Report(string context, string userMessage, string? detail = null)
    {
        MessageReports++;
        Record(new ErrorEntry(DateTime.UtcNow, context, userMessage, detail, null));
    }

    private void Record(ErrorEntry entry)
    {
        Entries.Add(entry);
        _handlers?.Invoke(entry);
    }
}

/// <summary>调度器替身：始终报告在 UI 线程上，回调就地执行。</summary>
internal sealed class ChangesImmediateDispatcher : IDispatcher
{
    /// <inheritdoc />
    public bool IsOnUiThread => true;

    /// <inheritdoc />
    public void Post(Action action) => action();

    /// <inheritdoc />
    public Task InvokeAsync(Action action)
    {
        action();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<T> InvokeAsync<T>(Func<T> func) => Task.FromResult(func());
}

/// <summary>测试共用的预览/报告构造器，避免每个用例重复写长构造。</summary>
internal static class ChangesFakes
{
    /// <summary>空变更集。</summary>
    public static ChangeSetPreview EmptyPreview { get; } = new(
        Array.Empty<ChangeItemDto>(),
        Array.Empty<string>(),
        0,
        Array.Empty<string>());

    /// <summary>构造一项变更明细。</summary>
    public static ChangeItemDto Item(
        string identifier,
        string? name = null,
        ChangeAction action = ChangeAction.Install,
        string? from = null,
        string? to = "1.0.0",
        ChangeOrigin origin = ChangeOrigin.UserRequested,
        long? bytes = null) =>
        new(identifier, name ?? identifier, action, from, to, origin, bytes);

    /// <summary>构造一份变更集预览。</summary>
    public static ChangeSetPreview Preview(
        IReadOnlyList<ChangeItemDto> items,
        long bytes = 0,
        IReadOnlyList<string>? conflicts = null,
        IReadOnlyList<string>? warnings = null) =>
        new(items, conflicts ?? Array.Empty<string>(), bytes, warnings ?? Array.Empty<string>());

    /// <summary>全成功安装报告。</summary>
    public static InstallReport SuccessReport() => new(
        new[] { "A" },
        Array.Empty<string>(),
        Array.Empty<string>(),
        Array.Empty<string>());

    /// <summary>部分失败安装报告。</summary>
    public static InstallReport PartialFailureReport() => new(
        new[] { "A" },
        Array.Empty<string>(),
        new[] { "B" },
        Array.Empty<string>());

    /// <summary>一组请求（用于触发重算）。</summary>
    public static IReadOnlyCollection<ChangeRequest> Requests(params string[] identifiers)
    {
        var list = new List<ChangeRequest>(identifiers.Length);
        foreach (var identifier in identifiers)
        {
            list.Add(new ChangeRequest(identifier, ChangeAction.Install));
        }

        return list;
    }
}

/// <summary>模组对话框服务替身：记录调用与入参，并支持预设返回结果（F-34..F-36 闭环测试）。</summary>
internal sealed class FakeModDialogService : IModDialogService
{
    public RecommendationSelection? NextRecommendationSelection { get; set; }
    public VirtualModuleChoice? NextVirtualModuleChoice { get; set; }

    public int SelectRecommendationsCallCount { get; private set; }
    public int SelectVirtualModuleCallCount { get; private set; }
    public int ShowConflictDetailsCallCount { get; private set; }

    public RecommendationDialogRequest? LastRecommendationRequest { get; private set; }
    public VirtualModuleRequest? LastVirtualModuleRequest { get; private set; }
    public ConflictDetailsRequest? LastConflictDetailsRequest { get; private set; }

    public Exception? ThrowOnRecommendations { get; set; }
    public Exception? ThrowOnVirtualModule { get; set; }
    public Exception? ThrowOnConflictDetails { get; set; }

    public Task<RecommendationSelection?> SelectRecommendationsAsync(
        RecommendationDialogRequest request,
        CancellationToken ct = default)
    {
        SelectRecommendationsCallCount++;
        LastRecommendationRequest = request;
        if (ThrowOnRecommendations is { } ex)
        {
            throw ex;
        }

        return Task.FromResult(NextRecommendationSelection);
    }

    public Task<VirtualModuleChoice?> SelectVirtualModuleAsync(
        VirtualModuleRequest request,
        CancellationToken ct = default)
    {
        SelectVirtualModuleCallCount++;
        LastVirtualModuleRequest = request;
        if (ThrowOnVirtualModule is { } ex)
        {
            throw ex;
        }

        return Task.FromResult(NextVirtualModuleChoice);
    }

    public Task ShowConflictDetailsAsync(
        ConflictDetailsRequest request,
        CancellationToken ct = default)
    {
        ShowConflictDetailsCallCount++;
        LastConflictDetailsRequest = request;
        if (ThrowOnConflictDetails is { } ex)
        {
            throw ex;
        }

        return Task.CompletedTask;
    }
}
