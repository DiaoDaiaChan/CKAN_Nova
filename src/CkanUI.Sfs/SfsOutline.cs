using System.Globalization;

namespace CkanUI.Sfs;

/// <summary>大纲里的块类别，决定 GUI 的分栏与图标。</summary>
public enum SfsSectionKind
{
    FileHeader = 0,
    Game = 1,
    Flight = 2,
    Scenario = 3,
    Roster = 4,
    Messages = 5,
    Other = 6,
}

/// <summary>大纲预算：超出预算的内容只统计不展开（超大整合包一个 SCENARIO 能有几十万行）。</summary>
public sealed class SfsOutlineOptions
{
    public static SfsOutlineOptions Default { get; } = new();

    public int MaxFieldsPerSection { get; init; } = 400;

    public int MaxNodeRefsPerSection { get; init; } = 400;

    public int MaxSections { get; init; } = 200;

    public int MaxHighlightFields { get; init; } = 16;
}

/// <summary>可直接编辑的键值字段（值保持字面量，不投影成数字）。</summary>
public sealed record SfsOutlineField
{
    public required string Path { get; init; }

    public required string Key { get; init; }

    public required string Label { get; init; }

    public required string Value { get; init; }

    public required int Line { get; init; }

    public SfsFieldKind Kind { get; init; }

    public int Priority { get; init; } = 100;

    public bool IsKnown { get; init; }

    public string? Unit { get; init; }

    public string? Group { get; init; }

    public string SectionId { get; init; } = string.Empty;

    /// <summary>展示用：已知键用标签，未知键退回键名（不隐藏、不改写）。</summary>
    public string DisplayName => IsKnown ? Label : Key;

    /// <summary>金额/时间这类值的高亮权重：越小越靠前。</summary>
    public bool IsHighlighted => IsKnown && Priority <= 5;

    public bool TryGetNumber(out double value)
        => double.TryParse(Value, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
}

/// <summary>块引用：树上的一行，展开时再调用 <see cref="SfsOutline.DescribeNode"/> 取子项。</summary>
public sealed record SfsOutlineNodeRef
{
    public required string Path { get; init; }

    public required string Name { get; init; }

    public required int Line { get; init; }

    public string Label { get; init; } = string.Empty;

    public string? Summary { get; init; }

    public int Depth { get; init; }

    public int ChildCount { get; init; }

    public int PairCount { get; init; }

    public int NodeCount { get; init; }

    /// <summary>块在原文里占的 UTF-8 字节数（含缩进与行尾；首尾行缺失时为 0）。</summary>
    public long ByteLength { get; init; }

    /// <summary>块的结束行号（1 起算；未闭合时为 0）。</summary>
    public int EndLine { get; init; }

    public SfsSectionKind Kind { get; init; }

    public string SectionId { get; init; } = string.Empty;

    public string DisplayName => string.IsNullOrEmpty(Label) ? Name : Label;
}

/// <summary>一个大纲分区（存档信息 / 飞行器 / 每个 SCENARIO / 其余块）。</summary>
public sealed class SfsOutlineSection
{
    public required string Id { get; init; }

    public required string Title { get; init; }

    public string Subtitle { get; init; } = string.Empty;

    public SfsSectionKind Kind { get; init; }

    public int Rank { get; init; } = 500;

    public int Order { get; init; }

    public IReadOnlyList<SfsOutlineField> Fields { get; init; } = [];

    public IReadOnlyList<SfsOutlineNodeRef> Nodes { get; init; } = [];

    public bool Truncated { get; init; }

    public int TotalPairCount { get; init; }

    public int TotalNodeCount { get; init; }
}

/// <summary>页首"常用内容"卡片：资金/科技/声望/时间 + 存档标识 + 规模统计。</summary>
public sealed class SfsOutlineSnapshot
{
    public string? SaveName { get; init; }

    public string? Version { get; init; }

    public string? Mode { get; init; }

    public SfsOutlineField? Funds { get; init; }

    public SfsOutlineField? Science { get; init; }

    public SfsOutlineField? Reputation { get; init; }

    public SfsOutlineField? GameTime { get; init; }

    public IReadOnlyList<SfsOutlineField> Highlights { get; init; } = [];

    public int VesselCount { get; init; }

    public int PartCount { get; init; }

    public int ScenarioCount { get; init; }

    public int CrewCount { get; init; }

