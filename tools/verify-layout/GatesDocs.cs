using System.Globalization;
using System.Text.RegularExpressions;

namespace CkanUI.Tools.VerifyLayout;

/// <summary>docs/** 文档清单（排除未定稿的 docs/_drafts，21 篇 §9 的临时区不参与内容门禁）。</summary>
internal static class DocSet
{
    public static List<string> MarkdownFiles(GateContext context)
    {
        return context.EnumerateWorkingTreeFiles("docs")
            .Where(path => path.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.StartsWith("docs/_drafts/", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();
    }
}

/// <summary>markdown 相对链接提取：跳过代码围栏，避免把示例代码里的 [x](y) 当成真链接。</summary>
internal static partial class MarkdownLinks
{
    [GeneratedRegex(@"!?\[[^\]]*\]\(\s*([^)\s]+)(?:\s+""[^""]*"")?\s*\)", RegexOptions.CultureInvariant)]
    private static partial Regex LinkPattern();

    public static IEnumerable<(string Target, int Line)> Extract(string text)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var inFence = false;
        for (var index = 0; index < lines.Length; index++)
        {
            var trimmed = lines[index].TrimStart();
            if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal))
            {
                inFence = !inFence;
                continue;
            }

            if (inFence)
            {
                continue;
            }

            foreach (Match match in LinkPattern().Matches(lines[index]))
            {
                yield return (match.Groups[1].Value, index + 1);
            }
        }
    }

    /// <summary>把链接目标解析成相对仓库根的路径；外链、锚点、绝对路径返回 null（由 H-12 跳过）。</summary>
    public static string? Resolve(Repo repo, string sourceFile, string target)
    {
        var value = target.Trim();
        if (value.Length == 0 || value.StartsWith('#') || value.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var hash = value.IndexOf('#', StringComparison.Ordinal);
        if (hash >= 0)
        {
            value = value[..hash];
        }

        var query = value.IndexOf('?', StringComparison.Ordinal);
        if (query >= 0)
        {
            value = value[..query];
        }

        if (value.Length == 0)
        {
            return null;
        }

        value = Uri.UnescapeDataString(value);
        if (value.StartsWith('/') || value.StartsWith('\\') || (value.Length > 1 && value[1] == ':'))
        {
            return null;
        }

        var directory = Path.GetDirectoryName(sourceFile.Replace('/', Path.DirectorySeparatorChar)) ?? ".";
        var combined = Path.GetFullPath(Path.Combine(repo.Root, directory, value.Replace('/', Path.DirectorySeparatorChar)));
        return repo.Relative(combined);
    }
}

/// <summary>H-04：docs/** 编号唯一 + 两类篇的模板（21 篇 §6 规则 1/2）。警告级。</summary>
internal static partial class H04DocTemplates
{
    public const string Id = "H-04";

