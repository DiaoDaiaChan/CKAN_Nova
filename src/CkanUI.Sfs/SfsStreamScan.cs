using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace CkanUI.Sfs;

/// <summary>流式扫描抓到的一条浅层键值（不做完整语法树，超大存档也不会爆内存）。</summary>
public sealed record SfsStreamField(string Path, string Key, string Value, int Line, int Depth);

/// <summary>流式扫描抓到的块（含行区间与估算字节数，用来指认“某个 mod 把几百兆塞在哪”）。</summary>
public sealed record SfsStreamBlock
{
    public string Path { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public int Line { get; init; }

    public int EndLine { get; init; }

    public int Depth { get; init; }

    /// <summary>块内（含嵌套子块）的键值总数。</summary>
    public int PairCount { get; init; }

    /// <summary>块内（含嵌套，不含自己）的块总数。</summary>
    public int NodeCount { get; init; }

    /// <summary>块的估算字节数（行内容 UTF-8 字节 + 行尾，±2% 以内；超大单行按原始长度计）。</summary>
    public long ByteLength { get; init; }

    public string? Summary { get; init; }
}

/// <summary>流式搜索命中行。</summary>
public sealed record SfsStreamHit(int Line, string Text, string Key, string Value, bool IsPair);

/// <summary>流式块详情（大文件模式下代替完整解析的懒展开）。</summary>
public sealed record SfsStreamDetail
{
    public string Path { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public int Line { get; init; }

    public int EndLine { get; init; }

    public bool Truncated { get; init; }

    public IReadOnlyList<SfsStreamField> Fields { get; init; } = [];

    public IReadOnlyList<SfsStreamBlock> Nodes { get; init; } = [];
}

/// <summary>一份流的扫描结果：计数、浅层字段、块结构、超大块清单。</summary>
public sealed class SfsStreamProfile
{
    public long ByteLength { get; internal set; }

    public int LineCount { get; internal set; }

    public int PairCount { get; internal set; }

    public int NodeCount { get; internal set; }

    public int MaxDepth { get; internal set; }

    public string? RootName { get; internal set; }

    public bool HasErrors { get; internal set; }

    /// <summary>是否因为预算只统计不展开（字段 / 块 / 超大块清单被截断）。</summary>
    public bool Truncated { get; internal set; }

    public IReadOnlyList<string> Issues { get; internal set; } = [];

    /// <summary>浅层键值（深度 &lt;= <see cref="SfsStreamOptions.FieldDepth"/>，有预算上限）。</summary>
    public IReadOnlyList<SfsStreamField> Fields { get; internal set; } = [];

    /// <summary>顶层块（GAME / FLIGHTSTATE / SCENARIO …）与超大块。</summary>
    public IReadOnlyList<SfsStreamBlock> Blocks { get; internal set; } = [];

    /// <summary>按字节数降序的超大块（疑似 mod 私有数据）。</summary>
    public IReadOnlyList<SfsStreamBlock> OversizedBlocks { get; internal set; } = [];

    /// <summary>节点名统计（VESSEL / PART / SCENARIO …），大小写不敏感。</summary>
    public IReadOnlyDictionary<string, int> NodeNameCounts { get; internal set; } =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>流式扫描的预算与阈值。</summary>
public sealed class SfsStreamOptions
{
    /// <summary>收集键值的最大深度（再深就不进大纲，只计数）。</summary>
    public int FieldDepth { get; init; } = 4;

    public int FieldBudget { get; init; } = 4000;

    /// <summary>收集块结构的最大深度。</summary>
    public int BlockDepth { get; init; } = 2;

    public int BlockBudget { get; init; } = 3000;

    public int OversizedBlockBudget { get; init; } = 64;

    /// <summary>超过这个字节数的块进“超大块”清单。</summary>
    public long OversizedBlockBytes { get; init; } = 4L * 1024 * 1024;

