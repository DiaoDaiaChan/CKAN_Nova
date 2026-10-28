using System.Collections.Generic;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Services.Search;
using NUnit.Framework;

namespace CkanUI.Services.Tests;

/// <summary>
/// 搜索语法反向格式化（12 篇 F-20/F-21）。
/// 面板改字段 → 生成语法文本 → 再解析回来必须得到同一份条件，
/// 否则"点选的条件"和"敲进去的条件"就是两套语义了。
/// </summary>
[TestFixture]
public sealed class ModSearchFormatterTests
{
    private readonly ModSearchParser _parser = new();
    private readonly ModSearchFormatter _formatter = new();

    [Test]
    public void EmptySpec_FormatsToEmptyText()
    {
        Assert.That(_formatter.Format(ModSearchSpec.Empty), Is.Empty);
    }

    [Test]
    public void Formats_FieldsInUpstreamOrder()
    {
        var spec = new ModSearchSpec
        {
            Name = "ModuleManager",
            Authors = new[] { "sarbian" },
            Description = "telemetry",
            Licenses = new[] { "MIT" },
            Localizations = new[] { "zh-cn" },
            DependsOn = new[] { "KSP" },
            Recommends = new[] { "Scatterer" },
            Suggests = new[] { "ClickThroughBlocker" },
            ConflictsWith = new[] { "MechJeb2" },
            Supports = new[] { "Kopernicus" },
            Tags = new[] { "plugin" },
            Labels = new[] { "My Label" },
            Compatible = true,
            Installed = false,
        };

        Assert.That(
            _formatter.Format(spec),
            Is.EqualTo(
                "ModuleManager @sarbian desc:telemetry lic:MIT lang:zh-cn dep:KSP "
                + "rec:Scatterer sug:ClickThroughBlocker conf:MechJeb2 sup:Kopernicus "
                + "tag:plugin label:MyLabel is:compatible not:installed"));
    }

    [Test]
    public void NegatedTerms_KeepMinusPrefix()
    {
        var spec = new ModSearchSpec
        {
            Authors = new[] { "-sarbian" },
            Licenses = new[] { "-GPL-3.0" },
        };

        Assert.That(_formatter.Format(spec), Is.EqualTo("-@sarbian -lic:GPL-3.0"));
    }

    [Test]
    public void BlankValues_AreDropped()
    {
        var spec = new ModSearchSpec { Authors = new[] { "  ", string.Empty }, Tags = new[] { " " } };

        Assert.That(_formatter.Format(spec), Is.Empty);
    }

    [TestCase("ModuleManager")]
    [TestCase("@sarbian")]
    [TestCase("desc:telemetry")]
    [TestCase("lic:MIT lang:zh-cn")]
    [TestCase("dep:KSP rec:Scatterer sug:ClickThroughBlocker conf:MechJeb2 sup:Kopernicus")]
    [TestCase("tag:plugin tag:tools")]
    [TestCase("label:Favourites")]
    [TestCase("is:installed not:upgradeable")]
    [TestCase("-@sarbian")]
    [TestCase("engineer tag:plugin is:installed -@cybutek")]
    public void Format_ReparsesToEquivalentSpec(string text)
    {
        var original = _parser.Parse(text);

        var reparsed = _parser.Parse(_formatter.Format(original));

        AssertEquivalent(original, reparsed);
    }

    private static void AssertEquivalent(ModSearchSpec expected, ModSearchSpec actual)
    {
        Assert.That(actual.Name, Is.EqualTo(expected.Name), "Name");
        Assert.That(actual.Description, Is.EqualTo(expected.Description), "Description");
        Assert.That(actual.Authors, Is.EquivalentTo(expected.Authors), "Authors");
        Assert.That(actual.Licenses, Is.EquivalentTo(expected.Licenses), "Licenses");
        Assert.That(actual.Localizations, Is.EquivalentTo(expected.Localizations), "Localizations");
        Assert.That(actual.DependsOn, Is.EquivalentTo(expected.DependsOn), "DependsOn");
        Assert.That(actual.Recommends, Is.EquivalentTo(expected.Recommends), "Recommends");
        Assert.That(actual.Suggests, Is.EquivalentTo(expected.Suggests), "Suggests");
        Assert.That(actual.ConflictsWith, Is.EquivalentTo(expected.ConflictsWith), "ConflictsWith");
        Assert.That(actual.Supports, Is.EquivalentTo(expected.Supports), "Supports");
        Assert.That(actual.Tags, Is.EquivalentTo(expected.Tags), "Tags");
        Assert.That(actual.Labels, Is.EquivalentTo(expected.Labels), "Labels");
        Assert.That(actual.Compatible, Is.EqualTo(expected.Compatible), "Compatible");
        Assert.That(actual.Installed, Is.EqualTo(expected.Installed), "Installed");
        Assert.That(actual.Cached, Is.EqualTo(expected.Cached), "Cached");
        Assert.That(actual.NewlyCompatible, Is.EqualTo(expected.NewlyCompatible), "NewlyCompatible");
        Assert.That(actual.Upgradeable, Is.EqualTo(expected.Upgradeable), "Upgradeable");
        Assert.That(actual.Replaceable, Is.EqualTo(expected.Replaceable), "Replaceable");
    }

}
