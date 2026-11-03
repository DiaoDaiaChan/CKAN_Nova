using System.Text.RegularExpressions;

namespace CkanUI.Plugin.Ai.Chat;

/// <summary>给模型看的文本整形：去标签、压空白、截断。</summary>
/// <remarks>
/// 工具结果要「紧凑 JSON，不要整段 HTML」：CKAN 的 abstract/description 原文里混着 BBCode 与 HTML，
/// 整段回给模型既贵又容易被当成乱码。这里统一压成一行纯文本再截断。
/// </remarks>
public static partial class AiText
{
    /// <summary>工具结果里单段文本的默认截断长度（字符）。</summary>
    public const int DefaultMaxChars = 400;

    /// <summary>去掉 HTML 标签并把连续空白压成单个空格。</summary>
    /// <param name="text">原始文本；<c>null</c>/空串返回空串。</param>
    /// <returns>单行纯文本。</returns>
    public static string PlainText(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var stripped = HtmlTag().Replace(text, " ");
        return Whitespace().Replace(stripped, " ").Trim();
    }

    /// <summary>纯文本化后截断（超长补省略号）。</summary>
    /// <param name="text">原始文本。</param>
    /// <param name="maxChars">上限字符数；<c>&lt;=0</c> 时用 <see cref="DefaultMaxChars"/>。</param>
    /// <returns>不会超过 <paramref name="maxChars"/> + 1 个字符的文本。</returns>
    public static string Truncate(string? text, int maxChars = DefaultMaxChars)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var limit = maxChars <= 0 ? DefaultMaxChars : maxChars;
        var trimmed = text.Trim();
        return trimmed.Length <= limit ? trimmed : trimmed[..limit] + "…";
    }

    /// <summary>纯文本化 + 截断（工具结果里的长字段都走这里）。</summary>
    /// <param name="text">原始文本。</param>
    /// <param name="maxChars">上限字符数。</param>
    /// <returns>紧凑单行文本。</returns>
    public static string Compact(string? text, int maxChars = DefaultMaxChars) => Truncate(PlainText(text), maxChars);

    [GeneratedRegex("<[^>]{1,200}>")]
    private static partial Regex HtmlTag();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}

/// <summary>OpenAI 兼容端点里一条对用户可见的对话消息。</summary>
/// <param name="Role">协议角色（<c>user</c> / <c>assistant</c>）。</param>
/// <param name="Text">正文。</param>
/// <param name="IsUser">是否用户发言（界面用来决定气泡左右与配色）。</param>
public sealed record AiChatMessage(string Role, string Text, bool IsUser)
{
    /// <summary>构造一条用户消息。</summary>
    /// <param name="text">正文。</param>
    /// <returns>用户消息。</returns>
    public static AiChatMessage User(string text) => new("user", text, true);

    /// <summary>构造一条助手消息。</summary>
    /// <param name="text">正文。</param>
    /// <returns>助手消息。</returns>
    public static AiChatMessage Assistant(string text) => new("assistant", text, false);
}

/// <summary>一次工具调用在界面上的轨迹行（折叠区里一行一条）。</summary>
/// <param name="Step">属于第几轮工具调用（从 1 开始）。</param>
/// <param name="Tool">工具名。</param>
/// <param name="Arguments">模型给的参数原文（可能是坏 JSON，原样展示）。</param>
/// <param name="Result">工具返回的紧凑 JSON。</param>
/// <param name="Ok">工具自己报的成功/失败。</param>
public sealed record AiToolTrace(int Step, string Tool, string Arguments, string Result, bool Ok)
{
    /// <summary>折叠区里的一行标题。</summary>
    public string Header => $"#{Step} {Tool}";

    /// <summary>结果的短预览（折叠行下面一行小字）。</summary>
    public string ResultPreview => AiText.Truncate(Result, 220);
}
