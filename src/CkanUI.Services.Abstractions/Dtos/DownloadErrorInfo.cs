namespace CkanUI.Services.Abstractions.Dtos;

/// <summary>
/// F-38 下载失败处置动作。
/// </summary>
public enum DownloadErrorDisposition
{
    /// <summary>重试失败项。</summary>
    Retry,

    /// <summary>跳过失败项继续。</summary>
    Skip,
}

/// <summary>
/// F-38 单个下载失败条目的信息（零第三方类型引用）。
/// </summary>
/// <param name="FileName">下载目标文件名或存储文件名。</param>
/// <param name="ModName">模组名称或标识。</param>
/// <param name="Category">错误分类（如：网络超时、404未找到、校验不匹配、限流、证书错误、IO错误等）。</param>
/// <param name="SuggestedAction">建议操作文案（如：检查网络连接后重试、跳过该模组、稍后重试等）。</param>
/// <param name="ErrorMessage">详细错误信息/异常摘要。</param>
/// <param name="SourceUrl">下载地址（可选，排查故障用）。</param>
/// <param name="IsRetryable">分类器判定的可重试标志。</param>
public sealed record DownloadErrorItemInfo(
    string FileName,
    string ModName,
    string Category,
    string SuggestedAction,
    string? ErrorMessage = null,
    string? SourceUrl = null,
    bool IsRetryable = true);

/// <summary>
/// F-38 下载失败处置对话框的用户选择结果。
/// </summary>
public enum DownloadErrorsDialogResult
{
    /// <summary>取消整个流程。</summary>
    Cancel,

    /// <summary>全部重试。</summary>
    RetryAll,

    /// <summary>跳过失败项继续。</summary>
    SkipFailed,

    /// <summary>按用户逐项定制的处置继续。</summary>
    Custom,
}
