using System.Text;
using CkanUI.Sfs;

namespace CkanUI.Services.ModConfig;

/// <summary>
/// 一行原文（不含 BOM、不含行尾），行尾单独保存。
/// 拆成两段是为了让"改一个值"只替换正文、行尾（LF/CRLF/CR）原样保留。
/// </summary>
/// <param name="Content">行正文。</param>
/// <param name="Terminator">行尾序列，文件最后一行可能为空串。</param>
public readonly record struct ConfigNodeLine(string Content, string Terminator);

/// <summary>一个可编辑的键值对在原文里的位置（CFG-01）。</summary>
/// <param name="LineNumber">1 基行号。</param>
/// <param name="Depth">缩进层数（根层为 0）。</param>
/// <param name="NodePath">所在节点的名字链，用 / 分隔；扁平形态为空串。</param>
/// <param name="Key">键名。</param>
/// <param name="Separator">原文里的分隔符（KSP 实测多为 " = "）。</param>
/// <param name="Value">值原文（可能含行尾注释，本类型不做剥离）。</param>
/// <param name="Comment">键上方最近的注释块（多行用换行连接并去掉注释符），没有则为 null。</param>
public sealed record ConfigNodePair(
    int LineNumber,
    int Depth,
    string NodePath,
    string Key,
    string Separator,
    string Value,
    string? Comment);

/// <summary>待落盘的一处行改写。</summary>
public sealed record ConfigNodeLineEdit(int LineNumber, string OldContent, string NewContent);

/// <summary>
/// 无损 ConfigNode 文档（CFG-01）。三个"不可协商"的设计点：
/// <list type="number">
/// <item>语义交给 <see cref="SfsParser"/>（哪些行是键值对、注释挂在哪、节点名链）；</item>
/// <item>原文按物理行切开保存（正文 + 行尾分离），因此未改动时 <see cref="Render"/> 必然逐字节等于原文——不是"应该"，是由构造保证；</item>
/// <item>改值只做**行内切片**：保留缩进、键名、分隔符两侧空格、行尾注释、行尾序列。所以"改 3 项只变 3 行"是自然结果，不需要 diff 修补。</item>
/// </list>
/// 为什么不复用 <see cref="SfsWriter"/>：它从语法树重建、没有"改写表"入口；这里要按行注入替换。
/// 注意 <see cref="SfsRawLine"/> 不带行号，所以节点名链改为一次递归遍历携带，不做行号区间推算（花括号闭行没有行号）。
/// </summary>
public sealed class ConfigNodeDocument
{
    /// <summary>裸值里会破坏 ConfigNode 逐行语法的字符：出现时必须改用双引号包裹。</summary>
    private static readonly char[] QuoteRequiredChars = [' ', '\t', '#', ';', '=', '"', '{', '}'];

    private readonly List<ConfigNodeLine> _lines;
    private readonly Dictionary<int, string> _edits;

    private ConfigNodeDocument(
        string text,
        string? sourcePath,
        bool hasBom,
        SfsParseResult? parseResult)
    {
        Sfs = parseResult?.Document;
        SourcePath = sourcePath ?? Sfs?.SourcePath;
        HasBom = hasBom;
        IsTextLossy = (Sfs?.IsTextLossy ?? false) || (parseResult is not null && parseResult.Document is null);
        ParseDiagnostics = parseResult?.Diagnostics ?? [];
        ParseErrorCount = parseResult?.ErrorCount ?? 1;
        _lines = SplitLines(text);
        _edits = [];

        var pairs = new List<ConfigNodePair>();
        if (Sfs is not null)
        {
            CollectPairs(Sfs.Elements, string.Empty, pairs);
        }

        Pairs = pairs;
        NewLines = CountNewLines(_lines);
        RootNodeName = FindRootNodeName(Sfs);
    }

    /// <summary>底层 Sfs 文档（语义层，只读；解析整体失败时为 null）。</summary>
    public SfsDocument? Sfs { get; }

    /// <summary>来源路径（纯字符串解析时为 null）。</summary>
    public string? SourcePath { get; }

