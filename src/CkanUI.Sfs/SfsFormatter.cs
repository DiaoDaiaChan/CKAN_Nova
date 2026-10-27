using System.Text;

namespace CkanUI.Sfs;

/// <summary>行尾风格。</summary>
public enum SfsNewLineMode
{
    /// <summary>按源文件多数派（§6.1 --eol=auto，默认）。</summary>
    Auto = 0,

    /// <summary>强制 LF。</summary>
    Lf = 1,

    /// <summary>强制 CRLF（KSP 实测行尾）。</summary>
    CrLf = 2,
}

/// <summary>缩进风格。</summary>
public enum SfsIndentMode
{
    /// <summary>每层一个制表符（KSP 实测，规范形式）。</summary>
    Tab = 0,

    /// <summary>每层 N 个空格。</summary>
    Spaces = 1,
}

/// <summary>空行策略。</summary>
public enum SfsBlankLineMode
{
    /// <summary>原样保留空行（默认，最保守）。</summary>
    Keep = 0,

    /// <summary>连续空行压成一行。</summary>
    Collapse = 1,

    /// <summary>删除所有空行。</summary>
    Strip = 2,
}

/// <summary>格式化选项（§6.1 CLI 参数的一一对应）。不可变，线程安全。</summary>
public sealed class SfsFormatOptions
{
    /// <summary>默认选项：制表符缩进、行尾自动、保留空行、保留 BOM、开启自校验。</summary>
    public static SfsFormatOptions Default { get; } = new SfsFormatOptions();

    /// <summary>行尾风格。</summary>
    public SfsNewLineMode NewLine { get; init; } = SfsNewLineMode.Auto;

    /// <summary>缩进风格。</summary>
    public SfsIndentMode Indent { get; init; } = SfsIndentMode.Tab;

    /// <summary>Spaces 模式下每层空格数；Tab 模式忽略。取值 1..16。</summary>
    public int IndentWidth { get; init; } = 1;

    /// <summary>空行策略。</summary>
    public SfsBlankLineMode BlankLines { get; init; } = SfsBlankLineMode.Keep;

    /// <summary>是否剥掉 BOM（默认保留，L1 保字）。</summary>
    public bool StripBom { get; init; }

    /// <summary>是否执行 §6.3 语义自校验（默认开启；关掉仅供诊断用）。</summary>
    public bool VerifySemantics { get; init; } = true;
}

/// <summary>格式化统计——用于 CLI 汇报"改了什么"，不参与语义。</summary>
public sealed class SfsFormatStats
{
    /// <summary>输出行数。</summary>
    public int Lines { get; internal set; }

    /// <summary>缩进被重写的行数。</summary>
    public int ReindentedLines { get; internal set; }

    /// <summary>键值分隔符被规范成 " = " 的次数。</summary>
    public int NormalizedSeparators { get; internal set; }

    /// <summary>行尾被改写的行数。</summary>
    public int NormalizedLineEndings { get; internal set; }

    /// <summary>被删除的空行数。</summary>
    public int BlankLinesRemoved { get; internal set; }

    /// <summary>被拆成"名字行 + 花括号行"的节点数（坏文件矩阵第 ⑥ 类）。</summary>
    public int NodeHeadersSplit { get; internal set; }

    /// <summary>原样保留的不可分词行数。</summary>
    public int OpaqueLinesPreserved { get; internal set; }

    /// <summary>原样保留的注释行数。</summary>
    public int CommentsPreserved { get; internal set; }
}

/// <summary>格式化结果。拒绝输出时 <see cref="Refused"/> 为 true 且 <see cref="Text"/> 等于源文本（调用方据此绝不写盘）。</summary>
public sealed class SfsFormatResult
{
    private readonly IReadOnlyList<SfsDiagnostic> _diagnostics;

