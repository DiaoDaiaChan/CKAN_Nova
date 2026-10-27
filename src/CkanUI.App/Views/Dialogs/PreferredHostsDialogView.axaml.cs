using System;
using Avalonia.Markup.Xaml;
using CkanUI.ViewModels.Dialogs;

namespace CkanUI.App.Views.Dialogs;

/// <summary>
/// 首选下载主机优先级可视化管理对话框视图（F-15 / UI-30）。
/// </summary>
public partial class PreferredHostsDialogView : DialogWindow
{
    private bool _initialized;

    public PreferredHostsDialogView()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <inheritdoc />
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is not PreferredHostsDialogViewModel viewModel || _initialized)
        {
            return;
        }

        _initialized = true;
        _ = viewModel.InitializeAsync();
    }
}
