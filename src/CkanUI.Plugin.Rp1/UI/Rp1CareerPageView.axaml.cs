using Avalonia.Controls;

namespace CkanUI.Plugin.Rp1.UI;

/// <summary>
/// 「RP-1 生涯日志」页的视图。只做生命周期接线：挂上可视树时 Start，离开时 Stop。
/// 页面没有轮询计时器；重新挂载或点刷新才会重新读存档。
/// </summary>
public partial class Rp1CareerPageView : UserControl
{
    private Rp1CareerPageViewModel? _viewModel;
    private bool _isAttached;

    public Rp1CareerPageView()
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
        var viewModel = DataContext as Rp1CareerPageViewModel;
        if (!ReferenceEquals(_viewModel, viewModel))
        {
            _viewModel?.Stop();
            _viewModel = viewModel;
        }

        _viewModel?.Start();
    }
}
