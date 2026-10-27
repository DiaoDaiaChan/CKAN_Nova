namespace CkanUI.Sfs;

/// <summary>行尾统计（13 篇 §6.1 的 --eol=auto 依据）。</summary>
public readonly record struct SfsNewLineCounts(int Crlf, int Lf, int Cr)
{
    /// <summary>总行数（含无行尾的末行）。</summary>
    public int Total => Crlf + Lf + Cr;

    /// <summary>是否存在多种行尾。</summary>
    public bool IsMixed
    {
        get
        {
            var kinds = (Crlf > 0 ? 1 : 0) + (Lf > 0 ? 1 : 0) + (Cr > 0 ? 1 : 0);
            return kinds > 1;
        }
    }

    /// <summary>多数派行尾；平局与全空时取 CRLF（KSP 实测与 13 篇 §2.2 的默认）。</summary>
    public string Dominant
    {
        get
        {
            if (Crlf >= Lf && Crlf >= Cr)
            {
                return Crlf > 0 ? "\r\n" : (Lf > 0 || Cr > 0 ? DominantOfTwo(Lf, Cr) : "\r\n");
            }

            return Lf >= Cr ? "\n" : "\r";
        }
    }

    private static string DominantOfTwo(int lf, int cr) => lf >= cr ? "\n" : "\r";
}

/// <summary>
/// 解析结果。**解析失败不抛异常**：错误以 <see cref="SfsDiagnostic"/> 形式返回，
/// 且尽量给出可用的 <see cref="Document"/>（结构损坏也能只读浏览与定位）。
/// </summary>
public sealed class SfsParseResult
{
    internal SfsParseResult(SfsDocument? document, IReadOnlyList<SfsDiagnostic> diagnostics)
    {
        Document = document;
        Diagnostics = diagnostics;

        var errors = 0;
        foreach (var diagnostic in diagnostics)
        {
            if (diagnostic.Severity == SfsSeverity.Error)
            {
                errors++;
            }
        }

        ErrorCount = errors;
    }

    /// <summary>解析出的文档（可能为 null，例如空字节）。</summary>
    public SfsDocument? Document { get; }

    /// <summary>诊断（错误级 + 警告级；全部带行号/列号/片段）。</summary>
    public IReadOnlyList<SfsDiagnostic> Diagnostics { get; }

    /// <summary>错误级诊断条数。</summary>
    public int ErrorCount { get; }

    /// <summary>是否有错误级诊断。</summary>
    public bool HasErrors => ErrorCount > 0;

    /// <summary>解析是否成功（有文档且无错误）。</summary>
    public bool Succeeded => Document is not null && ErrorCount == 0;
}

/// <summary>
/// SFS 文档：语法树 + 原始文本 + 行索引（13 篇 §4、§5）。
/// 只做**文本层**的事：值一律是字符串，不投影 float/int/bool（L4）。
/// </summary>
public sealed class SfsDocument
{
    private readonly List<SfsElement> _elements;
    private readonly List<SfsDiagnostic> _diagnostics;
    private readonly List<int> _deviantLineNumbers;
    private readonly List<int> _lineCharOffsets;

    internal SfsDocument(SfsParseState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        SourcePath = state.SourcePath;
        SourceText = state.SourceText;
        HasBom = state.HasBom;
        IsTextLossy = state.IsTextLossy;
        NewLines = state.NewLines;
        MaxDepth = state.MaxDepth;
        NodeCount = state.NodeCount;
        PairCount = state.PairCount;
        _elements = state.Elements;
        _diagnostics = state.Diagnostics;
        _deviantLineNumbers = state.DeviantLineNumbers;
        _lineCharOffsets = state.LineCharOffsets;
    }

    /// <summary>来源路径（内存文本解析时为 null）。</summary>
    public string? SourcePath { get; }

    /// <summary>去掉 BOM 后的原始文本（字节级往返的比对基准）。</summary>
    public string SourceText { get; }

