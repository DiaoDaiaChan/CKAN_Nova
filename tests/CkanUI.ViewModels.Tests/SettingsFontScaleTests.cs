using CkanUI.Services.Abstractions;
using CkanUI.ViewModels.Pages;
using CkanUI.ViewModels.Pages.Settings;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

/// <summary>
/// 字号缩放五档（20 篇 P-09 / THM-06）：档位映射与设置页接线。
/// </summary>
/// <remarks>
/// 真正写资源字典的是 App 层 <c>FontScaleHost</c>；这里锁住档位数与半磅取整规则，
/// 并验证设置页只写 ckan-ui.json 的 <c>FontScale</c>、不动其它字段。
/// </remarks>
[TestFixture]
public sealed class SettingsFontScaleTests
{
    [Test]
    public void Normalize_FallsBackToStandard()
    {
        Assert.That(UiFontScale.Normalize(null), Is.EqualTo("100"));
        Assert.That(UiFontScale.Normalize(string.Empty), Is.EqualTo("100"));
        Assert.That(UiFontScale.Normalize("huge"), Is.EqualTo("100"));
    }

    [TestCase("90", "90")]
    [TestCase("100", "100")]
    [TestCase("110", "110")]
    [TestCase("120", "120")]
    [TestCase("130", "130")]
    [TestCase("110%", "110")]
    [TestCase("1.3", "130")]
    [TestCase("0.9", "90")]
    [TestCase("105", "100")]
    [TestCase("200", "130")]
    [TestCase("50", "90")]
    public void Normalize_SnapsToNearestTier(string input, string expected)
    {
        Assert.That(UiFontScale.Normalize(input), Is.EqualTo(expected));
    }

    [Test]
    public void Factor_MatchesFiveTiers()
    {
        Assert.That(UiFontScale.Factor("90"), Is.EqualTo(0.9).Within(0.0001));
        Assert.That(UiFontScale.Factor("100"), Is.EqualTo(1.0).Within(0.0001));
        Assert.That(UiFontScale.Factor("130"), Is.EqualTo(1.3).Within(0.0001));
        Assert.That(UiFontScale.Factor(null), Is.EqualTo(1.0).Within(0.0001));
    }

    [Test]
    public void Apply_RoundsToHalfPoints()
    {
        // 基准值取自 Tokens.axaml：Caption 12 / Body 13 / Subtitle 15 / Heading 17 / Title 20。
        Assert.That(UiFontScale.Apply(12, "90"), Is.EqualTo(11.0));
        Assert.That(UiFontScale.Apply(13, "130"), Is.EqualTo(17.0));
        Assert.That(UiFontScale.Apply(15, "110"), Is.EqualTo(16.5));
        Assert.That(UiFontScale.Apply(17, "120"), Is.EqualTo(20.5));
        Assert.That(UiFontScale.Apply(20, "100"), Is.EqualTo(20.0));
        Assert.That(UiFontScale.Apply(12, null), Is.EqualTo(12.0));
    }

    [Test]
    public async Task SettingsPage_ExposesFiveFontScaleChoices()
    {
        var page = new SettingsPageViewModel(new FakeSettingsService(), new FakeUiSettingsStore());

        await page.LoadAsync();

        var item = Item(page, SettingsKeyNames.FontScale);
        Assert.That(item.DisplayName, Is.EqualTo("字号缩放"));
        Assert.That(item.Kind, Is.EqualTo(SettingItemKind.Choice));
        Assert.That(item.Persistence, Is.EqualTo(SettingPersistence.UiSettings));
        Assert.That(item.Choices.Select(choice => choice.Value), Is.EqualTo(UiFontScale.All));
        Assert.That(item.EditValue, Is.EqualTo("100"));
    }

    [Test]
    public async Task SettingsPage_LoadsStoredFontScale()
    {
        var store = new FakeUiSettingsStore().With(new UiSettings { FontScale = "120" });
        var page = new SettingsPageViewModel(new FakeSettingsService(), store);

        await page.LoadAsync();

        Assert.That(Item(page, SettingsKeyNames.FontScale).EditValue, Is.EqualTo("120"));
        Assert.That(Item(page, SettingsKeyNames.FontScale).IsDirty, Is.False);
    }

    [Test]
    public async Task SettingsPage_Save_WritesFontScaleOnly()
    {
        var settings = new FakeSettingsService();
        var store = new FakeUiSettingsStore();
        var page = new SettingsPageViewModel(settings, store);
        await page.LoadAsync();
        var item = Item(page, SettingsKeyNames.FontScale);
        item.SelectedChoice = item.Choices.Single(choice => choice.Value == "130");

        await page.SaveItemAsync(item);

        Assert.That(settings.Attempts, Is.Empty);
        Assert.That(store.Saved, Has.Count.EqualTo(1));
        Assert.That(store.Saved[0].FontScale, Is.EqualTo("130"));
        // 其它外观设置与窗口几何必须原样保留。
        Assert.That(store.Saved[0].Density, Is.EqualTo("standard"));
        Assert.That(store.Saved[0].WindowWidth, Is.EqualTo(1280));
        Assert.That(page.StatusText, Is.EqualTo("字号缩放：已保存"));
    }

    private static SettingItemViewModel Item(SettingsPageViewModel page, string key) =>
        page.Items.Single(item => string.Equals(item.Key, key, StringComparison.Ordinal));
}