    /// <summary>
    /// 原文是否带 UTF-8 BOM。写回必须保留：KSP 插件自己用 WriteAllText 重写时是不带 BOM 的，
    /// 我们不该因为"顺手"就把文件编码事实改掉。
    /// </summary>
    public bool HasBom { get; }

    /// <summary>原文是否含非法 UTF-8（此时禁止写盘，避免把坏字节洗成替换字符）。</summary>
    public bool IsTextLossy { get; }

    /// <summary>解析诊断。</summary>
    public IReadOnlyList<SfsDiagnostic> ParseDiagnostics { get; }

    /// <summary>解析错误条数。</summary>
    public int ParseErrorCount { get; }

    /// <summary>是否存在解析错误（有错误时禁止写盘）。</summary>
    public bool HasParseErrors => ParseErrorCount > 0;

    /// <summary>物理行数。</summary>
    public int LineCount => _lines.Count;

    /// <summary>行尾统计（本类自己数的，不依赖解析器）。</summary>
    public SfsNewLineCounts NewLines { get; }

    /// <summary>第一个节点的名字（node 形态用来校验 adapter 的 rootNode）。</summary>
    public string? RootNodeName { get; }

    /// <summary>文档是否为空（0 字节）。</summary>
    public bool IsEmpty => _lines.Count == 0;

    /// <summary>是否有未落盘的改动。</summary>
    public bool IsModified => _edits.Count > 0;

    /// <summary>文档里所有键值对（文件顺序）。</summary>
    public IReadOnlyList<ConfigNodePair> Pairs { get; }

    /// <summary>原文长度（字符数，不含 BOM）。</summary>
    public int SourceLength => _lines.Sum(static line => line.Content.Length + line.Terminator.Length);

    /// <summary>待落盘的改动（按行号升序）。</summary>
    public IReadOnlyList<ConfigNodeLineEdit> PendingEdits
    {
        get
        {
            var result = new List<ConfigNodeLineEdit>(_edits.Count);
            for (var index = 0; index < _lines.Count; index++)
            {
                if (_edits.TryGetValue(index + 1, out var newContent))
                {
                    result.Add(new ConfigNodeLineEdit(index + 1, _lines[index].Content, newContent));
                }
            }

            return result;
        }
    }

    /// <summary>从字符串解析（字符串里若带 BOM 会被识别并保留）。</summary>
    public static ConfigNodeDocument Parse(string text, string? sourcePath = null)
    {
        ArgumentNullException.ThrowIfNull(text);

        var hasBom = text.Length > 0 && text[0] == '\uFEFF';
        var body = hasBom ? text[1..] : text;
        return new ConfigNodeDocument(body, sourcePath, hasBom, SfsParser.Parse(body, sourcePath));
    }

    /// <summary>从 UTF-8 字节解析（保留 BOM 事实；非法字节降级为错误而不是抛异常）。</summary>
    public static ConfigNodeDocument ParseBytes(ReadOnlySpan<byte> utf8, string? sourcePath = null)
    {
        var parseResult = SfsParser.ParseBytes(utf8, sourcePath);
        var text = parseResult.Document?.SourceText ?? string.Empty;
        var hasBom = parseResult.Document?.HasBom ?? false;
        return new ConfigNodeDocument(text, sourcePath, hasBom, parseResult);
    }

