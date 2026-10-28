using System;
using System.Collections.Generic;
using System.Linq;
using CkanUI.Services.Abstractions.Logs;
using CkanUI.ViewModels.Pages.Logs;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

/// <summary>三段过滤条 + 搜索框（14 篇 §4.2①②）。</summary>
[TestFixture]
public sealed class LogFilterViewModelTests
{
    private static LogFilterViewModel Create() => new();

    [Test]
    public void Constructor_SelectsSpecDefaults()
    {
        var filter = Create();

        Assert.That(filter.Levels.Count, Is.EqualTo(Enum.GetValues<LogLevel>().Length));
        Assert.That(filter.Families.Count, Is.EqualTo(Enum.GetValues<LogFamily>().Length));
        Assert.That(filter.Sources.Count, Is.EqualTo(Enum.GetValues<LogSourceKind>().Length));

        Assert.That(
            filter.SelectedLevels,
            Is.EquivalentTo(new[] { LogLevel.Info, LogLevel.Warn, LogLevel.Error, LogLevel.Fatal, LogLevel.User }),
            "级别默认 Info 及以上（Trace/Debug 默认关掉）");
        Assert.That(filter.SelectedFamilies.Count, Is.EqualTo(Enum.GetValues<LogFamily>().Length), "族默认全选");
        Assert.That(
            filter.SelectedSources,
            Is.EquivalentTo(new[] { LogSourceKind.Log4Net, LogSourceKind.User, LogSourceKind.Exception, LogSourceKind.Child }),
            "源默认只开四路采集");
    }

    [Test]
    public void IsDefault_IsTrueRightAfterConstructionAndReset()
    {
        var filter = Create();
        Assert.That(filter.IsDefault, Is.True);

        filter.SearchText = "boom";
        Assert.That(filter.IsDefault, Is.False);

        filter.Reset();
        Assert.That(filter.IsDefault, Is.True);
        Assert.That(filter.SearchText, Is.Empty);
    }

    [Test]
    public void IsDefault_BecomesFalseWhenAFacetIsToggled()
    {
        var filter = Create();
        filter.Levels.Single(option => option.Level == LogLevel.Warn).IsSelected = false;

        Assert.That(filter.IsDefault, Is.False);
        Assert.That(filter.MatchesNothing, Is.False);
    }

    [Test]
    public void MatchesNothing_IsTrueWhenAnySegmentIsCleared()
    {
        var filter = Create();
        foreach (var option in filter.Levels)
        {
            option.IsSelected = false;
        }

        Assert.That(filter.MatchesNothing, Is.True);
        Assert.That(filter.Description, Is.EqualTo("当前过滤条件不会命中任何日志"));
    }

    [Test]
    public void Changed_IsRaisedForSearchTextAndFacetToggles()
    {
        var filter = Create();
        var raised = 0;
        filter.Changed += (_, _) => raised++;

        filter.SearchText = "net";
        filter.Families.Single(option => option.Family == LogFamily.Net).IsSelected = false;

        Assert.That(raised, Is.EqualTo(2));
    }

    [Test]
    public void BuildQuery_DropsSegmentThatIsFullySelected()
    {
        var filter = Create();
        foreach (var option in filter.Levels)
        {
            option.IsSelected = true;
        }

        var query = filter.BuildQuery();

        Assert.That(query.Levels, Is.Null, "全选等于不限，不要给查询服务添无意义的过滤");
        Assert.That(query.Families, Is.Null);
        Assert.That(query.Kinds, Is.Not.Null.And.Count.EqualTo(4), "源默认只勾 4 项，必须下推");
    }

    [Test]
    public void BuildQuery_PushesNarrowedSegmentsAndTake()
    {
        var filter = Create();
        filter.ShowOnlyErrors();

        var query = filter.BuildQuery(123);

        Assert.That(query.Take, Is.EqualTo(123));
        Assert.That(query.Levels, Is.EquivalentTo(new[] { LogLevel.Error, LogLevel.Fatal }));
        Assert.That(query.Kinds, Is.Null, "「仅错误」把源全开，等于不限");
    }

    [Test]
    public void BuildQuery_AppliesSearchSyntax()
    {
        var filter = Create();
        filter.SearchText = "族:net mod:RealChute";

        var query = filter.BuildQuery();

        Assert.That(query.Families, Is.EquivalentTo(new[] { LogFamily.Net }));
        Assert.That(query.Tags, Is.Not.Null);
        Assert.That(query.Tags!["mod"], Is.EqualTo("RealChute"));
    }

