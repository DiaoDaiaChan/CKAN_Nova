namespace CkanUI.ViewModels.Pages.ModPacks;

/// <summary>导入导出页的错误文案拼装（与 ReposErrorText 同构，全中文且标注可否重试）。</summary>
public static class ModPacksErrorText
{
    /// <summary>把服务层返回的消息与可否重试拼成一句用户可读的话。</summary>
    /// <param name="message">服务层消息，可能为空。</param>
    /// <param name="fallback">消息为空时的兜底文案。</param>
    /// <param name="retryable">是否可重试。</param>
    /// <returns>中文错误文案。</returns>
    public static string Compose(string? message, string fallback, bool retryable)
    {
        var text = string.IsNullOrWhiteSpace(message) ? fallback : message!;
        return retryable ? text + "（可重试）" : text + "（不可重试）";
    }

    /// <summary>把异常转成中文错误文案（页面不会把异常抛给界面）。</summary>
    /// <param name="exception">异常。</param>
    /// <param name="fallback">兜底文案。</param>
    /// <returns>中文错误文案。</returns>
    public static string FromException(Exception exception, string fallback)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return fallback + "：" + exception.Message + "（不可重试）";
    }
}
