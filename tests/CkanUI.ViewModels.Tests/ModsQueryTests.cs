using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Pages.Mods;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

/// <summary>
/// 搜索语法（03 篇 §3.4 / 12 篇 F-20、F-21 / VM-01）。
/// 解析走服务层真实 <c>ModSearchParser</c>（内部即上游 <c>ModSearch.Parse</c>），
/// 页面只消费 <c>ModSearchSpec</c> 并镜像 <c>ModSearch.Matches</c> 的匹配语义。
/// </summary>
[TestFixture]
public sealed class ModsQueryTests
{
    private static readonly ModRowData Installed = ModsRowFactory.Create(
        "ModuleManager", "Module Manager", "sarbian", installedVersion: "4.2.3", latestVersion: "4.2.3",
        tags: new[] { "plugin", "tools" },
        facts: new ModSearchFacts
        {
            Abstract = "Shared mod management library",
            Description = "Fixes part loading and tracks mod versions",
            Licenses = new[] { "MIT" },
            Localizations = new[] { "zh-cn", "de" },
            DependsOn = new[] { "KSP" },
            Labels = new[] { "Favourites" },
            IsCached = true,
        });

    private static readonly ModRowData NotInstalled = ModsRowFactory.Create(
        "KerbalEngineer", "Kerbal Engineer Redux", "cybutek", latestVersion: "1.1.9",
        tags: new[] { "plugin" },
        facts: new ModSearchFacts
        {
            Abstract = "Telescope-grade telemetry readouts",
            Description = "Displays delta-v and orbital data",
            Licenses = new[] { "GPL-3.0" },
            Localizations = new[] { "en-us" },
            DependsOn = new[] { "ModuleManager" },
            Recommends = new[] { "Scatterer" },
            Suggests = new[] { "ClickThroughBlocker" },
            ConflictsWith = new[] { "MechJeb2" },
            Supports = new[] { "Kopernicus" },
            IsCached = false,
        });

    [Test]
    public void EmptyQuery_MatchesEverything()
    {
        var query = Search.Parse("   ");

        Assert.That(query.IsEmpty, Is.True);
        Assert.That(query.HasUnsupportedTerms, Is.False);
        Assert.That(query.Matches(Installed), Is.True);
        Assert.That(query.Matches(NotInstalled), Is.True);
    }

    [Test]
    public void PlainText_MatchesNameAndIdentifier_CaseInsensitively()
    {
        var query = Search.Parse("engineer");

        Assert.That(query.Matches(NotInstalled), Is.True);
        Assert.That(query.Matches(Installed), Is.False);

        Assert.That(Search.Parse("kerbalengineer").Matches(NotInstalled), Is.True);
        Assert.That(Search.Parse("KERBALENGINEER").Matches(NotInstalled), Is.True);

        // 语义变更（对齐上游 ModSearch）：自由词只搜名称/标识符，作者必须写 @作者。
        Assert.That(Search.Parse("cybutek").Matches(NotInstalled), Is.False);
        Assert.That(Search.Parse("@cybutek").Matches(NotInstalled), Is.True);
    }

    [Test]
    public void AuthorPrefix_MatchesOnlyAuthor()
    {
        var query = Search.Parse("@sarbian");

        Assert.That(query.Matches(Installed), Is.True);
        Assert.That(query.Matches(NotInstalled), Is.False);
        Assert.That(query.HasUnsupportedTerms, Is.False);
    }

    [Test]
    public void DescriptionPrefix_SearchesAbstractAndDescription()
    {
        // 旧用例曾断言 desc: 属于"本页暂不支持"，现在解析委托模型层，属于正式支持。
        var query = Search.Parse("desc:telescope");

        Assert.That(query.HasUnsupportedTerms, Is.False);
        Assert.That(query.Matches(NotInstalled), Is.True);
        Assert.That(query.Matches(Installed), Is.False);

        Assert.That(Search.Parse("desc:loading").Matches(Installed), Is.True);
        Assert.That(Search.Parse("desc:loading").Matches(NotInstalled), Is.False);
    }

    [Test]
    public void LicensePrefix_MatchesLicenses()
    {
        Assert.That(Search.Parse("lic:MIT").Matches(Installed), Is.True);
        Assert.That(Search.Parse("lic:MIT").Matches(NotInstalled), Is.False);
        // 上游语义是 StartsWith + 忽略大小写，"gpl" 能命中 "GPL-3.0"。
        Assert.That(Search.Parse("lic:gpl").Matches(NotInstalled), Is.True);
        Assert.That(Search.Parse("lic:gpl").HasUnsupportedTerms, Is.False);
    }