    internal SfsFormatResult(
        string text,
        bool refused,
        string? refusalReason,
        bool selfCheckPassed,
        bool changed,
        string newLine,
        SfsFormatStats stats,
        List<SfsDiagnostic> diagnostics)
    {
        Text = text;
        Refused = refused;
        RefusalReason = refusalReason;
        SelfCheckPassed = selfCheckPassed;
        Changed = changed;
        NewLine = newLine;
        Stats = stats;
        _diagnostics = diagnostics;
    }

    /// <summary>格式化后的文本；<see cref="Refused"/> 时为源文本。</summary>
    public string Text { get; }

    /// <summary>是否因源文件不可信而拒绝产出（解析错误 / 非法 UTF-8 / 自校验失败）。</summary>
    public bool Refused { get; }

    /// <summary>拒绝原因（可读）。</summary>
    public string? RefusalReason { get; }

    /// <summary>§6.3 自校验是否通过。</summary>
    public bool SelfCheckPassed { get; }

    /// <summary>输出是否与源文本逐字节不同。</summary>
    public bool Changed { get; }

    /// <summary>实际使用的行尾字符串（"\r\n" 或 "\n"）。</summary>
    public string NewLine { get; }

    /// <summary>统计。</summary>
    public SfsFormatStats Stats { get; }

    /// <summary>格式化过程产生的诊断（自校验失败时为错误）。</summary>
    public IReadOnlyList<SfsDiagnostic> Diagnostics => _diagnostics;
}

/// <summary>
/// 规范格式化器（13 篇 §6.2/§6.3，WBS SFS-02）。
/// 输出恒为规范形式：<c>&lt;depth×TAB&gt;NAME</c> / <c>&lt;depth×TAB&gt;{</c> / <c>&lt;depth+1×TAB&gt;key = value</c> / <c>&lt;depth×TAB&gt;}</c>，
/// 花括号独占一行且与名字同缩进；空值保留尾空格（<c>key = </c>）。
/// 只改缩进 / 分隔符 / 行尾——<b>值的字节一个都不动</b>（L1），因此：
/// 幂等 format(format(x)) == format(x)（L2）；对已规范的文件 format(x) == x（L3）。
/// </summary>
public static class SfsFormatter
{
    private const string CanonicalSeparator = " = ";

    /// <summary>按规范形式格式化文档。</summary>
    public static SfsFormatResult Format(SfsDocument document, SfsFormatOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        options ??= SfsFormatOptions.Default;
        Validate(options);

        var stats = new SfsFormatStats();
        var diagnostics = new List<SfsDiagnostic>();
        var newLine = ResolveNewLine(document, options);

        // 源文件本身有解析错误 → 绝不"修"它（L1：我们不做猜测性修复）。
        var blocking = FirstError(document);
        if (blocking is not null)
        {
            return Refuse(document, newLine, stats, diagnostics, $"源文件存在解析错误，拒绝格式化：{blocking}");
        }

        if (document.IsTextLossy)
        {
            return Refuse(document, newLine, stats, diagnostics, "源文件不是合法 UTF-8，拒绝格式化以免二次破坏");
        }

        var builder = new StringBuilder(document.SourceText.Length + 64);
        Emit(document, options, newLine, builder, stats);
        var body = builder.ToString();

        if (options.VerifySemantics && !SelfCheck(document, body, out var reason))
        {
            diagnostics.Add(new SfsDiagnostic(
                SfsSeverity.Error,
                SfsDiagnosticCodes.EncodingOrLineEnding,
                0,
                0,
                reason));
            return Refuse(document, newLine, stats, diagnostics, reason);
        }

        var keepBom = document.HasBom && !options.StripBom;
        var text = keepBom ? string.Concat("\uFEFF", body) : body;

        // SourceText 不含 BOM（解析时已剥离），比较「是否有差异」时要把 BOM 补回去。
        var original = keepBom ? string.Concat("\uFEFF", document.SourceText) : document.SourceText;

        return new SfsFormatResult(
            text,
            refused: false,
            refusalReason: null,
            selfCheckPassed: options.VerifySemantics,
            changed: !string.Equals(text, original, StringComparison.Ordinal),
            newLine,
            stats,
            diagnostics);
    }

