using System;
using Avalonia.Markup.Xaml;
using CkanUI.ViewModels.Dialogs;

namespace CkanUI.App.Views.Dialogs;

/// <summary>
/// 标签组管理 / 编辑对话框视图（UI-24b / F-23 / F-24）。
/// </summary>
public partial class LabelEditDialogView : DialogWindow
{
    private bool _initialized;

    public LabelEditDialogView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <inheritdoc />
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is not LabelEditDialogViewModel viewModel || _initialized)
        {
            return;
        }

        _initialized = true;
        _ = viewModel.InitializeAsync();
    }
}
