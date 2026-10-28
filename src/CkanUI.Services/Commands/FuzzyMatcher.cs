using CkanUI.Services.Abstractions.Commands;

namespace CkanUI.Services.Commands;

/// <summary>
/// 命令面板的匹配算法（UI-40）：关键词切分 → 逐字段打分 → 标题高亮。
/// </summary>
/// <remarks>
/// <para><b>为什么是纯函数</b>：命令面板要在每次按键时重算，算法本身不碰 UI、不碰服务、不抛异常，
/// 才能在无头测试里逐条断言分数与高亮区间；也顺便保证它可以用在任何线程上（只读入参）。</para>
/// <para><b>切词</b>：按空白切成关键词，重复项忽略大小写去重；每个关键词都必须命中（AND），
/// 允许分别命中不同字段——「mod 更新」可以一条命中标题、另一条命中关键词。</para>
/// <para><b>命中强度</b>：完全相同 &gt; 前缀 &gt; 连续子串 &gt; 不连续子序列。
/// 前缀与连续子串额外带「越靠前越加分」，子序列按空档长度扣分（最多扣 8），
/// 于是输入「log」时「日志」的英文关键词与「安装历史」不会打平，排序稳定可预期。</para>
/// <para><b>字段权重</b>：标题 1000 &gt; 分类 400 &gt; 关键词 300 &gt; 副标题 200，
/// 同一个关键词只取命中的最好字段计分，因此标题命中永远压过关键词命中。</para>
/// <para><b>分数是整数</b>：排序不受浮点误差影响；同分时由 <see cref="Search"/> 的稳定排序保留注册顺序。</para>
/// </remarks>
public sealed class FuzzyMatcher : IFuzzyMatcher
{
    /// <summary>默认返回条数（命令面板一屏的上限）。</summary>
    public const int DefaultLimit = 50;

    /// <summary>查询长度上限；超长输入先截断再匹配（防御性，正常用户不会触发）。</summary>
    public const int MaxQueryLength = 256;

    private const int TitleBase = 1000;
    private const int CategoryBase = 400;
    private const int KeywordBase = 300;
    private const int SubtitleBase = 200;

    private const int ExactBonus = 500;
    private const int PrefixBonus = 300;
    private const int SubstringBonus = 150;
    private const int SubsequenceBonus = 50;

    private const int MaxPositionBonus = 16;
    private const int MaxGapPenalty = 8;

    /// <summary>命中方式，决定加成（顺序即强弱）。</summary>
    private enum MatchKind
    {
        /// <summary>整字段等于关键词。</summary>
        Exact,

        /// <summary>字段以关键词开头。</summary>
        Prefix,

        /// <summary>字段里存在连续的关键词。</summary>
        Substring,

        /// <summary>关键词的字符按顺序散布在字段里。</summary>
        Subsequence,
    }

    /// <inheritdoc />
    public IReadOnlyList<CommandMatch> Search(IReadOnlyList<CommandDescriptor> commands, string? query, int limit = DefaultLimit)
    {
        ArgumentNullException.ThrowIfNull(commands);

        var max = limit <= 0 ? DefaultLimit : limit;
        if (commands.Count == 0)
        {
            return Array.Empty<CommandMatch>();
        }

        var tokens = Tokenize(query);
        if (tokens.Count == 0)
        {
            // 空查询 = 浏览态：按分类名排序（同分类保持注册顺序，OrderBy 是稳定排序），不打分不亮高亮。
            return commands
                .OrderBy(command => command.Category, StringComparer.OrdinalIgnoreCase)
                .Take(max)
                .Select(command => new CommandMatch(command, 0, CommandMatch.NoHighlights))
                .ToArray();
        }

        var matches = new List<CommandMatch>(commands.Count);
        foreach (var command in commands)
        {
            var match = MatchTokens(command, tokens);
            if (match is not null)
            {
                matches.Add(match);
            }
        }

        // OrderByDescending 同为稳定排序：同分保注册顺序，用户不会看到列表在两次输入之间乱跳。
        return matches.OrderByDescending(match => match.Score).Take(max).ToArray();
    }

