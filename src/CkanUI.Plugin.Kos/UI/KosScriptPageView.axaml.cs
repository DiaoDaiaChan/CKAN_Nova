using Avalonia.Controls;

namespace CkanUI.Plugin.Kos.UI;

/// <summary>
/// 「kOS 脚本」页的视图。只做生命周期接线：挂上可视树时 Start/Refresh，离开时 Stop。
/// 页面没有 DispatcherTimer，也不会为“外部改动”常驻轮询——刷新由用户或重新挂载触发。
/// </summary>
public partial class KosScriptPageView : UserControl
{
    private KosScriptPageViewModel? _viewModel;
    private bool _isAttached;

    public KosScriptPageView()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnAttachedToVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        _isAttached = true;
        AttachViewModel();
    }

    private void OnDetachedFromVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        _isAttached = false;
        _viewModel?.Stop();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_isAttached)
        {
            AttachViewModel();
        }
    }

    private void AttachViewModel()
    {
        var viewModel = DataContext as KosScriptPageViewModel;
        if (!ReferenceEquals(_viewModel, viewModel))
        {
            _viewModel?.Stop();
            _viewModel = viewModel;
        }

        _viewModel?.Start();

        // 重新挂载时 Start 会自行判断是否已加载；这里显式 Refresh 对应“页面重新可见”的语义。
        // VM 在加载中会忽略这次调用，不会产生并发扫描。
        _viewModel?.Refresh();
    }
}
