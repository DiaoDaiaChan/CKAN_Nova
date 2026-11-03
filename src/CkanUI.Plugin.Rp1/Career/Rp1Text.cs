using System.Text;

namespace CkanUI.Plugin.Rp1.Career;

/// <summary>RP-1 配置里的文本清理：去掉 KSP 富文本标签，保留换行与可读正文。</summary>
public static class Rp1Text
{
    /// <summary>把 &amp;br;、&lt;color&gt;、&lt;b&gt; 一类标记清掉；空值返回 null。</summary>
    public static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = value
            .Replace("&br;", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br/>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br />", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

        var builder = new StringBuilder(text.Length);
        var inTag = false;
        foreach (var ch in text)
        {
            if (ch == '<')
            {
                inTag = true;
                continue;
            }

            if (ch == '>')
            {
                inTag = false;
                continue;
            }

            if (!inTag)
            {
                builder.Append(ch);
            }
        }

        var cleaned = builder.ToString().Trim();
        while (cleaned.Contains("\n\n\n", StringComparison.Ordinal))
        {
            cleaned = cleaned.Replace("\n\n\n", "\n\n", StringComparison.Ordinal);
        }

        return cleaned.Length == 0 ? null : cleaned;
    }
}
