using CkanUI.Services.Abstractions;

namespace CkanUI.ViewModels.Pages.Instances;

/// <summary>
/// 实例页错误文案的拼装（03 篇 §8 D-03 错误条 / 01 篇 §8 统一返回类型）。
/// </summary>
/// <remarks>
/// 约定：服务层给的 <see cref="OperationResult.Message"/> 是用户可读中文，直接用；
/// 是否可重试必须显式写在文案里——用户据此决定"再点一次"还是"去关掉占用程序的进程"。
/// </remarks>
public static class InstancesErrorText
{
    /// <summary>把服务结果拼成一行用户可读文案：<c>前缀：消息（可重试/不可重试）</c>。</summary>
    public static string Compose(string? message, string fallback, bool retryable)
    {
        var text = string.IsNullOrWhiteSpace(message) ? fallback : message!.Trim();
        return $"{text}（{(retryable ? "可重试" : "不可重试")}）";
    }

    /// <summary>把异常拼成一行用户可读文案（技术细节走 <c>Detail</c>，由错误条展开）。</summary>
    public static string FromException(Exception exception, string fallback)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var message = string.IsNullOrWhiteSpace(exception.Message) ? fallback : exception.Message;
        return $"{fallback}：{message}（不可重试）";
    }
}
