using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CkanUI.Services.Abstractions;

namespace CkanUI.ViewModels.Dialogs;

/// <summary>
/// 错误对话框（03 篇 §8 D-03 / 02 篇 §4 <c>IUser.RaiseError</c>）：非模态错误条也能复用本 VM 的内容。
/// 带"复制诊断信息"，把 <see cref="ErrorEntry"/> 的时间戳/上下文/用户消息/详情/异常拼成可粘贴文本。
/// 剪贴板契约尚未在 Abstractions 冻结，故用注入委托 <c>copyTextAsync</c>（App 侧注册为剪贴板写入）。
/// </summary>
public sealed partial class ErrorDialogViewModel : DialogViewModelBase
{
    private readonly ErrorEntry _entry;
    private readonly Func<string, Task>? _copyTextAsync;

    /// <summary>创建错误对话框；<paramref name="copyTextAsync"/> 为空时复制按钮只提示"没有剪贴板通道"。</summary>
    public ErrorDialogViewModel(ErrorEntry entry, Func<string, Task>? copyTextAsync = null)
        : base("出错了")
    {
        ArgumentNullException.ThrowIfNull(entry);

        _entry = entry;
        _copyTextAsync = copyTextAsync;
        PrimaryButtonText = "复制诊断信息";
        SecondaryButtonText = "关闭";
    }

    /// <summary>用户可读的错误消息（Core 英文原文照搬）。</summary>
    public string UserMessage => _entry.UserMessage;

    /// <summary>技术详情（可选）。</summary>
    public string? Detail => string.IsNullOrWhiteSpace(_entry.Detail) ? null : _entry.Detail;

    /// <summary>是否有技术详情。</summary>
    public bool HasDetail => Detail is not null;

    /// <summary>错误上下文（来自哪个操作），空时显示占位文案。</summary>
    public string ContextText => string.IsNullOrWhiteSpace(_entry.Context) ? "未标注上下文" : _entry.Context;

    /// <summary>错误时间戳原文。</summary>
    public DateTime Timestamp => _entry.Timestamp;

    /// <summary>格式化后的时间戳。</summary>
    public string TimestampText => _entry.Timestamp.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture);

    /// <summary>异常完整堆栈（无异常时为 null）。</summary>
    public string? ExceptionText => _entry.Exception?.ToString();

    /// <summary>是否有异常堆栈。</summary>
    public bool HasException => _entry.Exception is not null;

    /// <summary>可复制的诊断文本。</summary>
    public string DiagnosticText => DiagnosticTextBuilder.FromErrorEntry(_entry);

    /// <summary>上下文标签文案（不写在 XAML 里，03 篇 §11）。</summary>
    public string ContextLabelText => "上下文";

    /// <summary>技术细节区标题文案。</summary>
    public string DetailHeaderText => "技术细节";

    /// <summary>技术细节区是否展开（默认折叠，03 篇 §8 D-03）。</summary>
    [ObservableProperty]
    private bool _isDetailExpanded;

    /// <summary>复制结果提示（"已复制到剪贴板" / 失败原因）。</summary>
    [ObservableProperty]
    private string? _copyStatus;

    /// <summary>是否有复制结果提示。</summary>
    public bool HasCopyStatus => !string.IsNullOrEmpty(CopyStatus);

    /// <summary>复制按钮不关闭对话框（用户可能还要看堆栈）。</summary>
    protected override bool CloseAfterPrimary => false;

    /// <summary>主按钮 = 复制诊断信息。</summary>
    protected override void OnPrimary() => CopyDetailsCommand.Execute(null);

    /// <summary>复制诊断信息到剪贴板（经注入委托，VM 不碰 Avalonia）。</summary>
    [RelayCommand]
    private async Task CopyDetailsAsync()
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
            // 剪贴板被别的进程占用在 Windows 上很常见：提示而不是崩（05 篇 §7 禁止事件里 throw）。
            CopyStatus = "复制失败: " + ex.Message;
        }
    }

    partial void OnCopyStatusChanged(string? value) => OnPropertyChanged(nameof(HasCopyStatus));
}
