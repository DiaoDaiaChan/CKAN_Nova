namespace CkanUI.Sfs;

/// <summary>行内文本的纯函数工具。全部 O(行长)，不缓存。</summary>
internal static class SfsText
{
    /// <summary>行首连续空白（空格/制表符）。</summary>
    public static string LeadingWhitespace(string content)
    {
        var length = 0;
        while (length < content.Length && (content[length] == ' ' || content[length] == '\t'))
        {
            length++;
        }

        return content[..length];
    }

    /// <summary>把 <paramref name="content"/> 的出错片段给诊断用：去掉行首空白，保留原样内容。</summary>
    public static string Snippet(string content) => content;
}
