using Avalonia.Markup.Xaml;

namespace CkanUI.App.Views.Dialogs;

public partial class UpdateAvailableDialogView : DialogWindow
{
    public UpdateAvailableDialogView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
