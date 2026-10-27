using System;
using System.Collections.Generic;
using System.Linq;
using CKAN;
using CKAN.Games;
using CKAN.Versioning;
using CkanUI.Services.Models;
using NUnit.Framework;

namespace CkanUI.Services.Tests;

[TestFixture]
public sealed class ModSearchTests
{
    private static GUIMod CreateMockMod(
        string identifier,
        string name,
        string author,
        string description,
        string license = "MIT",
        string localization = "en-us",
        List<string>? tags = null,
        bool isInstalled = false,
        bool isCompatible = true,
        bool isCached = false,
        bool isNew = false,
        bool hasUpdate = false,
        bool hasReplacement = false,
        List<RelationshipDescriptor>? depends = null,
        List<RelationshipDescriptor>? recommends = null,
        List<RelationshipDescriptor>? suggests = null,
        List<RelationshipDescriptor>? conflicts = null,
        List<RelationshipDescriptor>? supports = null)
    {
        var json = $$"""
        {
            "spec_version": 1,
            "identifier": "{{identifier}}",
            "name": "{{name}}",
            "abstract": "{{description}}",
            "author": "{{author}}",
            "version": "1.0.0",
            "download": "https://example.com/mod.zip",
            "license": "{{license}}",
            "localizations": [ "{{localization}}" ]
        }
        """;
        var ckanMod = CkanModule.FromJson(json);
        if (tags != null)
        {
            ckanMod.Tags = tags.ToHashSet();
        }
        if (depends != null) ckanMod.depends = depends;
        if (recommends != null) ckanMod.recommends = recommends;
        if (suggests != null) ckanMod.suggests = suggests;
        if (conflicts != null) ckanMod.conflicts = conflicts;
        if (supports != null) ckanMod.supports = supports;

        return new GUIMod(
            mod: ckanMod,
            installed: isInstalled ? new InstalledModule(null, ckanMod, Array.Empty<string>(), false) : null,
            latestCompatibleMod: isCompatible ? ckanMod : null,
            latestAvailableMod: ckanMod,
            incompatible: !isCompatible,
            isNew: isNew,
            isCached: isCached,
            hasUpdate: hasUpdate,
            hasReplacement: hasReplacement
        );
    }

    // 1. 纯文本关键字匹配 Name
    [Test]
    public void Test01_Keyword_MatchesName()
    {
        var mod = CreateMockMod("MechJeb2", "MechJeb 2", "Sarbian", "Autopilot");
        var search = ModSearch.Parse("MechJeb");
        Assert.That(search.Matches(mod), Is.True);
    }

    // 2. 纯文本关键字匹配 Identifier
    [Test]
    public void Test02_Keyword_MatchesIdentifier()
    {
        var mod = CreateMockMod("KerbalAlarmClock", "Alarm Clock", "TriggerAu", "Time reminder");
        var search = ModSearch.Parse("KerbalAlarm");
        Assert.That(search.Matches(mod), Is.True);
    }

    // 3. 作者搜索 @Author 前缀
    [Test]
    public void Test03_Author_MatchesPrefix()
    {
        var mod = CreateMockMod("MechJeb2", "MechJeb 2", "Sarbian", "Autopilot");
        var search = ModSearch.Parse("@Sarbian");
        Assert.That(search.Matches(mod), Is.True);
    }

    // 4. 作者搜索不匹配
    [Test]
    public void Test04_Author_DoesNotMatch()
    {
        var mod = CreateMockMod("MechJeb2", "MechJeb 2", "Sarbian", "Autopilot");
        var search = ModSearch.Parse("@RoverDude");
        Assert.That(search.Matches(mod), Is.False);
    }

    // 5. 描述搜索 desc: 前缀
    [Test]
    public void Test05_Description_MatchesPrefix()
    {
        var mod = CreateMockMod("USI-LS", "USI Life Support", "RoverDude", "Life support mechanics");
        var search = ModSearch.Parse("desc:support");
        Assert.That(search.Matches(mod), Is.True);
    }

    // 6. 许可证搜索 lic: 前缀
    [Test]
    public void Test06_License_MatchesPrefix()
    {
        var mod = CreateMockMod("TestMod", "Test Mod", "Tester", "Desc", license: "GPL-3.0");
        var search = ModSearch.Parse("lic:GPL");
        Assert.That(search.Matches(mod), Is.True);
    }

    // 7. 本地化搜索 lang: 前缀
    [Test]
    public void Test07_Language_MatchesPrefix()
    {
        var mod = CreateMockMod("TestMod", "Test Mod", "Tester", "Desc", localization: "zh-cn");
        var search = ModSearch.Parse("lang:zh");
        Assert.That(search.Matches(mod), Is.True);
    }

    // 8. 标签搜索 tag: 前缀
    [Test]
    public void Test08_Tag_MatchesPrefix()
    {
        var mod = CreateMockMod("TestMod", "Test Mod", "Tester", "Desc", tags: new List<string> { "parts", "tech-tree" });
        var search = ModSearch.Parse("tag:parts");
        Assert.That(search.Matches(mod), Is.True);
    }

    // 9. is:compatible 过滤
    [Test]
    public void Test09_IsCompatible_True()
    {
        var mod = CreateMockMod("TestMod", "Test Mod", "Tester", "Desc", isCompatible: true);
        var search = ModSearch.Parse("is:compatible");
        Assert.That(search.Matches(mod), Is.True);
    }

    // 10. not:compatible 过滤
    [Test]
    public void Test10_NotCompatible_MatchesIncompatible()
    {
        var mod = CreateMockMod("TestMod", "Test Mod", "Tester", "Desc", isCompatible: false);
        var search = ModSearch.Parse("not:compatible");
        Assert.That(search.Matches(mod), Is.True);
    }

