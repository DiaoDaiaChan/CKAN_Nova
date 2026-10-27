using Avalonia.Styling;

using CkanUI.App;
using CkanUI.Services.Abstractions;

using NUnit.Framework;

namespace CkanUI.App.Tests;

/// <summary>主题映射（THM-01/THM-02、20 篇 P-08）：设置字符串 → Avalonia 主题变体。</summary>
[TestFixture]
public sealed class ThemeHostTests
{
    [TestCase("light", "Light")]
    [TestCase("Light", "Light")]
    [TestCase(" dark ", "Dark")]
    [TestCase("system", "Default")]
    [TestCase("", "Default")]
    [TestCase(null, "Default")]
    [TestCase("nonsense", "Default")]
    public void ToVariant_MapsKnownAndUnknownValues(string? theme, string expected)
    {
        var variant = ThemeHost.ToVariant(theme);
        Assert.That(variant.ToString(), Is.EqualTo(expected));
    }

    [TestCase("high-contrast")]
    [TestCase("HighContrast")]
    [TestCase("contrast")]
    public void ToVariant_MapsHighContrastToCustomVariant(string theme)
    {
        Assert.That(ThemeHost.ToVariant(theme), Is.EqualTo(ThemeHost.HighContrastVariant));
    }

    [Test]
    public void ToVariant_IsCaseAndWhitespaceInsensitive()
    {
        Assert.That(ThemeHost.ToVariant("  DARK  "), Is.EqualTo(ThemeVariant.Dark));
    }
}