    /// <summary>单行超过这个字节数就不解码（只在计数里体现），避免巨型单行把内存打爆。</summary>
    public long MaxLineBytes { get; init; } = 8L * 1024 * 1024;

    public static SfsStreamOptions Default { get; } = new();
}

/// <summary>
/// 流式扫描（SFS-04 大文件策略）：只走一遍行，不建语法树。
/// 用途：几十上百 MB 的整合包存档也能在秒级出大纲/搜索，并且能指认出“哪个块占了多少字节”。
/// 与 <see cref="SfsParser"/> 共用 <see cref="SfsLexer"/>，判定规则不复制。
/// </summary>
public static class SfsStreamScanner
{
    /// <summary>超过这个大小的存档走流式模式：完整语法树在几百 MB 时会吃掉 GB 级内存。</summary>
    public const long FullParseLimitBytes = 32L * 1024 * 1024;

    public static bool RequiresStreaming(long byteLength) => byteLength > FullParseLimitBytes;

    public static bool RequiresStreaming(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return new FileInfo(path).Length > FullParseLimitBytes;
    }

    /// <summary>扫一遍文件，产出计数 + 浅层字段 + 块结构 + 超大块清单。</summary>
    public static SfsStreamProfile Scan(string path, SfsStreamOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        options ??= SfsStreamOptions.Default;

        var profile = new SfsStreamProfile { ByteLength = new FileInfo(path).Length };
        var fields = new List<SfsStreamField>();
        var blocks = new List<SfsStreamBlock>();
        var oversized = new List<SfsStreamBlock>();
        var issues = new List<string>();
        var nameCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var scaleCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["VESSEL"] = 0,
            ["PART"] = 0,
            ["SCENARIO"] = 0,
            ["KERBAL"] = 0,
        };
        var frames = new List<Frame>();
        var names = new List<string>();

        long bytes = 0;
        var lineNumber = 0;
        string? pendingName = null;
        var pendingLine = 0;
        var maxDepth = 0;

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
        using var reader = new StreamReader(stream, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true, bufferSize: 1 << 16);