    [Test]
    public void LanguagePrefix_MatchesLocalizations()
    {
        Assert.That(Search.Parse("lang:zh").Matches(Installed), Is.True);
        Assert.That(Search.Parse("lang:zh").Matches(NotInstalled), Is.False);
        Assert.That(Search.Parse("lang:en").Matches(NotInstalled), Is.True);
    }

    [Test]
    public void DependsPrefix_MatchesDependencyNames()
    {
        // dep: 需要实例上下文，但事实由服务层写进行里，因此是真接线而不是"不支持"。
        var query = Search.Parse("dep:Module");

        Assert.That(query.HasUnsupportedTerms, Is.False);
        Assert.That(query.Matches(NotInstalled), Is.True);
        Assert.That(query.Matches(Installed), Is.False);
    }

    [Test]
    public void RecommendsPrefix_MatchesRecommends()
    {
        var query = Search.Parse("rec:Scatterer");

        Assert.That(query.HasUnsupportedTerms, Is.False);
        Assert.That(query.Matches(NotInstalled), Is.True);
        Assert.That(query.Matches(Installed), Is.False);
    }

    [Test]
    public void SuggestsPrefix_MatchesSuggests()
    {
        var query = Search.Parse("sug:Click");

        Assert.That(query.HasUnsupportedTerms, Is.False);
        Assert.That(query.Matches(NotInstalled), Is.True);
        Assert.That(query.Matches(Installed), Is.False);
    }

    [Test]
    public void ConflictsPrefix_MatchesConflicts()
    {
        var query = Search.Parse("conf:MechJeb");

        Assert.That(query.HasUnsupportedTerms, Is.False);
        Assert.That(query.Matches(NotInstalled), Is.True);
        Assert.That(query.Matches(Installed), Is.False);
    }

    [Test]
    public void SupportsPrefix_MatchesSupports()
    {
        var query = Search.Parse("sup:Kopernicus");

        Assert.That(query.HasUnsupportedTerms, Is.False);
        Assert.That(query.Matches(NotInstalled), Is.True);
        Assert.That(query.Matches(Installed), Is.False);
    }

    [Test]
    public void TagPrefix_MatchesTags()
    {
        Assert.That(Search.Parse("tag:tools").Matches(Installed), Is.True);
        Assert.That(Search.Parse("tag:tools").Matches(NotInstalled), Is.False);
        Assert.That(Search.Parse("tag:plugin").Matches(NotInstalled), Is.True);
        Assert.That(Search.Parse("tag:tools").HasUnsupportedTerms, Is.False);
    }

    [Test]
    public void LabelPrefix_UsesModuleLabelList()
    {
        // 对齐上游 ModuleLabelList：label:Favourites 命中"收藏"标签的模组。
        Assert.That(Search.Parse("label:Favourites").Matches(Installed), Is.True);
        Assert.That(Search.Parse("label:Favourites").Matches(NotInstalled), Is.False);
        // 标签名去空格、忽略大小写比较。
        Assert.That(Search.Parse("label:favourites").Matches(Installed), Is.True);
        // 三个默认标签里没有的（Hidden/Held）一个都不命中，但不报"无法解析"。
        Assert.That(Search.Parse("label:Hidden").Matches(Installed), Is.False);
        Assert.That(Search.Parse("label:Hidden").HasUnsupportedTerms, Is.False);
        // -label: 取反。
        Assert.That(Search.Parse("-label:Favourites").Matches(Installed), Is.False);
        Assert.That(Search.Parse("-label:Favourites").Matches(NotInstalled), Is.True);
    }

    [Test]
    public void StateTerms_RespectInstalledState()
    {
        Assert.That(Search.Parse("is:installed").Matches(Installed), Is.True);
        Assert.That(Search.Parse("is:installed").Matches(NotInstalled), Is.False);
        Assert.That(Search.Parse("not:installed").Matches(NotInstalled), Is.True);
        Assert.That(Search.Parse("not:installed").Matches(Installed), Is.False);
    }

    [Test]
    public void CachedState_UsesCacheFacts()
    {
        Assert.That(Search.Parse("is:cached").Matches(Installed), Is.True);
        Assert.That(Search.Parse("is:cached").Matches(NotInstalled), Is.False);
        Assert.That(Search.Parse("not:cached").Matches(NotInstalled), Is.True);
        Assert.That(Search.Parse("not:cached").Matches(Installed), Is.False);
        Assert.That(Search.Parse("not:cached").HasUnsupportedTerms, Is.False);
    }

    [Test]
    public void UpgradeableTerm_MatchesOnlyOutdatedInstalledRows()
    {
        var outdated = ModsRowFactory.Create("A", "A", installedVersion: "1.0.0", latestVersion: "2.0.0");
        var query = Search.Parse("is:upgradeable");

        Assert.That(query.Matches(outdated), Is.True);
        Assert.That(query.Matches(Installed), Is.False);
        Assert.That(query.Matches(NotInstalled), Is.False);
    }