    public int PairCount { get; init; }

    public int NodeCount { get; init; }

    public int LineCount { get; init; }

    /// <summary>是否因为预算而"只统计不展开"。</summary>
    public bool Truncated { get; init; }
}

/// <summary>懒展开结果：任意块节点的直接子项。</summary>
public sealed class SfsNodeDetail
{
    public required string Path { get; init; }

    public required string Name { get; init; }

    public required string Title { get; init; }

    public int Line { get; init; }

    public IReadOnlyList<SfsOutlineField> Fields { get; init; } = [];

    public IReadOnlyList<SfsOutlineNodeRef> Nodes { get; init; } = [];

    public bool Truncated { get; init; }
}

/// <summary>
/// 语义投影（13 篇 SFS-05）：把 <see cref="SfsDocument"/> 变成"人能读、键能改"的层级视图。
/// 规则只有两条：**已知键用词表排前面**，**未知内容原样保留、按原顺序排在后面**。
/// 因此整合包塞进来的任何字段都不会丢，只是没有中文标签。
/// </summary>
public sealed class SfsOutline
{
    private readonly Dictionary<int, string> _paths;
    private readonly Dictionary<int, SfsSectionKind> _kinds;

    private SfsOutline(
        SfsDocument document,
        SfsVocabulary vocabulary,
        SfsOutlineOptions options,
        SfsOutlineSnapshot snapshot,
        IReadOnlyList<SfsOutlineSection> sections,
        Dictionary<int, string> paths,
        Dictionary<int, SfsSectionKind> kinds,
        bool truncated)
    {
        Document = document;
        Vocabulary = vocabulary;
        Options = options;
        Snapshot = snapshot;
        Sections = sections;
        _paths = paths;
        _kinds = kinds;
        Truncated = truncated;
    }

    public SfsDocument Document { get; }

    public SfsVocabulary Vocabulary { get; }

    public SfsOutlineOptions Options { get; }

    public SfsOutlineSnapshot Snapshot { get; }

    public IReadOnlyList<SfsOutlineSection> Sections { get; }

    public bool Truncated { get; }

    public SfsOutlineField? FindField(int lineNumber, params SfsOutlineField[] extra)
    {
        foreach (var field in extra)
        {
            if (field.Line == lineNumber)
            {
                return field;
            }
        }

        foreach (var section in Sections)
        {
            foreach (var field in section.Fields)
            {
                if (field.Line == lineNumber)
                {
                    return field;
                }
            }
        }

        return null;
    }

