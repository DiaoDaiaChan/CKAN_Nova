using System.Text;

namespace CkanUI.Sfs;

/// <summary>
/// 括号栈解析器（13 篇 §5）。结构由花括号栈决定，**不信任缩进**（L6）；
/// 显式栈实现，100 层甚至更深的嵌套不会栈溢出（07 篇 §9.2）。
/// 容错策略：无法分词的行记为 <see cref="SfsOpaque"/> 原样保留并报 V-03，绝不静默丢弃。
/// </summary>
public static class SfsParser
{
    /// <summary>严格 UTF-8 解码器：遇到非法字节抛 <see cref="DecoderFallbackException"/>。</summary>
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private const int DeviantLineLimit = 20;

    /// <summary>解析内存文本。</summary>
    public static SfsParseResult Parse(string text, string? sourcePath = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        return ParseCore(text, sourcePath, hasBom: false, isLossy: false, preDiagnostics: null);
    }

    /// <summary>解析 UTF-8 字节（自动识别并剥离 BOM，非法字节降级为 V-05 错误）。</summary>
    public static SfsParseResult ParseBytes(ReadOnlySpan<byte> utf8, string? sourcePath = null)
    {
        var pre = new List<SfsDiagnostic>();
        var hasBom = utf8.Length >= 3 && utf8[0] == 0xEF && utf8[1] == 0xBB && utf8[2] == 0xBF;
        if (hasBom)
        {
            utf8 = utf8[3..];
        }

        string text;
        var lossy = false;
        try
        {
            text = StrictUtf8.GetString(utf8);
        }
        catch (DecoderFallbackException)
        {
            text = Encoding.UTF8.GetString(utf8);
            lossy = true;
            pre.Add(new SfsDiagnostic(
                SfsSeverity.Error,
                SfsDiagnosticCodes.EncodingOrLineEnding,
                0,
                0,
                "文件含非法 UTF-8 字节序列（已用替换字符降级读取，禁止按此内容落盘）"));
        }

        return ParseCore(text, sourcePath, hasBom, lossy, pre);
    }

