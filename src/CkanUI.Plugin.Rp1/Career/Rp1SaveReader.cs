using System.Globalization;
using System.Text;
using CkanUI.Services.Abstractions;
using CkanUI.Sfs;

namespace CkanUI.Plugin.Rp1.Career;

/// <summary>从一份 persistent.sfs 里抽出的 RP-1 场景节点与存档头信息。</summary>
public sealed record Rp1ScenarioSet(
    double? CurrentUt,
    string? GameVersion,
    string? SaveTitle,
    IReadOnlyDictionary<string, SfsNode> Scenarios,
    IReadOnlyList<string> Diagnostics);

/// <summary>
/// 只读 RP-1 相关的五个 SCENARIO。46 MB 的存档不建整棵树：
/// 先流式扫描拿块的行区间，再单次顺序读文件把这些区间切出来单独解析。
/// </summary>
public static class Rp1SaveReader
{
    private static readonly string[] TargetNames =
    [
        "CareerLog",
        "MilestoneHandler",
        "RP1ContractTracker",
        "ProgramHandler",
        "CrewHandler",
        "Funding",
        "ResearchAndDevelopment",
        "Reputation",
        "Confidence",
    ];

    /// <summary>读取存档；失败时返回带原因的结果，不抛 IO 异常。</summary>
    public static OperationResult<Rp1ScenarioSet> Read(string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return OperationResult<Rp1ScenarioSet>.Fail("存档文件不存在。", path);
        }

        try
        {
            var options = new SfsStreamOptions
            {
                FieldDepth = 1,
                FieldBudget = 256,
                BlockDepth = 1,
                BlockBudget = 4096,
                OversizedBlockBudget = 16,
            };
            var profile = SfsStreamScanner.Scan(path, options, cancellationToken);
            var diagnostics = new List<string>();
            if (profile.HasErrors && profile.Issues.Count > 0)
            {
                diagnostics.Add("存档结构有告警：" + string.Join("；", profile.Issues.Take(3)));
            }

            var ut = ParseDouble(profile.Fields.FirstOrDefault(field => field.Path == "GAME" && field.Key == "UT")?.Value);
            var version = profile.Fields.FirstOrDefault(field => field.Path == "GAME" && field.Key == "version")?.Value;
            var title = profile.Fields.FirstOrDefault(field => field.Path == "GAME" && field.Key == "Title")?.Value;

            var ranges = new List<(string Name, int Start, int End)>();
            foreach (var block in profile.Blocks)
            {
                if (block.Depth != 1 || block.EndLine <= 0)
                {
                    continue;
                }

                var name = ScenarioName(block);
                if (name is null || !TargetNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                ranges.Add((name, block.Line, block.EndLine));
            }

            ranges.Sort((left, right) => left.Start.CompareTo(right.Start));
            var slices = ReadSlices(path, ranges, cancellationToken);
            var scenarios = new Dictionary<string, SfsNode>(StringComparer.OrdinalIgnoreCase);
            foreach (var slice in slices)
            {
                var parsed = SfsParser.Parse(slice.Text, path);
                var node = parsed.Document?.Elements.OfType<SfsNode>()
                    .FirstOrDefault(candidate => candidate.Name.Equals("SCENARIO", StringComparison.OrdinalIgnoreCase));
                if (node is null)
                {
                    diagnostics.Add($"场景「{slice.Name}」解析失败，已跳过。");
                    continue;
                }

                scenarios[slice.Name] = node;
                if (parsed.HasErrors)
                {
                    diagnostics.Add($"场景「{slice.Name}」有 {parsed.ErrorCount} 条解析告警，仍按可读部分展示。");
                }
            }

            foreach (var name in TargetNames)
            {
                if (!scenarios.ContainsKey(name))
                {
                    diagnostics.Add($"存档里没有 {name} 场景：对应事件会缺失。");
                }
            }

            return OperationResult<Rp1ScenarioSet>.Ok(new Rp1ScenarioSet(ut, version, title, scenarios, diagnostics));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            return OperationResult<Rp1ScenarioSet>.Fail("读取存档失败：" + ex.Message, path);
        }
    }

    private static string? ScenarioName(SfsStreamBlock block)
    {
        if (string.IsNullOrWhiteSpace(block.Summary))
        {
            return null;
        }

        var separator = block.Summary.IndexOf(" · ", StringComparison.Ordinal);
        return separator <= 0 ? null : block.Summary[..separator].Trim();
    }

    private static List<(string Name, string Text)> ReadSlices(
        string path,
        IReadOnlyList<(string Name, int Start, int End)> ranges,
        CancellationToken cancellationToken)
    {
        var result = new List<(string Name, string Text)>(ranges.Count);
        if (ranges.Count == 0)
        {
            return result;
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16);
        using var reader = new StreamReader(stream, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true, bufferSize: 1 << 16);

        var index = 0;
        var lineNumber = 0;
        StringBuilder? buffer = null;
        var currentName = string.Empty;
        string? raw;
        while ((raw = reader.ReadLine()) is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lineNumber++;

            while (index < ranges.Count && lineNumber > ranges[index].End)
            {
                if (buffer is not null)
                {
                    result.Add((currentName, buffer.ToString()));
                    buffer = null;
                }

                index++;
            }

            if (index >= ranges.Count)
            {
                break;
            }

            var range = ranges[index];
            if (lineNumber == range.Start)
            {
                buffer = new StringBuilder();
                currentName = range.Name;
            }

            if (buffer is not null && lineNumber >= range.Start && lineNumber <= range.End)
            {
                buffer.AppendLine(raw);
            }

            if (buffer is not null && lineNumber >= range.End)
            {
                result.Add((currentName, buffer.ToString()));
                buffer = null;
                index++;
            }
        }

        if (buffer is not null)
        {
            result.Add((currentName, buffer.ToString()));
        }

        return result;
    }

    private static double? ParseDouble(string? value)
        => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && !double.IsNaN(parsed)
            ? parsed
            : null;
}
