namespace CkanUI.ViewModels.Pages.Repos;

/// <summary>仓库页的用户可见错误文案（03 篇 §8 / §10：页面级错误 + 是否可重试）。</summary>
public static class ReposErrorText
{
    /// <summary>失败结果转页面文案：<c>{message}（可重试/不可重试）</c>。</summary>
    public static string Compose(string? message, string fallback, bool retryable)
    {
        var text = string.IsNullOrWhiteSpace(message) ? fallback : message!;
        return text + (retryable ? "（可重试）" : "（不可重试）");
    }

    /// <summary>异常转页面文案：<c>{fallback}：{message}（不可重试）</c>。</summary>
    public static string FromException(Exception exception, string fallback)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return fallback + "：" + exception.Message + "（不可重试）";
    }
}
