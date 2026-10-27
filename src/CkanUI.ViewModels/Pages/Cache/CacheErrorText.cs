namespace CkanUI.ViewModels.Pages.Cache;

/// <summary>下载缓存页的错误文案（中文 + 可重试标记，03 篇 §17）。</summary>
public static class CacheErrorText
{
    /// <summary>把操作结果拼成「原因（可重试/不可重试）」。</summary>
    public static string Compose(string? message, string fallback, bool retryable)
    {
        var text = string.IsNullOrWhiteSpace(message) ? fallback : message;
        return text + (retryable ? "（可重试）" : "（不可重试）");
    }

    /// <summary>把异常拼成「兜底文案：消息（不可重试）」。</summary>
    public static string FromException(Exception exception, string fallback)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return fallback + "：" + exception.Message + "（不可重试）";
    }
}
