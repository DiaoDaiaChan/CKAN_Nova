using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CkanUI.ViewModels.Dialogs;

/// <summary>
/// 诊断信息对话框（06 篇 UI-09 "诊断复制" / 03 篇 §8 D-03）：展示环境键值对并支持一键复制，
/// 供 issue 报告使用。<c>copyTextAsync</c> 由 App 侧注入剪贴板实现（Abstractions 暂无剪贴板契约）。
/// </summary>
public sealed partial class DiagnosticDialogViewModel : DialogViewModelBase
{
    private readonly Func<string, Task>? _copyTextAsync;

    /// <summary>创建诊断对话框。</summary>
    public DiagnosticDialogViewModel(string title, IReadOnlyList<DiagnosticItem> items, Func<string, Task>? copyTextAsync = null)
        : base(title)
    {
        ArgumentNullException.ThrowIfNull(items);

        Items = items;
        _copyTextAsync = copyTextAsync;
        PrimaryButtonText = "复制诊断信息";
        SecondaryButtonText = "关闭";
    }

    /// <summary>诊断条目（顺序即展示顺序）。</summary>
    public IReadOnlyList<DiagnosticItem> Items { get; }

    /// <summary>是否没有诊断条目（空状态，03 篇 §10）。</summary>
    public bool IsEmpty => Items.Count == 0;

    /// <summary>空状态文案。</summary>
    public string EmptyText => "暂无诊断信息";

    /// <summary>列表区标题文案。</summary>
    public string ItemsHeaderText => "环境信息";

    /// <summary>可复制的诊断文本。</summary>
    public string DiagnosticText => DiagnosticTextBuilder.FromItems(Items);

    /// <summary>复制结果提示。</summary>
    [ObservableProperty]
    private string? _copyStatus;

    /// <summary>是否有复制结果提示。</summary>
    public bool HasCopyStatus => !string.IsNullOrEmpty(CopyStatus);

    /// <summary>复制按钮不关闭对话框。</summary>
    protected override bool CloseAfterPrimary => false;

    /// <inheritdoc />
    protected override void OnPrimary() => CopyAllCommand.Execute(null);

    /// <summary>复制全部诊断信息。</summary>
    [RelayCommand]
    private async Task CopyAllAsync()
    {
        if (_copyTextAsync is null)
        {
            CopyStatus = "当前环境没有剪贴板通道，无法复制";
            return;
        }

        try
        {
            await _copyTextAsync(DiagnosticText);
            CopyStatus = "已复制到剪贴板";
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or System.IO.IOException)
        {
            CopyStatus = "复制失败: " + ex.Message;
        }
    }

    partial void OnCopyStatusChanged(string? value) => OnPropertyChanged(nameof(HasCopyStatus));
}
