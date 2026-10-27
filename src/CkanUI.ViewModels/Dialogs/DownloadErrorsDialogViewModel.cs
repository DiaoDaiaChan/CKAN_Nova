using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.ViewModels.Dialogs;

/// <summary>
/// F-38 下载失败处置对话框视图模型（对应 UI-35 / INT-08）：
/// 展示下载失败的错误条目列表（文件名、模组名、错误分类、建议操作），
/// 提供「全部重试」（RetryAll）、「跳过失败项继续」（SkipFailed）、「取消」（Cancel），
/// 以及「复制全部错误信息到剪贴板」（CopyErrorsCommand）。
/// </summary>
public sealed partial class DownloadErrorsDialogViewModel : DialogViewModelBase
{
    private readonly Func<string, Task>? _copyTextAsync;

    /// <summary>
    /// 初始化下载失败处置对话框。
    /// </summary>
    /// <param name="errors">失败条目列表。</param>
    /// <param name="copyTextAsync">剪贴板委托（为 null 时提示当前环境无剪贴板通道）。</param>
    public DownloadErrorsDialogViewModel(
        IEnumerable<DownloadErrorItemInfo> errors,
        Func<string, Task>? copyTextAsync = null)
        : base("下载失败")
    {
        ArgumentNullException.ThrowIfNull(errors);

        _copyTextAsync = copyTextAsync;
        PrimaryButtonText = "全部重试";
        SecondaryButtonText = "取消";

        foreach (var error in errors)
        {
            var item = new DownloadErrorItemViewModel(error);
            item.PropertyChanged += (_, _) => OnItemDispositionChanged();
            Items.Add(item);
        }

        UpdateSummary();
    }

    /// <summary>下载失败条目列表。</summary>
    public ObservableCollection<DownloadErrorItemViewModel> Items { get; } = new();

    /// <summary>是否有失败条目。</summary>
    public bool HasItems => Items.Count > 0;

    /// <summary>失败条目总数。</summary>
    public int TotalCount => Items.Count;

    /// <summary>当前处置结果。</summary>
    public DownloadErrorsDialogResult Result { get; private set; } = DownloadErrorsDialogResult.Cancel;

    /// <summary>顶部说明文本。</summary>
    public string HeaderMessage => "以下模组或文件下载失败，请选择处置方式：";

    /// <summary>统计摘要文本。</summary>
    [ObservableProperty]
    private string _summaryText = string.Empty;

    /// <summary>复制到剪贴板后的状态提示。</summary>
    [ObservableProperty]
    private string? _copyStatus;

    /// <summary>是否有复制状态提示。</summary>
    public bool HasCopyStatus => !string.IsNullOrEmpty(CopyStatus);

    /// <summary>重试项数量。</summary>
    public int RetryCount => Items.Count(i => i.IsRetry);

    /// <summary>跳过项数量。</summary>
    public int SkipCount => Items.Count(i => i.IsSkip);

    /// <summary>格式化完整的全部错误诊断文本（供复制到剪贴板）。</summary>
    public string FormattedErrorText
    {
        get
        {
            var sb = new StringBuilder();
            sb.AppendLine($"=== 下载失败报告（共 {Items.Count} 项）===");
            for (var i = 0; i < Items.Count; i++)
            {
                var item = Items[i];
                sb.AppendLine();
                sb.AppendLine($"[{i + 1}] 模组: {item.ModName}");
                sb.AppendLine($"    文件: {item.FileName}");
                sb.AppendLine($"    分类: {item.Category}");
                sb.AppendLine($"    建议: {item.SuggestedAction}");
                if (!string.IsNullOrWhiteSpace(item.SourceUrl))
                {
                    sb.AppendLine($"    地址: {item.SourceUrl}");
                }
                if (!string.IsNullOrWhiteSpace(item.ErrorMessage))
                {
                    sb.AppendLine($"    错误: {item.ErrorMessage}");
                }
            }
            return sb.ToString().TrimEnd();
        }
    }

    /// <summary>「全部重试」按钮动作：将所有条目设为 Retry 并关闭对话框。</summary>
    [RelayCommand]
    public void RetryAll()
    {
        foreach (var item in Items)
        {
            item.Disposition = DownloadErrorDisposition.Retry;
        }
        Result = DownloadErrorsDialogResult.RetryAll;
        IsConfirmed = true;
        Close();
    }

    /// <summary>「跳过失败项继续」按钮动作：将所有条目设为 Skip 并关闭对话框。</summary>
    [RelayCommand]
    public void SkipFailed()
    {
        foreach (var item in Items)
        {
            item.Disposition = DownloadErrorDisposition.Skip;
        }
        Result = DownloadErrorsDialogResult.SkipFailed;
        IsConfirmed = true;
        Close();
    }

    /// <summary>「按当前选择继续」（若用户逐项调整了部分重试部分跳过）。</summary>
    [RelayCommand]
    public void ConfirmCustom()
    {
        Result = DownloadErrorsDialogResult.Custom;
        IsConfirmed = true;
        Close();
    }

    /// <summary>「复制全部错误信息到剪贴板」命令。</summary>
    [RelayCommand]
    public async Task CopyErrorsAsync()
    {
        if (_copyTextAsync is null)
        {
            CopyStatus = "当前环境没有剪贴板通道，无法复制";
            return;
        }

        try
        {
            await _copyTextAsync(FormattedErrorText);
            CopyStatus = "已复制全部错误信息到剪贴板";
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or System.IO.IOException)
        {
            CopyStatus = "复制失败: " + ex.Message;
        }
    }

    /// <inheritdoc />
    protected override void OnPrimary()
    {
        // 主按钮默认对应「全部重试」
        RetryAll();
    }

    /// <inheritdoc />
    protected override void OnSecondary()
    {
        // 取消
        Result = DownloadErrorsDialogResult.Cancel;
    }

    private void OnItemDispositionChanged()
    {
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        SummaryText = $"共 {TotalCount} 项失败：{RetryCount} 项重试，{SkipCount} 项跳过";
        OnPropertyChanged(nameof(RetryCount));
        OnPropertyChanged(nameof(SkipCount));
    }

    partial void OnCopyStatusChanged(string? value) => OnPropertyChanged(nameof(HasCopyStatus));
}