    /// <summary>解析文件（IO 异常原样抛出，由调用方决定提示方式；解析错误不抛）。</summary>
    public static SfsParseResult ParseFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var bytes = File.ReadAllBytes(path);
        return ParseBytes(bytes, path);
    }

    private static SfsParseResult ParseCore(
        string text,
        string? sourcePath,
        bool hasBom,
        bool isLossy,
        List<SfsDiagnostic>? preDiagnostics)
    {
        var state = new SfsParseState { SourcePath = sourcePath, SourceText = text, HasBom = hasBom, IsTextLossy = isLossy };
        if (preDiagnostics is not null)
        {
            state.Diagnostics.AddRange(preDiagnostics);
        }

        var topLevel = state.Elements;
        var open = new Stack<SfsNode>();
        var current = topLevel;

        SfsLineToken? pendingToken = null;
        var pendingRaw = default(SfsRawLine);
        var pendingLine = 0;
        var pendingDepth = 0;

        var crlf = 0;
        var lf = 0;
        var cr = 0;
        var pos = 0;
        var lineNumber = 0;

        while (pos < text.Length)
        {
            lineNumber++;
            var scan = pos;
            while (scan < text.Length && text[scan] != '\n' && text[scan] != '\r')
            {
                scan++;
            }

            var contentEnd = scan;
            var termEnd = scan;
            if (scan < text.Length)
            {
                if (text[scan] == '\r')
                {
                    termEnd = scan + 1 < text.Length && text[scan + 1] == '\n' ? scan + 2 : scan + 1;
                }
                else
                {
                    termEnd = scan + 1;
                }
            }

            var raw = new SfsRawLine(text[pos..contentEnd], text[contentEnd..termEnd]);
            state.LineCharOffsets.Add(pos);

            switch (raw.Terminator)
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

            var token = SfsLexer.Lex(raw.Content);
            var depthNow = open.Count;

            switch (token.Kind)
            {
                case SfsLineKind.Blank:
                    current.Add(new SfsBlank(lineNumber, depthNow, raw));
                    break;

                case SfsLineKind.Comment:
                    current.Add(new SfsComment(lineNumber, depthNow, raw, token.Body));
                    break;

                case SfsLineKind.Name:
                    if (pendingToken is not null)
                    {
                        FlushPending();
                    }

                    pendingToken = token;
                    pendingRaw = raw;
                    pendingLine = lineNumber;
                    pendingDepth = depthNow;
                    break;

                case SfsLineKind.OpenBrace:
                {
                    if (pendingToken is not null && token.Name.Length > 0)
                    {
                        FlushPending();
                    }

                    var usePending = pendingToken is not null;
                    var node = new SfsNode(
                        usePending ? pendingLine : lineNumber,
                        depthNow,
                        usePending ? pendingRaw : raw,
                        usePending ? pendingToken!.Name : token.Name,
                        usePending ? raw : null);

                    node.EndLineNumber = lineNumber;
                    current.Add(node);
                    open.Push(node);
                    current = node.ChildList;
                    state.NodeCount++;
                    if (depthNow + 1 > state.MaxDepth)
                    {
                        state.MaxDepth = depthNow + 1;
                    }

                    pendingToken = null;
                    break;
                }

                case SfsLineKind.CloseBrace:
                {
                    if (pendingToken is not null)
                    {
                        FlushPending();
                    }

                    if (open.Count == 0)
                    {
                        state.Diagnostics.Add(new SfsDiagnostic(
                            SfsSeverity.Error,
                            SfsDiagnosticCodes.UnbalancedBraces,
                            lineNumber,
                            token.Indent.Length + 1,
                            "多余的右花括号：没有与之配对的左花括号",
                            raw.Content));
                        current.Add(new SfsOpaque(lineNumber, depthNow, raw));
                    }
                    else
                    {
                        var node = open.Pop();
                        node.CloseLine = raw;
                        node.EndLineNumber = lineNumber;
                        current = open.Count > 0 ? open.Peek().ChildList : topLevel;
                    }

                    break;
                }

                default:
                {
                    if (pendingToken is not null)
                    {
                        FlushPending();
                    }

                    current.Add(new SfsPair(lineNumber, depthNow, raw, token.Key, token.Separator, token.Value, token.ValueStyle));
                    state.PairCount++;
                    break;
                }
            }

            pos = termEnd;
        }

        if (pendingToken is not null)
        {
            FlushPending();
        }

        while (open.Count > 0)
        {
            var node = open.Pop();
            node.EndLineNumber = lineNumber;
            state.Diagnostics.Add(new SfsDiagnostic(
                SfsSeverity.Error,
                SfsDiagnosticCodes.UnbalancedBraces,
                node.LineNumber,
                node.Indent.Length + 1,
                "块没有闭合：缺少右花括号",
                node.Content));
        }

        state.NewLines = new SfsNewLineCounts(crlf, lf, cr);
        CollectDeviantLines(text, state);
        state.Diagnostics.Sort(static (left, right) =>
        {
            var byLine = left.Line.CompareTo(right.Line);
            return byLine != 0 ? byLine : left.Column.CompareTo(right.Column);
        });

        var document = new SfsDocument(state);
        return new SfsParseResult(document, state.Diagnostics);

        void FlushPending()
        {
            if (pendingToken is null)
            {
                return;
            }

            state.Diagnostics.Add(new SfsDiagnostic(
                SfsSeverity.Error,
                SfsDiagnosticCodes.OpaqueLine,
                pendingLine,
                pendingToken.Indent.Length + 1,
                "无法解析的行：既不是键值对、也不是花括号行，且后面没有左花括号（不是节点名）",
                pendingRaw.Content));

            current.Add(new SfsOpaque(pendingLine, pendingDepth, pendingRaw));
            pendingToken = null;
        }
    }

    /// <summary>混合行尾时，收集与多数派不一致的行号（V-05 定位用，最多 20 条）。</summary>
    private static void CollectDeviantLines(string text, SfsParseState state)
    {
        if (!state.NewLines.IsMixed)
        {
            return;
        }

        var dominant = state.NewLines.Dominant;
        var pos = 0;
        var lineNumber = 0;
        while (pos < text.Length)
        {
            lineNumber++;
            var scan = pos;
            while (scan < text.Length && text[scan] != '\n' && text[scan] != '\r')
            {
                scan++;
            }

            var contentEnd = scan;
            var termEnd = scan;
            if (scan < text.Length)
            {
                if (text[scan] == '\r')
                {
                    termEnd = scan + 1 < text.Length && text[scan + 1] == '\n' ? scan + 2 : scan + 1;
                }
                else
                {
                    termEnd = scan + 1;
                }
            }

            var terminator = text[contentEnd..termEnd];
            if (terminator.Length > 0 && !terminator.Equals(dominant, StringComparison.Ordinal))
            {
                if (state.DeviantLineNumbers.Count < DeviantLineLimit)
                {
                    state.DeviantLineNumbers.Add(lineNumber);
                }
                else
                {
                    break;
                }
            }

            pos = termEnd;
        }
    }
}
