namespace CkanUI.Plugin.Ai.Chat;

/// <summary>端点拼接：把「Base URL」收敛成具体的 <c>/chat/completions</c>、<c>/models</c> 地址。</summary>
/// <remarks>
/// 用户填的 Base URL 可能是 <c>http://host:8317/v1</c>、<c>http://host:8317/v1/</c>，
/// 也可能已经贴了整条 <c>http://host:8317/v1/chat/completions</c>。三种都要能用——
/// 因为「填错一个斜杠就连不上」是用户最难自己诊断的故障。
/// </remarks>
public static class AiEndpoints
{
    private const string ChatCompletionsSegment = "chat/completions";

    /// <summary>拼出 <c>/chat/completions</c> 地址。</summary>
    /// <param name="baseUrl">设置里的 Base URL。</param>
    /// <returns>绝对地址。</returns>
    /// <exception cref="AiChatException">Base URL 为空或不是合法绝对地址。</exception>
    public static Uri ChatCompletions(string? baseUrl) => Combine(baseUrl, ChatCompletionsSegment);

    /// <summary>拼出 <c>/models</c> 地址（「测试连接」用：只读、便宜、OpenAI 兼容端点普遍支持）。</summary>
    /// <param name="baseUrl">设置里的 Base URL。</param>
    /// <returns>绝对地址。</returns>
    /// <exception cref="AiChatException">Base URL 为空或不是合法绝对地址。</exception>
    public static Uri Models(string? baseUrl) => Combine(baseUrl, "models");

    private static Uri Combine(string? baseUrl, string segment)
    {
        var root = (baseUrl ?? string.Empty).Trim().TrimEnd('/');
        if (root.Length == 0)
        {
            throw new AiChatException("没有配置 API Base URL。");
        }

        // 已经贴了整条地址就别再拼一段：/chat/completions/models 这种 404 用户查不出来。
        var address = root.EndsWith("/" + segment, System.StringComparison.OrdinalIgnoreCase)
            ? root
            : root + "/" + segment;

        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri))
        {
            throw new AiChatException("API Base URL 不是合法的绝对地址：" + root);
        }

        return uri;
    }
}
