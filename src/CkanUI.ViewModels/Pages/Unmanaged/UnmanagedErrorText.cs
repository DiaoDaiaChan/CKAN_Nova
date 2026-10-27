namespace CkanUI.ViewModels.Pages.Unmanaged;

/// <summary>未管理文件页的错误文案（与其它页保持同一格式：正文 + 可重试标记）。</summary>
public static class UnmanagedErrorText
{
    /// <summary>把服务返回的消息组合成「…（可重试）」形式。</summary>
    public static string Compose(string? message, string fallback, bool retryable)
    {
        var text = string.IsNullOrWhiteSpace(message) ? fallback : message;
        return $"{text}（{(retryable ? "可重试" : "不可重试")}）";
    }

    /// <summary>把异常组合成「…：<message>（不可重试）」形式。</summary>
    public static string FromException(Exception exception, string fallback)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return $"{fallback}：{exception.Message}（不可重试）";
    }
}