    /// <summary>顶层元素（文件里没有包裹节点时就是一串键值对，例如 .loadmeta）。</summary>
    public IReadOnlyList<SfsElement> Elements => _elements;

    /// <summary>根节点（第一个顶层块）；文件没有块时为 null。</summary>
    public SfsNode? Root
    {
        get
        {
            foreach (var element in _elements)
            {
                if (element is SfsNode node)
                {
                    return node;
                }
            }

            return null;
        }
    }

    /// <summary>是否原本带 UTF-8 BOM。</summary>
    public bool HasBom { get; }

    /// <summary>解码是否有损（含非法 UTF-8 字节，已用替换字符降级）。有损时禁止落盘。</summary>
    public bool IsTextLossy { get; }

    /// <summary>行尾统计。</summary>
    public SfsNewLineCounts NewLines { get; }

    /// <summary>多数派行尾。</summary>
    public string DominantNewLine => NewLines.Dominant;

    /// <summary>与多数派行尾不同的行号（最多列出 20 条，用于 V-05 定位）。</summary>
    public IReadOnlyList<int> DeviantLineNumbers => _deviantLineNumbers;

    /// <summary>物理行数。</summary>
    public int LineCount => _lineCharOffsets.Count;

    /// <summary>块节点总数。</summary>
    public int NodeCount { get; }

    /// <summary>键值对总数。</summary>
    public int PairCount { get; }

    /// <summary>最大嵌套层数（顶层块为 1）。</summary>
    public int MaxDepth { get; }

    /// <summary>解析期诊断（V-01 括号平衡、V-03 无法分词的行、V-05 非法 UTF-8）。</summary>
    public IReadOnlyList<SfsDiagnostic> ParseDiagnostics => _diagnostics;

    /// <summary>是否没有任何元素（空文件）。</summary>
    public bool IsEmpty => _elements.Count == 0;

    /// <summary>取某一行的 UTF-8 字节偏移（1 起算行号），供编辑后就地重写单行。</summary>
    public int GetLineByteOffset(int lineNumber)
    {
        var charOffset = GetLineCharOffset(lineNumber);
        return charOffset == 0 ? 0 : System.Text.Encoding.UTF8.GetByteCount(SourceText.AsSpan(0, charOffset));
    }

    /// <summary>取某一行的字符偏移（1 起算行号）。</summary>
    public int GetLineCharOffset(int lineNumber)
    {
        if (lineNumber < 1 || lineNumber > _lineCharOffsets.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(lineNumber));
        }

        return _lineCharOffsets[lineNumber - 1];
    }

    /// <summary>按行号取原始行内容（1 起算），供"跳到原始行"与诊断展示。</summary>
    public string GetLineContent(int lineNumber)
    {
        var start = GetLineCharOffset(lineNumber);
        return SfsLineIndex.SliceLine(SourceText, start, lineNumber, _lineCharOffsets);
    }
}

/// <summary>解析过程中的可变累积状态（内部类型，字段可见性不上浮）。</summary>
internal sealed class SfsParseState
{
    public string? SourcePath;

    public string SourceText = string.Empty;

    public bool HasBom;

    public bool IsTextLossy;

    public SfsNewLineCounts NewLines;

    public int MaxDepth;

    public int NodeCount;

    public int PairCount;

    public List<SfsElement> Elements { get; } = [];

    public List<SfsDiagnostic> Diagnostics { get; } = [];

    public List<int> DeviantLineNumbers { get; } = [];

    public List<int> LineCharOffsets { get; } = [];
}

/// <summary>从"字符下标"到"行内容"的纯函数工具。</summary>
internal static class SfsLineIndex
{
    public static string SliceLine(string text, int start, int lineNumber, List<int> offsets)
    {
        var next = lineNumber < offsets.Count ? offsets[lineNumber] : -1;
        if (next < 0)
        {
            return text[start..];
        }

        var end = next;
        while (end > start && (text[end - 1] == '\n' || text[end - 1] == '\r'))
        {
            end--;
        }

        return text[start..end];
    }
}
