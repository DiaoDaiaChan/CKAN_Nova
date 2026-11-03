using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Sfs;

namespace CkanUI.Services.Saves;

/// <summary>
/// 存档工作台的服务层实现：解析 → 语义投影（SFS-05/06）→ 结构化编辑（SFS-07）。
/// 解析与写盘都复用 <see cref="SfsParser"/> / <see cref="SfsEditor"/> / <see cref="SaveFileWriter"/>，
/// 本类只负责线程切换、异常兜底和 DTO 映射，**不重复实现任何格式规则**。
/// </summary>
public sealed class SaveEditorService : ISaveEditorService
{
    private readonly long _fullParseLimitBytes;

    /// <summary>
    /// <paramref name="fullParseLimitBytes"/> 是“完整解析”的字节上限：超过就改走流式扫描 / 行级补丁，
    /// 不再建整棵语法树（整合包动辄几百 MB，完整解析会把内存打爆）。测试可注入很小的阈值。
    /// </summary>
    public SaveEditorService(long fullParseLimitBytes = SfsStreamScanner.FullParseLimitBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fullParseLimitBytes);
        _fullParseLimitBytes = fullParseLimitBytes;
    }

    public Task<OperationResult<SaveOutlineDto>> LoadOutlineAsync(string filePath, CancellationToken cancellationToken = default)
        => RunAsync(filePath, () =>
        {
            if (IsLargeFile(filePath))
            {
                return LoadStreamingOutline(filePath, cancellationToken);
            }

            var parse = SfsParser.ParseFile(filePath);
            var document = parse.Document;
            if (document is null)
            {
                return OperationResult<SaveOutlineDto>.Fail(
                    parse.HasErrors ? "存档解析失败" : "存档是空文件",
                    parse.Diagnostics.Count > 0 ? parse.Diagnostics[0].ToString() : null);
            }

            var outline = SfsOutline.Build(document);
            var snapshot = outline.Snapshot;
            var sections = new List<SaveOutlineSectionDto>(outline.Sections.Count);
            foreach (var section in outline.Sections)
            {
                sections.Add(MapSection(section));
            }

            var dto = new SaveOutlineDto
            {
                FilePath = filePath,
                SaveName = snapshot.SaveName ?? Path.GetFileName(Path.GetDirectoryName(filePath)) ?? "存档",
                Version = snapshot.Version ?? "?",
                Mode = snapshot.Mode ?? "?",
                Funds = DisplayOrDash(snapshot.Funds),
                Science = DisplayOrDash(snapshot.Science),
                Reputation = DisplayOrDash(snapshot.Reputation),
                GameTime = DisplayOrDash(snapshot.GameTime),
                VesselCount = snapshot.VesselCount,
                PartCount = snapshot.PartCount,
                ScenarioCount = snapshot.ScenarioCount,
                CrewCount = snapshot.CrewCount,
                PairCount = snapshot.PairCount,
                NodeCount = snapshot.NodeCount,
                LineCount = snapshot.LineCount,
                Truncated = snapshot.Truncated,
                Highlights = MapFields(snapshot.Highlights),
                Sections = sections,
            };

            return OperationResult<SaveOutlineDto>.Ok(dto);
        }, cancellationToken);

    public Task<OperationResult<IReadOnlyList<SaveOutlineMatchDto>>> SearchAsync(
        string filePath,
        string query,
        int limit = 200,
        CancellationToken cancellationToken = default)
        => RunAsync(filePath, () =>
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return OperationResult<IReadOnlyList<SaveOutlineMatchDto>>.Ok([]);
            }

            limit = Math.Clamp(limit <= 0 ? 200 : limit, 1, 2000);
            if (IsLargeFile(filePath))
            {
                return SearchStreaming(filePath, query, limit, cancellationToken);
            }

            var parse = SfsParser.ParseFile(filePath);
            var document = parse.Document;
            if (document is null)
            {
                return OperationResult<IReadOnlyList<SaveOutlineMatchDto>>.Fail("存档解析失败");
            }

            var outline = SfsOutline.Build(document);
            var sectionTitles = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var section in outline.Sections)
            {
                sectionTitles[section.Id] = section.Title;
            }

            var hits = new List<SaveOutlineMatchDto>();
            var seenLines = new HashSet<int>();

            // ① 语义命中：键的中文标签、展示名、值都能搜到（"资金" → funds）。
            var semantic = outline.Search(query, limit);
            foreach (var field in semantic.Fields)
            {
                if (!seenLines.Add(field.Line))
                {
                    continue;
                }

                hits.Add(new SaveOutlineMatchDto
                {
                    Line = field.Line,
                    SectionTitle = sectionTitles.TryGetValue(field.SectionId, out var title) ? title : "其他",
                    Path = field.Path,
                    Key = field.Key,
                    Label = field.DisplayName,
                    Value = field.Value,
                    DisplayValue = Display(field),
                    Match = "键值",
                });
            }

            foreach (var node in semantic.Nodes)
            {
                if (!seenLines.Add(node.Line))
                {
                    continue;
                }

                hits.Add(new SaveOutlineMatchDto
                {
                    Line = node.Line,
                    SectionTitle = sectionTitles.TryGetValue(node.SectionId, out var title) ? title : "其他",
                    Path = node.Path,
                    Key = node.Name,
                    Label = node.DisplayName,
                    Value = node.Summary ?? string.Empty,
                    DisplayValue = node.Summary ?? string.Empty,
                    Match = "块",
                });
            }

            // ② 原文命中：大纲有预算，整合包塞进来的深层行不在分区里；这里按行扫一遍补齐。
            var tokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var lines = document.SourceText.Split(['\r', '\n']);
            for (var index = 0; index < lines.Length && hits.Count < limit; index++)
            {
                var line = lines[index];
                if (line.Length == 0 || !ContainsAll(line, tokens))
                {
                    continue;
                }

                var lineNumber = index + 1;
                if (!seenLines.Add(lineNumber))
                {
                    continue;
                }

                hits.Add(new SaveOutlineMatchDto
                {
                    Line = lineNumber,
                    SectionTitle = "原文",
                    Path = string.Empty,
                    Key = string.Empty,
                    Label = string.Empty,
                    Value = line.Trim(),
                    DisplayValue = line.Trim(),
                    Match = "原文",
                });
            }

            // 高亮片段在这里切好：界面只负责把三段拼起来，不需要自己算下标。
            var token = PickHighlightToken(tokens, hits);
            var highlighted = new List<SaveOutlineMatchDto>(hits.Count);
            foreach (var hit in hits)
            {
                highlighted.Add(WithHighlight(hit, token));
            }

            return OperationResult<IReadOnlyList<SaveOutlineMatchDto>>.Ok(highlighted);
        }, cancellationToken);

    public Task<OperationResult<SaveNodeDetailDto>> DescribeNodeAsync(
        string filePath,
        int line,
        CancellationToken cancellationToken = default)
        => RunAsync(filePath, () =>
        {
            if (IsLargeFile(filePath))
            {
                return DescribeStreaming(filePath, line, cancellationToken);
            }

            var parse = SfsParser.ParseFile(filePath);
            if (parse.Document is null)
            {
                return OperationResult<SaveNodeDetailDto>.Fail("存档解析失败");
            }

            var outline = SfsOutline.Build(parse.Document);
            var detail = outline.DescribeNode(line);
            if (detail is null)
            {
                return OperationResult<SaveNodeDetailDto>.Fail($"第 {line} 行不是块，无法展开");
            }

            var dto = new SaveNodeDetailDto
            {
                Path = detail.Path,
                Name = detail.Name,
                Title = detail.Title,
                Line = detail.Line,
                Truncated = detail.Truncated,
                Fields = MapFields(detail.Fields),
                Nodes = MapNodes(detail.Nodes),
            };
            return OperationResult<SaveNodeDetailDto>.Ok(dto);
        }, cancellationToken);

    public Task<OperationResult<SaveEditOutcomeDto>> EditAsync(SaveEditRequest request, CancellationToken cancellationToken = default)
        => RunAsync(request?.FilePath ?? string.Empty, () =>
        {
            ArgumentNullException.ThrowIfNull(request);
            var path = request.FilePath;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return OperationResult<SaveEditOutcomeDto>.Fail("存档文件不存在", path);
            }

            if (IsLargeFile(path))
            {
                return EditStreaming(path, request);
            }

            var parse = SfsParser.ParseFile(path);
            var document = parse.Document;
            if (document is null)
            {
                return OperationResult<SaveEditOutcomeDto>.Fail("存档解析失败，拒绝编辑");
            }

            if (parse.HasErrors)
            {
                return OperationResult<SaveEditOutcomeDto>.Fail(
                    "存档有解析错误，先修好结构再编辑",
                    parse.Diagnostics.Count > 0 ? parse.Diagnostics[0].ToString() : null);
            }

            if (document.IsTextLossy)
            {
                return OperationResult<SaveEditOutcomeDto>.Fail("文件含非法 UTF-8 字节，写回会丢内容，拒绝编辑");
            }

            var result = request.Kind switch
            {
                SaveEditKind.SetValue => SfsEditor.SetValue(document, request.Line, request.Value),
                SaveEditKind.RenameKey => SfsEditor.RenameKey(document, request.Line, request.Key),
                SaveEditKind.DeleteElement => SfsEditor.DeleteElement(document, request.Line),
                SaveEditKind.DuplicateElement => SfsEditor.DuplicateElement(document, request.Line),
                SaveEditKind.InsertPair => SfsEditor.InsertPair(document, request.Line, request.Key, request.Value),
                _ => throw new ArgumentOutOfRangeException(nameof(request), request.Kind, "未知编辑种类"),
            };

            string? backup = null;
            var written = false;
            if (result.Succeeded && request.Write)
            {
                backup = SaveFileWriter.CreatePreEditSnapshot(path);
                if (backup is null)
                {
                    return OperationResult<SaveEditOutcomeDto>.Fail("写前快照创建失败，已放弃覆盖写");
                }

                SaveFileWriter.WriteAtomic(path, result.Text, File.GetLastWriteTimeUtc(path));
                written = true;
            }

            var outcome = new SaveEditOutcomeDto
            {
                Succeeded = result.Succeeded,
                Changed = result.Changed,
                Verified = result.Verified,
                Summary = result.Summary,
                Issues = result.Issues,
                CandidateText = request.Write ? null : result.Text,
                BackupPath = backup,
                Written = written,
            };
            return OperationResult<SaveEditOutcomeDto>.Ok(outcome);
        }, cancellationToken);

    private static Task<T> RunAsync<T>(string filePath, Func<T> work, CancellationToken cancellationToken)
        => Task.Run(work, cancellationToken);

    private bool IsLargeFile(string path)
        => File.Exists(path) && new FileInfo(path).Length > _fullParseLimitBytes;

    private OperationResult<SaveOutlineDto> LoadStreamingOutline(string filePath, CancellationToken cancellationToken)
    {
        var profile = SfsStreamScanner.Scan(filePath, cancellationToken: cancellationToken);
        var outline = SfsStreamOutline.Build(profile);
        var snapshot = outline.Snapshot;

        var sections = new List<SaveOutlineSectionDto>(outline.Sections.Count);
        foreach (var section in outline.Sections)
        {
            sections.Add(MapSection(section));
        }

        var dto = new SaveOutlineDto
        {
            FilePath = filePath,
            SaveName = snapshot.SaveName ?? Path.GetFileName(Path.GetDirectoryName(filePath)) ?? "存档",
            Version = snapshot.Version ?? "?",
            Mode = snapshot.Mode ?? "?",
            Funds = DisplayOrDash(snapshot.Funds),
            Science = DisplayOrDash(snapshot.Science),
            Reputation = DisplayOrDash(snapshot.Reputation),
            GameTime = DisplayOrDash(snapshot.GameTime),
            VesselCount = snapshot.VesselCount,
            PartCount = snapshot.PartCount,
            ScenarioCount = snapshot.ScenarioCount,
            CrewCount = snapshot.CrewCount,
            PairCount = snapshot.PairCount,
            NodeCount = snapshot.NodeCount,
            LineCount = snapshot.LineCount,
            Truncated = snapshot.Truncated,
            Highlights = MapFields(snapshot.Highlights),
            Sections = sections,
            LargeFileMode = true,
            LargeFileNote = BuildLargeFileNote(filePath, profile.ByteLength),
            OversizedBlocks = MapStreamNodes(outline.OversizedBlocks, SfsVocabulary.Default),
        };

        return OperationResult<SaveOutlineDto>.Ok(dto);
    }

    private OperationResult<IReadOnlyList<SaveOutlineMatchDto>> SearchStreaming(
        string filePath,
        string query,
        int limit,
        CancellationToken cancellationToken)
    {
        var tokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var vocabulary = SfsVocabulary.Default;
        var hits = SfsStreamScanner.Search(
            filePath,
            query,
            limit,
            cancellationToken,
            key => MatchesLabel(key, tokens, vocabulary),
            name => MatchesSectionLabel(name, tokens, vocabulary));
        var matches = new List<SaveOutlineMatchDto>(hits.Count);
        foreach (var hit in hits)
        {
            var text = hit.Text.Trim();
            matches.Add(new SaveOutlineMatchDto
            {
                Line = hit.Line,
                SectionTitle = "原文",
                Key = hit.Key,
                Label = hit.IsPair ? hit.Key : "块",
                Value = text,
                DisplayValue = text,
                Match = hit.IsPair ? "键值" : "原文",
            });
        }

        var token = PickHighlightToken(tokens, matches);
        var highlighted = new List<SaveOutlineMatchDto>(matches.Count);
        foreach (var match in matches)
        {
            highlighted.Add(WithHighlight(match, token));
        }

        return OperationResult<IReadOnlyList<SaveOutlineMatchDto>>.Ok(highlighted);
    }

    /// <summary>块名也走词表标签：按“飞行器”能搜到 FLIGHTSTATE 块。</summary>
    private static bool MatchesSectionLabel(string name, string[] tokens, SfsVocabulary vocabulary)
    {
        var title = vocabulary.MatchSection(name, null)?.Title;
        if (string.IsNullOrEmpty(title))
        {
            return false;
        }

        foreach (var token in tokens)
        {
            if (title.IndexOf(token, StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>流式搜索没有语法树，用词表把中文标签补上：按“资金”也能搜到 funds 行。</summary>
    private static bool MatchesLabel(string key, string[] tokens, SfsVocabulary vocabulary)
    {
        var label = vocabulary.MatchKey(key)?.Label;
        if (string.IsNullOrEmpty(label))
        {
            return false;
        }

        foreach (var token in tokens)
        {
            if (label.IndexOf(token, StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }
        }

        return true;
    }

    private OperationResult<SaveNodeDetailDto> DescribeStreaming(string filePath, int line, CancellationToken cancellationToken)
    {
        var detail = SfsStreamScanner.Describe(filePath, line, cancellationToken: cancellationToken);
        if (detail is null)
        {
            return OperationResult<SaveNodeDetailDto>.Fail($"第 {line} 行不是块，无法展开");
        }

        var vocabulary = SfsVocabulary.Default;
        var dto = new SaveNodeDetailDto
        {
            Path = detail.Path,
            Name = detail.Name,
            Title = vocabulary.MatchSection(detail.Name)?.Title ?? detail.Name,
            Line = detail.Line,
            Truncated = detail.Truncated,
            Fields = MapStreamFields(detail.Fields, vocabulary),
            Nodes = MapStreamNodes(detail.Nodes, vocabulary),
        };
        return OperationResult<SaveNodeDetailDto>.Ok(dto);
    }

    /// <summary>
    /// 大文件编辑：只支持“改值 / 改键名”这类单行改写（<see cref="SfsLinePatcher"/>）。
    /// 整块复制/删除要搬动未知长度的大段内容，流式路径不硬做，交给命令行或文本编辑器。
    /// </summary>
    private OperationResult<SaveEditOutcomeDto> EditStreaming(string path, SaveEditRequest request)
    {
        if (request.Kind is not (SaveEditKind.SetValue or SaveEditKind.RenameKey))
        {
            return OperationResult<SaveEditOutcomeDto>.Fail(
                "大文件模式只支持改值和改键名；整块复制/删除请用 ckanui-sfs 命令行或文本编辑器。");
        }

        var newKey = string.Empty;
        var newValue = request.Value ?? string.Empty;
        if (request.Kind == SaveEditKind.RenameKey)
        {
            newKey = request.Key ?? string.Empty;
            if (!SfsLinePatcher.TryReadLine(path, request.Line, out var content, out var readError))
            {
                return OperationResult<SaveEditOutcomeDto>.Fail(readError);
            }

            var token = SfsLexer.Lex(content);
            if (token.Kind != SfsLineKind.KeyValue)
            {
                return OperationResult<SaveEditOutcomeDto>.Fail($"第 {request.Line} 行不是键值对，不能改键名");
            }

            newValue = token.Value;
        }

        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(directory))
        {
            return OperationResult<SaveEditOutcomeDto>.Fail("存档路径没有目录部分，无法创建临时文件", path);
        }

        var temp = Path.Combine(directory, Path.GetFileName(path) + ".patch-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var baseline = SfsStreamScanner.Scan(path);
            if (baseline.HasErrors)
            {
                return OperationResult<SaveEditOutcomeDto>.Fail(
                    "存档流式扫描发现结构错误，先修好结构再编辑",
                    baseline.Issues.Count > 0 ? baseline.Issues[0] : null);
            }

            var patch = SfsLinePatcher.ReplaceValue(path, request.Line, newKey, newValue, temp);
            if (!patch.Succeeded)
            {
                return OperationResult<SaveEditOutcomeDto>.Ok(new SaveEditOutcomeDto
                {
                    Succeeded = false,
                    Summary = patch.Summary,
                    Issues = patch.Issues,
                });
            }

            if (patch.TempPath is null)
            {
                return OperationResult<SaveEditOutcomeDto>.Ok(new SaveEditOutcomeDto
                {
                    Succeeded = true,
                    Changed = false,
                    Verified = true,
                    Summary = patch.Summary,
                });
            }

            // 复核：补丁文件必须还能被流式扫描，且结构计数和原文一致（改值/改名不该动结构）。
            var after = SfsStreamScanner.Scan(temp);
            if (after.HasErrors || after.PairCount != baseline.PairCount || after.NodeCount != baseline.NodeCount)
            {
                return OperationResult<SaveEditOutcomeDto>.Ok(new SaveEditOutcomeDto
                {
                    Succeeded = false,
                    Summary = "补丁复核失败，未写回原文件",
                    Issues = after.Issues.Count > 0
                        ? after.Issues
                        : new[] { $"补丁后计数不一致：键值 {baseline.PairCount} → {after.PairCount}，块 {baseline.NodeCount} → {after.NodeCount}" },
                });
            }

            string? backup = null;
            var written = false;
            if (request.Write)
            {
                backup = SaveFileWriter.CreatePreEditSnapshot(path);
                if (backup is null)
                {
                    return OperationResult<SaveEditOutcomeDto>.Fail("写前快照创建失败，已放弃覆盖写");
                }

                SaveFileWriter.CommitTemp(temp, path, File.GetLastWriteTimeUtc(path));
                written = true;
            }

            return OperationResult<SaveEditOutcomeDto>.Ok(new SaveEditOutcomeDto
            {
                Succeeded = true,
                Changed = true,
                Verified = true,
                Summary = patch.Summary,
                BackupPath = backup,
                Written = written,
            });
        }
        finally
        {
            if (File.Exists(temp))
            {
                try
                {
                    File.Delete(temp);
                }
                catch (IOException)
                {
                    // 临时文件删不掉不影响结果，下次打开会被当作垃圾文件忽略。
                }
            }
        }
    }

    private string BuildLargeFileNote(string filePath, long byteLength)
    {
        var sizeMb = byteLength / (1024d * 1024d);
        var limitMb = _fullParseLimitBytes / (1024d * 1024d);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"这份存档 {sizeMb:0.0} MB，超过完整解析上限 {limitMb:0.#} MB：已改用流式模式（只统计 + 按需展开，内存恒定），整块复制/删除请用 ckanui-sfs 命令行。");
    }

    private static IReadOnlyList<SaveOutlineFieldDto> MapStreamFields(
        IReadOnlyList<SfsStreamField> fields,
        SfsVocabulary vocabulary)
    {
        var result = new List<SaveOutlineFieldDto>(fields.Count);
        foreach (var field in fields)
        {
            var rule = vocabulary.MatchKey(field.Key);
            result.Add(MapField(new SfsOutlineField
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
            }));
        }

        return result;
    }

    private static IReadOnlyList<SaveOutlineNodeDto> MapStreamNodes(
        IReadOnlyList<SfsStreamBlock> nodes,
        SfsVocabulary vocabulary)
    {
        var result = new List<SaveOutlineNodeDto>(nodes.Count);
        foreach (var node in nodes)
        {
            result.Add(new SaveOutlineNodeDto
            {
                Path = node.Path,
                Name = node.Name,
                Label = vocabulary.MatchSection(node.Name)?.Title ?? string.Empty,
                Summary = node.Summary ?? string.Empty,
                Line = node.Line,
                Depth = node.Depth,
                ChildCount = node.NodeCount,
                PairCount = node.PairCount,
                NodeCount = node.NodeCount,
                ByteLength = node.ByteLength,
                EndLine = node.EndLine,
            });
        }

        return result;
    }

    /// <summary>挑一个能在命中项里真实出现的词做高亮（优先最长的那个，短词容易命中无关位置）。</summary>
    private static string? PickHighlightToken(string[] tokens, List<SaveOutlineMatchDto> hits)
    {
        foreach (var token in tokens.OrderByDescending(token => token.Length))
        {
            foreach (var hit in hits)
            {
                if (hit.DisplayValue.Contains(token, StringComparison.OrdinalIgnoreCase))
                {
                    return token;
                }
            }
        }

        return null;
    }

    /// <summary>把展示值切成“前缀 / 命中 / 后缀”三段；命中不到就整段当前缀。</summary>
    private static SaveOutlineMatchDto WithHighlight(SaveOutlineMatchDto match, string? token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return match with { HighlightPrefix = match.DisplayValue };
        }

        var index = match.DisplayValue.IndexOf(token, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return match with { HighlightPrefix = match.DisplayValue };
        }

        return match with
        {
            HighlightPrefix = match.DisplayValue[..index],
            HighlightHit = match.DisplayValue.Substring(index, token.Length),
            HighlightSuffix = match.DisplayValue[(index + token.Length)..],
        };
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

    private static string DisplayOrDash(SfsOutlineField? field)
        => field is null || string.IsNullOrEmpty(field.Value) ? "—" : Display(field);

    private static string Display(SfsOutlineField field)
    {
        var unit = field.Unit;
        if (string.IsNullOrEmpty(unit))
        {
            return field.Value;
        }

        // 词表用 "√" 表示金额前缀（游戏内是 √15000，不是 15000√）。
        return unit == "√" ? "√" + field.Value : field.Value + unit;
    }

    private static IReadOnlyList<SaveOutlineFieldDto> MapFields(IReadOnlyList<SfsOutlineField> fields)
    {
        var result = new List<SaveOutlineFieldDto>(fields.Count);
        foreach (var field in fields)
        {
            result.Add(MapField(field));
        }

        return result;
    }

    private static SaveOutlineFieldDto MapField(SfsOutlineField field) => new()
    {
        Path = field.Path,
        Key = field.Key,
        Label = field.DisplayName,
        Value = field.Value,
        DisplayValue = Display(field),
        Line = field.Line,
        Kind = field.Kind.ToString(),
        Group = field.Group ?? string.Empty,
        SectionId = field.SectionId,
        Priority = field.Priority,
        IsKnown = field.IsKnown,
        IsHighlighted = field.IsHighlighted,
    };

    private static IReadOnlyList<SaveOutlineNodeDto> MapNodes(IReadOnlyList<SfsOutlineNodeRef> nodes)
    {
        var result = new List<SaveOutlineNodeDto>(nodes.Count);
        foreach (var node in nodes)
        {
            result.Add(new SaveOutlineNodeDto
            {
                Path = node.Path,
                Name = node.Name,
                Label = node.DisplayName,
                Summary = node.Summary ?? string.Empty,
                Line = node.Line,
                Depth = node.Depth,
                ChildCount = node.ChildCount,
                PairCount = node.PairCount,
                NodeCount = node.NodeCount,
                ByteLength = node.ByteLength,
                EndLine = node.EndLine,
                SectionId = node.SectionId,
            });
        }

        return result;
    }

    private static SaveOutlineSectionDto MapSection(SfsOutlineSection section) => new()
    {
        Id = section.Id,
        Title = section.Title,
        Subtitle = section.Subtitle,
        Kind = section.Kind.ToString(),
        Rank = section.Rank,
        Order = section.Order,
        Truncated = section.Truncated,
        TotalPairCount = section.TotalPairCount,
        TotalNodeCount = section.TotalNodeCount,
        Fields = MapFields(section.Fields),
        Nodes = MapNodes(section.Nodes),
    };
}