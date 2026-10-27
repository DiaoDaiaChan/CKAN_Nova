using CommunityToolkit.Mvvm.ComponentModel;
using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.ViewModels.Dialogs;

/// <summary>
/// F-38 下载错误条目视图模型（展示文件名、模组名、错误分类、建议操作、详细错误）。
/// </summary>
public sealed partial class DownloadErrorItemViewModel : ObservableObject
{
    private readonly DownloadErrorItemInfo _info;

    /// <summary>
    /// 初始化条目视图模型。
    /// </summary>
    public DownloadErrorItemViewModel(DownloadErrorItemInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        _info = info;
        _disposition = info.IsRetryable ? DownloadErrorDisposition.Retry : DownloadErrorDisposition.Skip;
    }

    /// <summary>底层数据条目。</summary>
    public DownloadErrorItemInfo Info => _info;

    /// <summary>文件名。</summary>
    public string FileName => _info.FileName;

    /// <summary>模组名。</summary>
    public string ModName => _info.ModName;

    /// <summary>错误分类。</summary>
    public string Category => _info.Category;

    /// <summary>建议操作。</summary>
    public string SuggestedAction => _info.SuggestedAction;

    /// <summary>详细错误信息。</summary>
    public string? ErrorMessage => _info.ErrorMessage;

    /// <summary>下载地址（排障用）。</summary>
    public string? SourceUrl => _info.SourceUrl;

    /// <summary>分类器是否判定为可重试。</summary>
    public bool IsRetryable => _info.IsRetryable;

    /// <summary>当前处置方式（重试或跳过）。</summary>
    [ObservableProperty]
    private DownloadErrorDisposition _disposition;

    /// <summary>是否选择重试。</summary>
    public bool IsRetry
    {
        get => Disposition == DownloadErrorDisposition.Retry;
        set
        {
            if (value && Disposition != DownloadErrorDisposition.Retry)
            {
                Disposition = DownloadErrorDisposition.Retry;
            }
        }
    }

    /// <summary>是否选择跳过。</summary>
    public bool IsSkip
    {
        get => Disposition == DownloadErrorDisposition.Skip;
        set
        {
            if (value && Disposition != DownloadErrorDisposition.Skip)
            {
                Disposition = DownloadErrorDisposition.Skip;
            }
        }
    }

    partial void OnDispositionChanged(DownloadErrorDisposition value)
    {
        OnPropertyChanged(nameof(IsRetry));
        OnPropertyChanged(nameof(IsSkip));
    }
}
