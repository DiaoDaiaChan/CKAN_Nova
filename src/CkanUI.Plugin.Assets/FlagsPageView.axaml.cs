using System;
using Avalonia.Controls;

namespace CkanUI.Plugin.Assets;

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
        // 文本输入（重命名）的实现挂在 App 层（依赖 TextInputDialogView），插件不引用 App：
        // 由宿主在构造页面时注入 IInstanceTextPrompt，缺省时页面按未注入处理（见交付报告）。
        _viewModel.ShellLauncher ??= new ShellAssetsLauncher();
    }
}
