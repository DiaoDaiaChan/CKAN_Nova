using System.Globalization;
using System.Text;

namespace CkanUI.Sfs;

/// <summary>校验结果：一组按行列排序的诊断 + 汇总。</summary>
public sealed class SfsValidationResult
{
    private readonly IReadOnlyList<SfsDiagnostic> _diagnostics;

    internal SfsValidationResult(IReadOnlyList<SfsDiagnostic> diagnostics, string? sourcePath)
    {
        _diagnostics = diagnostics;
        SourcePath = sourcePath;
        var errors = 0;
        var warnings = 0;
        foreach (var diagnostic in diagnostics)
        {
            if (diagnostic.Severity == SfsSeverity.Error)
            {
                errors++;
            }
            else if (diagnostic.Severity == SfsSeverity.Warning)
            {
                warnings++;
            }
        }

        ErrorCount = errors;
        WarningCount = warnings;
    }

    /// <summary>被校验的文件路径（来自文本时为 null）。</summary>
    public string? SourcePath { get; }

    /// <summary>诊断列表（按行号、列号排序）。</summary>
    public IReadOnlyList<SfsDiagnostic> Diagnostics => _diagnostics;

    /// <summary>错误数。</summary>
    public int ErrorCount { get; }

    /// <summary>警告数。</summary>
    public int WarningCount { get; }

    /// <summary>是否存在错误。</summary>
    public bool HasErrors => ErrorCount > 0;

    /// <summary>是否存在警告。</summary>
    public bool HasWarnings => WarningCount > 0;

    /// <summary>是否完全干净（无任何诊断）。</summary>
    public bool IsClean => _diagnostics.Count == 0;

