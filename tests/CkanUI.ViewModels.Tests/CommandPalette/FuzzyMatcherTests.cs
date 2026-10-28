using CkanUI.Services.Abstractions.Commands;
using CkanUI.Services.Commands;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests.CommandPalette;

/// <summary>
/// 命令匹配算法（纯函数）：命中方式权重、AND 语义、排序、上限与标题高亮。
/// 断言以「相对先后」为主、少数用例断言精确分数，这样调整权重时不会满屏红。
/// </summary>
[TestFixture]
public sealed class FuzzyMatcherTests
{
    private static CommandDescriptor Cmd(
        string id,
        string title,
        string category = "常规",
        string subtitle = "",
        string[]? keywords = null,
        string shortcutText = "",
        bool isEnabled = true) =>
        new(id, title)
        {
            Category = category,
            Subtitle = subtitle,
            Keywords = keywords ?? Array.Empty<string>(),
            ShortcutText = shortcutText,
            IsEnabled = isEnabled,
        };

    [Test]
    public void Search_EmptyCommandList_ReturnsEmpty()
    {
        var matcher = new FuzzyMatcher();

        Assert.That(
            matcher.Search(Array.Empty<CommandDescriptor>(), "日志"),
            Is.Empty,
            "空目录直接返回空结果，不做任何打分");
    }

    [Test]
    public void Search_EmptyQuery_ReturnsAllOrderedByCategory()
    {
        var matcher = new FuzzyMatcher();
        var commands = new[]
        {
            Cmd("c1", "第一条", "beta"),
            Cmd("c2", "第二条", "Alpha"),
            Cmd("c3", "第三条", "Alpha"),
        };

        var results = matcher.Search(commands, string.Empty);

        Assert.That(results.Count, Is.EqualTo(3), "空查询 = 浏览态，列出全部命令");
        Assert.That(
            results.Select(result => result.Command.Id),
            Is.EqualTo(new[] { "c2", "c3", "c1" }),
            "按分类名不区分大小写排序，同分类保持注册顺序");
    }

    [Test]
    public void Search_EmptyQuery_ScoreZeroAndNoHighlights()
    {
        var matcher = new FuzzyMatcher();

        var results = matcher.Search(new[] { Cmd("c1", "设置", "界面") }, "   ");

        Assert.That(results[0].Score, Is.EqualTo(0), "浏览态不打分");
        Assert.That(results[0].TitleHighlights, Is.Empty, "浏览态不亮高亮");
    }

    [Test]
    public void Search_ExactTitleOutranksPrefixAndSubstring()
    {
        var matcher = new FuzzyMatcher();
        var commands = new[]
        {
            Cmd("substring", "查看日志"),
            Cmd("exact", "日志"),
            Cmd("prefix", "日志目录"),
        };

        var results = matcher.Search(commands, "日志");

        Assert.That(
            results.Select(result => result.Command.Id),
            Is.EqualTo(new[] { "exact", "prefix", "substring" }),
            "完全相同 > 前缀 > 连续子串");
        Assert.That(results[0].Score, Is.EqualTo(1516), "标题 1000 + 完全相同 500 + 位置奖励 16");
        Assert.That(results[1].Score, Is.EqualTo(1316), "标题 1000 + 前缀 300 + 位置奖励 16");
        Assert.That(results[2].Score, Is.EqualTo(1164), "标题 1000 + 子串 150 + 位置奖励 (16 - 2)");
    }

    [Test]
    public void Search_TitleHitOutranksKeywordHit()
    {
        var matcher = new FuzzyMatcher();
        var commands = new[]
        {
            Cmd("keyword", "安装", keywords: new[] { "日志" }),
            Cmd("title", "日志"),
        };

        var results = matcher.Search(commands, "日志");

        Assert.That(
            results.Select(result => result.Command.Id),
            Is.EqualTo(new[] { "title", "keyword" }),
            "同一个词只取命中的最好字段，标题命中压过关键词命中");
        Assert.That(results[0].Score, Is.GreaterThan(results[1].Score));
    }

    [Test]
    public void Search_RequiresEveryToken_AndAllowsDifferentFields()
    {
        var matcher = new FuzzyMatcher();
        var command = Cmd("nav:logs", "打开日志目录", keywords: new[] { "log" });

        Assert.That(
            matcher.Search(new[] { command }, "日志 log").Count,
            Is.EqualTo(1),
            "两个词分别命中标题与关键词，仍算命中（AND 语义）");
        Assert.That(
            matcher.Search(new[] { command }, "日志 不存在").Count,
            Is.EqualTo(0),
            "只要有一个词完全没命中，整条命令就不出现");
    }