    /// <summary>搜索：空格分隔多词，全部命中（路径 / 键 / 标签 / 值）才算命中；返回字段与块引用。</summary>
    public SfsOutlineSearchResult Search(string? query, int limit = 200)
    {
        var fields = new List<SfsOutlineField>();
        var nodes = new List<SfsOutlineNodeRef>();
        var tokens = (query ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (tokens.Length == 0)
        {
            return new SfsOutlineSearchResult(fields, nodes, 0);
        }

        var seenNodes = new HashSet<int>();

        bool Matches(string haystack)
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

        void ConsiderNode(SfsOutlineNodeRef node)
        {
            if (fields.Count >= limit)
            {
                return;
            }

            if (seenNodes.Add(node.Line) && Matches(string.Concat(node.Path, " ", node.Name, " ", node.Label, " ", node.Summary)))
            {
                nodes.Add(node);
            }
        }

        foreach (var section in Sections)
        {
            foreach (var field in section.Fields)
            {
                if (fields.Count >= limit)
                {
                    break;
                }

                if (Matches(string.Concat(field.Path, " ", field.Key, " ", field.Label, " ", field.Value)))
                {
                    fields.Add(field);
                }
            }

            foreach (var node in section.Nodes)
            {
                ConsiderNode(node);
            }
        }

        return new SfsOutlineSearchResult(fields, nodes, fields.Count + nodes.Count);
    }

    /// <summary>懒展开一个块：返回它的直接键值与子块引用。行号不是块时返回 null。</summary>
    public SfsNodeDetail? DescribeNode(int lineNumber)
    {
        var node = FindNode(lineNumber);
        if (node is null)
        {
            return null;
        }

        var basePath = _paths.TryGetValue(lineNumber, out var stored) ? stored : node.Name;
        var kind = _kinds.TryGetValue(lineNumber, out var storedKind) ? storedKind : SfsSectionKind.Other;
        var scan = DescribeChildren(node, basePath, kind, Options, Vocabulary, Document);
        var fields = scan.Fields;
        var nodes = scan.Nodes;
        var truncated = scan.Truncated;
        var title = node.Name;
        var namePair = node.FindPair("name") ?? node.FindPair("vesselName") ?? node.FindPair("Title");
        if (namePair is not null && namePair.Value.Length > 0)
        {
            title = string.Concat(node.Name, " · ", namePair.Value);
        }

        return new SfsNodeDetail
        {
            Path = basePath,
            Name = node.Name,
            Title = title,
            Line = node.LineNumber,
            Fields = fields,
            Nodes = nodes,
            Truncated = truncated,
        };
    }

    private SfsNode? FindNode(int lineNumber)
    {
        SfsNode? Search(IReadOnlyList<SfsElement> elements)
        {
            foreach (var element in elements)
            {
                if (element is SfsNode node)
                {
                    if (node.LineNumber == lineNumber)
                    {
                        return node;
                    }

                    if (lineNumber >= node.LineNumber && lineNumber <= node.EndLineNumber)
                    {
                        var found = Search(node.Children);
                        if (found is not null)
                        {
                            return found;
                        }
                    }
                }
            }

            return null;
        }

        return Search(Document.Elements);
    }

    /// <summary>构建大纲。文档本身必须已经解析成功（有解析错误时仍可构建，只是标记 Truncated/统计不可信）。</summary>
    public static SfsOutline Build(SfsDocument document, SfsVocabulary? vocabulary = null, SfsOutlineOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        vocabulary ??= SfsVocabulary.Default;
        options ??= SfsOutlineOptions.Default;

        var sections = new List<SfsOutlineSection>();
        var paths = new Dictionary<int, string>();
        var kinds = new Dictionary<int, SfsSectionKind>();
        var highlights = new List<SfsOutlineField>();
        var highlightKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var order = 0;
        var truncated = false;

        var root = document.Root;
        var counters = CountScale(document);

        void AddHighlight(SfsOutlineField field)
        {
            if (highlights.Count >= options.MaxHighlightFields)
            {
                return;
            }

            if (field.IsHighlighted && highlightKeys.Add(field.Key))
            {
                highlights.Add(field);
            }
        }

        SfsOutlineSection BuildSection(string id, string title, string subtitle, SfsSectionKind kind, int rank, SfsNode? node, IReadOnlyList<SfsOutlineField>? presetFields = null)
        {
            var fields = presetFields is null ? new List<SfsOutlineField>() : [.. presetFields];
            var nodes = new List<SfsOutlineNodeRef>();
            var sectionTruncated = false;
            var pairCount = 0;
            var nodeCount = 0;

            if (node is not null)
            {
                var path = paths.TryGetValue(node.LineNumber, out var existing) ? existing : node.Name;
                var described = DescribeChildren(node, path, kind, options, vocabulary, document);
                pairCount = CountPairs(node);
                nodeCount = CountNodes(node);
                sectionTruncated = described.Truncated;
                foreach (var field in described.Fields)
                {
                    if (fields.Count >= options.MaxFieldsPerSection)
                    {
                        sectionTruncated = true;
                        break;
                    }

                    fields.Add(field with { SectionId = id });
                    AddHighlight(field);
                }

                foreach (var child in described.Nodes)
                {
                    if (nodes.Count >= options.MaxNodeRefsPerSection)
                    {
                        sectionTruncated = true;
                        break;
                    }

                    nodes.Add(child with { SectionId = id });
                    paths[child.Line] = child.Path;
                    kinds[child.Line] = kind;
                }
            }

            return new SfsOutlineSection
            {
                Id = id,
                Title = title,
                Subtitle = subtitle,
                Kind = kind,
                Rank = rank,
                Order = order++,
                Fields = fields,
                Nodes = nodes,
                Truncated = sectionTruncated,
                TotalPairCount = pairCount,
                TotalNodeCount = nodeCount,
            };
        }

        // 1) 存档根（GAME）自身的键值 = 存档信息。
        if (root is not null)
        {
            paths[root.LineNumber] = root.Name;
            kinds[root.LineNumber] = SfsSectionKind.Game;
            var rootFields = new List<SfsOutlineField>();
            foreach (var pair in root.Pairs())
            {
                if (rootFields.Count >= options.MaxFieldsPerSection)
                {
                    truncated = true;
                    break;
                }

                var field = ToField(pair, root.Name, SfsSectionKind.Game, vocabulary, "game");
                rootFields.Add(field);
                AddHighlight(field);
            }

            sections.Add(new SfsOutlineSection
            {
                Id = "game",
                Title = "存档信息",
                Subtitle = root.Name,
                Kind = SfsSectionKind.Game,
                Rank = 0,
                Order = order++,
                Fields = rootFields,
                Nodes = [],
                TotalPairCount = rootFields.Count,
                TotalNodeCount = 0,
            });

            // 2) GAME 下的每个块 = 一个分区；SCENARIO 一个一行（按场景名排序）。
            var scenarioIndex = 0;
            foreach (var child in root.Children)
            {
                if (child is not SfsNode node)
                {
                    continue;
                }

                if (sections.Count >= options.MaxSections)
                {
                    truncated = true;
                    break;
                }

                var scenarioName = node.Name.Equals(SfsValidator.ScenarioNodeName, StringComparison.OrdinalIgnoreCase)
                    ? node.FindPair("name")?.Value
                    : null;
                var rule = vocabulary.MatchSection(node.Name, scenarioName);
                var kind = node.Name.ToUpperInvariant() switch
                {
                    "FLIGHTSTATE" => SfsSectionKind.Flight,
                    "ROSTER" => SfsSectionKind.Roster,
                    "MESSAGESYSTEM" => SfsSectionKind.Messages,
                    "SCENARIO" => SfsSectionKind.Scenario,
                    _ => SfsSectionKind.Other,
                };

                var id = kind == SfsSectionKind.Scenario
                    ? string.Create(CultureInfo.InvariantCulture, $"scenario:{scenarioIndex++}:{scenarioName}")
                    : "node:" + node.LineNumber.ToString(CultureInfo.InvariantCulture);
                var title = rule?.Title ?? node.Name;
                var subtitle = scenarioName is { Length: > 0 } ? scenarioName : node.Name;
                var path = string.Create(CultureInfo.InvariantCulture, $"{root.Name}/{node.Name}#{ScenarioOrdinal(root, node)}");
                paths[node.LineNumber] = path;
                kinds[node.LineNumber] = kind;
                sections.Add(BuildSection(id, title, subtitle, kind, rule?.Rank ?? 500, node));
            }
        }
        else
        {
            // 没有根块（例如 .loadmeta）：顶层键值对单独成一段。
            var fields = new List<SfsOutlineField>();
            foreach (var element in document.Elements)
            {
                if (element is SfsPair pair)
                {
                    var field = ToField(pair, string.Empty, SfsSectionKind.FileHeader, vocabulary, "header");
                    fields.Add(field);
                    AddHighlight(field);
                }
            }

            sections.Add(new SfsOutlineSection
            {
                Id = "header",
                Title = "文件内容",
                Kind = SfsSectionKind.FileHeader,
                Rank = 0,
                Order = order++,
                Fields = fields,
                Nodes = [],
                TotalPairCount = fields.Count,
            });
        }

        var ordered = sections
            .OrderBy(static section => section.Rank)
            .ThenBy(static section => section.Order)
            .ToArray();

        var snapshot = new SfsOutlineSnapshot
        {
            SaveName = FindValue(highlights, "Title") ?? FindValue(ordered, "name"),
            Version = FindValue(highlights, "version"),
            Mode = RootValue(document, "Mode") ?? FindValue(highlights, "mode"),
            Funds = FindFieldByKey(highlights, "funds"),
            Science = FindFieldByKey(highlights, "sci"),
            Reputation = FindFieldByKey(highlights, "rep"),
            GameTime = FindFieldByKey(highlights, "UT"),
            Highlights = [.. highlights.OrderBy(static field => field.Priority)],
            VesselCount = counters.Vessels,
            PartCount = counters.Parts,
            ScenarioCount = counters.Scenarios,
            CrewCount = counters.Crew,
            PairCount = document.PairCount,
            NodeCount = document.NodeCount,
            LineCount = document.LineCount,
            Truncated = truncated,
        };

        return new SfsOutline(document, vocabulary, options, snapshot, ordered, paths, kinds, truncated);
    }

    private static int ScenarioOrdinal(SfsNode root, SfsNode target)
    {
        var ordinal = 0;
        foreach (var child in root.Children)
        {
            if (ReferenceEquals(child, target))
            {
                return ordinal;
            }

            if (child is SfsNode node && node.Name.Equals(target.Name, StringComparison.Ordinal))
            {
                ordinal++;
            }
        }

        return ordinal;
    }

    private static string? RootValue(SfsDocument document, string key)
    {
        if (document.Root is not { } root)
        {
            return null;
        }

        foreach (var element in root.Children)
        {
            if (element is SfsPair pair
                && pair.Key.Equals(key, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(pair.Value))
            {
                return pair.Value.Trim();
            }
        }

        return null;
    }

    private static string? FindValue(IReadOnlyList<SfsOutlineField> fields, string key)
    {
        foreach (var field in fields)
        {
            if (field.Key.Equals(key, StringComparison.OrdinalIgnoreCase) && field.Value.Length > 0)
            {
                return field.Value;
            }
        }

        return null;
    }

    private static string? FindValue(IReadOnlyList<SfsOutlineSection> sections, string key)
    {
        foreach (var section in sections)
        {
            var found = FindValue(section.Fields, key);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private static SfsOutlineField? FindFieldByKey(IReadOnlyList<SfsOutlineField> fields, string key)
    {
        foreach (var field in fields)
        {
            if (field.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                return field;
            }
        }

        return null;
    }

    private static SfsOutlineField ToField(SfsPair pair, string path, SfsSectionKind kind, SfsVocabulary vocabulary, string sectionId)
    {
        var rule = vocabulary.MatchKey(pair.Key);
        return new SfsOutlineField
        {
            Path = path,
            Key = pair.Key,
            Label = rule?.Label ?? pair.Key,
            Value = pair.Value,
            Line = pair.LineNumber,
            Kind = vocabulary.Classify(pair.Key, pair.Value),
            Priority = rule?.Priority ?? 100,
            IsKnown = rule is not null,
            Unit = rule?.Unit,
            Group = rule?.Group,
            SectionId = sectionId,
        };
    }

    private static ChildScan DescribeChildren(
        SfsNode node,
        string path,
        SfsSectionKind kind,
        SfsOutlineOptions options,
        SfsVocabulary? vocabulary = null,
        SfsDocument? document = null)
    {
        vocabulary ??= SfsVocabulary.Default;
        var fields = new List<SfsOutlineField>();
        var nodes = new List<SfsOutlineNodeRef>();
        var truncated = false;
        var pairCount = 0;
        var nodeCount = 0;
        var ordinal = 0;

        foreach (var child in node.Children)
        {
            switch (child)
            {
                case SfsPair pair:
                    pairCount++;
                    if (fields.Count < options.MaxFieldsPerSection)
                    {
                        fields.Add(ToField(pair, path, kind, vocabulary, string.Empty));
                    }
                    else
                    {
                        truncated = true;
                    }

                    break;

                case SfsNode childNode:
                {
                    nodeCount++;
                    var childOrdinal = ordinal++;
                    var childPath = string.Create(CultureInfo.InvariantCulture, $"{path}/{childNode.Name}#{childOrdinal}");
                    if (nodes.Count >= options.MaxNodeRefsPerSection)
                    {
                        truncated = true;
                        break;
                    }

                    nodes.Add(new SfsOutlineNodeRef
                    {
                        Path = childPath,
                        Name = childNode.Name,
                        Label = DescribeLabel(childNode, vocabulary),
                        Summary = Summarize(childNode, vocabulary),
                        Line = childNode.LineNumber,
                        Depth = childNode.Depth,
                        ChildCount = childNode.Children.Count,
                        PairCount = CountPairs(childNode),
                        NodeCount = CountNodes(childNode),
                        ByteLength = MeasureBytes(document, childNode),
                        EndLine = childNode.EndLineNumber,
                        Kind = kind,
                    });
                    break;
                }
            }
        }

        return new ChildScan(fields, nodes, truncated, pairCount, nodeCount);
    }

    private static long MeasureBytes(SfsDocument? document, SfsNode node)
    {
        if (document is null || node.EndLineNumber < node.LineNumber)
        {
            return 0;
        }

        var start = document.GetLineByteOffset(node.LineNumber);
        var end = node.EndLineNumber >= document.LineCount
            ? System.Text.Encoding.UTF8.GetByteCount(document.SourceText)
            : document.GetLineByteOffset(node.EndLineNumber + 1);
        return Math.Max(0, end - start);
    }

    private static string DescribeLabel(SfsNode node, SfsVocabulary vocabulary)
    {
        var namePair = node.FindPair("name") ?? node.FindPair("vesselName") ?? node.FindPair("part") ?? node.FindPair("Title");
        if (namePair is not null && namePair.Value.Length > 0)
        {
            var shortName = namePair.Value.Length > 64 ? namePair.Value[..64] + "…" : namePair.Value;
            return string.Concat(node.Name, " · ", shortName);
        }

        return node.Name;
    }

    private static string? Summarize(SfsNode node, SfsVocabulary vocabulary)
    {
        var parts = 0;
        var modules = 0;
        var crew = 0;
        var displayName = node.Children
            .OfType<SfsPair>()
            .FirstOrDefault(pair => pair.Key.Equals("name", StringComparison.OrdinalIgnoreCase) && pair.Value.Length > 0)
            ?.Value.Trim();
        foreach (var child in node.Children)
        {
            if (child is not SfsNode childNode)
            {
                continue;
            }

            if (childNode.Name.Equals("PART", StringComparison.OrdinalIgnoreCase))
            {
                parts++;
            }
            else if (childNode.Name.Equals("MODULE", StringComparison.OrdinalIgnoreCase))
            {
                modules++;
            }
            else if (childNode.Name.Equals("CREW", StringComparison.OrdinalIgnoreCase) || childNode.Name.Equals("KERBAL", StringComparison.OrdinalIgnoreCase))
            {
                crew++;
            }
        }

        if (parts == 0 && modules == 0 && crew == 0)
        {
            var count = CountPairs(node);
            if (count == 0)
            {
                return displayName;
            }

            var pairs = string.Create(CultureInfo.InvariantCulture, $"{count} 个键值");
            return string.IsNullOrWhiteSpace(displayName) ? pairs : $"{displayName} · {pairs}";
        }

        var summary = string.Create(CultureInfo.InvariantCulture, $"{parts} 部件 · {modules} 模块");
        if (crew > 0)
        {
            summary += string.Create(CultureInfo.InvariantCulture, $" · {crew} 乘员");
        }

        return string.IsNullOrWhiteSpace(displayName) ? summary : $"{displayName} · {summary}";
    }

    private static int CountPairs(SfsNode node)
    {
        var count = 0;
        var stack = new Stack<SfsNode>();
        stack.Push(node);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            foreach (var child in current.Children)
            {
                if (child is SfsPair)
                {
                    count++;
                }
                else if (child is SfsNode nested)
                {
                    stack.Push(nested);
                }
            }
        }

        return count;
    }

    private static int CountNodes(SfsNode node)
    {
        var count = 0;
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

    private sealed record ChildScan(
        List<SfsOutlineField> Fields,
        List<SfsOutlineNodeRef> Nodes,
        bool Truncated,
        int PairCount,
        int NodeCount);

    private readonly record struct Scale(int Vessels, int Parts, int Scenarios, int Crew);

    /// <summary>全树计数：只统计名字，不建任何中间集合，超大文档也只是多一遍线性扫描。</summary>
    private static Scale CountScale(SfsDocument document)
    {
        var vessels = 0;
        var parts = 0;
        var scenarios = 0;
        var crew = 0;
        var stack = new Stack<SfsElement>();
        for (var index = document.Elements.Count - 1; index >= 0; index--)
        {
            stack.Push(document.Elements[index]);
        }

        while (stack.Count > 0)
        {
            if (stack.Pop() is not SfsNode node)
            {
                continue;
            }

            switch (node.Name.ToUpperInvariant())
            {
                case "VESSEL":
                    vessels++;
                    break;
                case "PART":
                    parts++;
                    break;
                case "SCENARIO":
                    scenarios++;
                    break;
                case "KERBAL":
                    crew++;
                    break;
                default:
                    break;
            }

            for (var index = node.Children.Count - 1; index >= 0; index--)
            {
                stack.Push(node.Children[index]);
            }
        }

        return new Scale(vessels, parts, scenarios, crew);
    }
}

/// <summary>大纲搜索命中。</summary>
public sealed record SfsOutlineSearchResult(
    IReadOnlyList<SfsOutlineField> Fields,
    IReadOnlyList<SfsOutlineNodeRef> Nodes,
    int Total);