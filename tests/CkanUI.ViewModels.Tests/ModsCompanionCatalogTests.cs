using CkanUI.ViewModels.Pages.Mods;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

/// <summary>
/// 联动模组清单（13 篇 F-17 配套）：插件与模组的对应关系是页面声明的静态表，
/// 这里钉住"清单内容、大小写不敏感、未知标识符不误判"三件事。
/// </summary>
[TestFixture]
public sealed class ModsCompanionCatalogTests
{
    [Test]
    public void Catalog_ListsThePluginsThatReadGameDataDirectories()
    {
        var identifiers = ModsCompanionCatalog.All.Select(link => link.ModIdentifier).ToArray();

        Assert.That(identifiers, Is.EquivalentTo(new[]
        {
            "kOS",
            "RP-1",
            "KSPPerformanceProfiler",
            "Soundtrack",
            "SoundtrackEditor",
            "SoundtrackEditorForked",
        }));
    }

    [Test]
    public void Catalog_PluginIdsAndNamesMatchTheBundledPlugins()
    {
        var byMod = ModsCompanionCatalog.All.ToDictionary(link => link.ModIdentifier, StringComparer.Ordinal);

        Assert.That(byMod["kOS"].PluginId, Is.EqualTo("com.ckanui.kos"));
        Assert.That(byMod["kOS"].PluginName, Is.EqualTo("kOS 脚本"));
        Assert.That(byMod["RP-1"].PluginId, Is.EqualTo("com.ckanui.rp1"));
        Assert.That(byMod["RP-1"].PluginName, Is.EqualTo("RP-1 生涯日志"));
        Assert.That(byMod["KSPPerformanceProfiler"].PluginId, Is.EqualTo("com.ckanui.performance"));
        Assert.That(byMod["KSPPerformanceProfiler"].PluginName, Is.EqualTo("性能"));
        Assert.That(byMod["Soundtrack"].PluginId, Is.EqualTo("com.ckanui.soundtrack"));
        Assert.That(byMod["Soundtrack"].PluginName, Is.EqualTo("音乐"));
    }

    [Test]
    public void Catalog_EveryEntryExplainsTheBenefit()
    {
        foreach (var link in ModsCompanionCatalog.All)
        {
            Assert.That(link.Benefit, Is.Not.Empty, $"{link.ModIdentifier} 必须说清联动收益");
        }
    }

    [Test]
    public void IsCompanion_IgnoresCaseAndRejectsUnknownOrEmpty()
    {
        Assert.That(ModsCompanionCatalog.IsCompanion("kos"), Is.True);
        Assert.That(ModsCompanionCatalog.IsCompanion("RP-1"), Is.True);
        Assert.That(ModsCompanionCatalog.IsCompanion("soundtrackeditorforked"), Is.True);
        Assert.That(ModsCompanionCatalog.IsCompanion("UniversalI18n"), Is.False);
        Assert.That(ModsCompanionCatalog.IsCompanion(""), Is.False);
        Assert.That(ModsCompanionCatalog.IsCompanion(null), Is.False);
        Assert.That(ModsCompanionCatalog.IsCompanion("   "), Is.False);
    }

    [Test]
    public void TryGet_ReturnsTheMatchingLink()
    {
        Assert.That(ModsCompanionCatalog.TryGet("kOS", out var link), Is.True);
        Assert.That(link.ModIdentifier, Is.EqualTo("kOS"));
        Assert.That(ModsCompanionCatalog.TryGet("Unknown", out _), Is.False);
    }
}