    [Test]
    public void BuildQuery_LetsSearchSyntaxNarrowWithinSelectedFacets()
    {
        var filter = Create();
        filter.SearchText = "级别:error";

        var query = filter.BuildQuery();

        Assert.That(query.Levels, Is.EquivalentTo(new[] { LogLevel.Error }), "搜索框的级别条件在勾选范围内收窄");
    }

    [Test]
    public void BuildQuery_FallsBackToSearchSyntaxWhenFacetsAndSyntaxConflict()
    {
        var filter = Create();
        foreach (var option in filter.Levels)
        {
            option.IsSelected = option.Level == LogLevel.Warn;
        }

        filter.SearchText = "级别:error";
        var query = filter.BuildQuery();

        // 两边互斥时以搜索框为准：返回空集合在 LogQuery 里表示「不限」，反而会放开全部日志。
        Assert.That(query.Levels, Is.EquivalentTo(new[] { LogLevel.Error }));
    }

    [Test]
    public void ShowOnlyErrors_KeepsOnlyErrorLevelsAndAllSources()
    {
        var filter = Create();
        filter.ShowOnlyErrors();

        Assert.That(filter.SelectedLevels, Is.EquivalentTo(new[] { LogLevel.Error, LogLevel.Fatal }));
        Assert.That(filter.SelectedSources.Count, Is.EqualTo(Enum.GetValues<LogSourceKind>().Length));
        Assert.That(filter.MatchesNothing, Is.False);
    }

    [Test]
    public void Reset_RestoresSpecDefaults()
    {
        var filter = Create();
        filter.ShowOnlyErrors();
        filter.SearchText = "boom";

        filter.Reset();

        Assert.That(filter.SelectedLevels.Count, Is.EqualTo(5));
        Assert.That(filter.SelectedFamilies.Count, Is.EqualTo(Enum.GetValues<LogFamily>().Length));
        Assert.That(filter.SelectedSources.Count, Is.EqualTo(4));
        Assert.That(filter.SearchText, Is.Empty);
    }

    [Test]
    public void ClearSearchCommand_EmptiesSearchText()
    {
        var filter = Create();
        filter.SearchText = "boom";

        filter.ClearSearchCommand.Execute(null);

        Assert.That(filter.SearchText, Is.Empty);
    }

    [Test]
    public void ResetCommand_RestoresDefaults()
    {
        var filter = Create();
        filter.ShowOnlyErrors();

        filter.ResetCommand.Execute(null);

        Assert.That(filter.IsDefault, Is.True);
    }

    [Test]
    public void Description_DescribesCurrentQueryInChinese()
    {
        var filter = Create();
        filter.ShowOnlyErrors();

        Assert.That(filter.Description, Does.Contain("级别:"));
        Assert.That(filter.Description, Does.Contain("错误"));
        Assert.That(filter.Description, Does.Not.Contain("源:"), "仅错误会把源全开，全选等于不限，源段应消失");
    }

    [Test]
    public void Description_UsesChineseLabelsForSearchSyntax()
    {
        var filter = Create();
        filter.SearchText = "族:net 级别:error mod:RealChute";

        var description = filter.Description;

        Assert.That(description, Does.Contain("级别:错误"), "语法里的 error 要翻译成中文");
        Assert.That(description, Does.Contain("族:网络"));
        Assert.That(description, Does.Contain("mod:RealChute"));
        Assert.That(description, Does.Not.Contain("族:net"), "族名不该以枚举名出现");
    }

    [Test]
    public void Description_IsAllLogsByDefault()
    {
        var filter = Create();

        Assert.That(filter.Description, Does.Contain("级别:信息"), "默认级别从 Info 起");
        Assert.That(filter.Description, Does.Contain("源:内核日志"), "默认源是内核日志/用户消息/异常/子进程");
        Assert.That(filter.Description, Does.Not.Contain("族:"), "默认族全选，等于不限");
    }

    [Test]
    public void Options_ExposeLabelsAndGlyphs()
    {
        var filter = Create();

        Assert.That(filter.Levels.All(option => option.Label.Length > 0), Is.True);
        Assert.That(filter.Levels.All(option => option.Glyph.Length > 0), Is.True);
        Assert.That(filter.Families.All(option => option.Label.Length > 0), Is.True);
        Assert.That(filter.Sources.All(option => option.Label.Length > 0), Is.True);
    }
}
