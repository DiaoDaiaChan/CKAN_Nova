using CommunityToolkit.Mvvm.ComponentModel;

namespace CkanUI.ViewModels.Dialogs;

/// <summary>
/// 确认（是/否）对话框（03 篇 §8 D-01 / 02 篇 §4 <c>IUser.RaiseYesNoDialog</c> 的唯一出口）。
/// 按钮文案可定制；需要"不再询问"时打开 <c>showDoNotAskAgain</c>，由调用方持久化到 ckan-ui.json。
/// </summary>
public sealed partial class ConfirmDialogViewModel : DialogViewModelBase
{
    /// <summary>创建确认对话框。</summary>
    public ConfirmDialogViewModel(
        string title,
        string message,
        string? detail = null,
        string confirmText = "确定",
        string cancelText = "取消",
        bool showDoNotAskAgain = false)
        : base(title)
    {
        ArgumentNullException.ThrowIfNull(message);

        Message = message;
        Detail = string.IsNullOrWhiteSpace(detail) ? null : detail;
        PrimaryButtonText = string.IsNullOrWhiteSpace(confirmText) ? "确定" : confirmText;
        SecondaryButtonText = string.IsNullOrWhiteSpace(cancelText) ? "取消" : cancelText;
        ShowDoNotAskAgain = showDoNotAskAgain;
    }

    /// <summary>主体文案（Core 抛上来的英文原文照搬，UI 不二次翻译，03 篇 §11）。</summary>
    public string Message { get; }

    /// <summary>补充说明（可选）。</summary>
    public string? Detail { get; }

    /// <summary>是否有补充说明。</summary>
    public bool HasDetail => Detail is not null;

    /// <summary>是否显示"不再询问"勾选框。</summary>
    public bool ShowDoNotAskAgain { get; }

    /// <summary>"不再询问"文案（不写在 XAML 里，03 篇 §11）。</summary>
    public string DoNotAskAgainText => "不再询问";

    /// <summary>用户是否勾选了"不再询问"。</summary>
    [ObservableProperty]
    private bool _doNotAskAgain;

    /// <summary>确认结果：true = 主按钮，false = 取消（未点任何按钮时也是 false）。</summary>
    public bool Result { get; private set; }

    /// <inheritdoc />
    protected override void OnPrimary() => Result = true;

    /// <inheritdoc />
    protected override void OnSecondary() => Result = false;
}
