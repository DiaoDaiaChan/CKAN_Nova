using System.Globalization;

namespace CkanUI.Sfs;

/// <summary>流式大纲：页首常用项 + 分区列表，形状与完整解析的大纲一致，供百 MB 存档复用同一套界面。</summary>
public sealed class SfsStreamOutlineResult
{
    public required SfsOutlineSnapshot Snapshot { get; init; }

    public required IReadOnlyList<SfsOutlineSection> Sections { get; init; }

    /// <summary>大到不能当普通块展示的块（多为 mod 私有数据）。</summary>
    public IReadOnlyList<SfsStreamBlock> OversizedBlocks { get; init; } = [];
}

/// <summary>
/// 把 <see cref="SfsStreamProfile"/> 投成 <see cref="SfsOutlineSnapshot"/> + 分区。
/// 只依赖词表规则（<see cref="SfsVocabulary"/>），未知键照常出现，只是没有中文标签。
/// 深层内容不在这里展开——GUI 展开某个块时再调 <see cref="SfsStreamScanner.Describe"/>。
/// </summary>
public static class SfsStreamOutline
{
    public static SfsStreamOutlineResult Build(
        SfsStreamProfile profile,
        SfsVocabulary? vocabulary = null,
        SfsOutlineOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        vocabulary ??= SfsVocabulary.Default;
        options ??= SfsOutlineOptions.Default;

        var sections = new List<SfsOutlineSection>();
        var highlights = new List<SfsOutlineField>();
        var order = 0;

        var hasRoot = !string.IsNullOrEmpty(profile.RootName);
        var rootDepth = hasRoot ? 1 : 0;
        var rootFields = new List<SfsOutlineField>();
        var rootTruncated = false;
        foreach (var field in profile.Fields)
        {
            if (field.Depth != rootDepth)
            {
                continue;
            }

            if (rootFields.Count >= options.MaxFieldsPerSection)
            {
                rootTruncated = true;
                break;
            }

            var mapped = ToField(field, hasRoot ? "game" : "header", vocabulary);
            rootFields.Add(mapped);
            AddHighlight(highlights, mapped, options);
        }

        sections.Add(new SfsOutlineSection
        {
            Id = hasRoot ? "game" : "header",
            Title = hasRoot ? "存档信息" : "文件内容",
            Subtitle = profile.RootName ?? string.Empty,
            Kind = hasRoot ? SfsSectionKind.Game : SfsSectionKind.FileHeader,
            Rank = 0,
            Order = order++,
            Fields = rootFields,
            Nodes = [],
            Truncated = rootTruncated,
            TotalPairCount = rootFields.Count,
        });

        var scenarioIndex = 0;
        var topLevel = profile.Blocks
            .Where(block => block.Depth == rootDepth)
            .OrderBy(block => block.Line)
            .ToList();

        foreach (var block in topLevel)
        {
            if (sections.Count >= options.MaxSections)
            {
                break;
            }

            var isScenario = block.Name.Equals(SfsValidator.ScenarioNodeName, StringComparison.OrdinalIgnoreCase);
            // 同名兄弟块（SCENARIO）共用同一条 Path，必须再用行区间区分，否则每个分区都会列出全部兄弟的字段。
            var scenarioName = isScenario ? FindDirectValue(profile, block, "name") : null;
            var rule = vocabulary.MatchSection(block.Name, scenarioName);
            var kind = block.Name.ToUpperInvariant() switch
            {
                "FLIGHTSTATE" => SfsSectionKind.Flight,
                "ROSTER" => SfsSectionKind.Roster,
                "MESSAGESYSTEM" => SfsSectionKind.Messages,
                "SCENARIO" => SfsSectionKind.Scenario,
                _ => SfsSectionKind.Other,
            };
            var id = kind == SfsSectionKind.Scenario
                ? string.Create(CultureInfo.InvariantCulture, $"scenario:{scenarioIndex++}:{scenarioName}")
                : "node:" + block.Line.ToString(CultureInfo.InvariantCulture);

            var fields = new List<SfsOutlineField>();
            var sectionTruncated = false;
            foreach (var field in profile.Fields)
            {
                if (field.Depth != block.Depth + 1
                    || !string.Equals(field.Path, block.Path, StringComparison.Ordinal)
                    || field.Line <= block.Line
                    || (block.EndLine > 0 && field.Line >= block.EndLine))
                {
                    continue;
                }

                if (fields.Count >= options.MaxFieldsPerSection)
                {
                    sectionTruncated = true;
                    break;
                }

                var mapped = ToField(field, id, vocabulary);
                fields.Add(mapped);
                AddHighlight(highlights, mapped, options);
            }

            var nodes = new List<SfsOutlineNodeRef>();
            var childOrdinals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var child in profile.Blocks
                .Where(candidate => candidate.Depth == block.Depth + 1
                    && IsDirectChild(candidate.Path, block.Path)
                    && candidate.Line > block.Line
                    && (block.EndLine <= 0 || candidate.EndLine <= block.EndLine))
                .OrderBy(candidate => candidate.Line))
            {
                if (nodes.Count >= options.MaxNodeRefsPerSection)
                {
                    sectionTruncated = true;
                    break;
                }

                var ordinal = childOrdinals.TryGetValue(child.Name, out var seen) ? seen : 0;
                childOrdinals[child.Name] = ordinal + 1;
                nodes.Add(ToNodeRef(child, id, kind, ordinal, vocabulary));
            }

            sections.Add(new SfsOutlineSection
            {
                Id = id,
                Title = rule?.Title ?? block.Name,
                Subtitle = string.IsNullOrEmpty(scenarioName) ? block.Name : scenarioName,
                Kind = kind,
                Rank = rule?.Rank ?? 500,
                Order = order++,
                Fields = fields,
                Nodes = nodes,
                Truncated = sectionTruncated,
                TotalPairCount = block.PairCount,
                TotalNodeCount = block.NodeCount,
            });
        }

