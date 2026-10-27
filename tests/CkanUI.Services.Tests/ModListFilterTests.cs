using CKAN;
using CkanUI.Services.Models;
using NUnit.Framework;

namespace CkanUI.Services.Tests;

/// <summary>
/// 单元测试：ModIndexOperations 过滤与投影纯函数（MOD-04 / 04 篇 §4、§5）。
/// 纯内存对象，无 WinForms DataGridView 依赖。
/// </summary>
[TestFixture]
public sealed class ModListFilterTests
{
    private static GUIMod CreateMockMod(
        string identifier,
        string name,
        string author,
        string description,
        bool isCompatible = true,
        bool isInstalled = false,
        bool isCached = false,
        bool isNew = false,
        bool hasUpdate = false)
    {
        var json = $$"""
        {
            "spec_version": 1,
            "identifier": "{{identifier}}",
            "name": "{{name}}",
            "abstract": "{{description}}",
            "author": "{{author}}",
            "version": "1.0.0",
            "download": "https://example.com/{{identifier}}.zip"
        }
        """;
        var ckanMod = CkanModule.FromJson(json);
        return new GUIMod(
            mod: ckanMod,
            installed: isInstalled ? new InstalledModule(null, ckanMod, Array.Empty<string>(), false) : null,
            latestCompatibleMod: isCompatible ? ckanMod : null,
            latestAvailableMod: ckanMod,
            incompatible: !isCompatible,
            isNew: isNew,
            isCached: isCached,
            hasUpdate: hasUpdate,
            hasReplacement: false
        );
    }

    [Test]
    public void FilterMods_WithNullOrEmptySearches_ReturnsAllMods()
    {
        var mods = new[]
        {
            CreateMockMod("Mod1", "Alpha", "AuthorA", "Desc 1"),
            CreateMockMod("Mod2", "Beta", "AuthorB", "Desc 2"),
        };

        var resNull = ModIndexOperations.FilterMods(mods, null);
        Assert.That(resNull, Has.Count.EqualTo(2));

        var resEmpty = ModIndexOperations.FilterMods(mods, Array.Empty<ModSearch>());
        Assert.That(resEmpty, Has.Count.EqualTo(2));
    }

    [Test]
    public void FilterMods_WithKeywordSearch_FiltersCorrectly()
    {
        var mods = new[]
        {
            CreateMockMod("MechJeb2", "MechJeb 2", "Sarbian", "Autopilot"),
            CreateMockMod("AlarmClock", "Alarm Clock", "TriggerAu", "Timer"),
        };

        var search = ModSearch.Parse("MechJeb");
        var res = ModIndexOperations.FilterMods(mods, new[] { search });

        Assert.That(res, Has.Count.EqualTo(1));
        Assert.That(res[0].Identifier, Is.EqualTo("MechJeb2"));
    }

    [Test]
    public void FilterMods_MultipleSearches_ActsAsOrFilter()
    {
        var mods = new[]
        {
            CreateMockMod("ModA", "Alpha", "AuthorA", "Desc"),
            CreateMockMod("ModB", "Beta", "AuthorB", "Desc"),
            CreateMockMod("ModC", "Gamma", "AuthorC", "Desc"),
        };

        var searchA = ModSearch.Parse("Alpha");
        var searchB = ModSearch.Parse("Beta");
        var res = ModIndexOperations.FilterMods(mods, new[] { searchA, searchB });

        Assert.That(res, Has.Count.EqualTo(2));
        Assert.That(res.Select(m => m.Identifier), Is.EquivalentTo(new[] { "ModA", "ModB" }));
    }

    [Test]
    public void SortMods_ByNameAscendingAndDescending()
    {
        var mods = new[]
        {
            CreateMockMod("ZMod", "Zeta", "Author", "Desc"),
            CreateMockMod("AMod", "Alpha", "Author", "Desc"),
            CreateMockMod("MMod", "Mu", "Author", "Desc"),
        };

        var sortedAsc = ModIndexOperations.SortMods(mods, "Name", ascending: true).ToList();
        Assert.That(sortedAsc[0].Name, Is.EqualTo("Alpha"));
        Assert.That(sortedAsc[1].Name, Is.EqualTo("Mu"));
        Assert.That(sortedAsc[2].Name, Is.EqualTo("Zeta"));

        var sortedDesc = ModIndexOperations.SortMods(mods, "Name", ascending: false).ToList();
        Assert.That(sortedDesc[0].Name, Is.EqualTo("Zeta"));
        Assert.That(sortedDesc[1].Name, Is.EqualTo("Mu"));
        Assert.That(sortedDesc[2].Name, Is.EqualTo("Alpha"));
    }

    [Test]
    public void ProjectToRows_ConvertsGuiModToPureModRowData()
    {
        var mod = CreateMockMod("TestMod", "Test Mod", "Tester", "A cool mod", isCompatible: true, isInstalled: true);
        var rows = ModIndexOperations.ProjectToRows(new[] { mod });

        Assert.That(rows, Has.Count.EqualTo(1));
        var row = rows[0];
        Assert.That(row.Identifier, Is.EqualTo("TestMod"));
        Assert.That(row.Name, Is.EqualTo("Test Mod"));
        Assert.That(row.Author, Is.EqualTo("Tester"));
        Assert.That(row.IsInstalled, Is.True);
        Assert.That(row.IsCompatible, Is.True);
    }
}

