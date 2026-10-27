using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CkanUI.ViewModels.Dialogs;

/// <summary>
/// 对话框视图模型基类（04 篇 §2 第 40 行 / 03 篇 §8 D-01..D-09）：统一标题、按钮文案、校验错误与关闭状态。
/// 视图只负责"渲染 + 关掉自己"，弹窗时机与 owner 由 <c>IDialogService</c> 决定（05 篇 §3.2），
/// 因此这里的 <see cref="IsClosed"/> 是唯一的关闭信号，headless 下可直接断言。
/// </summary>
public abstract partial class DialogViewModelBase : ObservableObject
{
    /// <summary>创建对话框基类，<paramref name="title"/> 为窗口标题。</summary>
    protected DialogViewModelBase(string title)
    {
        ArgumentNullException.ThrowIfNull(title);
        Title = title;
    }

    /// <summary>窗口标题。</summary>
    public string Title { get; }

    /// <summary>主按钮文案（默认"确定"，由派生类按语义改写）。</summary>
    public string PrimaryButtonText { get; protected set; } = "确定";

    /// <summary>次按钮文案（默认"取消"；置空表示不显示次按钮）。</summary>
    public string SecondaryButtonText { get; protected set; } = "取消";

    /// <summary>校验错误文案；非空时视图在按钮上方显示一行红字，且主按钮不会真正提交。</summary>
    [ObservableProperty]
    private string? _validationError;

    /// <summary>视图是否应当关闭。置为 true 后由视图（<c>DialogWindow</c>）调用自身 <c>Close()</c>。</summary>
    [ObservableProperty]
    private bool _isClosed;

    /// <summary>主按钮是否被成功按下（校验通过后才为 true）。</summary>
    public bool IsConfirmed { get; protected set; }

    /// <summary>是否有校验错误。</summary>
    public bool HasValidationError => !string.IsNullOrEmpty(ValidationError);

    /// <summary>是否显示次按钮。</summary>
    public bool HasSecondaryButton => !string.IsNullOrEmpty(SecondaryButtonText);

    /// <summary>主按钮按下后是否关闭对话框（"复制诊断信息"这类按钮不关闭）。</summary>
    protected virtual bool CloseAfterPrimary => true;

    /// <summary>请求关闭对话框（视图据此关闭窗口）。</summary>
    public void Close() => IsClosed = true;

    /// <summary>主按钮命令：先校验，通过后执行派生类动作，再按 <see cref="CloseAfterPrimary"/> 决定是否关闭。</summary>
    [RelayCommand]
    private void Primary()
    {
        if (!Validate())
        {
            return;
        }

        OnPrimary();

        if (CloseAfterPrimary)
        {
            IsConfirmed = true;
            Close();
        }
    }

    /// <summary>次按钮命令（取消/关闭）：一律关闭，返回值语义由派生类决定。</summary>
    [RelayCommand]
    private void Secondary()
    {
        OnSecondary();
        Close();
    }

    /// <summary>校验钩子；返回 false 时主按钮不提交，派生类应同时设置 <see cref="ValidationError"/>。</summary>
    protected virtual bool Validate() => true;

    /// <summary>主按钮动作钩子。</summary>
    protected virtual void OnPrimary()
    {
    }

    /// <summary>次按钮动作钩子。</summary>
    protected virtual void OnSecondary()
    {
    }

    partial void OnValidationErrorChanged(string? value) => OnPropertyChanged(nameof(HasValidationError));
}