    [Test]
    public void Negation_ExcludesMatchingRows()
    {
        var query = Search.Parse("-engineer");

        Assert.That(query.Matches(NotInstalled), Is.False);
        Assert.That(query.Matches(Installed), Is.True);
    }

    [Test]
    public void FreeTextWithExclusionAndMultipleConditions_CombineWithAnd()
    {
        // 自由词 + -排除：名字里没有 engineer 的行。
        var exclusion = Search.Parse("-engineer");
        Assert.That(exclusion.Matches(Installed), Is.True);
        Assert.That(exclusion.Matches(NotInstalled), Is.False);

        // 多条件 AND：作者对、离线安装状态对、标签对。
        var combined = Search.Parse("@cybutek tag:plugin not:installed");
        Assert.That(combined.Matches(NotInstalled), Is.True);
        Assert.That(combined.Matches(Installed), Is.False);

        // 任意一个条件不满足即整体不匹配。
        Assert.That(Search.Parse("@cybutek is:installed").Matches(NotInstalled), Is.False);
    }

    [Test]
    public void MultipleTerms_AreCombinedWithAnd()
    {
        var query = Search.Parse("@cybutek is:installed");

        Assert.That(query.Matches(NotInstalled), Is.False);
        Assert.That(query.Matches(Installed), Is.False);
    }

    [Test]
    public void UnknownState_IsReportedWithReason()
    {
        var query = Search.Parse("is:bogus");

        Assert.That(query.HasUnsupportedTerms, Is.True);
        Assert.That(query.UnsupportedTerms, Is.EquivalentTo(new[] { "is:bogus" }));
        Assert.That(query.Unsupported[0].Reason, Is.EqualTo(ModSearchUnsupportedReason.UnknownState));
        Assert.That(query.UnsupportedMessage, Does.Contain("当前无法解析：is:bogus"));
        // 无法解析的条件不参与匹配，避免"输了一个不认识的状态就什么都搜不到"。
        Assert.That(query.Matches(Installed), Is.True);
        Assert.That(query.Matches(NotInstalled), Is.True);
    }

    [Test]
    public void UnknownPrefix_IsReportedWithReason()
    {
        var query = Search.Parse("foo:bar");

        Assert.That(query.HasUnsupportedTerms, Is.True);
        Assert.That(query.Unsupported[0].Reason, Is.EqualTo(ModSearchUnsupportedReason.UnknownPrefix));
        Assert.That(query.UnsupportedMessage, Does.Contain("foo:bar"));
        Assert.That(query.UnsupportedMessage, Does.Contain("未知的搜索前缀"));
    }

    [Test]
    public void SupportedSyntax_ReportsNothing_AsUnsupported()
    {
        const string text =
            "desc:mod lic:MIT lang:zh dep:Module rec:Scatterer sug:Click conf:MechJeb sup:Kopernicus " +
            "tag:plugin label:Favourites is:installed not:cached @sarbian engineer";

        var query = Search.Parse(text);

        Assert.That(query.HasUnsupportedTerms, Is.False, query.UnsupportedMessage);
        Assert.That(query.UnsupportedTerms, Is.Empty);
        Assert.That(query.UnsupportedMessage, Is.Empty);
    }

    [Test]
    public void UnsupportedTerms_DoNotBreakTheRestOfTheQuery()
    {
        var query = Search.Parse("is:bogus tag:plugin");

        Assert.That(query.HasUnsupportedTerms, Is.True);
        Assert.That(query.Matches(Installed), Is.True);
        Assert.That(query.Matches(NotInstalled), Is.True);

        // 另一半条件仍然生效。
        Assert.That(Search.Parse("is:bogus tag:tools").Matches(NotInstalled), Is.False);
        Assert.That(Search.Parse("is:bogus tag:tools").Matches(Installed), Is.True);
    }

    [Test]
    public void UnparsedFallback_NeverFilters_AndReportsEveryTerm()
    {
        // 宿主没接线解析器时的降级：不过滤，但每个词都要在状态栏被说明。
        var query = ModsQuery.Unparsed("engineer tag:plugin");

        Assert.That(query.HasUnsupportedTerms, Is.True);
        Assert.That(query.UnsupportedTerms, Is.EquivalentTo(new[] { "engineer", "tag:plugin" }));
        Assert.That(query.Unsupported[0].Reason, Is.EqualTo(ModSearchUnsupportedReason.InstanceContext));
        Assert.That(query.Matches(Installed), Is.True);
        Assert.That(query.Matches(NotInstalled), Is.True);
        Assert.That(query.UnsupportedMessage, Does.Contain("需要实例上下文"));
    }
}