    /// <inheritdoc />
    public CommandMatch? Match(CommandDescriptor command, string? query)
    {
        ArgumentNullException.ThrowIfNull(command);

        var tokens = Tokenize(query);
        return tokens.Count == 0
            ? new CommandMatch(command, 0, CommandMatch.NoHighlights)
            : MatchTokens(command, tokens);
    }

    /// <summary>所有关键词都命中才算命中（AND 语义）；返回累计分数与标题高亮。</summary>
    private static CommandMatch? MatchTokens(CommandDescriptor command, List<string> tokens)
    {
        var score = 0;
        var highlights = new List<TextMatchRange>();

        foreach (var token in tokens)
        {
            if (!TryScoreToken(command, token, out var tokenScore))
            {
                return null;
            }

            score += tokenScore;

            // 高亮独立于计分：即使这个关键词是靠副标题或关键词命中的，只要标题里也有它的字符，
            // 就一并点亮——用户看到的是「为什么这条会出现在列表里」。
            CollectTitleHighlight(command.Title, token, highlights);
        }

        return new CommandMatch(command, score, MergeRanges(highlights));
    }

    /// <summary>给单个关键词取所有字段里的最高分；没命中返回 false。</summary>
    private static bool TryScoreToken(CommandDescriptor command, string token, out int score)
    {
        score = 0;
        var best = int.MinValue;

        if (TryScoreField(command.Title, token, TitleBase, out var titleScore))
        {
            best = Math.Max(best, titleScore);
        }

        if (TryScoreField(command.Category, token, CategoryBase, out var categoryScore))
        {
            best = Math.Max(best, categoryScore);
        }

        if (TryScoreField(command.Subtitle, token, SubtitleBase, out var subtitleScore))
        {
            best = Math.Max(best, subtitleScore);
        }

        foreach (var keyword in command.Keywords)
        {
            if (TryScoreField(keyword, token, KeywordBase, out var keywordScore))
            {
                best = Math.Max(best, keywordScore);
            }
        }

        if (best == int.MinValue)
        {
            return false;
        }

        score = best;
        return true;
    }

    /// <summary>在单个字段里匹配关键词：字段基础分 + 命中方式加成 + 位置奖励 - 空档惩罚。</summary>
    private static bool TryScoreField(string? field, string token, int baseScore, out int score)
    {
        score = 0;
        if (string.IsNullOrEmpty(field) || token.Length == 0 || field.Length < token.Length)
        {
            return false;
        }

        int start;
        var gaps = 0;

        if (string.Equals(field, token, StringComparison.OrdinalIgnoreCase))
        {
            start = 0;
            score = baseScore + ExactBonus + PositionBonus(start);
            return true;
        }

        if (field.StartsWith(token, StringComparison.OrdinalIgnoreCase))
        {
            start = 0;
            score = baseScore + PrefixBonus + PositionBonus(start);
            return true;
        }

        var index = field.IndexOf(token, StringComparison.OrdinalIgnoreCase);
        if (index >= 0)
        {
            score = baseScore + SubstringBonus + PositionBonus(index);
            return true;
        }

        if (!TryMatchSubsequence(field, token, out start, out gaps))
        {
            return false;
        }

        score = baseScore + SubsequenceBonus + PositionBonus(start) - GapPenalty(gaps);
        return true;
    }

    /// <summary>按顺序贪心找齐关键词的所有字符（找到最靠左的一组解，结果确定、可测）。</summary>
    private static bool TryMatchSubsequence(string field, string token, out int start, out int gaps)
    {
        start = 0;
        gaps = 0;

        var tokenIndex = 0;
        var firstMatch = -1;
        var lastMatch = -1;

        for (var i = 0; i < field.Length && tokenIndex < token.Length; i++)
        {
            if (!EqualsIgnoreCase(field[i], token[tokenIndex]))
            {
                continue;
            }

            if (firstMatch < 0)
            {
                firstMatch = i;
            }

            lastMatch = i;
            tokenIndex++;
        }

        if (tokenIndex < token.Length)
        {
            return false;
        }

        start = firstMatch;
        gaps = lastMatch - firstMatch + 1 - token.Length;
        return true;
    }

