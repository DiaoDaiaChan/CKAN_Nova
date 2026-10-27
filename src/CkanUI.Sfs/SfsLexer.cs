namespace CkanUI.Sfs;

/// <summary>单行分词结果的类别（13 篇 §5 令牌模型）。</summary>
public enum SfsLineKind
{
    /// <summary>空白行。</summary>
    Blank = 0,

    /// <summary>注释行（双斜杠或井号开头）。</summary>
    Comment = 1,

    /// <summary>键值对。</summary>
    KeyValue = 2,

    /// <summary>只有名字、没有左花括号的行；是否真是节点名要看下一行（由解析器判定）。</summary>
    Name = 3,

    /// <summary>含左花括号的行：单独一个、名字同行、名字紧贴。</summary>
    OpenBrace = 4,

    /// <summary>只含右花括号的行。</summary>
    CloseBrace = 5,
}

/// <summary>一行文本的分词结果。不可变。</summary>
public sealed class SfsLineToken
{
    /// <summary>行类别。</summary>
    public SfsLineKind Kind { get; init; }

    /// <summary>整行内容（不含行尾）。</summary>
    public string Content { get; init; } = string.Empty;

    /// <summary>行首空白。</summary>
    public string Indent { get; init; } = string.Empty;

    /// <summary>去掉行首空白后的正文（保留行尾空白）。</summary>
    public string Body { get; init; } = string.Empty;

    /// <summary>键（仅 KeyValue 有效）。</summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>键与值之间的原始分隔符（仅 KeyValue 有效）。</summary>
    public string Separator { get; init; } = string.Empty;

    /// <summary>值原文（仅 KeyValue 有效）。</summary>
    public string Value { get; init; } = string.Empty;

    /// <summary>节点名（仅 Name / OpenBrace 有效）。</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>值的引号风格分类。</summary>
    public SfsValueStyle ValueStyle { get; init; }
}

/// <summary>
/// 行导向分词器（13 篇 §4/§5）。一行一个令牌，值不拆。
/// 关键判定：**第一个等号先于第一个左花括号出现时，整行就是键值对**——这保证了
/// 值里出现花括号/等号/反斜杠时不会被误判成结构（L7）。
/// </summary>
public static class SfsLexer
{
    /// <summary>把一行（不含行尾）分词。</summary>
    public static SfsLineToken Lex(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var indentLength = 0;
        while (indentLength < content.Length && (content[indentLength] == ' ' || content[indentLength] == '\t'))
        {
            indentLength++;
        }

        var indent = content[..indentLength];
        var body = content[indentLength..];

        if (body.Length == 0)
        {
            return new SfsLineToken { Kind = SfsLineKind.Blank, Content = content, Indent = indent, Body = body };
        }

        if ((body.Length >= 2 && body[0] == '/' && body[1] == '/') || body[0] == '#')
        {
            return new SfsLineToken { Kind = SfsLineKind.Comment, Content = content, Indent = indent, Body = body };
        }

        var equalsIndex = body.IndexOf('=', StringComparison.Ordinal);
        var braceIndex = body.IndexOf('{', StringComparison.Ordinal);

        if (equalsIndex >= 0 && (braceIndex < 0 || equalsIndex < braceIndex))
        {
            var keyEnd = equalsIndex;
            while (keyEnd > 0 && (body[keyEnd - 1] == ' ' || body[keyEnd - 1] == '\t'))
            {
                keyEnd--;
            }

            var separatorEnd = equalsIndex + 1;
            while (separatorEnd < body.Length && (body[separatorEnd] == ' ' || body[separatorEnd] == '\t'))
            {
                separatorEnd++;
            }

            var key = body[..keyEnd];
            var separator = body[keyEnd..separatorEnd];
            var value = body[separatorEnd..];

            return new SfsLineToken
            {
                Kind = SfsLineKind.KeyValue,
                Content = content,
                Indent = indent,
                Body = body,
                Key = key,
                Separator = separator,
                Value = value,
                ValueStyle = DetectValueStyle(value),
            };
        }

        var trimmed = body.TrimEnd(' ', '\t');

        if (trimmed[^1] == '{')
        {
            return new SfsLineToken
            {
                Kind = SfsLineKind.OpenBrace,
                Content = content,
                Indent = indent,
                Body = body,
                Name = trimmed[..^1].Trim(),
            };
        }

        if (trimmed.Equals("}", StringComparison.Ordinal))
        {
            return new SfsLineToken { Kind = SfsLineKind.CloseBrace, Content = content, Indent = indent, Body = body };
        }

        return new SfsLineToken
        {
            Kind = SfsLineKind.Name,
            Content = content,
            Indent = indent,
            Body = body,
            Name = trimmed,
        };
    }

    private static SfsValueStyle DetectValueStyle(string value)
    {
        if (value.Length >= 2)
        {
            if (value[0] == '"' && value[^1] == '"')
            {
                return SfsValueStyle.DoubleQuoted;
            }

            if (value[0] == '\'' && value[^1] == '\'')
            {
                return SfsValueStyle.SingleQuoted;
            }
        }

        return SfsValueStyle.Bare;
    }
}
