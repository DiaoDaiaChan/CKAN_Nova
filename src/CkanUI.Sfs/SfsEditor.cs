using System.Globalization;
using System.Text;

namespace CkanUI.Sfs;

/// <summary>一次编辑的结果：新文本 + 是否通过"只改了该改的"自校验。</summary>
public sealed class SfsEditResult
{
    public required string Text { get; init; }

    public required bool Changed { get; init; }

    public required bool Verified { get; init; }

    public required string Summary { get; init; }

    public IReadOnlyList<string> Issues { get; init; } = [];

    public bool Succeeded => Changed && Verified && Issues.Count == 0;
}

/// <summary>
/// 结构化编辑（13 篇 §8 就地改值）：**行级补丁 + 语义自校验**。
/// 编辑只重写目标行（或插入/删除整块），其余字节原样保留；写回前重新解析，
/// 比"节点数/键值数增量 + 键值多重集增量"是否正好等于预期。对不上就拒绝交付。
/// </summary>
public static class SfsEditor
{
    private const char Field = '\u001F';

    /// <summary>改一个键值对的值。只重写这一行，缩进与分隔符保持原样。</summary>
    public static SfsEditResult SetValue(SfsDocument document, int lineNumber, string value)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(value);
        if (!TryValidateValue(value, out var error))
        {
            return Failure(document, error);
        }

        var buffer = SfsLineBuffer.From(document);
        var pair = FindPair(document, lineNumber);
        if (pair is null)
        {
            return Failure(document, $"第 {lineNumber} 行不是键值对，不能改值");
        }

