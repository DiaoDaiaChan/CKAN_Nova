using CkanUI.Services.Abstractions;
using CkanUI.ViewModels.Pages;
using CkanUI.ViewModels.Pages.Settings;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

/// <summary>
/// 动效强度四档（20 篇 P-10 / THM-07）：档位解析、时长表与设置页接线。
/// </summary>
/// <remarks>
/// 真正写资源字典的是 App 层 <c>MotionHost</c>；这里锁住「哪一档给多少毫秒」以及
/// 「跟随系统」的落地规则——判定错档会让「关闭动画」看起来没生效。
/// </remarks>
[TestFixture]
public sealed class SettingsMotionScaleTests
{
    [Test]
    public void Normalize_FallsBackToAuto()
    {
        Assert.That(UiMotionScale.Normalize(null), Is.EqualTo(UiMotionScale.Auto));
        Assert.That(UiMotionScale.Normalize(string.Empty), Is.EqualTo(UiMotionScale.Auto));
        Assert.That(UiMotionScale.Normalize("  "), Is.EqualTo(UiMotionScale.Auto));
        Assert.That(UiMotionScale.Normalize("bogus"), Is.EqualTo(UiMotionScale.Auto));
    }

    [TestCase("FULL", "full")]
    [TestCase(" Reduced ", "reduced")]
    [TestCase("none", "none")]
    [TestCase("auto", "auto")]
    public void Normalize_KeepsKnownTiersCaseInsensitively(string input, string expected)
    {
        Assert.That(UiMotionScale.Normalize(input), Is.EqualTo(expected));
    }

    [TestCase("auto", true, "full")]
    [TestCase("auto", false, "reduced")]
    [TestCase("full", false, "full")]
    [TestCase("reduced", true, "reduced")]
    [TestCase("none", true, "none")]
    [TestCase(null, true, "full")]
    [TestCase(null, false, "reduced")]
    public void Resolve_MapsAutoToSystemPreference(string? intensity, bool systemAnimations, string expected)
    {
        Assert.That(UiMotionScale.Resolve(intensity, systemAnimations), Is.EqualTo(expected));
    }

    [Test]
    public void Milliseconds_Full_KeepsAllThreeTiers()
    {
        Assert.That(UiMotionScale.Milliseconds("full", true, MotionSpeed.Fast), Is.EqualTo(90));
        Assert.That(UiMotionScale.Milliseconds("full", true, MotionSpeed.Base), Is.EqualTo(150));
        Assert.That(UiMotionScale.Milliseconds("full", true, MotionSpeed.Page), Is.EqualTo(200));
    }

    [Test]
    public void Milliseconds_Reduced_KeepsOnlyFastFeedback()
    {
        Assert.That(UiMotionScale.Milliseconds("reduced", true, MotionSpeed.Fast), Is.EqualTo(90));
        Assert.That(UiMotionScale.Milliseconds("reduced", true, MotionSpeed.Base), Is.Zero);
        Assert.That(UiMotionScale.Milliseconds("reduced", true, MotionSpeed.Page), Is.Zero);
    }

    [Test]
    public void Milliseconds_None_IsInstantEverywhere()
    {
        Assert.That(UiMotionScale.Milliseconds("none", true, MotionSpeed.Fast), Is.Zero);
        Assert.That(UiMotionScale.Milliseconds("none", true, MotionSpeed.Base), Is.Zero);
        Assert.That(UiMotionScale.Milliseconds("none", true, MotionSpeed.Page), Is.Zero);
    }

    [Test]
    public void Milliseconds_AutoWithoutSystemAnimations_BehavesLikeReduced()
    {
        Assert.That(UiMotionScale.Milliseconds("auto", false, MotionSpeed.Fast), Is.EqualTo(90));
        Assert.That(UiMotionScale.Milliseconds("auto", false, MotionSpeed.Page), Is.Zero);
    }

    [Test]
    public void Duration_UsesMilliseconds()
    {
        Assert.That(
            UiMotionScale.Duration("full", true, MotionSpeed.Page),
            Is.EqualTo(TimeSpan.FromMilliseconds(200)));
        Assert.That(UiMotionScale.Duration("none", true, MotionSpeed.Fast), Is.EqualTo(TimeSpan.Zero));
    }

    [TestCase("full", true, true)]
    [TestCase("reduced", true, false)]
    [TestCase("none", true, false)]
    [TestCase("auto", false, false)]
    public void PageTransitionsEnabled_OnlyForFull(string intensity, bool systemAnimations, bool expected)
    {
        Assert.That(UiMotionScale.PageTransitionsEnabled(intensity, systemAnimations), Is.EqualTo(expected));
    }

    [Test]
    public async Task SettingsPage_ExposesFourMotionChoices()
    {
        var page = new SettingsPageViewModel(new FakeSettingsService(), new FakeUiSettingsStore());

        await page.LoadAsync();

        var item = Item(page, SettingsKeyNames.MotionIntensity);

        Assert.That(item.DisplayName, Is.EqualTo("动效强度"));
        Assert.That(item.Kind, Is.EqualTo(SettingItemKind.Choice));
        Assert.That(item.Persistence, Is.EqualTo(SettingPersistence.UiSettings));
        Assert.That(item.Choices!.Select(choice => choice.Value), Is.EqualTo(UiMotionScale.All));
        Assert.That(item.EditValue, Is.EqualTo(UiMotionScale.Auto));
    }

    [Test]
    public async Task SettingsPage_LoadsStoredMotionIntensity()
    {
        var store = new FakeUiSettingsStore().With(new UiSettings { MotionIntensity = "reduced" });

        var page = new SettingsPageViewModel(new FakeSettingsService(), store);

        await page.LoadAsync();

        Assert.That(Item(page, SettingsKeyNames.MotionIntensity).EditValue, Is.EqualTo("reduced"));
    }

    [Test]
    public async Task SettingsPage_Save_WritesMotionIntensityOnly()
    {
        var store = new FakeUiSettingsStore();
        var page = new SettingsPageViewModel(new FakeSettingsService(), store);
        await page.LoadAsync();
        var item = Item(page, SettingsKeyNames.MotionIntensity);

        item.SelectedChoice = item.Choices!.Single(choice => choice.Value == "none");
        await page.SaveItemAsync(item);

        Assert.That(store.Saved, Has.Count.EqualTo(1));
        Assert.That(store.Saved[0].MotionIntensity, Is.EqualTo("none"));
        Assert.That(store.Saved[0].WindowWidth, Is.EqualTo(1280));
        Assert.That(page.StatusText, Is.EqualTo("动效强度：已保存"));
    }

    private static SettingItemViewModel Item(SettingsPageViewModel page, string key) =>
        page.Items.Single(item => item.Key == key);
}
