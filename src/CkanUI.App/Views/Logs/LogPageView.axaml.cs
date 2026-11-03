using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CkanUI.ViewModels.Pages.Logs;

namespace CkanUI.App.Views.Logs;

/// <summary>日志页（14 篇 §4.1 / 06 篇 UI-LOG-01）。视图只做布局，过滤/聚合/导出全在视图模型里。</summary>
public partial class LogPageView : UserControl
{
    /// <summary>离底部多近算「贴在末尾」：留一点余量，避免亚像素误差把跟随开关误判成关。</summary>
    private const double BottomTolerance = 32d;

    /// <summary>列表行的固定高度，与 XAML 里的行高保持一致（滚动锚点按行数换算像素）。</summary>
    private const double RowHeight = 20d;

    private ListBox? _rowsList;
    private ScrollViewer? _rowsScroll;
    private LogPageViewModel? _viewModel;
    private double _savedOffset;
    private double _pendingShift;
    private bool _restorePending;
    private bool _applyingScroll;

    /// <summary>构造并装载 XAML。</summary>
    public LogPageView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        AttachedToVisualTree += (_, _) => AttachRowsScroll();
        DetachedFromVisualTree += (_, _) => DetachRowsScroll();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.Rows.CollectionChanged -= OnRowsChanged;
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = DataContext as LogPageViewModel;
        if (_viewModel is not null)
        {
            _viewModel.Rows.CollectionChanged += OnRowsChanged;
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void AttachRowsScroll()
    {
        _rowsList = this.FindControl<ListBox>("LogList");
        if (_rowsList is null)
        {
            return;
        }

        _rowsList.Loaded -= OnRowsListLoaded;
        _rowsList.Loaded += OnRowsListLoaded;
        if (_rowsList.IsLoaded)
        {
            OnRowsListLoaded(_rowsList, EventArgs.Empty);
        }
    }

    private void OnRowsListLoaded(object? sender, EventArgs e)
    {
        if (_rowsList is null)
        {
            return;
        }

        _rowsScroll ??= _rowsList.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (_rowsScroll is null)
        {
            return;
        }

        _rowsScroll.ScrollChanged -= OnScrollChanged;
        _rowsScroll.ScrollChanged += OnScrollChanged;
        RestoreScroll();
    }

    private void DetachRowsScroll()
    {
        if (_rowsScroll is not null)
        {
            _rowsScroll.ScrollChanged -= OnScrollChanged;
        }

        if (_rowsList is not null)
        {
            _rowsList.Loaded -= OnRowsListLoaded;
        }

        _rowsScroll = null;
        _rowsList = null;
    }

    /// <summary>
    /// 记录滚动位置；用户主动往上滚时顺手关掉「跟随末尾」。
    /// 只用 OffsetDelta 判断方向：行追加会让 Extent 变大、Offset 不变，此时不能当成用户滚开。
    /// </summary>
    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_rowsScroll is null || _applyingScroll)
        {
            return;
        }

        _savedOffset = _rowsScroll.Offset.Y;

        // 只认「范围没变、位置往上走」：内容增删也会让 Offset 被动变化（夹取），那不是用户在滚。
        if (e.OffsetDelta.Y < -1 && Math.Abs(e.ExtentDelta.Y) < 0.5 && _viewModel is { IsFollowTail: true } viewModel)
        {
            viewModel.IsFollowTail = false;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LogPageViewModel.IsFollowTail))
        {
            RequestRestore();
        }
    }

    private void OnRowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // 窗口往前滑时会从头部删掉最旧的行，内容整体上移；把这份位移补进锚点，
        // 用户正在往上翻历史时才不会被每条新日志推着往下走。
        if (e.Action == NotifyCollectionChangedAction.Remove && e.OldStartingIndex == 0)
        {
            _pendingShift -= RowHeight;
        }
        else if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            _pendingShift = 0d;
        }

        RequestRestore();
    }

    /// <summary>
    /// 集合变了以后补一次滚动定位：跟随末尾 → 贴底；否则回到用户原来的位置（越界时夹回范围内）。
    /// 必须等布局跑完，否则 Extent 还是旧值，夹取会把位置算错。
    /// </summary>
    private void RequestRestore()
    {
        if (_restorePending)
        {
            return;
        }

        _restorePending = true;
        Dispatcher.UIThread.Post(
            () =>
            {
                _restorePending = false;
                RestoreScroll();
            },
            DispatcherPriority.Loaded);
    }

    private void RestoreScroll()
    {
        if (_rowsScroll is null)
        {
            return;
        }

        var max = Math.Max(0d, _rowsScroll.Extent.Height - _rowsScroll.Viewport.Height);
        var anchor = _savedOffset + _pendingShift;
        _pendingShift = 0d;
        var target = _viewModel is { IsFollowTail: true } ? max : Math.Clamp(anchor, 0d, max);
        if (Math.Abs(_rowsScroll.Offset.Y - target) < 0.5d)
        {
            return;
        }

        _applyingScroll = true;
        try
        {
            _rowsScroll.Offset = new Vector(0d, target);
        }
        finally
        {
            _applyingScroll = false;
            _savedOffset = target;
        }
    }
}