        buffer.Replace(lineNumber, pair.Indent + pair.Key + pair.Separator + value);
        var expectation = new Expectation(
            NodeDelta: 0,
            PairDelta: 0,
            Removed: [string.Concat(pair.Key, Field, pair.Value)],
            Added: [string.Concat(pair.Key, Field, value)]);
        return Finish(document, buffer, expectation, $"第 {lineNumber} 行 {pair.Key}: {Quote(pair.Value)} → {Quote(value)}");
    }

    /// <summary>改键名（值不动）。</summary>
    public static SfsEditResult RenameKey(SfsDocument document, int lineNumber, string key)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(key);
        if (!TryValidateKey(key, out var error))
        {
            return Failure(document, error);
        }

        var buffer = SfsLineBuffer.From(document);
        var pair = FindPair(document, lineNumber);
        if (pair is null)
        {
            return Failure(document, $"第 {lineNumber} 行不是键值对，不能改键名");
        }

        buffer.Replace(lineNumber, pair.Indent + key + pair.Separator + pair.Value);
        var expectation = new Expectation(
            NodeDelta: 0,
            PairDelta: 0,
            Removed: [string.Concat(pair.Key, Field, pair.Value)],
            Added: [string.Concat(key, Field, pair.Value)]);
        return Finish(document, buffer, expectation, $"第 {lineNumber} 行键名 {pair.Key} → {key}");
    }

    /// <summary>删除一个键值对或整块（含其所有子行）。</summary>
    public static SfsEditResult DeleteElement(SfsDocument document, int lineNumber)
    {
        ArgumentNullException.ThrowIfNull(document);
        var element = FindElement(document, lineNumber);
        if (element is null)
        {
            return Failure(document, $"第 {lineNumber} 行没有可删除的元素");
        }

        var buffer = SfsLineBuffer.From(document);
        var end = EndLine(element);
        var removed = CollectPairs(element);
        buffer.Remove(lineNumber, end);
        var expectation = new Expectation(
            NodeDelta: -(element is SfsNode node ? CountNodes(node) : 0),
            PairDelta: -removed.Count,
            Removed: removed,
            Added: []);
        return Finish(document, buffer, expectation, $"删除第 {lineNumber}–{end} 行（{Describe(element)}）");
    }

    /// <summary>复制一个键值对或整块，插在它自己后面（改 PART 阵列、复制整艘船都用它）。</summary>
    public static SfsEditResult DuplicateElement(SfsDocument document, int lineNumber)
    {
        ArgumentNullException.ThrowIfNull(document);
        var element = FindElement(document, lineNumber);
        if (element is null)
        {
            return Failure(document, $"第 {lineNumber} 行没有可复制的元素");
        }

        var buffer = SfsLineBuffer.From(document);
        var end = EndLine(element);
        buffer.Duplicate(lineNumber, end);
        var added = CollectPairs(element);
        var expectation = new Expectation(
            NodeDelta: element is SfsNode duplicated ? CountNodes(duplicated) : 0,
            PairDelta: added.Count,
            Removed: [],
            Added: added);
        return Finish(document, buffer, expectation, $"复制第 {lineNumber}–{end} 行（{Describe(element)}）");
    }

    /// <summary>在块的末尾追加一个键值对。缩进跟随同级元素，没有同级就用父缩进 + 一个制表符。</summary>
    public static SfsEditResult InsertPair(SfsDocument document, int parentLine, string key, string value)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);
        if (!TryValidateKey(key, out var keyError))
        {
            return Failure(document, keyError);
        }

        if (!TryValidateValue(value, out var valueError))
        {
            return Failure(document, valueError);
        }

        var parent = FindNode(document, parentLine);
        if (parent is null)
        {
            return Failure(document, $"第 {parentLine} 行不是块节点，不能插入键值对");
        }

        var buffer = SfsLineBuffer.From(document);
        var indent = ChildIndent(parent);
        var line = indent + key + " = " + value;
        var insertAt = parent.CloseLine is not null ? parent.EndLineNumber : buffer.Count + 1;
        var terminator = ChildTerminator(parent);
        buffer.Insert(insertAt, line + terminator);
        var expectation = new Expectation(
            NodeDelta: 0,
            PairDelta: 1,
            Removed: [],
            Added: [string.Concat(key, Field, value)]);
        return Finish(document, buffer, expectation, $"在第 {parentLine} 行块内插入 {key} = {value}");
    }

    /// <summary>取某一行对应的元素（节点或键值对）。</summary>
    public static SfsElement? FindElement(SfsDocument document, int lineNumber)
    {
        var node = FindNode(document, lineNumber);
        if (node is not null)
        {
            return node;
        }

        return FindPair(document, lineNumber);
    }

    /// <summary>取某一行对应的块节点。</summary>
    public static SfsNode? FindNode(SfsDocument document, int lineNumber)
    {
        ArgumentNullException.ThrowIfNull(document);
        return FindNodeCore(document.Elements, lineNumber);
    }

    /// <summary>取某一行对应的键值对。</summary>
    public static SfsPair? FindPair(SfsDocument document, int lineNumber)
    {
        ArgumentNullException.ThrowIfNull(document);
        var node = FindNode(document, lineNumber);
        if (node is not null)
        {
            return null;
        }

        var stack = new Stack<SfsElement>();
        for (var index = document.Elements.Count - 1; index >= 0; index--)
        {
            stack.Push(document.Elements[index]);
        }

        while (stack.Count > 0)
        {
            switch (stack.Pop())
            {
                case SfsPair pair when pair.LineNumber == lineNumber:
                    return pair;
                case SfsNode nested:
                    for (var index = nested.Children.Count - 1; index >= 0; index--)
                    {
                        stack.Push(nested.Children[index]);
                    }

                    break;
                default:
                    break;
            }
        }

        return null;
    }

    private static SfsNode? FindNodeCore(IReadOnlyList<SfsElement> elements, int lineNumber)
    {
        foreach (var element in elements)
        {
            if (element is not SfsNode node)
            {
                continue;
            }

            if (node.LineNumber == lineNumber)
            {
                return node;
            }

            if (lineNumber > node.LineNumber && lineNumber <= node.EndLineNumber)
            {
                var found = FindNodeCore(node.Children, lineNumber);
                if (found is not null)
                {
                    return found;
                }
            }
        }

        return null;
    }

    private static int EndLine(SfsElement element) => element is SfsNode node ? node.EndLineNumber : element.LineNumber;

    private static string Describe(SfsElement element) => element switch
    {
        SfsNode node => $"{node.Name} 块",
        SfsPair pair => $"{pair.Key} = {Quote(pair.Value)}",
        _ => element.GetType().Name,
    };

    private static string ChildIndent(SfsNode parent)
    {
        foreach (var child in parent.Children)
        {
            if (child is SfsPair pair)
            {
                return pair.Indent;
            }

            if (child is SfsNode node)
            {
                return node.Indent;
            }
        }

        return parent.Indent + "\t";
    }

    private static string ChildTerminator(SfsNode parent)
    {
        foreach (var child in parent.Children)
        {
            if (child.Terminator.Length > 0)
            {
                return child.Terminator;
            }
        }

        return parent.Terminator.Length > 0 ? parent.Terminator : "\r\n";
    }

    private static List<string> CollectPairs(SfsElement element)
    {
        var pairs = new List<string>();
        var stack = new Stack<SfsElement>();
        stack.Push(element);
        while (stack.Count > 0)
        {
            switch (stack.Pop())
            {
                case SfsPair pair:
                    pairs.Add(string.Concat(pair.Key, Field, pair.Value));
                    break;
                case SfsNode node:
                    for (var index = node.Children.Count - 1; index >= 0; index--)
                    {
                        stack.Push(node.Children[index]);
                    }

                    break;
                default:
                    break;
            }
        }

        return pairs;
    }

    private static int CountNodes(SfsNode node)
    {
        // 含节点自己：删除/复制一个块时，它的花括号行也算一个节点增量。
        var count = 1;
        var stack = new Stack<SfsNode>();
        stack.Push(node);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            foreach (var child in current.Children)
            {
                if (child is SfsNode nested)
                {
                    count++;
                    stack.Push(nested);
                }
            }
        }

        return count;
    }

    /// <summary>逐项校验：重新解析 + 计数增量 + 键值多重集增量，必须与预期完全一致。</summary>
    private static SfsEditResult Finish(SfsDocument original, SfsLineBuffer buffer, Expectation expectation, string summary)
    {
        var body = buffer.ToBody();
        var text = buffer.ToString();
        // 解析时不能带 BOM：Parse(text) 会把 BOM 当成首行内容，导致根块识别失败。
        var reparsed = SfsParser.Parse(body, original.SourcePath);
        var issues = new List<string>();

        if (reparsed.Document is null)
        {
            issues.Add("编辑后的文本无法解析");
            return new SfsEditResult { Text = text, Changed = true, Verified = false, Summary = summary, Issues = issues };
        }

        var candidate = reparsed.Document;
        foreach (var diagnostic in candidate.ParseDiagnostics)
        {
            if (diagnostic.Severity == SfsSeverity.Error)
            {
                issues.Add($"编辑后出现解析错误：{diagnostic}");
                break;
            }
        }

        var nodeDelta = candidate.NodeCount - original.NodeCount;
        if (nodeDelta != expectation.NodeDelta)
        {
            issues.Add($"块数量增量不符：预期 {expectation.NodeDelta}，实际 {nodeDelta}");
        }

        var pairDelta = candidate.PairCount - original.PairCount;
        if (pairDelta != expectation.PairDelta)
        {
            issues.Add($"键值数量增量不符：预期 {expectation.PairDelta}，实际 {pairDelta}");
        }

        var before = BuildPairSet(original);
        var after = BuildPairSet(candidate);
        var (removed, added) = Diff(before, after);
        if (!MultisetEquals(removed, expectation.Removed))
        {
            issues.Add($"被改动的旧值不是预期的那一条：{DescribeDiff(removed)}");
        }

        if (!MultisetEquals(added, expectation.Added))
        {
            issues.Add($"新增的值不是预期的那一条：{DescribeDiff(added)}");
        }

        return new SfsEditResult
        {
            Text = text,
            Changed = true,
            Verified = issues.Count == 0,
            Summary = summary,
            Issues = issues,
        };
    }

    private static SfsEditResult Failure(SfsDocument document, string reason)
        => new()
        {
            Text = (document.HasBom ? "\uFEFF" : string.Empty) + document.SourceText,
            Changed = false,
            Verified = false,
            Summary = "未修改",
            Issues = [reason],
        };

    private static List<string> BuildPairSet(SfsDocument document)
    {
        var entries = new List<string>(document.PairCount);
        var stack = new Stack<SfsElement>();
        for (var index = document.Elements.Count - 1; index >= 0; index--)
        {
            stack.Push(document.Elements[index]);
        }

        while (stack.Count > 0)
        {
            switch (stack.Pop())
            {
                case SfsPair pair:
                    entries.Add(string.Concat(pair.Key, Field, pair.Value));
                    break;
                case SfsNode node:
                    for (var index = node.Children.Count - 1; index >= 0; index--)
                    {
                        stack.Push(node.Children[index]);
                    }

                    break;
                default:
                    break;
            }
        }

        entries.Sort(StringComparer.Ordinal);
        return entries;
    }

    private static (List<string> Removed, List<string> Added) Diff(IReadOnlyList<string> before, IReadOnlyList<string> after)
    {
        var removed = new List<string>();
        var added = new List<string>();
        var left = 0;
        var right = 0;
        while (left < before.Count || right < after.Count)
        {
            if (left >= before.Count)
            {
                added.Add(after[right++]);
                continue;
            }

            if (right >= after.Count)
            {
                removed.Add(before[left++]);
                continue;
            }

            var compare = string.CompareOrdinal(before[left], after[right]);
            if (compare == 0)
            {
                left++;
                right++;
            }
            else if (compare < 0)
            {
                removed.Add(before[left++]);
            }
            else
            {
                added.Add(after[right++]);
            }
        }

        return (removed, added);
    }

    private static bool MultisetEquals(List<string> actual, IReadOnlyList<string> expected)
    {
        if (actual.Count != expected.Count)
        {
            return false;
        }

        var sorted = new List<string>(expected);
        sorted.Sort(StringComparer.Ordinal);
        for (var index = 0; index < actual.Count; index++)
        {
            if (!string.Equals(actual[index], sorted[index], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static string DescribeDiff(List<string> entries)
    {
        if (entries.Count == 0)
        {
            return "（无变化）";
        }

        var shown = entries.Take(3).Select(static entry => entry.Replace(Field, '='));
        return string.Join("、", shown) + (entries.Count > 3 ? $" 等 {entries.Count} 条" : string.Empty);
    }

    /// <summary>值里不允许换行（会把一行拆成两行，破坏往返）；其余字符（含空格、反斜杠、引号）都是字面量。</summary>
    public static bool TryValidateValue(string value, out string error)
    {
        error = string.Empty;
        if (value.Contains('\r') || value.Contains('\n'))
        {
            error = "值里不能有换行";
            return false;
        }

        return true;
    }

    /// <summary>键名不能为空、不能含空白/花括号/等号/注释前缀（否则写出来的行不再是键值对）。</summary>
    public static bool TryValidateKey(string key, out string error)
    {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(key))
        {
            error = "键名不能为空";
            return false;
        }

        foreach (var ch in key)
        {
            if (char.IsWhiteSpace(ch) || ch is '{' or '}' or '=' or '#' || ch == '/')
            {
                error = $"键名不能包含空白或 {{ }} = # / 字符：{key}";
                return false;
            }
        }

        return true;
    }

    private static string Quote(string value) => value.Length == 0 ? "（空）" : value;

    private readonly record struct Expectation(int NodeDelta, int PairDelta, List<string> Removed, List<string> Added);

    /// <summary>行缓冲：把源文本拆成"内容 + 行尾"，编辑后原样拼回（BOM 单独处理）。</summary>
    private sealed class SfsLineBuffer
    {
        private readonly List<string> _contents = [];
        private readonly List<string> _terminators = [];
        private readonly bool _hasBom;

        private SfsLineBuffer(bool hasBom)
        {
            _hasBom = hasBom;
        }

        public int Count => _contents.Count;

        public static SfsLineBuffer From(SfsDocument document)
        {
            var buffer = new SfsLineBuffer(document.HasBom);
            var text = document.SourceText;
            var pos = 0;
            while (pos < text.Length)
            {
                var scan = pos;
                while (scan < text.Length && text[scan] != '\n' && text[scan] != '\r')
                {
                    scan++;
                }

                var termEnd = scan;
                if (scan < text.Length)
                {
                    termEnd = text[scan] == '\r' && scan + 1 < text.Length && text[scan + 1] == '\n' ? scan + 2 : scan + 1;
                }

                buffer._contents.Add(text[pos..scan]);
                buffer._terminators.Add(text[scan..termEnd]);
                pos = termEnd;
            }

            return buffer;
        }

        public void Replace(int lineNumber, string content)
        {
            _contents[lineNumber - 1] = content;
        }

        public void Remove(int fromLine, int toLine)
        {
            var index = fromLine - 1;
            var count = toLine - fromLine + 1;
            _contents.RemoveRange(index, count);
            _terminators.RemoveRange(index, count);
        }

        public void Duplicate(int fromLine, int toLine)
        {
            var index = fromLine - 1;
            var count = toLine - fromLine + 1;
            var contents = _contents.GetRange(index, count);
            var terminators = _terminators.GetRange(index, count);
            _contents.InsertRange(index + count, contents);
            _terminators.InsertRange(index + count, terminators);
        }

        /// <summary>在 1 起算的行号之前插入一整行（行内容含行尾）。</summary>
        public void Insert(int beforeLine, string lineWithTerminator)
        {
            var index = Math.Clamp(beforeLine - 1, 0, _contents.Count);
            var terminator = lineWithTerminator.EndsWith("\r\n", StringComparison.Ordinal)
                ? "\r\n"
                : lineWithTerminator.EndsWith('\n') ? "\n" : lineWithTerminator.EndsWith('\r') ? "\r" : string.Empty;
            var content = terminator.Length == 0 ? lineWithTerminator : lineWithTerminator[..^terminator.Length];
            _contents.Insert(index, content);
            _terminators.Insert(index, terminator);
        }

        /// <summary>不含 BOM 的正文（重新解析用）。</summary>
        public string ToBody()
        {
            var builder = new StringBuilder();
            for (var index = 0; index < _contents.Count; index++)
            {
                builder.Append(_contents[index]).Append(_terminators[index]);
            }

            return builder.ToString();
        }

        public override string ToString()
        {
            var builder = new StringBuilder();
            if (_hasBom)
            {
                builder.Append('\uFEFF');
            }

            for (var index = 0; index < _contents.Count; index++)
            {
                builder.Append(_contents[index]).Append(_terminators[index]);
            }

            return builder.ToString();
        }
    }
}
