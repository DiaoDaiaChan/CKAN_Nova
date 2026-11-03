using CkanUI.Services.Abstractions;

namespace CkanUI.Plugin.Saves;

/// <summary>
/// 存档页错误文案拼装（03 篇 §8 D-03 错误条 / 01 篇 §8 统一返回类型）。
/// </summary>
/// <remarks>服务层的 <see cref="OperationResult.Message"/> 已是用户可读中文；这里只补"可重试/不可重试"。</remarks>
public static class SavesErrorText
{
    /// <summary>把服务结果拼成一行文案：<c>消息（可重试/不可重试）</c>。</summary>
    public static string Compose(string? message, string fallback, bool retryable)
    {
        var text = string.IsNullOrWhiteSpace(message) ? fallback : message!.Trim();
        return $"{text}（{(retryable ? "可重试" : "不可重试")}）";
    }

    /// <summary>把异常拼成一行文案（技术细节走错误条展开区）。</summary>
    public static string FromException(Exception exception, string fallback)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var message = string.IsNullOrWhiteSpace(exception.Message) ? fallback : exception.Message;
        return $"{fallback}：{message}（不可重试）";
    }
}