    /// <summary>从文件解析。IO 异常由调用方捕获并转成 OperationResult。</summary>
    public static ConfigNodeDocument ParseFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return ParseBytes(File.ReadAllBytes(path), path);
    }

    /// <summary>取指定行（1 基）的原文。</summary>
    public string GetLineContent(int lineNumber)
        => lineNumber >= 1 && lineNumber <= _lines.Count ? _lines[lineNumber - 1].Content : string.Empty;

    /// <summary>取键上方最近的注释块。</summary>
    public string? GetCommentFor(int lineNumber)
    {
        foreach (var pair in Pairs)
        {
            if (pair.LineNumber == lineNumber)
            {
                return pair.Comment;
            }
        }

        return null;
    }

    /// <summary>
    /// 按键名找位置。<paramref name="nodePath"/> 为 null 表示不限层级；
    /// 空串表示只要根层（扁平形态）；其余按 "A/B" 匹配节点名链。
    /// </summary>
    public ConfigNodePair? FindPair(string key, string? nodePath = null)
    {
        if (string.IsNullOrEmpty(key))
        {
            return null;
        }

        foreach (var pair in Pairs)
        {
            if (!string.Equals(pair.Key, key, StringComparison.Ordinal))
            {
                continue;
            }

            if (nodePath is null || string.Equals(pair.NodePath, nodePath, StringComparison.Ordinal))
            {
                return pair;
            }
        }

        return null;
    }

    /// <summary>
    /// 把某一行的值改掉。只改行内值片段：缩进、键名、分隔符、行尾注释、行尾序列全部保留。
    /// 返回 false 时 <paramref name="error"/> 给出可直接展示的中文原因。
    /// </summary>
    public bool TrySetValue(int lineNumber, string? newValue, out string? error)
    {
        error = null;

        if (newValue is null)
        {
            error = "新值不能为空。";
            return false;
        }

        if (newValue.IndexOfAny(['\n', '\r']) >= 0)
        {
            error = "值里不能含换行——ConfigNode 是逐行语法，换行会写出结构损坏的文件。";
            return false;
        }

        ConfigNodePair? target = null;
        foreach (var pair in Pairs)
        {
            if (pair.LineNumber == lineNumber)
            {
                target = pair;
                break;
            }
        }

        if (target is null)
        {
            error = $"第 {lineNumber} 行不是键值对（注释、空行或花括号行都不能改值）。";
            return false;
        }

        if (!TryRewrite(_lines[lineNumber - 1].Content, newValue, out var rewritten, out error))
        {
            return false;
        }

        if (string.Equals(rewritten, _lines[lineNumber - 1].Content, StringComparison.Ordinal))
        {
            // 值没变就撤掉之前的待写改动，保证 IsModified 反映真实差异。
            _edits.Remove(lineNumber);
            return true;
        }

        _edits[lineNumber] = rewritten;
        return true;
    }

    /// <summary>按键名改值（取第一条匹配）。</summary>
    public bool TrySetValue(string key, string? newValue, string? nodePath, out string? error)
    {
        var pair = FindPair(key, nodePath);
        if (pair is null)
        {
            error = $"文件里没有键 `{key}`（adapter 声明的键与实际文件不一致）。";
            return false;
        }

        return TrySetValue(pair.LineNumber, newValue, out error);
    }

    /// <summary>放弃全部改动。</summary>
    public void Revert() => _edits.Clear();

    /// <summary>把改动后的文档渲染成文本；无改动时与原文逐字节一致。</summary>
    public string Render()
    {
        var builder = new StringBuilder(SourceLength + 64);
        if (HasBom)
        {
            builder.Append('\uFEFF');
        }

        for (var index = 0; index < _lines.Count; index++)
        {
            var line = _lines[index];
            builder.Append(_edits.TryGetValue(index + 1, out var replacement) ? replacement : line.Content);
            builder.Append(line.Terminator);
        }

        return builder.ToString();
    }

    /// <summary>渲染成 UTF-8 字节（保留 BOM）。</summary>
    public byte[] RenderBytes() => Encoding.UTF8.GetBytes(Render());

    /// <summary>
    /// 把一行原文里的"值片段"替换掉：定位首个 `=` 之后的值 token，只换它，
    /// 其余（缩进 / 键名 / 分隔符两侧空格 / 行尾注释）留在原位。
    /// </summary>
    private static bool TryRewrite(string line, string newValue, out string rewritten, out string? error)
    {
        rewritten = line;
        error = null;

        var equalsIndex = -1;
        var braceIndex = -1;
        for (var index = 0; index < line.Length; index++)
        {
            var ch = line[index];
            if (ch == '=')
            {
                equalsIndex = index;
                break;
            }

            if (ch == '{')
            {
                braceIndex = index;
                break;
            }
        }

        if (equalsIndex < 0)
        {
            error = "这一行找不到 `=`，无法安全定位值片段。";
            return false;
        }

        if (braceIndex >= 0)
        {
            error = "这一行在 `=` 之前出现了 `{`，看起来不是键值对。";
            return false;
        }

        var valueStart = equalsIndex + 1;
        while (valueStart < line.Length && (line[valueStart] == ' ' || line[valueStart] == '\t'))
        {
            valueStart++;
        }

        int valueEnd;
        if (valueStart < line.Length && line[valueStart] == '"')
        {
            var closing = line.IndexOf('"', valueStart + 1);
            valueEnd = closing < 0 ? line.Length : closing + 1;
        }
        else
        {
            // 裸值：扫到行尾；中途出现"空白 + 注释起始符"时把后面当行尾注释保留。
            var cursor = valueStart;
            while (cursor < line.Length)
            {
                var ch = line[cursor];
                if ((ch == '/' || ch == '#')
                    && cursor > valueStart
                    && (line[cursor - 1] == ' ' || line[cursor - 1] == '\t'))
                {
                    break;
                }

                cursor++;
            }

            // 值 token 结束在注释前的空白处：空白属于"后缀"，跟着注释一起原样保留。
            while (cursor > valueStart && (line[cursor - 1] == ' ' || line[cursor - 1] == '\t'))
            {
                cursor--;
            }

            valueEnd = cursor;
        }

        if (!TryQuote(newValue, out var quoted, out error))
        {
            return false;
        }

        var suffix = line[valueEnd..];
        if (suffix.Length > 0 && valueEnd == valueStart)
        {
            // 原行是 `key = ` 后直接接注释：补一个空格再写值，避免两个 token 粘在一起。
            suffix = " " + suffix;
        }

        rewritten = string.Concat(line[..valueStart], quoted, suffix);
        return true;
    }

    /// <summary>按需加引号：含空格/注释符/等号/花括号等破坏性字符时用双引号包裹。</summary>
    private static bool TryQuote(string value, out string quoted, out string? error)
    {
        quoted = value;
        error = null;

        var needsQuote = false;
        foreach (var ch in QuoteRequiredChars)
        {
            if (value.IndexOf(ch) >= 0)
            {
                needsQuote = true;
                break;
            }
        }

        if (!needsQuote)
        {
            return true;
        }

        if (value.IndexOf('"') >= 0)
        {
            error = "值里同时含空白/注释符与双引号，ConfigNode 不支持转义，无法安全写入。";
            return false;
        }

        quoted = string.Concat("\"", value, "\"");
        return true;
    }

    /// <summary>把文本按物理行切开，正文与行尾分离。</summary>
    private static List<ConfigNodeLine> SplitLines(string text)
    {
        var lines = new List<ConfigNodeLine>();
        if (text.Length == 0)
        {
            return lines;
        }

        var start = 0;
        for (var index = 0; index < text.Length; index++)
        {
            var ch = text[index];
            if (ch == '\r')
            {
                if (index + 1 < text.Length && text[index + 1] == '\n')
                {
                    lines.Add(new ConfigNodeLine(text[start..index], "\r\n"));
                    index++;
                }
                else
                {
                    lines.Add(new ConfigNodeLine(text[start..index], "\r"));
                }

                start = index + 1;
            }
            else if (ch == '\n')
            {
                lines.Add(new ConfigNodeLine(text[start..index], "\n"));
                start = index + 1;
            }
        }

        if (start < text.Length)
        {
            lines.Add(new ConfigNodeLine(text[start..], string.Empty));
        }

        return lines;
    }

    /// <summary>数行尾（本类自己数，解析失败时也能给出统计）。</summary>
    private static SfsNewLineCounts CountNewLines(List<ConfigNodeLine> lines)
    {
        var crlf = 0;
        var lf = 0;
        var cr = 0;
        foreach (var line in lines)
        {
            switch (line.Terminator)
            {
                case "\r\n":
                    crlf++;
                    break;
                case "\n":
                    lf++;
                    break;
                case "\r":
                    cr++;
                    break;
                default:
                    break;
            }
        }

        return new SfsNewLineCounts(crlf, lf, cr);
    }

    /// <summary>
    /// 一次递归遍历产出"文件顺序的元素表"和"带节点名链的键值对表"，最后统一补注释。
    /// 注释必须等整棵树走完再回溯：只有那时 flattened 才是完整的文件行序。
    /// </summary>
    private static void CollectPairs(IReadOnlyList<SfsElement> elements, string nodePath, List<ConfigNodePair> output)
    {
        var flattened = new List<SfsElement>();
        Walk(elements, nodePath, flattened, output);
        AttachComments(flattened, output);
    }

    /// <summary>递归：节点只进名链（自身不是行元素），其余元素按文件顺序登记。</summary>
    private static void Walk(
        IReadOnlyList<SfsElement> elements,
        string nodePath,
        List<SfsElement> flattened,
        List<ConfigNodePair> output)
    {
        foreach (var element in elements)
        {
            if (element is SfsNode node)
            {
                var childPath = nodePath.Length == 0 ? node.Name : string.Concat(nodePath, "/", node.Name);
                Walk(node.Children, childPath, flattened, output);
                continue;
            }

            flattened.Add(element);

            if (element is not SfsPair pair)
            {
                continue;
            }

            output.Add(new ConfigNodePair(
                pair.LineNumber,
                pair.Depth,
                nodePath,
                pair.Key,
                string.IsNullOrEmpty(pair.Separator) ? " = " : pair.Separator,
                pair.Value,
                null));
        }
    }

    /// <summary>给每个键值对挂"上方最近的注释块"：跳过空行，连续注释行合并，遇到其它元素即停。</summary>
    private static void AttachComments(List<SfsElement> flattened, List<ConfigNodePair> output)
    {
        if (flattened.Count == 0 || output.Count == 0)
        {
            return;
        }

        var indexByLine = new Dictionary<int, int>(flattened.Count);
        for (var index = 0; index < flattened.Count; index++)
        {
            indexByLine[flattened[index].LineNumber] = index;
        }

        for (var pairIndex = 0; pairIndex < output.Count; pairIndex++)
        {
            var pair = output[pairIndex];
            if (pair.Comment is not null || !indexByLine.TryGetValue(pair.LineNumber, out var flatIndex))
            {
                continue;
            }

            var comment = CollectComment(flattened, flatIndex);
            if (comment is not null)
            {
                output[pairIndex] = pair with { Comment = comment };
            }
        }
    }

    /// <summary>向上回溯注释块。</summary>
    private static string? CollectComment(List<SfsElement> flattened, int pairIndex)
    {
        var collected = new List<string>();
        for (var index = pairIndex - 1; index >= 0; index--)
        {
            switch (flattened[index])
            {
                case SfsBlank:
                    continue;
                case SfsComment comment:
                    collected.Add(StripCommentMarker(comment.Text));
                    continue;
                default:
                    index = -1;
                    break;
            }
        }

        if (collected.Count == 0)
        {
            return null;
        }

        collected.Reverse();
        var joined = string.Join('\n', collected.ConvertAll(static line => line.TrimEnd()));
        return joined.Trim().Length == 0 ? null : joined.Trim();
    }

    /// <summary>去掉注释起始符与多余空格，只留人看的说明文字。</summary>
    private static string StripCommentMarker(string text)
    {
        var trimmed = text.TrimStart();
        if (trimmed.StartsWith("//", StringComparison.Ordinal))
        {
            trimmed = trimmed[2..];
        }
        else if (trimmed.StartsWith('#'))
        {
            trimmed = trimmed[1..];
        }

        return trimmed.Trim();
    }

    /// <summary>取第一个节点名（node 形态用来校验 adapter 的 rootNode 声明）。</summary>
    private static string? FindRootNodeName(SfsDocument? document)
    {
        if (document is null)
        {
            return null;
        }

        foreach (var element in document.Elements)
        {
            if (element is SfsNode node)
            {
                return node.Name;
            }
        }

        return null;
    }
}
