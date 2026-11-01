using System.Globalization;

using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CkanUI.ViewModels.Operations;

/// <summary>
/// 后台操作队列浮层的视图模型（06 篇 VM-05，04 篇「OperationQueueViewModel」）。
/// </summary>
/// <remarks>
/// 数据来源只有两个，符合 05 篇 §4 的节流口径：
/// ① <see cref="IOperationQueue.Operations"/> 的同步快照（构造时取一次，之后作为兜底）；
/// ② <see cref="IOperationQueue.Updated"/> 事件（队列内部已按 100ms 合并）。
/// 事件可能在后台线程到达（契约注释说"已在 UI 线程"，但实现层允许任何线程发布），
/// 因此一律经 <see cref="IDispatcher.Post"/> 封送后再改状态——05 篇 §2 禁止视图模型直接 SetProperty。
/// 列表整体替换（team-brief §4 铁律 7）：每 100ms 只发一次 Items 的 PropertyChanged，
/// 不逐行触发通知，也不做增量集合变更。
/// </remarks>
public sealed partial class OperationQueueViewModel : ObservableObject, IActivatable, IDisposable
{
    /// <summary>全部完成后的淡出透明度（03 篇 §1：完成后浮层变淡但不立刻消失，用户还能看到结果）。</summary>
    public const double FadedOpacity = 0.55;

    /// <summary>有任务进行时的正常透明度。</summary>
    public const double ActiveOpacity = 1.0;

    private readonly IOperationQueue _queue;
    private readonly IDispatcher _dispatcher;
    private readonly OperationRateTracker _rates = new();
    private readonly HashSet<string> _cleared = new(StringComparer.Ordinal);
    private readonly Action<string> _cancelRequest;
    private IOperationClock _clock = MonotonicOperationClock.Instance;

    private IReadOnlyList<OperationProgress> _snapshot = Array.Empty<OperationProgress>();
    private IReadOnlyList<OperationItemViewModel> _items = Array.Empty<OperationItemViewModel>();
    private int _disposed;
    private bool _subscribed;

    /// <summary>构造视图模型。</summary>
    /// <param name="queue">后台操作队列（唯一的进度来源）。</param>
    /// <param name="dispatcher">UI 线程封送器（05 篇 §2：事件必须回到 UI 线程再改状态）。</param>
    public OperationQueueViewModel(IOperationQueue queue, IDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(dispatcher);

        _queue = queue;
        _dispatcher = dispatcher;
        _cancelRequest = taskId => _queue.Cancel(taskId);

        Subscribe();
        ApplySnapshot(_queue.Operations);
    }

    /// <summary>时间源；单测可替换为假时钟以稳定推算速率（05 篇 §8）。</summary>
    /// <remarks>
    /// 换时钟必须丢弃旧的采样：不同时间源的时间戳不可比较，
    /// 否则"上一次采样在真实时钟下、这一次在假时钟下"会算出负的间隔，速率永远出不来。
    /// </remarks>
    public IOperationClock Clock
    {
        get => _clock;
        set
        {
            if (ReferenceEquals(_clock, value) || value is null)
            {
                return;
            }

            _clock = value;
            _rates.Reset();
        }
    }

    /// <summary>当前显示的任务行（整体替换，不增量变更）。</summary>
    public IReadOnlyList<OperationItemViewModel> Items
    {
        get => _items;
        private set => SetProperty(ref _items, value);
    }

    /// <summary>聚合百分比（0..100，多任务时按快照平均，见 <see cref="OperationAggregate"/>）。</summary>
    [ObservableProperty]
    private double _overallPercent;

    /// <summary>聚合百分比文案。</summary>
    [ObservableProperty]
    private string _overallPercentText = "0%";

    /// <summary>汇总文案（<c>进行中 2 个任务 · 已完成 1</c>）。</summary>
    [ObservableProperty]
    private string _summaryText = "无后台任务";

    /// <summary>可见任务总数（已"清除已完成"的行不计入）。</summary>
    [ObservableProperty]
    private int _totalCount;

    /// <summary>进行中的任务数。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveOperations))]
    private int _activeCount;

    /// <summary>是否存在进行中的活动任务。</summary>
    public bool HasActiveOperations => ActiveCount > 0;

    /// <summary>已完成的任务数。</summary>
    [ObservableProperty]
    private int _completedCount;

    /// <summary>失败的任务数。</summary>
    [ObservableProperty]
    private int _failedCount;

    /// <summary>是否有可见任务（空队列时浮层整体隐藏）。</summary>
    [ObservableProperty]
    private bool _isVisible;

    /// <summary>是否所有可见任务都已结束（决定淡出）。</summary>
    [ObservableProperty]
    private bool _isAllCompleted;