        string? raw;
        while ((raw = reader.ReadLine()) is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lineNumber++;

            var lineStart = bytes;
            bytes += Encoding.UTF8.GetByteCount(raw) + 2;
            var depth = frames.Count;
            var token = SfsLexer.Lex(raw);

            switch (token.Kind)
            {
                case SfsLineKind.Blank:
                case SfsLineKind.Comment:
                    break;

                case SfsLineKind.Name:
                    if (pendingName is not null)
                    {
                        AddIssue($"第 {pendingLine} 行不是块名（后面没有左花括号）", issues);
                    }

                    pendingName = token.Name;
                    pendingLine = lineNumber;
                    break;

                case SfsLineKind.OpenBrace:
                {
                    if (pendingName is not null && token.Name.Length > 0)
                    {
                        AddIssue($"第 {pendingLine} 行不是块名（后面没有左花括号）", issues);
                        pendingName = null;
                    }

                    var usePending = pendingName is not null && token.Name.Length == 0;
                    var name = usePending ? pendingName! : token.Name;
                    var nodeLine = usePending ? pendingLine : lineNumber;

                    names.Add(name);
                    frames.Add(new Frame
                    {
                        Name = name,
                        NameIndex = names.Count - 1,
                        Line = nodeLine,
                        Depth = depth,
                        StartBytes = lineStart,
                    });

                    if (scaleCounts.TryGetValue(name, out var scaleCount))
                    {
                        scaleCounts[name] = scaleCount + 1;
                    }

                    // 普通块名只留前 128 个，防止恶意/异常存档用海量唯一块名撑爆字典；
                    // 但 VESSEL/PART/SCENARIO/KERBAL 这类规模统计走独立计数器，不能被清掉。
                    if (nameCounts.TryGetValue(name, out var count))
                    {
                        nameCounts[name] = count + 1;
                    }
                    else if (nameCounts.Count < 128)
                    {
                        nameCounts[name] = 1;
                    }

                    profile.NodeCount++;
                    profile.RootName ??= name;
                    if (depth + 1 > maxDepth)
                    {
                        maxDepth = depth + 1;
                    }

                    pendingName = null;
                    break;
                }

                case SfsLineKind.CloseBrace:
                {
                    if (pendingName is not null)
                    {
                        AddIssue($"第 {pendingLine} 行不是块名（后面没有左花括号）", issues);
                        pendingName = null;
                    }

                    if (frames.Count == 0)
                    {
                        AddIssue($"第 {lineNumber} 行有多余的右花括号", issues);
                        break;
                    }

                    var frame = frames[^1];
                    frames.RemoveAt(frames.Count - 1);
                    var block = BuildBlock(frame, lineNumber, bytes, names, capturePath: frame.Depth <= options.BlockDepth || bytes - frame.StartBytes >= options.OversizedBlockBytes);

                    if (frame.Depth <= options.BlockDepth && blocks.Count < options.BlockBudget)
                    {
                        blocks.Add(block);
                    }

                    if (frame.Depth > 0 && block.ByteLength >= options.OversizedBlockBytes && oversized.Count < options.OversizedBlockBudget)
                    {
                        oversized.Add(block);
                    }

                    names.RemoveAt(names.Count - 1);

                    if (frames.Count > 0)
                    {
                        var parent = frames[^1];
                        parent.PairCount += frame.PairCount + 0;
                        parent.NodeCount += frame.NodeCount + 1;
                    }

                    break;
                }

                case SfsLineKind.KeyValue:
                {
                    if (pendingName is not null)
                    {
                        AddIssue($"第 {pendingLine} 行不是块名（后面没有左花括号）", issues);
                        pendingName = null;
                    }

                    profile.PairCount++;
                    if (frames.Count > 0)
                    {
                        var frame = frames[^1];
                        frame.PairCount++;
                        if (string.IsNullOrWhiteSpace(frame.DisplayName)
                            && token.Key.Equals("name", StringComparison.OrdinalIgnoreCase)
                            && token.Value.Length > 0)
                        {
                            frame.DisplayName = token.Value.Trim();
                        }
                    }

                    if (depth <= options.FieldDepth && fields.Count < options.FieldBudget)
                    {
                        fields.Add(new SfsStreamField(BuildPath(names), token.Key, Truncate(token.Value, 512), lineNumber, depth));
                    }

                    break;
                }
            }
        }

        if (pendingName is not null)
        {
            AddIssue($"第 {pendingLine} 行不是块名（后面没有左花括号）", issues);
        }

        while (frames.Count > 0)
        {
            var frame = frames[^1];
            frames.RemoveAt(frames.Count - 1);
            AddIssue($"第 {frame.Line} 行的块「{frame.Name}」没有闭合", issues);
        }

        profile.LineCount = lineNumber;
        profile.MaxDepth = maxDepth;
        profile.HasErrors = issues.Count > 0;
        profile.Truncated = fields.Count >= options.FieldBudget
            || blocks.Count >= options.BlockBudget
            || oversized.Count >= options.OversizedBlockBudget;
        profile.Issues = issues;
        profile.Fields = fields;
        profile.Blocks = blocks.OrderBy(block => block.Line).ToList();
        profile.OversizedBlocks = SortOversized(oversized);
        foreach (var pair in scaleCounts)
        {
            if (pair.Value > 0)
            {
                nameCounts[pair.Key] = pair.Value;
            }
        }

        profile.NodeNameCounts = nameCounts;
        return profile;
    }

