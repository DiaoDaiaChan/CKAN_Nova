using NUnit.Framework;

namespace CkanUI.Localization.Tests;

/// <summary>回退链：当前语言 → 父语言 → en-US → key 本身（16 篇 §4.2 + 任务口径）。</summary>
[TestFixture]
public sealed class FallbackTests
{
    [Test]
    public void Fallback_RegionalCulture_FallsBackToParentLanguagePack()
    {
        // 只随包 zh（未随包 zh-CN）时，zh-CN 应命中 zh 的内容。
        var loc = TestPacks.Create(
            "zh-CN",
            TestPacks.Pack("zh", (LocalizationKeys.CommonStatusReady, "就绪")),
            TestPacks.Pack("en-US", (LocalizationKeys.CommonStatusReady, "Ready")));

        loc.SetCulture("zh-CN");

        Assert.That(loc.Get(LocalizationKeys.CommonStatusReady), Is.EqualTo("就绪"));
    }

    [Test]
    public void Fallback_MissingKeyInCurrentLanguage_UsesEnglish()
    {
        var loc = TestPacks.Create(
            "zh-CN",
            TestPacks.Pack("zh-CN", (LocalizationKeys.CommonStatusReady, "就绪")),
            TestPacks.Pack("en-US", (LocalizationKeys.CommonStatusReady, "Ready"), ("Ui_Only_En", "English only")));

        Assert.That(loc.Get("Ui_Only_En"), Is.EqualTo("English only"));
    }

    [Test]
    public void Fallback_CurrentLanguageWinsOverEnglish()
    {
        var loc = TestPacks.Create(
            "zh-CN",
            TestPacks.Pack("zh-CN", (LocalizationKeys.CommonStatusReady, "就绪")),
            TestPacks.Pack("en-US", (LocalizationKeys.CommonStatusReady, "Ready")));

        Assert.That(loc.Get(LocalizationKeys.CommonStatusReady), Is.EqualTo("就绪"));
    }

    [Test]
    public void Fallback_DoesNotReachOtherLanguages()
    {
        // 回退链只包含"当前语言 + en-US"，德语包里的键不该被中文界面看到。
        var log = new RecordingLocalizationLog();
        var loc = TestPacks.Create(
            "zh-CN",
            log,
            TestPacks.Pack("zh-CN", (LocalizationKeys.CommonStatusReady, "就绪")),
            TestPacks.Pack("en-US", (LocalizationKeys.CommonStatusReady, "Ready")),
            TestPacks.Pack("de-DE", ("Ui_Only_De", "nur Deutsch")));

        Assert.That(loc.Get("Ui_Only_De"), Is.EqualTo("Ui_Only_De"));
        Assert.That(log.Contains("Ui_Only_De"), Is.True);
    }

    [Test]
    public void Fallback_EmptyEnglishPack_ReturnsKey()
    {
        var loc = TestPacks.Create(
            "zh-CN",
            TestPacks.Pack("zh-CN", (LocalizationKeys.CommonStatusReady, "就绪")),
            LanguagePack.Empty("en-US"));

        Assert.That(loc.Get(LocalizationKeys.CommonStatusReady), Is.EqualTo("就绪"));
        Assert.That(loc.Get("Ui_No_Such_Key"), Is.EqualTo("Ui_No_Such_Key"));
    }

    [Test]
    public void Fallback_AfterSwitchingAway_OldLanguageKeysDisappear()
    {
        var loc = TestPacks.Create(
            "zh-CN",
            TestPacks.Pack("zh-CN", (LocalizationKeys.CommonStatusReady, "就绪"), ("Ui_Only_Zh", "仅中文")),
            TestPacks.Pack("en-US", (LocalizationKeys.CommonStatusReady, "Ready")));

        Assert.That(loc.Get("Ui_Only_Zh"), Is.EqualTo("仅中文"));

        loc.SetCulture("en-US");

        // 切到 en-US 后，中文独有的键不再命中，回退链落到 key 本身。
        Assert.That(loc.Get("Ui_Only_Zh"), Is.EqualTo("Ui_Only_Zh"));
    }
}
