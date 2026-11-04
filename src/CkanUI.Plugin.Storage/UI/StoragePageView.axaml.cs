using Avalonia.Controls;

namespace CkanUI.Plugin.Storage.UI;

/// <summary>
/// 「存储管理」页的视图。只做生命周期接线：挂上可视树时 Start（首次扫描），离开时 Stop。
/// 页面没有 DispatcherTimer，也不会常驻轮询磁盘——刷新由用户点「扫描」或重新挂载触发。
/// </summary>
public partial class StoragePageView : UserControl
{
    private StoragePageViewModel? _viewModel;
    private bool _isAttached;

    public StoragePageView()
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
        var viewModel = DataContext as StoragePageViewModel;
        if (!ReferenceEquals(_viewModel, viewModel))
        {
            _viewModel?.Stop();
            _viewModel = viewModel;
        }

        // Start 只自动扫描一次；重新挂载不会重复扫盘，用户要新数据就点「扫描」。
        _viewModel?.Start();
    }
}