    /// <summary>把关键词在标题里的命中位置收集成高亮区间（先入段，最后统一排序合并）。</summary>
    private static void CollectTitleHighlight(string title, string token, List<TextMatchRange> ranges)
    {
        if (string.IsNullOrEmpty(title) || token.Length == 0 || title.Length < token.Length)
        {
            return;
        }

        var index = title.IndexOf(token, StringComparison.OrdinalIgnoreCase);
        if (index >= 0)
        {
            ranges.Add(new TextMatchRange(index, token.Length));
            return;
        }

        // 不连续命中：逐个字符成段，相邻的段在 MergeRanges 里会被合并成一段。
        var tokenIndex = 0;
        for (var i = 0; i < title.Length && tokenIndex < token.Length; i++)
        {
            if (!EqualsIgnoreCase(title[i], token[tokenIndex]))
            {
                continue;
            }

            ranges.Add(new TextMatchRange(i, 1));
            tokenIndex++;
        }
    }

    /// <summary>排序并合并重叠/相邻区间；没有区间时返回共享的空数组。</summary>
    private static IReadOnlyList<TextMatchRange> MergeRanges(List<TextMatchRange> ranges)
    {
        if (ranges.Count == 0)
        {
            return CommandMatch.NoHighlights;
        }

        ranges.Sort(static (left, right) => left.Start.CompareTo(right.Start));

        var merged = new List<TextMatchRange>(ranges.Count);
        var current = ranges[0];
        for (var i = 1; i < ranges.Count; i++)
        {
            var next = ranges[i];
            if (next.Start <= current.End)
            {
                var end = Math.Max(current.End, next.End);
                current = new TextMatchRange(current.Start, end - current.Start);
                continue;
            }

            merged.Add(current);
            current = next;
        }

        merged.Add(current);
        return merged;
    }

    /// <summary>命中越靠前越加分（最多 16，避免长标题里靠后的命中被过度奖励）。</summary>
    private static int PositionBonus(int start) => start >= MaxPositionBonus ? 0 : MaxPositionBonus - start;

    /// <summary>子序列的空档越长扣分越多（最多 8 分，保证永远不会把分数扣成负）。</summary>
    private static int GapPenalty(int gaps) => gaps <= 0 ? 0 : Math.Min(gaps, MaxGapPenalty);

    /// <summary>按空白切词并去重（忽略大小写）；超长查询先截断。</summary>
    private static List<string> Tokenize(string? query)
    {
        var tokens = new List<string>();
        if (string.IsNullOrWhiteSpace(query))
        {
            return tokens;
        }

        var text = query.Length > MaxQueryLength ? query[..MaxQueryLength] : query;
        var start = -1;

        // 循环多跑一格（i == text.Length）当作哨兵，省掉结束后的重复收尾。
        for (var i = 0; i <= text.Length; i++)
        {
            var isSeparator = i == text.Length || char.IsWhiteSpace(text[i]);
            if (isSeparator)
            {
                if (start >= 0)
                {
                    AddToken(tokens, text[start..i]);
                    start = -1;
                }

                continue;
            }

            if (start < 0)
            {
                start = i;
            }
        }

        return tokens;
    }

    /// <summary>登记一个关键词；空串与重复项（忽略大小写）丢弃。</summary>
    private static void AddToken(List<string> tokens, string token)
    {
        if (token.Length == 0)
        {
            return;
        }

        foreach (var existing in tokens)
        {
            if (string.Equals(existing, token, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        tokens.Add(token);
    }

    /// <summary>单个字符的大小写不敏感比较（逐字符用不变文化大小写折叠，不用 ToString 分配）。</summary>
    private static bool EqualsIgnoreCase(char left, char right) =>
        left == right || char.ToUpperInvariant(left) == char.ToUpperInvariant(right);
}
