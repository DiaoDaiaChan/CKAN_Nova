using Avalonia.Controls;

namespace CkanUI.Plugin.ModPackShare.UI;

/// <summary>
/// 「整合包配置」页视图：只做可视树事件到 VM 生命周期（Start/Stop）的转接。
/// </summary>
/// <remarks>
/// 页面没有定时器也没有轮询：数据只在挂载、切实例、用户点按钮时刷新，
/// 所以离开可视树不需要停任何东西——留着 Stop 是为了宿主换 VM 时能对称地收尾。
/// </remarks>
public partial class ModPackSharePageView : UserControl
{
    private ModPackSharePageViewModel? _viewModel;
    private bool _isAttached;

    public ModPackSharePageView()
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

    /// <summary>挂上新 VM：旧 VM 先 Stop，新 VM 先 Start 再 Refresh（Start 只负责首次加载）。</summary>
    private void AttachViewModel()
    {
        var next = DataContext as ModPackSharePageViewModel;
        if (ReferenceEquals(next, _viewModel))
        {
            return;
        }

        _viewModel?.Stop();
        _viewModel = next;
        if (_viewModel is null)
        {
            return;
        }

        _viewModel.Start();
        _viewModel.Refresh();
    }
}