    /// <summary>基线篇（00–10）：<c>## N. 标题</c>。</summary>
    [GeneratedRegex(@"^##\s+\d+\.\s+\S", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex NumberedHeading();

    /// <summary>能力域篇（11–21）：<c>## §N 标题</c>。</summary>
    [GeneratedRegex(@"^##\s+§\d+", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex SectionHeading();

    /// <summary>修订记录章节（两种篇都允许 <c>## 修订记录</c> 或 <c>## N. 修订记录</c> 写法）。</summary>
    [GeneratedRegex(@"^#{2,3}\s+.*修订记录\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex RevisionHeading();

    /// <summary>文档编号前缀：<c>NN-</c>、<c>NNN-</c>，允许 <c>00a-</c> 这类附标。</summary>
    [GeneratedRegex(@"^(\d{2,4}[a-z]?)[-_]", RegexOptions.CultureInvariant)]
    private static partial Regex NumberPrefix();

    /// <summary>21 篇 §6 规则 2 的例外：12/13 篇沿用原编号，只要目标章节语义在即通过。</summary>
    private static readonly HashSet<string> NumberingException = new(StringComparer.Ordinal) { "12", "13" };

    public static void Run(GateContext context)
    {
        var numbering = new Dictionary<(string Directory, string Prefix), string>(StringTupleComparer.Instance);

        foreach (var path in DocSet.MarkdownFiles(context))
        {
            if (path.Equals("docs/README.md", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var full = Path.Combine(context.Repo.Root, path.Replace('/', Path.DirectorySeparatorChar));
            var text = File.ReadAllText(full);
            var name = Path.GetFileName(path);
            var hasRevision = RevisionHeading().IsMatch(text);

            var match = NumberPrefix().Match(name);
            if (match.Success)
            {
                var key = (Path.GetDirectoryName(path)?.Replace('\\', '/') ?? string.Empty, match.Groups[1].Value);
                if (numbering.TryGetValue(key, out var existing))
                {
                    context.Add(Id, GateLevel.Warning, $"文档编号重复: {path} 与 {existing} 同为 {match.Groups[1].Value}");
                }
                else
                {
                    numbering[key] = path;
                }
            }

            // 21 篇 §6 的模板规则只约束 docs/plan（基线篇 00–10 / 能力域篇 11–21）；
            // ops/ 与 design/ 下的文档各有自己的写法，只查编号唯一性，不套模板。
            if (!path.StartsWith("docs/plan/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var isBaseline = IsBaseline(match);
            if (isBaseline)
            {
                if (!NumberedHeading().IsMatch(text))
                {
                    context.Add(Id, GateLevel.Warning, $"文档不符合模板: {path}（基线篇需要至少一个 \"## N. 标题\" 章节）");
                }

                if (hasRevision && !name.StartsWith("00-index", StringComparison.Ordinal))
                {
                    context.Add(Id, GateLevel.Warning, $"文档不符合模板: {path}（基线篇修订历史应只记在 00-index.md）");
                }

                continue;
            }

            // 能力域篇（11–21）按 21 篇 §12 的语义要求判定：必须有编号分节（"## §N" 或 "## N."）。
            // 11/17/18/19/20 篇在规格仓原文即写作 "## 1. 目标与非目标"（早期沿用了基线篇写法），
            // 迁移要求逐字保真，故两种写法都接受；判据只要求「有编号分节」，不锁定符号写法。
            var exception = NumberingException.Contains(match.Success ? match.Groups[1].Value : string.Empty);
            if (!exception && !SectionHeading().IsMatch(text) && !NumberedHeading().IsMatch(text))
            {
                context.Add(Id, GateLevel.Warning, $"文档不符合模板: {path}（能力域篇需要编号分节，\"## §1\" 或 \"## 1.\" 均可）");
            }

            if (!hasRevision)
            {
                context.Add(Id, GateLevel.Warning, $"文档不符合模板: {path}（能力域篇需要末尾 \"## 修订记录\"）");
            }
        }
    }

    private static bool IsBaseline(Match match)
    {
        if (!match.Success)
        {
            return true;
        }

        var digits = new string(match.Groups[1].Value.TakeWhile(char.IsDigit).ToArray());
        if (!int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
        {
            return true;
        }

        return number <= 10;
    }
}

/// <summary>H-05：docs/README.md 索引必须覆盖 docs/**/*.md（21 篇 §6 规则 3：不登记 = 不存在）。</summary>
internal static class H05DocIndex
{
    public const string Id = "H-05";

    private const string IndexFile = "docs/README.md";

    public static void Run(GateContext context)
    {
        var indexPath = Path.Combine(context.Repo.Root, IndexFile.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(indexPath))
        {
            context.Add(Id, GateLevel.Warning, $"缺少文档索引: {IndexFile}");
            return;
        }

        var text = File.ReadAllText(indexPath);
        var covered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (target, _) in MarkdownLinks.Extract(text))
        {
            var resolved = MarkdownLinks.Resolve(context.Repo, IndexFile, target);
            if (resolved is not null)
            {
                covered.Add(resolved);
            }
        }

        foreach (var path in DocSet.MarkdownFiles(context))
        {
            if (path.Equals(IndexFile, StringComparison.OrdinalIgnoreCase) || covered.Contains(path))
            {
                continue;
            }

            var relativeToDocs = path["docs/".Length..];
            if (text.Contains(relativeToDocs, StringComparison.Ordinal))
            {
                continue;
            }

            context.Add(Id, GateLevel.Warning, $"文档未登记索引: {path}");
        }
    }
}

/// <summary>H-12：文档内相对链接必须可解析（自己实现，避免 shell 拼接的假阳性）。</summary>
internal static class H12DocLinks
{
    public const string Id = "H-12";

    public static void Run(GateContext context)
    {
        foreach (var path in DocSet.MarkdownFiles(context))
        {
            var full = Path.Combine(context.Repo.Root, path.Replace('/', Path.DirectorySeparatorChar));
            foreach (var (target, line) in MarkdownLinks.Extract(File.ReadAllText(full)))
            {
                var resolved = MarkdownLinks.Resolve(context.Repo, path, target);
                if (resolved is null)
                {
                    continue;
                }

                var absolute = Path.Combine(context.Repo.Root, resolved.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(absolute) && !Directory.Exists(absolute))
                {
                    context.Add(Id, GateLevel.Blocking, $"断链: {path}:{line} → {target}");
                }
            }
        }
    }
}

/// <summary>元组键的序数比较器（默认比较器对字符串会用区域性规则，路径必须序数比较）。</summary>
internal sealed class StringTupleComparer : IEqualityComparer<(string Directory, string Prefix)>
{
    public static readonly StringTupleComparer Instance = new();

    public bool Equals((string Directory, string Prefix) x, (string Directory, string Prefix) y) =>
        string.Equals(x.Directory, y.Directory, StringComparison.Ordinal) &&
        string.Equals(x.Prefix, y.Prefix, StringComparison.Ordinal);

    public int GetHashCode((string Directory, string Prefix) tuple) =>
        HashCode.Combine(
            StringComparer.Ordinal.GetHashCode(tuple.Directory),
            StringComparer.Ordinal.GetHashCode(tuple.Prefix));
}