        var ordered = sections.OrderBy(static section => section.Rank).ThenBy(static section => section.Order).ToArray();
        var snapshot = new SfsOutlineSnapshot
        {
            SaveName = FindValue(rootFields, "Title") ?? FindValue(rootFields, "name"),
            Version = FindValue(rootFields, "version"),
            Mode = FindValue(rootFields, "mode"),
            Funds = FindField(rootFields, "funds", highlights),
            Science = FindField(rootFields, "sci", highlights),
            Reputation = FindField(rootFields, "rep", highlights),
            GameTime = FindField(rootFields, "UT", highlights),
            Highlights = [.. highlights.OrderBy(static field => field.Priority)],
            VesselCount = Count(profile, "VESSEL"),
            PartCount = Count(profile, "PART"),
            ScenarioCount = Count(profile, "SCENARIO"),
            CrewCount = Count(profile, "KERBAL"),
            PairCount = profile.PairCount,
            NodeCount = profile.NodeCount,
            LineCount = profile.LineCount,
            Truncated = profile.Truncated,
        };

        return new SfsStreamOutlineResult
        {
            Snapshot = snapshot,
            Sections = ordered,
            OversizedBlocks = profile.OversizedBlocks,
        };
    }

    private static void AddHighlight(List<SfsOutlineField> highlights, SfsOutlineField field, SfsOutlineOptions options)
    {
        if (!field.IsHighlighted || highlights.Count >= options.MaxHighlightFields)
        {
            return;
        }

        highlights.Add(field);
    }

    private static SfsOutlineField? FindField(
        IReadOnlyList<SfsOutlineField> fields,
        string key,
        IReadOnlyList<SfsOutlineField> highlights)
    {
        foreach (var field in fields)
        {
            if (string.Equals(field.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                return field;
            }
        }

        foreach (var field in highlights)
        {
            if (string.Equals(field.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                return field;
            }
        }

        return null;
    }

    private static string? FindValue(IReadOnlyList<SfsOutlineField> fields, string key)
    {
        foreach (var field in fields)
        {
            if (string.Equals(field.Key, key, StringComparison.OrdinalIgnoreCase) && field.Value.Length > 0)
            {
                return field.Value;
            }
        }

        return null;
    }

    /// <summary>取某个块自己的直接字段：同名兄弟块共用 Path，用行区间隔离。</summary>
    private static string? FindDirectValue(SfsStreamProfile profile, SfsStreamBlock block, string key)
    {
        foreach (var field in profile.Fields)
        {
            if (field.Depth != block.Depth + 1
                || !string.Equals(field.Path, block.Path, StringComparison.Ordinal)
                || !string.Equals(field.Key, key, StringComparison.OrdinalIgnoreCase)
                || field.Line <= block.Line
                || (block.EndLine > 0 && field.Line >= block.EndLine))
            {
                continue;
            }

            return field.Value;
        }

        return null;
    }

    private static int Count(SfsStreamProfile profile, string name)
        => profile.NodeNameCounts.TryGetValue(name, out var count) ? count : 0;

    private static bool IsDirectChild(string path, string parent)
        => path.Length > parent.Length + 1
            && path.StartsWith(parent, StringComparison.Ordinal)
            && path[parent.Length] == '/'
            && path.IndexOf('/', parent.Length + 1) < 0;

    private static SfsOutlineField ToField(SfsStreamField field, string sectionId, SfsVocabulary vocabulary)
    {
        var rule = vocabulary.MatchKey(field.Key);
        return new SfsOutlineField
        {
            Path = field.Path,
            Key = field.Key,
            Label = rule?.Label ?? field.Key,
            Value = field.Value,
            Line = field.Line,
            Kind = rule?.Kind ?? SfsVocabulary.InferKind(field.Value),
            Priority = rule?.Priority ?? 100,
            IsKnown = rule is not null,
            Unit = rule?.Unit,
            Group = rule?.Group,
            SectionId = sectionId,
        };
    }

    private static SfsOutlineNodeRef ToNodeRef(
        SfsStreamBlock block,
        string sectionId,
        SfsSectionKind kind,
        int ordinal,
        SfsVocabulary vocabulary)
    {
        var rule = vocabulary.MatchSection(block.Name);
        return new SfsOutlineNodeRef
        {
            Path = string.Create(CultureInfo.InvariantCulture, $"{block.Path}#{ordinal}"),
            Name = block.Name,
            Line = block.Line,
            Label = rule?.Title ?? string.Empty,
            Summary = block.Summary,
            Depth = block.Depth,
            ChildCount = block.NodeCount,
            PairCount = block.PairCount,
            NodeCount = block.NodeCount,
            ByteLength = block.ByteLength,
            EndLine = block.EndLine,
            Kind = kind,
            SectionId = sectionId,
        };
    }
}