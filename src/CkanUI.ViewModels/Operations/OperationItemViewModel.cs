using CkanUI.Services.Abstractions.Dtos;
using CommunityToolkit.Mvvm.Input;

namespace CkanUI.ViewModels.Operations;

/// <summary>
/// 队列里的一行任务（03 篇 §1「操作状态区」、06 篇 UI-08）。
/// </summary>
/// <remarks>
/// 刻意做成不可变投影而不是 <c>ObservableObject</c>：队列每 100ms 推一次快照，
/// 逐行触发 <c>PropertyChanged</c> 会造成成百上千次绑定刷新（team-brief §4 铁律 7「列表投影整体替换」）。
/// 所有显示文案在构造时算好，视图里不做格式化、不做转换器。
/// </remarks>
public sealed class OperationItemViewModel
{
    private readonly Action<string> _cancelRequest;

    /// <summary>构造一行任务。</summary>
    /// <param name="progress">队列快照里的原始进度记录。</param>
    /// <param name="bytes">从消息里解析出的字节进度；无则 <see cref="OperationByteProgress.None"/>。</param>
    /// <param name="rate">速率与剩余时间推算结果。</param>
    /// <param name="cancelRequest">取消回调（队列的 <c>Cancel</c> 门面）。</param>
    public OperationItemViewModel(
        OperationProgress progress,
        OperationByteProgress bytes,
        OperationRate rate,
        Action<string> cancelRequest)
    {
        ArgumentNullException.ThrowIfNull(progress);
        ArgumentNullException.ThrowIfNull(rate);
        ArgumentNullException.ThrowIfNull(cancelRequest);

        _cancelRequest = cancelRequest;
        Progress = progress;
        Percent = Math.Clamp(progress.Percent, 0, 100);
        PercentText = OperationDisplayFormatter.FormatPercent(Percent);
        KindText = OperationDisplayFormatter.FormatKind(progress.Kind);
        StateText = OperationDisplayFormatter.FormatState(progress.State);
        BytesText = bytes.HasValue
            ? OperationDisplayFormatter.FormatBytePair(bytes.Transferred, bytes.Total)
            : OperationDisplayFormatter.Placeholder;
        HasByteProgress = bytes.HasValue;
        SpeedText = OperationDisplayFormatter.FormatSpeed(rate.BytesPerSecond);
        RemainingText = OperationDisplayFormatter.FormatRemaining(rate.Remaining);
        CancelCommand = new RelayCommand(RequestCancel);
    }

    /// <summary>原始进度记录（聚合计算仍用它，避免再从显示文案反推状态）。</summary>
    public OperationProgress Progress { get; }

    /// <summary>任务 id（取消时回传给队列）。</summary>
    public string TaskId => Progress.TaskId;

    /// <summary>任务标题；空标题时给占位文案，避免空白行。</summary>
    public string Title => string.IsNullOrWhiteSpace(Progress.Title) ? "未命名任务" : Progress.Title;

    /// <summary>任务种类原始值（服务层写入的标识）。</summary>
    public string Kind => Progress.Kind;

    /// <summary>任务种类中文名。</summary>
    public string KindText { get; }

    /// <summary>状态中文名。</summary>
    public string StateText { get; }

    /// <summary>百分比（0..100，已夹紧）。</summary>
    public double Percent { get; }

    /// <summary>百分比文案（<c>42%</c>）。</summary>
    public string PercentText { get; }

    /// <summary>是否解析出了字节进度（决定字节列是否显示）。</summary>
    public bool HasByteProgress { get; }

    /// <summary>"已传 / 总字节"文案；无数据时为 <c>—</c>。</summary>
    public string BytesText { get; }

    /// <summary>速率文案（<c>1.2 MB/s</c> 或 <c>—</c>）。</summary>
    public string SpeedText { get; }

    /// <summary>剩余时间文案（<c>剩余 00:42</c> 或 <c>剩余 —</c>）。</summary>
    public string RemainingText { get; }

    /// <summary>队列消息原文（Core 文案，UI 不二次翻译，03 篇 §11）。</summary>
    public string Message => Progress.Message ?? string.Empty;

    /// <summary>是否有消息可显示。</summary>
    public bool ShowMessage => Message.Length > 0;

    /// <summary>任务是否已结束。</summary>
    public bool IsFinished => OperationAggregate.IsFinishedState(Progress.State);

    /// <summary>任务是否仍在等待或进行中。</summary>
    public bool IsRunning => Progress.State is OperationState.Pending or OperationState.Running;

    /// <summary>
    /// 是否显示取消按钮：既要求队列标记可取消，也要求任务还没结束
    /// （05 篇 §5：取消是请求，任务结束后按钮必须消失，不能留下"点了没反应"的按钮）。
    /// </summary>
    public bool IsCancellable => Progress.CanCancel && IsRunning;

    /// <summary>取消本任务。</summary>
    public IRelayCommand CancelCommand { get; }

    private void RequestCancel() => _cancelRequest(TaskId);
}
