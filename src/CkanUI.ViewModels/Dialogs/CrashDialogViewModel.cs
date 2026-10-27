using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CkanUI.ViewModels.Dialogs;

/// <summary>
/// 崩溃报告对话框（03 篇 §8 D-08 / ARC-04 三条崩溃兜底）：未捕获异常时显示摘要、日志路径与完整堆栈，
/// 并允许"复制诊断信息"。窗口的 owner 由调用方在 UI 线程预先捕获（05 篇 §3.2），本 VM 不关心窗口。
/// </summary>
public sealed partial class CrashDialogViewModel : DialogViewModelBase
{
    private readonly Exception _exception;
    private readonly Func<string, Task>? _copyTextAsync;

    /// <summary>创建崩溃对话框；<paramref name="logPath"/> 为已落盘的日志文件路径（可空）。</summary>
    public CrashDialogViewModel(Exception exception, string? logPath = null, Func<string, Task>? copyTextAsync = null)
        : base("程序遇到未处理的异常")
    {
        ArgumentNullException.ThrowIfNull(exception);

        _exception = exception;
        _copyTextAsync = copyTextAsync;
        LogPath = string.IsNullOrWhiteSpace(logPath) ? null : logPath;
        PrimaryButtonText = "退出程序";
        SecondaryButtonText = "返回程序";
    }

    /// <summary>给用户的操作建议（固定文案，中文）。</summary>
    public string SummaryText => "已捕获未处理异常，当前操作已停止。可复制诊断信息或查看日志后再关闭。";

    /// <summary>日志文件路径（可空）。</summary>
    public string? LogPath { get; }

    /// <summary>是否有日志路径。</summary>
    public bool HasLogPath => LogPath is not null;

    /// <summary>日志路径展示文案。</summary>
    public string LogPathText => LogPath ?? "本次未记录日志路径";

    /// <summary>异常完整文本（类型 + 消息 + 堆栈）。</summary>
    public string ExceptionText => _exception.ToString();

    /// <summary>可复制的诊断文本（含崩溃时间与日志路径）。</summary>
    public string DiagnosticText => DiagnosticTextBuilder.FromCrash(DateTime.Now, _exception, LogPath);

    /// <summary>复制按钮文案（不写在 XAML 里，03 篇 §11）。</summary>
    public string CopyButtonText => "复制诊断信息";

    /// <summary>日志路径标签文案。</summary>
    public string LogPathLabelText => "日志文件";

    /// <summary>技术细节区标题文案。</summary>
    public string DetailHeaderText => "异常详情";

    /// <summary>技术细节区是否展开（崩溃场景默认展开，D-08 要能直接看到堆栈）。</summary>
    [ObservableProperty]
    private bool _isDetailExpanded = true;

    /// <summary>复制结果提示。</summary>
    [ObservableProperty]
    private string? _copyStatus;

    /// <summary>是否有复制结果提示。</summary>
    public bool HasCopyStatus => !string.IsNullOrEmpty(CopyStatus);

    /// <summary>关闭语义（默认"返回程序"）。</summary>
    public CrashDialogResult Result { get; private set; } = CrashDialogResult.ReturnToApp;

    /// <inheritdoc />
    protected override void OnPrimary() => Result = CrashDialogResult.Exit;

    /// <inheritdoc />
    protected override void OnSecondary() => Result = CrashDialogResult.ReturnToApp;

    /// <summary>复制诊断信息（经注入委托，VM 不碰 Avalonia）。</summary>
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
            CopyStatus = "复制失败: " + ex.Message;
        }
    }

    partial void OnCopyStatusChanged(string? value) => OnPropertyChanged(nameof(HasCopyStatus));
}
