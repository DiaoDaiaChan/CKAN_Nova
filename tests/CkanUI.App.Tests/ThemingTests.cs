using Avalonia.Controls;
using CkanUI.App.Theming;
using NUnit.Framework;

namespace CkanUI.App.Tests;

[TestFixture]
public sealed class ThemingTests
{
    [Test]
    public void AccentPalettes_ContainsAllTwelvePresets()
    {
        var presets = AccentPalettes.All;
        Assert.That(presets, Has.Count.EqualTo(12));

        var expectedIds = new[]
        {
            "blue", "indigo", "purple", "magenta",
            "red", "orange", "amber", "lime",
            "green", "teal", "cyan", "slate"
        };

        foreach (var id in expectedIds)
        {
            Assert.That(AccentPalettes.TryGet(id, out var palette), Is.True, $"Palette '{id}' should exist.");
            Assert.That(palette, Is.Not.Null);
            Assert.That(palette!.Key, Is.EqualTo(id));
            Assert.That(palette.LightHex, Does.StartWith("#"));
            Assert.That(palette.DarkHex, Does.StartWith("#"));
        }
    }

    [TestCase("BLUE", "blue")]
    [TestCase("  lime  ", "lime")]
    [TestCase("PURPLE", "purple")]
    public void AccentPalettes_TryGet_IsCaseAndWhitespaceInsensitive(string input, string expectedId)
    {
        var found = AccentPalettes.TryGet(input, out var palette);
        Assert.That(found, Is.True);
        Assert.That(palette!.Key, Is.EqualTo(expectedId));
    }

    [TestCase("unknown")]
    [TestCase("")]
    [TestCase(null)]
    public void AccentPalettes_TryGet_Invalid_ReturnsFalse(string? input)
    {
        var found = AccentPalettes.TryGet(input, out var palette);
        Assert.That(found, Is.False);
        Assert.That(palette, Is.Null);
    }

    [TestCase("none", 0.0)]
    [TestCase("0", 0.0)]
    [TestCase("sharp", 0.0)]
    [TestCase("round", 1.5)]
    [TestCase("large", 1.5)]
    [TestCase("1.5", 1.5)]
    [TestCase("standard", 1.0)]
    [TestCase("", 1.0)]
    [TestCase(null, 1.0)]
    [TestCase("unknown", 1.0)]
    public void RadiusHost_ResolveScale_ResolvesCorrectly(string? input, double expected)
    {
        Assert.That(RadiusHost.ResolveScale(input), Is.EqualTo(expected).Within(0.001));
    }

    [TestCase(12, "100", 12.0)]
    [TestCase(12, "90", 11.0)]
    [TestCase(13, "130", 17.0)]
    [TestCase(15, "110", 16.5)]
    [TestCase(20, null, 20.0)]
    public void FontScaleHost_ResolveFontSize_ScalesCopyTokens(double baseSize, string? scale, double expected)
    {
        Assert.That(FontScaleHost.ResolveFontSize(baseSize, scale), Is.EqualTo(expected).Within(0.001));
    }

    [Test]
    public void BackdropHost_ResolveHints_ResolvesMica()
    {
        var hints = BackdropHost.ResolveHints("mica");
        Assert.That(hints, Has.Count.EqualTo(2));
        Assert.That(hints[0], Is.EqualTo(WindowTransparencyLevel.Mica));
    }

    [Test]
    public void BackdropHost_ResolveHints_ResolvesAcrylic()
    {
        var hints = BackdropHost.ResolveHints("acrylic");
        Assert.That(hints, Has.Count.EqualTo(2));
        Assert.That(hints[0], Is.EqualTo(WindowTransparencyLevel.AcrylicBlur));
    }

    [Test]
    public void BackdropHost_ResolveHints_ResolvesNone()
    {
        var hints = BackdropHost.ResolveHints("none");
        Assert.That(hints, Has.Count.EqualTo(1));
        Assert.That(hints[0], Is.EqualTo(WindowTransparencyLevel.None));
    }

    [Test]
    public void BackdropHost_ResolveHints_ResolvesAuto()
    {
        var hints = BackdropHost.ResolveHints("auto");
        Assert.That(hints, Has.Count.EqualTo(3));
        Assert.That(hints[0], Is.EqualTo(WindowTransparencyLevel.AcrylicBlur));
        Assert.That(hints[1], Is.EqualTo(WindowTransparencyLevel.Mica));
        Assert.That(hints[2], Is.EqualTo(WindowTransparencyLevel.None));
    }
}
