using System;
using Avalonia.Controls;

namespace CkanUI.Plugin.Assets;

/// <summary>加载图工作台视图：只做绑定与平台能力接线，业务全在 <see cref="LoadingImagesPageViewModel"/>。</summary>
public partial class LoadingImagesPageView : UserControl
{
    private LoadingImagesPageViewModel? _viewModel;

    /// <summary>构造视图（ViewLocator 无参构造，因此这里不能有参数）。</summary>
    public LoadingImagesPageView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        _viewModel = DataContext as LoadingImagesPageViewModel;
        if (_viewModel is null)
        {
            return;
        }

        _viewModel.FilePicker ??= new AvaloniaAssetFilePicker(this);
        _viewModel.ShellLauncher ??= new ShellAssetsLauncher();
    }
}
