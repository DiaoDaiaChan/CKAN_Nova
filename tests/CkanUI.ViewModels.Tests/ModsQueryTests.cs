using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Pages.Mods;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

/// <summary>搜索解析与匹配（03 篇 §3.4 / VM-01）。</summary>
[TestFixture]
public sealed class ModsQueryTests
{
    private static readonly ModRowData Installed = ModsRowFactory.Create(
        "ModuleManager", "Module Manager", "sarbian", installedVersion: "4.2.3", latestVersion: "4.2.3",
        tags: new[] { "plugin", "tools" });

    private static readonly ModRowData NotInstalled = ModsRowFactory.Create(
        "KerbalEngineer", "Kerbal Engineer Redux", "cybutek", latestVersion: "1.1.9",
        tags: new[] { "plugin" });

    [Test]
    public void EmptyQuery_MatchesEverything()
    {
        var query = ModsQuery.Parse("   ");

        Assert.That(query.IsEmpty, Is.True);
        Assert.That(query.Matches(Installed), Is.True);
        Assert.That(query.Matches(NotInstalled), Is.True);
    }

    [Test]
    public void PlainText_MatchesNameIdentifierAndAuthor_CaseInsensitively()
    {
        var query = ModsQuery.Parse("engineer");

        Assert.That(query.Matches(NotInstalled), Is.True);
        Assert.That(query.Matches(Installed), Is.False);

        Assert.That(ModsQuery.Parse("kerbalengineer").Matches(NotInstalled), Is.True);
        Assert.That(ModsQuery.Parse("CYBUTEK").Matches(NotInstalled), Is.True);
    }

    [Test]
    public void AuthorPrefix_MatchesOnlyAuthor()
    {
        var query = ModsQuery.Parse("@sarbian");

        Assert.That(query.Matches(Installed), Is.True);
        Assert.That(query.Matches(NotInstalled), Is.False);
    }

    [Test]
    public void TagPrefix_MatchesTags()
    {
        Assert.That(ModsQuery.Parse("tag:tools").Matches(Installed), Is.True);
        Assert.That(ModsQuery.Parse("tag:tools").Matches(NotInstalled), Is.False);
        Assert.That(ModsQuery.Parse("tag:plugin").Matches(NotInstalled), Is.True);
    }

    [Test]
    public void StateTerms_RespectInstalledState()
    {
        Assert.That(ModsQuery.Parse("is:installed").Matches(Installed), Is.True);
        Assert.That(ModsQuery.Parse("is:installed").Matches(NotInstalled), Is.False);
        Assert.That(ModsQuery.Parse("not:installed").Matches(NotInstalled), Is.True);
        Assert.That(ModsQuery.Parse("not:installed").Matches(Installed), Is.False);
    }

    [Test]
    public void UpgradeableTerm_MatchesOnlyOutdatedInstalledRows()
    {
        var outdated = ModsRowFactory.Create("A", "A", installedVersion: "1.0.0", latestVersion: "2.0.0");
        var query = ModsQuery.Parse("is:upgradeable");

        Assert.That(query.Matches(outdated), Is.True);
        Assert.That(query.Matches(Installed), Is.False);
        Assert.That(query.Matches(NotInstalled), Is.False);
    }

    [Test]
    public void Negation_ExcludesMatchingRows()
    {
        var query = ModsQuery.Parse("-engineer");

        Assert.That(query.Matches(NotInstalled), Is.False);
        Assert.That(query.Matches(Installed), Is.True);
    }

    [Test]
    public void UnsupportedPrefix_IsReported_AndDoesNotBreakOtherTerms()
    {
        var query = ModsQuery.Parse("desc:engineer");

        Assert.That(query.HasUnsupportedTerms, Is.True);
        Assert.That(query.UnsupportedTerms, Has.Count.EqualTo(1));
        Assert.That(query.IsEmpty, Is.True);
        // 不支持的条件不参与匹配，避免"输了一个不认识的前缀就什么都搜不到"。
        Assert.That(query.Matches(NotInstalled), Is.True);
        Assert.That(query.Matches(Installed), Is.True);
    }

    [Test]
    public void MultipleTerms_AreCombinedWithAnd()
    {
        var query = ModsQuery.Parse("@cybutek is:installed");

        Assert.That(query.Matches(NotInstalled), Is.False);
        Assert.That(query.Matches(Installed), Is.False);
    }
}