    /// <summary>流式搜索：按行匹配（空格分词全命中），命中行不需要完整解析。</summary>
    public static IReadOnlyList<SfsStreamHit> Search(
        string path,
        string query,
        int limit = 200,
        CancellationToken cancellationToken = default,
        Func<string, bool>? keyMatch = null,
        Func<string, bool>? blockMatch = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        limit = Math.Clamp(limit <= 0 ? 200 : limit, 1, 2000);
        var tokens = (query ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var hits = new List<SfsStreamHit>();
        if (tokens.Length == 0)
        {
            return hits;
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
        using var reader = new StreamReader(stream, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true, bufferSize: 1 << 16);
        var lineNumber = 0;
        string? raw;
        while ((raw = reader.ReadLine()) is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lineNumber++;
            if (raw.Length == 0)
            {
                continue;
            }

            var matched = ContainsAll(raw, tokens);
            SfsLineToken? token = null;
            if (!matched && (keyMatch is not null || blockMatch is not null))
            {
                token = SfsLexer.Lex(raw);
                matched = token.Kind switch
                {
                    SfsLineKind.KeyValue => keyMatch?.Invoke(token.Key) ?? false,
                    SfsLineKind.Name => blockMatch?.Invoke(token.Name) ?? false,
                    SfsLineKind.OpenBrace => token.Name.Length > 0 && (blockMatch?.Invoke(token.Name) ?? false),
                    _ => false,
                };
            }

            if (!matched)
            {
                continue;
            }

            token ??= SfsLexer.Lex(raw);
            var isPair = token.Kind == SfsLineKind.KeyValue;
            hits.Add(new SfsStreamHit(
                lineNumber,
                Truncate(raw.Trim(), 400),
                isPair ? token.Key : string.Empty,
                isPair ? Truncate(token.Value, 400) : string.Empty,
                isPair));
            if (hits.Count >= limit)
            {
                break;
            }
        }

        return hits;
    }

    /// <summary>流式展开某个块：只抓它的直接键值与直接子块，不建全树。</summary>
    /// <summary>
    /// 懒展开一个块：只抓它的直接键值和直接子块，扫到目标块闭合就停。
    /// 深层内容继续点下一层再展开，因此百 MB 文件也能按需下钻。
    /// </summary>
    public static SfsStreamDetail? Describe(string path, int line, SfsStreamOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        options ??= SfsStreamOptions.Default;

        var frames = new List<Frame>();
        var names = new List<string>();
        var fields = new List<SfsStreamField>();
        var children = new List<SfsStreamBlock>();
        var lineNumber = 0;
        var targetDepth = -1;
        Frame? targetFrame = null;
        var targetPath = string.Empty;
        var truncated = false;
        var closed = false;
        string? pendingName = null;
        var pendingLine = 0;

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
        using var reader = new StreamReader(stream, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true, bufferSize: 1 << 16);

        string? raw;
        while ((raw = reader.ReadLine()) is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lineNumber++;
            var token = SfsLexer.Lex(raw);
            var depth = frames.Count;

            if (token.Kind == SfsLineKind.Name)
            {
                pendingName = token.Name;
                pendingLine = lineNumber;
                continue;
            }

            if (token.Kind == SfsLineKind.OpenBrace)
            {
                var usePending = pendingName is not null && token.Name.Length == 0;
                var name = usePending ? pendingName! : token.Name;
                var nodeLine = usePending ? pendingLine : lineNumber;
                pendingName = null;
                names.Add(name);

                var isTarget = targetDepth < 0 && (lineNumber == line || nodeLine == line);
                frames.Add(new Frame { Name = name, NameIndex = names.Count - 1, Line = nodeLine, Depth = depth, StartBytes = 0 });

                if (isTarget)
                {
                    targetDepth = depth;
                    targetFrame = frames[^1];
                    targetPath = BuildPath(names);
                }

                continue;
            }

            if (token.Kind == SfsLineKind.CloseBrace)
            {
                if (frames.Count == 0)
                {
                    if (targetDepth >= 0)
                    {
                        closed = true;
                        break;
                    }

                    continue;
                }

                var frame = frames[^1];
                frames.RemoveAt(frames.Count - 1);

                if (targetDepth >= 0 && frame.Depth == targetDepth + 1)
                {
                    // 目标块的直接子块：路径要在名字出栈前算好。
                    children.Add(new SfsStreamBlock
                    {
                        Path = BuildPath(names),
                        Name = frame.Name,
                        Line = frame.Line,
                        EndLine = lineNumber,
                        Depth = frame.Depth,
                        PairCount = frame.PairCount,
                        NodeCount = frame.NodeCount,
                        ByteLength = 0,
                        Summary = FormatSummary(frame),
                    });
                }

                names.RemoveAt(names.Count - 1);

                if (frames.Count > 0)
                {
                    var parent = frames[^1];
                    parent.PairCount += frame.PairCount;
                    parent.NodeCount += frame.NodeCount + 1;
                }

                if (targetDepth >= 0 && frame.Depth == targetDepth)
                {
                    closed = true;
                    break;
                }

                continue;
            }

            if (token.Kind == SfsLineKind.KeyValue)
            {
                if (frames.Count > 0)
                {
                    var parent = frames[^1];
                    parent.PairCount++;
                    if (string.IsNullOrWhiteSpace(parent.DisplayName)
                        && token.Key.Equals("name", StringComparison.OrdinalIgnoreCase)
                        && token.Value.Length > 0)
                    {
                        parent.DisplayName = token.Value.Trim();
                    }
                }

                if (targetDepth >= 0 && depth == targetDepth + 1)
                {
                    if (fields.Count >= options.FieldBudget)
                    {
                        truncated = true;
                    }
                    else
                    {
                        fields.Add(new SfsStreamField(BuildPath(names), token.Key, Truncate(token.Value, 512), lineNumber, depth));
                    }
                }

                continue;
            }

            if (targetDepth < 0 && lineNumber > line + 4096)
            {
                // 目标行根本不存在时不要白扫整个大文件。
                break;
            }
        }

        if (targetDepth < 0)
        {
            return null;
        }

        return new SfsStreamDetail
        {
            Path = targetPath,
            Name = targetFrame?.Name ?? string.Empty,
            Line = targetFrame?.Line ?? line,
            EndLine = closed ? lineNumber : 0,
            Truncated = truncated || !closed,
            Fields = fields,
            Nodes = children,
        };
    }

    private static SfsStreamBlock BuildBlock(Frame frame, int endLine, long bytes, List<string> names, bool capturePath) => new()
    {
        Path = capturePath ? BuildPath(names) : string.Empty,
        Name = frame.Name,
        Line = frame.Line,
        EndLine = endLine,
        Depth = frame.Depth,
        PairCount = frame.PairCount,
        NodeCount = frame.NodeCount,
        ByteLength = Math.Max(0, bytes - frame.StartBytes),
        Summary = FormatSummary(frame),
    };

    private static string FormatSummary(Frame frame)
    {
        var summary = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{frame.PairCount} 键值 · {frame.NodeCount} 块");
        return string.IsNullOrWhiteSpace(frame.DisplayName) ? summary : $"{frame.DisplayName} · {summary}";
    }

    private static IReadOnlyList<SfsStreamBlock> SortOversized(List<SfsStreamBlock> blocks)
    {
        blocks.Sort(static (left, right) => right.ByteLength.CompareTo(left.ByteLength));
        return blocks;
    }

    private static string BuildPath(List<string> names)
        => names.Count == 0 ? string.Empty : string.Join('/', names);

    private static void AddIssue(string message, List<string> issues)
    {
        if (issues.Count < 32)
        {
            issues.Add(message);
        }
    }

    private static bool ContainsAll(string haystack, string[] tokens)
    {
        foreach (var token in tokens)
        {
            if (haystack.IndexOf(token, StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }
        }

        return true;
    }

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max] + "…";

    private sealed class Frame
    {
        public string Name { get; init; } = string.Empty;

        public string? DisplayName { get; set; }

        public int NameIndex { get; init; }

        public int Line { get; init; }

        public int Depth { get; init; }

        public long StartBytes { get; init; }

        public int PairCount { get; set; }

        public int NodeCount { get; set; }
    }
}