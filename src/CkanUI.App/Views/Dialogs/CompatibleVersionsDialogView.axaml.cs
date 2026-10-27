using System;
using Avalonia.Markup.Xaml;
using CkanUI.ViewModels.Dialogs;

namespace CkanUI.App.Views.Dialogs;

/// <summary>
/// 兼容游戏版本管理对话框视图（F-14 / UI-11）。
/// </summary>
public partial class CompatibleVersionsDialogView : DialogWindow
{
    private bool _initialized;

    public CompatibleVersionsDialogView()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <inheritdoc />
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is not CompatibleVersionsDialogViewModel viewModel || _initialized)
        {
            return;
        }

        _initialized = true;
        _ = viewModel.InitializeAsync();
    }
}