    /// <summary>是否含指定编号的诊断。</summary>
    public bool Contains(string code)
    {
        foreach (var diagnostic in _diagnostics)
        {
            if (string.Equals(diagnostic.Code, code, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>按编号取第一条诊断。</summary>
    public SfsDiagnostic? Find(string code)
    {
        foreach (var diagnostic in _diagnostics)
        {
            if (string.Equals(diagnostic.Code, code, StringComparison.Ordinal))
            {
                return diagnostic;
            }
        }

        return null;
    }

    /// <summary>人读报告（CLI validate 用）。</summary>
    public string ToReport()
    {
        var builder = new StringBuilder();
        foreach (var diagnostic in _diagnostics)
        {
            builder.AppendLine(diagnostic.ToString());
        }

        builder.Append(FormattableString.Invariant($"共 {_diagnostics.Count} 条诊断（错误 {ErrorCount}，警告 {WarningCount}）"));
        return builder.ToString();
    }
}

/// <summary>
/// 校验器（13 篇 §6.4，WBS SFS-03）。
/// V-01 括号平衡 / V-02 根唯一且为 GAME / V-03 每行可分词 / V-04 同节点重复键 /
/// V-05 BOM·行尾混合·UTF-8 非法 / V-06 缩进与深度不一致 / V-07 深度或行长超限 / V-08 SCENARIO 缺 name。
/// 每条诊断都带行号、列号、严重级别与可读原因；V-09（.loadmeta 一致性）需要存档包，本层不实现。
/// </summary>
public static class SfsValidator
{
    /// <summary>建议的最大嵌套深度（§6.4 V-07）。</summary>
    public const int MaxRecommendedDepth = 16;

    /// <summary>建议的最大单行长度（字节，§6.4 V-07）。</summary>
    public const int MaxRecommendedLineLength = 64 * 1024;

    /// <summary>KSP 存档的唯一根节点名（§2.2 实测）。</summary>
    public const string ExpectedRootName = "GAME";

    /// <summary>存档场景节点名（实测 31 个同名兄弟）。</summary>
    public const string ScenarioNodeName = "SCENARIO";

    private const int MaxPerCategory = 20;

    /// <summary>校验已解析文档。</summary>
    public static SfsValidationResult Validate(SfsDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var diagnostics = new List<SfsDiagnostic>(document.ParseDiagnostics);

        CheckEncoding(document, diagnostics);
        CheckRoot(document, diagnostics);
        CheckDuplicateKeys(document, diagnostics);
        CheckIndentation(document, diagnostics);
        CheckLimits(document, diagnostics);
        CheckScenarios(document, diagnostics);

        diagnostics.Sort(static (left, right) =>
        {
            var byLine = left.Line.CompareTo(right.Line);
            return byLine != 0 ? byLine : left.Column.CompareTo(right.Column);
        });

        return new SfsValidationResult(diagnostics, document.SourcePath);
    }

    /// <summary>解析并校验文本。</summary>
    public static SfsValidationResult ValidateText(string text, string? sourcePath = null)
    {
        var parsed = SfsParser.Parse(text, sourcePath);
        if (parsed.Document is null)
        {
            var diagnostics = new List<SfsDiagnostic>(parsed.Diagnostics);
            return new SfsValidationResult(diagnostics, sourcePath);
        }

        return Validate(parsed.Document);
    }

    /// <summary>解析并校验文件（IO 异常向上抛，由 CLI 转成退出码）。</summary>
    public static SfsValidationResult ValidateFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var parsed = SfsParser.ParseFile(path);
        if (parsed.Document is null)
        {
            var diagnostics = new List<SfsDiagnostic>(parsed.Diagnostics);
            return new SfsValidationResult(diagnostics, path);
        }

        return Validate(parsed.Document);
    }

    /// <summary>V-05：BOM 与行尾混合（UTF-8 非法由解析层报）。</summary>
    private static void CheckEncoding(SfsDocument document, List<SfsDiagnostic> diagnostics)
    {
        if (document.HasBom)
        {
            diagnostics.Add(new SfsDiagnostic(
                SfsSeverity.Warning,
                SfsDiagnosticCodes.EncodingOrLineEnding,
                1,
                1,
                "文件带 UTF-8 BOM；KSP 实测存档无 BOM，建议去掉以免工具链差异",
                "\uFEFF"));
        }

        var counts = document.NewLines;
        if (!counts.IsMixed)
        {
            return;
        }

        var firstDeviant = document.DeviantLineNumbers.Count > 0 ? document.DeviantLineNumbers[0] : 1;
        diagnostics.Add(new SfsDiagnostic(
            SfsSeverity.Warning,
            SfsDiagnosticCodes.EncodingOrLineEnding,
            firstDeviant,
            1,
            FormattableString.Invariant(
                $"行尾风格混合：CRLF {counts.Crlf} 行、LF {counts.Lf} 行、CR {counts.Cr} 行；首个异常行是第 {firstDeviant} 行"),
            document.GetLineContent(firstDeviant)));
    }

    /// <summary>V-02：有块结构时，顶层必须恰好一个节点且名为 GAME。</summary>
    private static void CheckRoot(SfsDocument document, List<SfsDiagnostic> diagnostics)
    {
        var roots = new List<SfsNode>(2);
        foreach (var element in document.Elements)
        {
            if (element is SfsNode node)
            {
                roots.Add(node);
            }
        }

        if (roots.Count == 0)
        {
            // 扁平文件（如 .loadmeta）没有根节点是合法的，不报。
            return;
        }

        if (roots.Count > 1)
        {
            diagnostics.Add(new SfsDiagnostic(
                SfsSeverity.Error,
                SfsDiagnosticCodes.RootNode,
                roots[1].LineNumber,
                1,
                FormattableString.Invariant($"顶层出现 {roots.Count} 个节点，KSP 存档必须只有一个根 GAME（第 2 个是 {roots[1].Name}）"),
                roots[1].Raw.Content));
            return;
        }

        var root = roots[0];
        if (!string.Equals(root.Name, ExpectedRootName, StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add(new SfsDiagnostic(
                SfsSeverity.Error,
                SfsDiagnosticCodes.RootNode,
                root.LineNumber,
                1,
                FormattableString.Invariant($"根节点应为 {ExpectedRootName}，实际是 {root.Name}"),
                root.Raw.Content));
        }
    }

    /// <summary>V-04：同节点重复键（L5 不合并，只警告）。</summary>
    private static void CheckDuplicateKeys(SfsDocument document, List<SfsDiagnostic> diagnostics)
    {
        var reported = 0;
        var work = new Stack<SfsElement>();
        for (var index = document.Elements.Count - 1; index >= 0; index--)
        {
            work.Push(document.Elements[index]);
        }

        while (work.Count > 0 && reported < MaxPerCategory)
        {
            var element = work.Pop();
            if (element is not SfsNode node)
            {
                continue;
            }

            for (var index = node.Children.Count - 1; index >= 0; index--)
            {
                work.Push(node.Children[index]);
            }

            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var pair in node.Pairs())
            {
                seen.TryGetValue(pair.Key, out var count);
                count++;
                seen[pair.Key] = count;
                if (count == 2 && reported < MaxPerCategory)
                {
                    reported++;
                    diagnostics.Add(new SfsDiagnostic(
                        SfsSeverity.Warning,
                        SfsDiagnosticCodes.DuplicateKey,
                        pair.LineNumber,
                        1,
                        FormattableString.Invariant($"节点 {node.Name} 内重复键 {pair.Key}（按 L5 原样保留，不合并）"),
                        pair.Raw.Content));
                }
            }
        }
    }

    /// <summary>V-06：缩进与深度不一致（L6 结构由括号栈决定，缩进只是装饰）。</summary>
    private static void CheckIndentation(SfsDocument document, List<SfsDiagnostic> diagnostics)
    {
        var reported = 0;
        var work = new Stack<SfsElement>();
        for (var index = document.Elements.Count - 1; index >= 0; index--)
        {
            work.Push(document.Elements[index]);
        }

        while (work.Count > 0 && reported < MaxPerCategory)
        {
            var element = work.Pop();
            if (element is SfsNode node)
            {
                for (var index = node.Children.Count - 1; index >= 0; index--)
                {
                    work.Push(node.Children[index]);
                }
            }

            if (element is SfsBlank)
            {
                continue;
            }

            if (!TryDescribeIndentMismatch(element.Indent, element.Depth, out var message))
            {
                continue;
            }

            reported++;
            diagnostics.Add(new SfsDiagnostic(
                SfsSeverity.Warning,
                SfsDiagnosticCodes.Indentation,
                element.LineNumber,
                1,
                message,
                element.Raw.Content));

            if (element is SfsNode node2 && node2.BraceLine is { } braceLine
                && TryDescribeIndentMismatch(SfsText.LeadingWhitespace(braceLine.Content), node2.Depth, out var braceMessage))
            {
                reported++;
                diagnostics.Add(new SfsDiagnostic(
                    SfsSeverity.Warning,
                    SfsDiagnosticCodes.Indentation,
                    node2.LineNumber,
                    1,
                    braceMessage,
                    braceLine.Content));
            }
        }
    }

    private static bool TryDescribeIndentMismatch(string indent, int depth, out string message)
    {
        message = string.Empty;
        var tabs = 0;
        var spaces = 0;
        foreach (var character in indent)
        {
            if (character == '\t')
            {
                tabs++;
            }
            else if (character == ' ')
            {
                spaces++;
            }
            else
            {
                return false;
            }
        }

        if (spaces == 0 && tabs == depth)
        {
            return false;
        }

        message = FormattableString.Invariant(
            $"缩进与深度不一致：深度 {depth} 应为 {depth} 个制表符，实际 {tabs} 个制表符 + {spaces} 个空格");
        return true;
    }

    /// <summary>V-07：深度超限或单行过长。</summary>
    private static void CheckLimits(SfsDocument document, List<SfsDiagnostic> diagnostics)
    {
        if (document.MaxDepth > MaxRecommendedDepth)
        {
            diagnostics.Add(new SfsDiagnostic(
                SfsSeverity.Warning,
                SfsDiagnosticCodes.DepthOrLineLength,
                1,
                1,
                FormattableString.Invariant($"嵌套深度 {document.MaxDepth} 超过建议上限 {MaxRecommendedDepth}")));
        }

        var reported = 0;
        for (var line = 1; line <= document.LineCount && reported < MaxPerCategory; line++)
        {
            var content = document.GetLineContent(line);
            var bytes = Encoding.UTF8.GetByteCount(content);
            if (bytes <= MaxRecommendedLineLength)
            {
                continue;
            }

            reported++;
            diagnostics.Add(new SfsDiagnostic(
                SfsSeverity.Warning,
                SfsDiagnosticCodes.DepthOrLineLength,
                line,
                1,
                FormattableString.Invariant($"单行 {bytes} 字节，超过建议上限 {MaxRecommendedLineLength} 字节"),
                content));
        }
    }

    /// <summary>V-08：SCENARIO 缺 name 子键（实测靠 name 区分 31 个同名兄弟）。</summary>
    private static void CheckScenarios(SfsDocument document, List<SfsDiagnostic> diagnostics)
    {
        var reported = 0;
        var work = new Stack<SfsElement>();
        for (var index = document.Elements.Count - 1; index >= 0; index--)
        {
            work.Push(document.Elements[index]);
        }

        while (work.Count > 0 && reported < MaxPerCategory)
        {
            if (work.Pop() is not SfsNode node)
            {
                continue;
            }

            for (var index = node.Children.Count - 1; index >= 0; index--)
            {
                work.Push(node.Children[index]);
            }

            if (!string.Equals(node.Name, ScenarioNodeName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (node.FindPair("name") is not null)
            {
                continue;
            }

            reported++;
            diagnostics.Add(new SfsDiagnostic(
                SfsSeverity.Warning,
                SfsDiagnosticCodes.ScenarioName,
                node.LineNumber,
                1,
                "SCENARIO 缺少 name 子键，KSP 载入时可能无法区分同名场景",
                node.Raw.Content));
        }
    }
}