    // 11. is:installed 过滤
    [Test]
    public void Test11_IsInstalled_True()
    {
        var mod = CreateMockMod("TestMod", "Test Mod", "Tester", "Desc", isInstalled: true);
        var search = ModSearch.Parse("is:installed");
        Assert.That(search.Matches(mod), Is.True);
    }

    // 12. not:installed 过滤
    [Test]
    public void Test12_NotInstalled_True()
    {
        var mod = CreateMockMod("TestMod", "Test Mod", "Tester", "Desc", isInstalled: false);
        var search = ModSearch.Parse("not:installed");
        Assert.That(search.Matches(mod), Is.True);
    }

    // 13. is:cached 与 not:cached 过滤
    [Test]
    public void Test13_Cached_Filter()
    {
        var cachedMod = CreateMockMod("CachedMod", "Cached", "Tester", "Desc", isCached: true);
        var uncachedMod = CreateMockMod("UncachedMod", "Uncached", "Tester", "Desc", isCached: false);
        var searchCached = ModSearch.Parse("is:cached");
        var searchUncached = ModSearch.Parse("not:cached");

        Assert.That(searchCached.Matches(cachedMod), Is.True);
        Assert.That(searchCached.Matches(uncachedMod), Is.False);
        Assert.That(searchUncached.Matches(uncachedMod), Is.True);
    }

    // 14. is:upgradeable 过滤
    [Test]
    public void Test14_IsUpgradeable_Filter()
    {
        var upgMod = CreateMockMod("UpgMod", "Upgradeable", "Tester", "Desc", isInstalled: true, hasUpdate: true);
        var noUpgMod = CreateMockMod("NoUpgMod", "No Upgrade", "Tester", "Desc", isInstalled: true, hasUpdate: false);
        var search = ModSearch.Parse("is:upgradeable");

        Assert.That(search.Matches(upgMod), Is.True);
        Assert.That(search.Matches(noUpgMod), Is.False);
    }

    // 15. is:replaceable 过滤
    [Test]
    public void Test15_IsReplaceable_Filter()
    {
        var replMod = CreateMockMod("ReplMod", "Replaceable", "Tester", "Desc", isInstalled: true, hasReplacement: true);
        var noReplMod = CreateMockMod("NoReplMod", "No Replace", "Tester", "Desc", isInstalled: true, hasReplacement: false);
        var search = ModSearch.Parse("is:replaceable");

        Assert.That(search.Matches(replMod), Is.True);
        Assert.That(search.Matches(noReplMod), Is.False);
    }

    // 16. 否定关键字 -Keyword
    [Test]
    public void Test16_Negation_Keyword()
    {
        var modA = CreateMockMod("ModA", "Alpha Mod", "Tester", "Desc");
        var modB = CreateMockMod("ModB", "Beta Mod", "Tester", "Desc");
        var search = ModSearch.Parse("-Alpha");

        Assert.That(search.Matches(modA), Is.False);
        Assert.That(search.Matches(modB), Is.True);
    }

    // 17. 否定作者 -@Author
    [Test]
    public void Test17_Negation_Author()
    {
        var modA = CreateMockMod("ModA", "Alpha", "Sarbian", "Desc");
        var modB = CreateMockMod("ModB", "Beta", "RoverDude", "Desc");
        var search = ModSearch.Parse("-@Sarbian");

        Assert.That(search.Matches(modA), Is.False);
        Assert.That(search.Matches(modB), Is.True);
    }

    // 18. 否定标签 -tag:TagName
    [Test]
    public void Test18_Negation_Tag()
    {
        var modA = CreateMockMod("ModA", "Alpha", "Tester", "Desc", tags: new List<string> { "parts" });
        var modB = CreateMockMod("ModB", "Beta", "Tester", "Desc", tags: new List<string> { "plugin" });
        var search = ModSearch.Parse("-tag:parts");

        Assert.That(search.Matches(modA), Is.False);
        Assert.That(search.Matches(modB), Is.True);
    }

    // 19. 组合搜索：关键字 + 作者 + 状态
    [Test]
    public void Test19_Combination_Keyword_Author_Status()
    {
        var targetMod = CreateMockMod("RealismOverhaul", "Realism Overhaul", "ROTeam", "Full realism overhaul", isCompatible: true);
        var otherMod = CreateMockMod("OtherMod", "Realism Something", "SomeoneElse", "Other mod", isCompatible: true);
        var incompMod = CreateMockMod("RealismIncomp", "Realism Overhaul", "ROTeam", "Full realism", isCompatible: false);

        var search = ModSearch.Parse("Realism @ROTeam is:compatible");

        Assert.That(search.Matches(targetMod), Is.True);
        Assert.That(search.Matches(otherMod), Is.False);
        Assert.That(search.Matches(incompMod), Is.False);
    }

    // 20. 关系搜索 dep: (DependsOn)
    [Test]
    public void Test20_Relationship_DependsOn()
    {
        var depRel = new ModuleRelationshipDescriptor { name = "ModuleManager" };
        var modWithDep = CreateMockMod("ModWithDep", "Mod With Dep", "Tester", "Desc", depends: new List<RelationshipDescriptor> { depRel });
        var modWithoutDep = CreateMockMod("ModWithoutDep", "Mod Without Dep", "Tester", "Desc");

        var search = ModSearch.Parse("dep:ModuleManager");

        Assert.That(search.Matches(modWithDep), Is.True);
        Assert.That(search.Matches(modWithoutDep), Is.False);
    }
}