    /// <summary>浮层是否展开成任务列表。</summary>
    [ObservableProperty]
    private bool _isExpanded;

    /// <summary>展开/收起按钮的文案。</summary>
    [ObservableProperty]
    private string _expandButtonText = "展开";

    /// <summary>浮层整体透明度：进行中 1.0，全部完成后 0.55（淡出）。</summary>
    [ObservableProperty]
    private double _overlayOpacity = ActiveOpacity;

    /// <summary>是否存在可取消的任务（决定"全部取消"是否可用）。</summary>
    [ObservableProperty]
    private bool _hasCancellable;

    /// <summary>空状态文案（03 篇 §10：每处列表都要有空状态）。</summary>
    [ObservableProperty]
    private string _emptyText = "无后台任务";

    /// <summary>订阅队列事件；重复调用无副作用（04 篇 §1：订阅在 Activate）。</summary>
    public void Activate() => Subscribe();

    /// <summary>退订队列事件（04 篇 §1：退订在 Deactivate）。</summary>
    public void Deactivate() => Unsubscribe();

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Unsubscribe();
        _rates.Reset();
    }

    /// <summary>展开/收起任务列表。</summary>
    [RelayCommand]
    private void ToggleExpand() => IsExpanded = !IsExpanded;

    /// <summary>取消单个任务（05 篇 §5：只发取消请求，状态等队列回推）。</summary>
    /// <param name="taskId">任务 id。</param>
    [RelayCommand]
    private void CancelOperation(string? taskId)
    {
        if (string.IsNullOrWhiteSpace(taskId) || Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        _queue.Cancel(taskId);
    }

    /// <summary>取消全部可取消任务。</summary>
    [RelayCommand(CanExecute = nameof(CanCancelAll))]
    private void CancelAll()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        // Items 是不可变快照，遍历期间队列推新快照也不会改动它（05 篇 §7 禁止边遍历边改集合）
        foreach (var item in Items)
        {
            if (item.IsCancellable)
            {
                _queue.Cancel(item.TaskId);
            }
        }
    }

    /// <summary>清除已结束的任务行（只在本地隐藏，队列的 30s 保留期照旧）。</summary>
    [RelayCommand(CanExecute = nameof(CanClearCompleted))]
    private void ClearCompleted()
    {
        foreach (var progress in _snapshot)
        {
            if (OperationAggregate.IsFinishedState(progress.State))
            {
                _cleared.Add(progress.TaskId);
            }
        }

        ApplySnapshot(_snapshot);
    }

    private bool CanCancelAll() => HasCancellable;

    private bool CanClearCompleted()
    {
        foreach (var item in Items)
        {
            if (item.IsFinished)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>展开状态变化时同步按钮文案。</summary>
    /// <param name="value">新的展开状态。</param>
    partial void OnIsExpandedChanged(bool value) => ExpandButtonText = value ? "收起" : "展开";

    private void Subscribe()
    {
        if (_subscribed || Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        _queue.Updated += OnQueueUpdated;
        _subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!_subscribed)
        {
            return;
        }

        _queue.Updated -= OnQueueUpdated;
        _subscribed = false;
    }

    /// <summary>队列推送入口：可能在后台线程，必须封送到 UI 线程再改状态（05 篇 §2 / §3.3）。</summary>
    /// <param name="snapshot">队列快照。</param>
    private void OnQueueUpdated(IReadOnlyList<OperationProgress> snapshot)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        try
        {
            _dispatcher.Post(() => ApplySnapshot(snapshot));
        }
        catch (ObjectDisposedException)
        {
            // 应用退出过程中调度器已释放：丢弃这次更新即可，事件处理里不允许抛出（05 篇 §7）
        }
    }

    /// <summary>把队列快照投影成行与聚合值（只在 UI 线程调用）。</summary>
    /// <param name="snapshot">队列快照。</param>
    private void ApplySnapshot(IReadOnlyList<OperationProgress> snapshot)
    {
        if (snapshot is null || Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        _snapshot = snapshot;
        PruneCleared(snapshot);

        var now = _clock.TimestampMilliseconds;
        var items = new List<OperationItemViewModel>(snapshot.Count);
        var visible = new List<OperationProgress>(snapshot.Count);

        foreach (var progress in snapshot)
        {
            if (_cleared.Contains(progress.TaskId))
            {
                if (OperationAggregate.IsFinishedState(progress.State))
                {
                    continue;
                }

                // 任务重新活跃（例如重试）：撤销隐藏，否则用户会看不到正在跑的任务
                _cleared.Remove(progress.TaskId);
            }

            OperationByteProgress.TryParse(progress.Message, out var bytes);
            var rate = OperationAggregate.IsFinishedState(progress.State)
                ? OperationRate.Unknown
                : _rates.Track(
                    progress.TaskId,
                    progress.Percent,
                    bytes.HasValue ? bytes.Transferred : null,
                    bytes.HasValue ? bytes.Total : null,
                    now);

            items.Add(new OperationItemViewModel(progress, bytes, rate, _cancelRequest));
            visible.Add(progress);
        }

        items.Sort(CompareItems);

        if (snapshot.Count == 0)
        {
            // 队列清空后旧采样没有意义，留着只会让下一批任务继承错误的速率
            _rates.Reset();
        }

        var aggregate = OperationAggregate.Compute(visible);
        var hadItems = _items.Count > 0;

        Items = items;
        OverallPercent = aggregate.OverallPercent;
        OverallPercentText = OperationDisplayFormatter.FormatPercent(aggregate.OverallPercent);
        TotalCount = aggregate.TotalCount;
        ActiveCount = aggregate.ActiveCount;
        CompletedCount = aggregate.CompletedCount;
        FailedCount = aggregate.FailedCount;
        HasCancellable = HasCancellableItem(items);
        IsVisible = items.Count > 0;
        IsAllCompleted = items.Count > 0 && aggregate.IsAllCompleted;
        OverlayOpacity = IsAllCompleted ? FadedOpacity : ActiveOpacity;
        SummaryText = BuildSummary(aggregate, items.Count);
        EmptyText = items.Count == 0 ? "无后台任务" : SummaryText;

        CancelAllCommand.NotifyCanExecuteChanged();
        ClearCompletedCommand.NotifyCanExecuteChanged();

        if (hadItems && items.Count == 0)
        {
            // 原先有运行中任务，所有任务执行完毕并移出队列后自动收起
            IsExpanded = false;
        }
        else if (!hadItems && items.Count > 0)
        {
            // 第一次出现任务时自动展开，用户不必先点一下才知道有后台任务在跑
            IsExpanded = true;
        }
    }

    private static bool HasCancellableItem(List<OperationItemViewModel> items)
    {
        foreach (var item in items)
        {
            if (item.IsCancellable)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>排序：进行中 → 等待 → 失败 → 已取消 → 已完成，同状态按标题序数比较（避免文化相关的排序抖动）。</summary>
    private static int CompareItems(OperationItemViewModel left, OperationItemViewModel right)
    {
        var byState = StateRank(left.Progress.State).CompareTo(StateRank(right.Progress.State));
        return byState != 0
            ? byState
            : string.Compare(left.Title, right.Title, StringComparison.Ordinal);
    }

    private static int StateRank(OperationState state) => state switch
    {
        OperationState.Running => 0,
        OperationState.Pending => 1,
        OperationState.Failed => 2,
        OperationState.Cancelled => 3,
        OperationState.Succeeded => 4,
        _ => 5,
    };

    private static string BuildSummary(OperationAggregate aggregate, int visibleCount)
    {
        if (visibleCount == 0)
        {
            return "无后台任务";
        }

        if (aggregate.ActiveCount > 0)
        {
            var text = string.Format(CultureInfo.InvariantCulture, "进行中 {0} 个任务", aggregate.ActiveCount);
            if (aggregate.CompletedCount > 0)
            {
                text = string.Concat(text, string.Format(CultureInfo.InvariantCulture, " · 已完成 {0}", aggregate.CompletedCount));
            }

            if (aggregate.FailedCount > 0)
            {
                text = string.Concat(text, string.Format(CultureInfo.InvariantCulture, " · 失败 {0}", aggregate.FailedCount));
            }

            if (aggregate.CancelledCount > 0)
            {
                text = string.Concat(text, string.Format(CultureInfo.InvariantCulture, " · 已取消 {0}", aggregate.CancelledCount));
            }

            return text;
        }

        return aggregate.FailedCount > 0
            ? string.Format(CultureInfo.InvariantCulture, "任务结束 · 失败 {0} / 共 {1} 个", aggregate.FailedCount, aggregate.TotalCount)
            : string.Format(CultureInfo.InvariantCulture, "全部完成 · 共 {0} 个任务", aggregate.TotalCount);
    }

    /// <summary>丢弃已离开队列的任务的"清除"标记与速率采样，避免随会话无限增长。</summary>
    private void PruneCleared(IReadOnlyList<OperationProgress> snapshot)
    {
        if (_cleared.Count == 0)
        {
            return;
        }

        var alive = new HashSet<string>(StringComparer.Ordinal);
        foreach (var progress in snapshot)
        {
            alive.Add(progress.TaskId);
        }

        _cleared.RemoveWhere(id => !alive.Contains(id));
    }
}