    [Test]
    public void Search_RespectsLimit()
    {
        var matcher = new FuzzyMatcher();
        var commands = Enumerable.Range(0, 5)
            .Select(index => Cmd("c" + index, "命令" + index))
            .ToArray();

        var results = matcher.Search(commands, "命令", 2);

        Assert.That(results.Count, Is.EqualTo(2), "limit 限制返回条数");
    }

    [Test]
    public void Search_NonPositiveLimit_FallsBackToDefaultLimit()
    {
        var matcher = new FuzzyMatcher();
        var commands = Enumerable.Range(0, 3)
            .Select(index => Cmd("c" + index, "命令" + index))
            .ToArray();

        Assert.That(matcher.Search(commands, "命令", 0).Count, Is.EqualTo(3), "limit <= 0 时退回默认上限 50");
        Assert.That(FuzzyMatcher.DefaultLimit, Is.EqualTo(50));
    }

    [Test]
    public void Search_DuplicateTokens_AreDeduplicatedIgnoringCase()
    {
        var matcher = new FuzzyMatcher();
        var command = Cmd("nav:logs", "安装", keywords: new[] { "log" });

        var single = matcher.Search(new[] { command }, "log");
        var duplicated = matcher.Search(new[] { command }, "  log   LOG  log ");

        Assert.That(duplicated[0].Score, Is.EqualTo(single[0].Score), "重复词（忽略大小写）只算一次，分数不该翻倍");
    }

    [Test]
    public void Search_EqualScores_KeepRegistrationOrder()
    {
        var matcher = new FuzzyMatcher();
        var commands = new[] { Cmd("first", "重复命令"), Cmd("second", "重复命令") };

        var results = matcher.Search(commands, "重复");

        Assert.That(
            results.Select(result => result.Command.Id),
            Is.EqualTo(new[] { "first", "second" }),
            "同分时由稳定排序保留注册顺序，列表不会在两次输入之间乱跳");
    }

    [Test]
    public void Search_VeryLongQuery_DoesNotThrow()
    {
        var matcher = new FuzzyMatcher();
        var query = new string('a', FuzzyMatcher.MaxQueryLength + 40) + " 日志";

        Assert.DoesNotThrow(() => matcher.Search(new[] { Cmd("c1", "日志") }, query));
    }

    [Test]
    public void Match_EmptyQuery_ReturnsZeroScoreMatch()
    {
        var matcher = new FuzzyMatcher();
        var command = Cmd("c1", "设置");

        var match = matcher.Match(command, null);

        Assert.That(match, Is.Not.Null);
        Assert.That(match!.Score, Is.EqualTo(0));
        Assert.That(match.TitleHighlights, Is.Empty);
    }

    [Test]
    public void Match_NoFieldHit_ReturnsNull()
    {
        var matcher = new FuzzyMatcher();

        Assert.That(matcher.Match(Cmd("c1", "设置"), "zzz"), Is.Null, "没有任何字段命中时返回 null");
    }

    [Test]
    public void Match_ContiguousTitleHit_ProducesSingleRange()
    {
        var matcher = new FuzzyMatcher();

        var match = matcher.Match(Cmd("action:open-log-folder", "打开日志目录"), "日志");

        Assert.That(match, Is.Not.Null);
        Assert.That(match!.TitleHighlights.Count, Is.EqualTo(1), "连续命中合并成一段");
        Assert.That(match.TitleHighlights[0], Is.EqualTo(new TextMatchRange(2, 2)));
    }

    [Test]
    public void Match_SubsequenceTitleHit_ProducesPerCharacterRanges()
    {
        var matcher = new FuzzyMatcher();

        var match = matcher.Match(Cmd("c1", "Settings"), "stg");

        Assert.That(match, Is.Not.Null);
        Assert.That(match!.Score, Is.EqualTo(1062), "标题 1000 + 子序列 50 + 位置奖励 16 - 空档 4");
        Assert.That(
            match.TitleHighlights.Select(range => range.Start),
            Is.EqualTo(new[] { 0, 2, 6 }),
            "不连续命中逐字符成段（段间距大于 1 时不合并）");
    }

    [Test]
    public void Match_CategoryOrSubtitleHit_StillCounts()
    {
        var matcher = new FuzzyMatcher();
        var command = Cmd("c1", "导出", "界面", "把诊断信息打包");

        var match = matcher.Match(command, "诊断");

        Assert.That(match, Is.Not.Null, "副标题参与匹配（权重最低）");
        Assert.That(match!.Score, Is.GreaterThan(0));
        Assert.That(match.TitleHighlights, Is.Empty, "副标题命中不会给标题加高亮");
    }
}
