using CommunityToolkit.Mvvm.ComponentModel;

namespace CkanUI.ViewModels.Dialogs;

/// <summary>
/// 单行文本输入对话框（F-06「增 / 改一条启动命令」用）。
/// </summary>
/// <remarks>
/// <para><c>IDialogService</c> 只有确认 / 单选 / 提示 / 进度四种对话，没有文本输入；
/// 该契约属于 Abstractions，本任务不改，因此这里提供一个通用 VM，
/// 由页面层宿主能力接口（<c>IInstanceTextPrompt</c>）的实现去弹它。</para>
/// <para>返回值语义：<see cref="Result"/> 为去首尾空白后的文本；取消或未确认时为 null。
/// 校验只看「非空」，更细的规则（是否重复、是否像命令行）交给调用方。</para>
/// </remarks>
public sealed partial class TextInputDialogViewModel : DialogViewModelBase
{
    /// <summary>创建文本输入对话框。</summary>
    /// <param name="title">窗口标题。</param>
    /// <param name="message">说明文案。</param>
    /// <param name="initialValue">初始值（编辑已有条目时传入）。</param>
    /// <param name="placeholder">输入框占位文案。</param>
    /// <param name="allowEmpty">是否允许提交空文本（默认不允许）。</param>
    public TextInputDialogViewModel(
        string title,
        string message,
        string? initialValue = null,
        string? placeholder = null,
        bool allowEmpty = false)
        : base(title)
    {
        ArgumentNullException.ThrowIfNull(message);

        Message = message;
        Placeholder = placeholder ?? string.Empty;
        AllowEmpty = allowEmpty;
        _text = initialValue ?? string.Empty;
        PrimaryButtonText = "确定";
        SecondaryButtonText = "取消";
    }

    /// <summary>说明文案。</summary>
    public string Message { get; }

    /// <summary>输入框占位文案（为空时视图不显示占位）。</summary>
    public string Placeholder { get; }

    /// <summary>是否允许提交空文本。</summary>
    public bool AllowEmpty { get; }

    /// <summary>输入框内容。</summary>
    [ObservableProperty]
    private string _text;

    /// <summary>对话框结果：去首尾空白的文本；取消时为 null。</summary>
    public string? Result { get; private set; }

    /// <inheritdoc />
    protected override bool Validate()
    {
        if (!AllowEmpty && string.IsNullOrWhiteSpace(Text))
        {
            ValidationError = "内容不能为空";
            return false;
        }

        ValidationError = null;
        return true;
    }

    /// <inheritdoc />
    protected override void OnPrimary() => Result = (Text ?? string.Empty).Trim();

    /// <inheritdoc />
    protected override void OnSecondary() => Result = null;
}
