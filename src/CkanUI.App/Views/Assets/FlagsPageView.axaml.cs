using System;
using Avalonia.Controls;
using CkanUI.ViewModels.Pages.Assets;

namespace CkanUI.App.Views.Assets;

/// <summary>旗帜工作台视图：只做绑定与平台能力接线，业务全在 <see cref="FlagsPageViewModel"/>。</summary>
public partial class FlagsPageView : UserControl
{
    private FlagsPageViewModel? _viewModel;

    /// <summary>构造视图（ViewLocator 无参构造，因此这里不能有参数）。</summary>
    public FlagsPageView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        _viewModel = DataContext as FlagsPageViewModel;
        if (_viewModel is null)
        {
            return;
        }

        _viewModel.FilePicker ??= new AvaloniaAssetFilePicker(this);
        _viewModel.TextPrompt ??= new AvaloniaInstanceTextPrompt(this);
        _viewModel.ShellLauncher ??= new ShellAssetsLauncher();
    }
}
