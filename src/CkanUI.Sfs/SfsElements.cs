namespace CkanUI.Sfs;

/// <summary>
/// 值字面风格的**分类**（不解释、不还原）。
/// 13 篇 L7「值内不转义」：反斜杠、双引号、单引号、等号、制表符、空格全部是字面字符。
/// 因此这里只记录"原样看起来像什么"，序列化时一律把原字节写回。
/// </summary>
public enum SfsValueStyle
{
    /// <summary>无引号包裹。</summary>
    Bare = 0,

    /// <summary>首尾是双引号（引号本身也是字面字符，不参与转义）。</summary>
    DoubleQuoted = 1,

    /// <summary>首尾是单引号。</summary>
    SingleQuoted = 2,
}

/// <summary>物理行：Content 不含行尾，Terminator 是 CRLF/LF/CR/空串。</summary>
public readonly record struct SfsRawLine(string Content, string Terminator);

/// <summary>
/// 语法树元素基类。每个元素恰好拥有 1..2 个物理行（节点 = 名字行 + 可选花括号开行 + 可选花括号闭行）。
/// LineNumber 是 1 起算的行号，供诊断与"跳到原始行"使用（13 篇 §5 行索引）。
/// </summary>
public abstract class SfsElement
{
    private protected SfsElement(int lineNumber, int depth, SfsRawLine raw)
    {
        LineNumber = lineNumber;
        Depth = depth;
        Raw = raw;
    }

    /// <summary>1 起算的首行行号。</summary>
    public int LineNumber { get; }

    /// <summary>外层节点层数（顶层元素为 0）。结构由花括号栈决定，不信任缩进（L6）。</summary>
    public int Depth { get; }

    /// <summary>首个物理行的原始内容与行尾。</summary>
    public SfsRawLine Raw { get; }

    /// <summary>首行的原始内容（不含行尾）。</summary>
    public string Content => Raw.Content;

    /// <summary>首行的原始行尾（可能是空串）。</summary>
    public string Terminator => Raw.Terminator;

    /// <summary>行首连续空白。</summary>
    public string Indent => SfsText.LeadingWhitespace(Raw.Content);

    /// <summary>去掉行首空白后的正文。</summary>
    public string Body => Raw.Content[Indent.Length..];
}

/// <summary>空行（只有空白字符的行）。</summary>
public sealed class SfsBlank : SfsElement
{
    internal SfsBlank(int lineNumber, int depth, SfsRawLine raw)
        : base(lineNumber, depth, raw)
    {
    }
}

/// <summary>注释行：双斜杠或井号开头。内容原样保留（13 篇 §2.2、§6.2）。</summary>
public sealed class SfsComment : SfsElement
{
    internal SfsComment(int lineNumber, int depth, SfsRawLine raw, string text)
        : base(lineNumber, depth, raw)
    {
        Text = text;
    }

    /// <summary>注释正文（含前缀，不含行首空白）。</summary>
    public string Text { get; }
}

/// <summary>无法分词的原始行（13 篇 §5 容错）：绝不静默丢弃，原样保留并在校验里报 V-03。</summary>
public sealed class SfsOpaque : SfsElement
{
    internal SfsOpaque(int lineNumber, int depth, SfsRawLine raw)
        : base(lineNumber, depth, raw)
    {
    }

    /// <summary>原始正文（不含行首空白）。</summary>
    public string Text => Body;
}

/// <summary>
/// 键值对。同一节点内重复键**原样保留、顺序不变**（L5），只在校验时警告。
/// Key/Value 都是字面文本：数字/布尔/枚举**不投影**（L4），写回时不做任何数值往返。
/// </summary>
public sealed class SfsPair : SfsElement
{
    internal SfsPair(int lineNumber, int depth, SfsRawLine raw, string key, string separator, string value, SfsValueStyle style)
        : base(lineNumber, depth, raw)
    {
        Key = key;
        Separator = separator;
        Value = value;
        ValueStyle = style;
    }

    /// <summary>键（已去掉键尾空白）。</summary>
    public string Key { get; }

    /// <summary>键与值之间的原始分隔符，例如 " = "、"="、"  =  "；空值时为 " = "。</summary>
    public string Separator { get; }

    /// <summary>值原文：从分隔符之后到行尾，一个字节都不动（L1/L7）。空值时为 ""。</summary>
    public string Value { get; }

    /// <summary>值的引号风格（仅分类，不解释）。</summary>
    public SfsValueStyle ValueStyle { get; }
}

/// <summary>
/// 块节点。名字行与左花括号可以在同一行（NAME {，解析仍成功，格式化后独占一行），
/// 也可以各占一行（KSP 实测写法）。
/// </summary>
public sealed class SfsNode : SfsElement
{
    private readonly List<SfsElement> _children = [];

    internal SfsNode(int lineNumber, int depth, SfsRawLine raw, string name, SfsRawLine? braceLine)
        : base(lineNumber, depth, raw)
    {
        Name = name;
        BraceLine = braceLine;
    }

    /// <summary>节点名（已去空白）；无名节点为 ""。</summary>
    public string Name { get; }

    /// <summary>独占一行的左花括号行；为 null 表示与名字同行。</summary>
    public SfsRawLine? BraceLine { get; }

    /// <summary>独占一行的右花括号行；为 null 表示块未闭合（V-01 已报错）。</summary>
    public SfsRawLine? CloseLine { get; internal set; }

    /// <summary>子元素（保持原文件顺序）。</summary>
    public IReadOnlyList<SfsElement> Children => _children;

    internal List<SfsElement> ChildList => _children;

    /// <summary>本节点直接包含的键值对，按原顺序。</summary>
    public IEnumerable<SfsPair> Pairs()
    {
        foreach (var child in _children)
        {
            if (child is SfsPair pair)
            {
                yield return pair;
            }
        }
    }

    /// <summary>取第一个同名键（重复键语义按 L5 不合并，这里只是只读便利方法）。</summary>
    public SfsPair? FindPair(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        foreach (var pair in Pairs())
        {
            if (pair.Key.Equals(key, StringComparison.Ordinal))
            {
                return pair;
            }
        }

        return null;
    }
}
