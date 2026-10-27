using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CkanUI.ViewModels.Dialogs;

/// <summary>
/// 单选对话框（03 篇 §8 D-02 / 02 篇 §4 <c>IUser.RaiseSelectionDialog</c> 的唯一出口）。
/// 返回值语义与 <c>IDialogService.SelectAsync</c> 一致：<b>0 基索引，取消返回 -1</b>（SP-04 待 Core 侧逐调用点确认，
/// 本 VM 按 -1 实现，若 Core 期望别的值只需改 <see cref="OnSecondary"/>）。
/// </summary>
public sealed partial class SelectDialogViewModel : DialogViewModelBase
{
    /// <summary>创建单选对话框；<paramref name="defaultIndex"/> 会被夹到合法区间，空列表时为 -1。</summary>
    public SelectDialogViewModel(string title, string message, IReadOnlyList<string> options, int defaultIndex = 0)
        : base(title)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(options);

        Message = message;
        Options = options;
        _selectedIndex = options.Count == 0 ? -1 : Math.Clamp(defaultIndex, 0, options.Count - 1);
        PrimaryButtonText = "确定";
        SecondaryButtonText = "取消";
    }

    /// <summary>提示文案（Core 给的原文）。</summary>
    public string Message { get; }

    /// <summary>候选项（显示文本，顺序即索引顺序）。</summary>
    public IReadOnlyList<string> Options { get; }

    /// <summary>是否有候选项；为空时主按钮无效（校验错误"没有可选项"）。</summary>
    public bool HasOptions => Options.Count > 0;

    /// <summary>当前选中索引；空列表为 -1。</summary>
    [ObservableProperty]
    private int _selectedIndex;

    /// <summary>当前选中项的显示文本（无选中时为 null）。</summary>
    public string? SelectedText =>
        SelectedIndex >= 0 && SelectedIndex < Options.Count ? Options[SelectedIndex] : null;

    /// <summary>对话框结果：0 基索引；取消或没有可选项时为 -1。</summary>
    public int Result { get; private set; } = -1;

    /// <summary>双击某个候选项直接确认（等价于选中 + 主按钮）。</summary>
    [RelayCommand]
    private void Activate(string? option)
    {
        var index = option is null ? -1 : IndexOf(option);
        if (index < 0)
        {
            return;
        }

        SelectedIndex = index;
        if (!Validate())
        {
            return;
        }

        Result = index;
        IsConfirmed = true;
        Close();
    }

    /// <inheritdoc />
    protected override bool Validate()
    {
        if (!HasOptions)
        {
            ValidationError = "没有可选项";
            return false;
        }

        if (SelectedIndex < 0 || SelectedIndex >= Options.Count)
        {
            ValidationError = "请先选择一个条目";
            return false;
        }

        ValidationError = null;
        return true;
    }

    /// <inheritdoc />
    protected override void OnPrimary() => Result = SelectedIndex;

    /// <inheritdoc />
    protected override void OnSecondary() => Result = -1;

    partial void OnSelectedIndexChanged(int value) => OnPropertyChanged(nameof(SelectedText));

    private int IndexOf(string option)
    {
        for (var i = 0; i < Options.Count; i++)
        {
            if (string.Equals(Options[i], option, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }
}