    /// <summary>按规范形式格式化文本（便捷重载）。</summary>
    public static SfsFormatResult FormatText(string text, SfsFormatOptions? options = null, string? sourcePath = null)
    {
        var parsed = SfsParser.Parse(text, sourcePath);
        if (parsed.Document is null)
        {
            var stats = new SfsFormatStats();
            return Refuse(parsed, stats, [.. parsed.Diagnostics]);
        }

        return Format(parsed.Document, options);
    }

    private static void Validate(SfsFormatOptions options)
    {
        if (options.IndentWidth is < 1 or > 16)
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.IndentWidth, "缩进宽度必须在 1..16 之间");
        }
    }

    private static string ResolveNewLine(SfsDocument document, SfsFormatOptions options)
        => options.NewLine switch
        {
            SfsNewLineMode.Lf => "\n",
            SfsNewLineMode.CrLf => "\r\n",
            _ => document.DominantNewLine,
        };

    private static SfsDiagnostic? FirstError(SfsDocument document)
    {
        foreach (var diagnostic in document.ParseDiagnostics)
        {
            if (diagnostic.Severity == SfsSeverity.Error)
            {
                return diagnostic;
            }
        }

        return null;
    }

    private static SfsFormatResult Refuse(
        SfsDocument document,
        string newLine,
        SfsFormatStats stats,
        List<SfsDiagnostic> diagnostics,
        string reason)
        => new SfsFormatResult(
            document.SourceText,
            refused: true,
            refusalReason: reason,
            selfCheckPassed: false,
            changed: false,
            newLine,
            stats,
            diagnostics);

    private static SfsFormatResult Refuse(SfsParseResult parsed, SfsFormatStats stats, List<SfsDiagnostic> diagnostics)
        => new SfsFormatResult(
            string.Empty,
            refused: true,
            refusalReason: parsed.Diagnostics.Count > 0 ? parsed.Diagnostics[0].Message : "无法解析源文本",
            selfCheckPassed: false,
            changed: false,
            "\r\n",
            stats,
            diagnostics);

    /// <summary>显式栈遍历（100 层嵌套也不爆栈），逐元素产出规范行。</summary>
    private static void Emit(
        SfsDocument document,
        SfsFormatOptions options,
        string newLine,
        StringBuilder builder,
        SfsFormatStats stats)
    {
        var work = new Stack<WorkItem>();
        PushRange(work, document.Elements, 0);
        var previousWasBlank = false;

        while (work.Count > 0)
        {
            var (element, depth, role) = work.Pop();

            if (element is SfsNode node)
            {
                switch (role)
                {
                    case SfsLineRole.Close:
                        var closeTerminator = string.Empty;
                        if (node.CloseLine is { } closeLine)
                        {
                            closeTerminator = closeLine.Terminator;
                            CountIndent(stats, SfsText.LeadingWhitespace(closeLine.Content), options, depth);
                        }

                        EmitLine(builder, options, depth, "}", closeTerminator, newLine, stats);
                        previousWasBlank = false;
                        break;

                    case SfsLineRole.Brace:
                        // 无名节点（裸 "{"）首行已经写了左花括号，不再重复。
                        if (node.Name.Length == 0)
                        {
                            break;
                        }

                        if (node.BraceLine is { } braceLine)
                        {
                            CountIndent(stats, SfsText.LeadingWhitespace(braceLine.Content), options, depth);
                            EmitLine(builder, options, depth, "{", braceLine.Terminator, newLine, stats);
                        }
                        else
                        {
                            // 坏文件矩阵第 ⑥ 类：同行 "NODE {" → 拆成独占一行。
                            stats.NodeHeadersSplit++;
                            EmitLine(builder, options, depth, "{", node.Raw.Terminator, newLine, stats);
                        }

                        previousWasBlank = false;
                        break;

                    default:
                        CountIndent(stats, node.Indent, options, depth);
                        EmitLine(builder, options, depth, node.Name.Length > 0 ? node.Name : "{", node.Raw.Terminator, newLine, stats);
                        work.Push(new WorkItem(node, depth, SfsLineRole.Close));
                        PushRange(work, node.Children, depth + 1);

                        // 栈是 LIFO：左花括号必须最后压栈，才会在"名字之后、子元素之前"弹出。
                        // 无名节点（裸 "{")的首行已写了左花括号，此处会把该角色跳过，不重复输出。
                        work.Push(new WorkItem(node, depth, SfsLineRole.Brace));
                        previousWasBlank = false;
                        break;
                }

                continue;
            }

            switch (element)
            {
                case SfsPair pair:
                    if (!string.Equals(pair.Separator, CanonicalSeparator, StringComparison.Ordinal))
                    {
                        stats.NormalizedSeparators++;
                    }

                    CountIndent(stats, pair.Indent, options, depth);
                    EmitPair(builder, options, depth, pair, newLine, stats);
                    previousWasBlank = false;
                    break;

                case SfsComment:
                    stats.CommentsPreserved++;
                    CountIndent(stats, element.Indent, options, depth);
                    EmitLine(builder, options, depth, element.Body, element.Terminator, newLine, stats);
                    previousWasBlank = false;
                    break;

                case SfsOpaque:
                    stats.OpaqueLinesPreserved++;
                    CountIndent(stats, element.Indent, options, depth);
                    EmitLine(builder, options, depth, element.Body, element.Terminator, newLine, stats);
                    previousWasBlank = false;
                    break;

                default:
                    // 空行：规范形式不带缩进。
                    if (options.BlankLines == SfsBlankLineMode.Keep
                        || (options.BlankLines == SfsBlankLineMode.Collapse && !previousWasBlank))
                    {
                        EmitLine(builder, options, 0, string.Empty, element.Terminator, newLine, stats);
                    }
                    else
                    {
                        stats.BlankLinesRemoved++;
                    }

                    previousWasBlank = true;
                    break;
            }
        }
    }

    private static void EmitPair(
        StringBuilder builder,
        SfsFormatOptions options,
        int depth,
        SfsPair pair,
        string newLine,
        SfsFormatStats stats)
    {
        AppendIndent(builder, options, depth);
        builder.Append(pair.Key);
        builder.Append(CanonicalSeparator);
        builder.Append(pair.Value);
        builder.Append(newLine);
        stats.Lines++;
        if (!string.Equals(pair.Terminator, newLine, StringComparison.Ordinal))
        {
            stats.NormalizedLineEndings++;
        }
    }

    private static void EmitLine(
        StringBuilder builder,
        SfsFormatOptions options,
        int depth,
        string body,
        string originalTerminator,
        string newLine,
        SfsFormatStats stats)
    {
        AppendIndent(builder, options, depth);
        builder.Append(body);
        builder.Append(newLine);
        stats.Lines++;
        if (!string.Equals(originalTerminator, newLine, StringComparison.Ordinal))
        {
            stats.NormalizedLineEndings++;
        }
    }

    private static void AppendIndent(StringBuilder builder, SfsFormatOptions options, int depth)
    {
        if (depth <= 0)
        {
            return;
        }

        if (options.Indent == SfsIndentMode.Tab)
        {
            builder.Append('\t', depth);
        }
        else
        {
            builder.Append(' ', depth * options.IndentWidth);
        }
    }

    private static void CountIndent(SfsFormatStats stats, string? originalIndent, SfsFormatOptions options, int depth)
    {
        if (!IndentMatches(originalIndent, options, depth))
        {
            stats.ReindentedLines++;
        }
    }

    private static bool IndentMatches(string? originalIndent, SfsFormatOptions options, int depth)
    {
        originalIndent ??= string.Empty;
        if (options.Indent == SfsIndentMode.Tab)
        {
            if (originalIndent.Length != depth)
            {
                return false;
            }

            for (var index = 0; index < depth; index++)
            {
                if (originalIndent[index] != '\t')
                {
                    return false;
                }
            }

            return true;
        }

        var width = depth * options.IndentWidth;
        if (originalIndent.Length != width)
        {
            return false;
        }

        for (var index = 0; index < width; index++)
        {
            if (originalIndent[index] != ' ')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>§6.3 自校验：重新解析输出，比"结构同构 + 值多重集相等"。</summary>
    private static bool SelfCheck(SfsDocument original, string body, out string reason)
    {
        var reparsed = SfsParser.Parse(body, original.SourcePath);
        if (reparsed.Document is null || reparsed.HasErrors)
        {
            reason = reparsed.Diagnostics.Count > 0
                ? $"格式化结果无法重新解析：{reparsed.Diagnostics[0]}"
                : "格式化结果无法重新解析";
            return false;
        }

        var candidate = reparsed.Document;
        if (candidate.NodeCount != original.NodeCount || candidate.PairCount != original.PairCount)
        {
            reason = $"格式化改变了结构：节点 {original.NodeCount}→{candidate.NodeCount}，键值 {original.PairCount}→{candidate.PairCount}";
            return false;
        }

        var leftShape = ShapeSignature(original);
        var rightShape = ShapeSignature(candidate);
        if (leftShape.Count != rightShape.Count)
        {
            reason = $"格式化改变了结构：形状签名 {leftShape.Count}→{rightShape.Count}";
            return false;
        }

        for (var index = 0; index < leftShape.Count; index++)
        {
            if (!string.Equals(leftShape[index], rightShape[index], StringComparison.Ordinal))
            {
                reason = $"格式化改变了结构：第 {index + 1} 项 \"{leftShape[index]}\" → \"{rightShape[index]}\"";
                return false;
            }
        }

        var leftValues = SfsValueIndex.Build(original);
        var rightValues = SfsValueIndex.Build(candidate);
        if (leftValues.Count != rightValues.Count)
        {
            reason = $"格式化改变了值的数量：{leftValues.Count}→{rightValues.Count}";
            return false;
        }

        for (var index = 0; index < leftValues.Count; index++)
        {
            if (!string.Equals(leftValues[index], rightValues[index], StringComparison.Ordinal))
            {
                reason = $"格式化改变了值：\"{leftValues[index]}\" → \"{rightValues[index]}\"";
                return false;
            }
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>形状签名：节点名/键/注释/不可分词行按文档顺序 + 深度。空行不算（可被策略删除）。</summary>
    private static List<string> ShapeSignature(SfsDocument document)
    {
        var items = new List<string>(document.NodeCount + document.PairCount + 16);
        var work = new Stack<SfsElement>();
        for (var index = document.Elements.Count - 1; index >= 0; index--)
        {
            work.Push(document.Elements[index]);
        }

        while (work.Count > 0)
        {
            var element = work.Pop();
            switch (element)
            {
                case SfsNode node:
                    items.Add(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"N{element.Depth}:{node.Name}"));
                    for (var index = node.Children.Count - 1; index >= 0; index--)
                    {
                        work.Push(node.Children[index]);
                    }

                    break;

                case SfsPair pair:
                    items.Add(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"P{element.Depth}:{pair.Key}"));
                    break;

                case SfsComment:
                    items.Add(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"C{element.Depth}:{element.Body}"));
                    break;

                case SfsOpaque:
                    items.Add(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"O{element.Depth}:{element.Body}"));
                    break;

                default:
                    break;
            }
        }

        return items;
    }

    private static void PushRange(Stack<WorkItem> work, IReadOnlyList<SfsElement> elements, int depth)
    {
        for (var index = elements.Count - 1; index >= 0; index--)
        {
            work.Push(new WorkItem(elements[index], depth, SfsLineRole.Element));
        }
    }

    private readonly record struct WorkItem(SfsElement Element, int Depth, SfsLineRole Role);
}
