using NUnit.Framework;

namespace CkanUI.Localization.Tests;

/// <summary>语言下拉显示格式：03 篇 §17.3 规定 `简体中文 · Chinese, Simplified · 99%`。</summary>
[TestFixture]
public sealed class LocaleInfoTests
{
    [Test]
    public void DisplayText_MatchesSpecFormat()
    {
        var info = new LocaleInfo("zh-CN", "简体中文", "Chinese, Simplified", 0.99, false);

        Assert.That(info.DisplayName, Is.EqualTo("简体中文 · Chinese, Simplified"));
        Assert.That(info.DisplayText, Is.EqualTo("简体中文 · Chinese, Simplified · 99%"));
    }

    [Test]
    public void Coverage_IsClampedIntoUnitRange()
    {
        Assert.That(new LocaleInfo("x", "x", "x", 1.5, true).Coverage, Is.EqualTo(1d));
        Assert.That(new LocaleInfo("x", "x", "x", -0.2, false).Coverage, Is.EqualTo(0d));
    }

    [Test]
    public void EmptyNames_FallBackToLocale()
    {
        var info = new LocaleInfo("de-DE", "  ", string.Empty, 0d, false);

        Assert.That(info.NativeName, Is.EqualTo("de-DE"));
        Assert.That(info.EnglishName, Is.EqualTo("de-DE"));
    }
}
