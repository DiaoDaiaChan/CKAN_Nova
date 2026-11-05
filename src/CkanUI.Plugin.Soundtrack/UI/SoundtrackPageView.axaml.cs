using System;
using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace CkanUI.Plugin.Soundtrack.UI;

/// <summary>
/// 「音乐」页签视图。
/// </summary>
/// <remarks>
/// <para>必须是 XxxPageView、与 <see cref="SoundtrackPageViewModel"/> 同命名空间同程序集、且有无参构造函数：
/// 宿主 ViewLocator 靠这条约定把插件页 VM 映射到视图（docs/design/plugins.md §6.1）。</para>
/// <para>视图代码只做两件事：把“有人在看”告诉 VM；选中项变化时把它滚进可视区，
/// 免得上一次选中的列表藏在节点树折叠的分组里、右侧却在编辑它。释放归宿主，视图不 Dispose 页面。</para>
/// </remarks>
public partial class SoundtrackPageView : UserControl
{
    private SoundtrackPageViewModel? _viewModel;

    /// <summary>供宿主 ViewLocator 反射实例化。</summary>
    public SoundtrackPageView()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    /// <inheritdoc />
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = DataContext as SoundtrackPageViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnAttachedToVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        (DataContext as SoundtrackPageViewModel)?.Start();
    }

    private void OnDetachedFromVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        (DataContext as SoundtrackPageViewModel)?.Stop();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SoundtrackPageViewModel.SelectedPlaylist))
        {
            return;
        }

        // 等这一轮布局把节点容器建出来再滚，否则新分组还没有可滚的控件。
        Dispatcher.UIThread.Post(BringSelectionIntoView, DispatcherPriority.Background);
    }

    /// <summary>乐曲库可能上千首，右栏放不下：用独立无标题栏窗口做筛选和添加。</summary>
    private void OnOpenLibraryPicker(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null || TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        var picker = new SoundtrackLibraryPickerViewModel(
            _viewModel.AllLibraryRows,
            row =>
            {
                _viewModel.AddTrack(row);
                return row.IsAdded;
            },
            row => _viewModel.PlayLibraryTrack(row),
            _viewModel.SelectedPlaylistName);

        var window = new SoundtrackLibraryWindow { DataContext = picker };
        window.Show(owner);
    }

    private void BringSelectionIntoView()
    {
        if (_viewModel?.SelectedPlaylist is not { } selected)
        {
            return;
        }

        var node = this.GetVisualDescendants()
            .OfType<Button>()
            .FirstOrDefault(button => ReferenceEquals(button.DataContext, selected));
        node?.BringIntoView();
    }
}
