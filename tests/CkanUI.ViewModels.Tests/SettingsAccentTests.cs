using CkanUI.Services.Abstractions;
using CkanUI.ViewModels.Pages;
using CkanUI.ViewModels.Pages.Settings;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

/// <summary>
/// 自定义强调色与对比度门禁（20 篇 P-03 / THM-02）：WCAG 算术、建议色、设置页两行联动。
/// </summary>
/// <remarks>
/// 这里只锁 VM 层的算法与写入规则；真正的资源注入（<c>AccentHost</c>）与预设色板守卫在
/// <c>tests/CkanUI.App.Tests/AccentContrastThemeTests.cs</c>。
/// </remarks>
[TestFixture]
public sealed class SettingsAccentTests
{
    [TestCase("#1F6FEB", 0x1F, 0x6F, 0xEB)]
    [TestCase("1f6feb", 0x1F, 0x6F, 0xEB)]
    [TestCase("  #FFFFFF  ", 0xFF, 0xFF, 0xFF)]
    public void TryParse_AcceptsHashAndBareHex(string text, int r, int g, int b)
    {
        Assert.That(AccentContrast.TryParse(text, out var rgb), Is.True);
        Assert.That(rgb, Is.EqualTo(new AccentRgb((byte)r, (byte)g, (byte)b)));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("#12")]
    [TestCase("#GGGGGG")]
    [TestCase("1234567")]
    [TestCase("蓝色")]
    public void TryParse_RejectsMalformed(string? text) =>
        Assert.That(AccentContrast.TryParse(text, out _), Is.False);

    [Test]
    public void Ratio_WhiteOnBlack_IsTwentyOne()
    {
        Assert.That(AccentContrast.Ratio(new AccentRgb(0xFF, 0xFF, 0xFF), new AccentRgb(0, 0, 0)), Is.EqualTo(21.0).Within(0.01));
    }

    [Test]
    public void ToHex_IsUpperCaseAndPrefixed()
    {
        Assert.That(AccentContrast.ToHex(new AccentRgb(0x1F, 0x6F, 0xEB)), Is.EqualTo("#1F6FEB"));
        Assert.That(AccentContrast.ToHex(new AccentRgb(0, 0, 0)), Is.EqualTo("#000000"));
    }

    [Test]
    public void Validate_LightTheme_RejectsBrightColor_AndSuggestsCompliantOne()
    {
        var verdict = AccentContrast.Validate("#FFE082", "light", darkActive: false);

        Assert.That(verdict.IsValid, Is.False);
        Assert.That(verdict.Ratio, Is.LessThan(AccentContrast.MinimumRatio));
        Assert.That(verdict.Suggested, Is.Not.Null);
        Assert.That(verdict.Message, Does.Contain("建议改用"));
        Assert.That(AccentContrast.Ratio(verdict.Suggested!.Value, AccentContrast.LightPole), Is.GreaterThanOrEqualTo(AccentContrast.MinimumRatio));
    }

    [Test]
    public void Validate_DarkTheme_AcceptsBrightColor()
    {
        var verdict = AccentContrast.Validate("#FFE082", "dark", darkActive: true);

        Assert.That(verdict.IsValid, Is.True);
        Assert.That(verdict.Suggested, Is.Null);
        Assert.That(verdict.Ratio, Is.GreaterThan(AccentContrast.MinimumRatio));
    }

    [Test]
    public void Validate_DarkTheme_RejectsDarkColor_AndSuggestsCompliantOne()
    {
        var verdict = AccentContrast.Validate("#1F1F1F", "dark", darkActive: true);

        Assert.That(verdict.IsValid, Is.False);
        Assert.That(verdict.Suggested, Is.Not.Null);
        Assert.That(AccentContrast.Ratio(verdict.Suggested!.Value, AccentContrast.DarkPole), Is.GreaterThanOrEqualTo(AccentContrast.MinimumRatio));
    }

    [Test]
    public void Validate_Unparsable_ExplainsTheExpectedFormat()
    {
        var verdict = AccentContrast.Validate("蓝色", "dark", darkActive: true);

        Assert.That(verdict.IsValid, Is.False);
        Assert.That(verdict.Suggested, Is.Null);
        Assert.That(verdict.Message, Does.Contain("#RRGGBB"));
    }

    [Test]
    public void Validate_SystemTheme_FollowsTheProvidedProbe()
    {
        // #4C9AFF 在深色底上合规、在浅色底上只有 2.3:1，正好用来验证「跟随系统」到底比哪一档。
        Assert.That(AccentContrast.Validate("#4C9AFF", "system", darkActive: true).IsValid, Is.True);
        Assert.That(AccentContrast.Validate("#4C9AFF", "system", darkActive: false).IsValid, Is.False);
    }

    [Test]
    public void NormalizeTheme_MapsSystemToProbeAndKeepsExplicitValues()
    {
        Assert.That(AccentContrast.NormalizeTheme("system", darkActive: true), Is.EqualTo("dark"));
        Assert.That(AccentContrast.NormalizeTheme("system", darkActive: false), Is.EqualTo("light"));
        Assert.That(AccentContrast.NormalizeTheme(null, darkActive: false), Is.EqualTo("light"));
        Assert.That(AccentContrast.NormalizeTheme("Dark", darkActive: false), Is.EqualTo("dark"));
        Assert.That(AccentContrast.NormalizeTheme("high-contrast", darkActive: false), Is.EqualTo("highcontrast"));
    }

    [Test]
    public async Task Page_ExposesCustomAccentRow()
    {
        var page = new SettingsPageViewModel(new FakeSettingsService(), new FakeUiSettingsStore());

        await page.LoadAsync();

        var item = Item(page, SettingsKeyNames.AccentColorCustom);
        Assert.That(item.DisplayName, Is.EqualTo("自定义强调色"));
        Assert.That(item.Kind, Is.EqualTo(SettingItemKind.Text));
        Assert.That(item.Persistence, Is.EqualTo(SettingPersistence.UiSettings));
        Assert.That(item.EditValue, Is.Empty);
    }

    [Test]
    public async Task Page_LoadsCustomHexIntoBothRows()
    {
        var store = new FakeUiSettingsStore().With(new UiSettings { AccentColor = "#1F6FEB" });
        var page = new SettingsPageViewModel(new FakeSettingsService(), store);

        await page.LoadAsync();

        Assert.That(Item(page, SettingsKeyNames.AccentColorCustom).EditValue, Is.EqualTo("#1F6FEB"));

        // 预设行要合成一个候选，否则用户会看到下拉框空白（值不在候选里）。
        var preset = Item(page, SettingsKeyNames.AccentColor);
        Assert.That(preset.EditValue, Is.EqualTo("#1F6FEB"));
        Assert.That(preset.SelectedChoice?.Label, Is.EqualTo("自定义（#1F6FEB）"));
    }

    [Test]
    public async Task Page_SaveValidHex_NormalizesAndSyncsPresetRow()
    {
        var store = new FakeUiSettingsStore();
        var page = new SettingsPageViewModel(new FakeSettingsService(), store);
        await page.LoadAsync();
        Item(page, SettingsKeyNames.Theme).EditValue = "light";
        var item = Item(page, SettingsKeyNames.AccentColorCustom);
        item.EditValue = "1f6feb";

        await page.SaveItemAsync(item);

        Assert.That(store.Saved, Has.Count.EqualTo(1));
        Assert.That(store.Saved[0].AccentColor, Is.EqualTo("#1F6FEB"));
        Assert.That(Item(page, SettingsKeyNames.AccentColor).SelectedChoice?.Value, Is.EqualTo("#1F6FEB"));
        Assert.That(page.StatusText, Is.EqualTo("自定义强调色：已保存"));
    }

    [Test]
    public async Task Page_SaveLowContrastHex_IsRejectedWithSuggestion()
    {
        var store = new FakeUiSettingsStore();
        var page = new SettingsPageViewModel(new FakeSettingsService(), store);
        await page.LoadAsync();
        Item(page, SettingsKeyNames.Theme).EditValue = "light";
        var item = Item(page, SettingsKeyNames.AccentColorCustom);
        item.EditValue = "#FFE082";

        await page.SaveItemAsync(item);

        Assert.That(store.Saved, Is.Empty);
        Assert.That(item.ErrorText, Does.Contain("建议改用"));
        Assert.That(item.IsRetryable, Is.False);
        Assert.That(page.StatusText, Is.EqualTo("自定义强调色：保存失败"));
    }

    [Test]
    public async Task Page_EmptyCustomHex_FallsBackToSystemAccent()
    {
        var store = new FakeUiSettingsStore().With(new UiSettings { AccentColor = "#1F6FEB" });
        var page = new SettingsPageViewModel(new FakeSettingsService(), store);
        await page.LoadAsync();
        var custom = Item(page, SettingsKeyNames.AccentColorCustom);
        Assert.That(custom.EditValue, Is.EqualTo("#1F6FEB"));

        // 清空自定义色 = 放弃自己定的那个色：预设行此时指向合成的自定义候选，没有可回落的档位，
        // 于是回到「跟随系统」，而不是把这个刚被清掉的 Hex 又写回配置。
        custom.EditValue = string.Empty;
        await page.SaveItemAsync(custom);

        Assert.That(store.Saved, Has.Count.EqualTo(1));
        Assert.That(store.Saved[0].AccentColor, Is.EqualTo("default"));
        Assert.That(custom.EditValue, Is.Empty);
        Assert.That(Item(page, SettingsKeyNames.AccentColor).SelectedChoice?.Value, Is.EqualTo("default"));
    }

    [Test]
    public async Task Page_SavingPreset_ClearsTheCustomRow()
    {
        var store = new FakeUiSettingsStore().With(new UiSettings { AccentColor = "#4C9AFF" });
        var page = new SettingsPageViewModel(new FakeSettingsService(), store);
        await page.LoadAsync();
        var preset = Item(page, SettingsKeyNames.AccentColor);
        preset.SelectedChoice = preset.Choices.Single(choice => choice.Value == "teal");

        await page.SaveItemAsync(preset);

        Assert.That(store.Saved[0].AccentColor, Is.EqualTo("teal"));
        Assert.That(Item(page, SettingsKeyNames.AccentColorCustom).EditValue, Is.Empty);
        Assert.That(Item(page, SettingsKeyNames.AccentColor).Choices.Any(choice => choice.Value.StartsWith('#')), Is.False);
    }

    [Test]
    public async Task Page_CustomHex_WithSystemTheme_UsesTheProbe()
    {
        var store = new FakeUiSettingsStore();
        var page = new SettingsPageViewModel(new FakeSettingsService(), store) { DarkThemeProbe = () => false };
        await page.LoadAsync();

        // 只有「跟随系统」才需要探针；显式选主题时以主题为准（上一条用例已经锁了这条规则）。
        Item(page, SettingsKeyNames.Theme).EditValue = "system";
        var item = Item(page, SettingsKeyNames.AccentColorCustom);
        item.EditValue = "#4C9AFF";

        await page.SaveItemAsync(item);

        // 同 #4C9AFF：浅色底上 2.3:1，必须被挡下来。
        Assert.That(store.Saved, Is.Empty);
        Assert.That(item.ErrorText, Does.Contain("浅色主题"));
    }

    private static SettingItemViewModel Item(SettingsPageViewModel page, string key) =>
        page.Items.Single(item => string.Equals(item.Key, key, StringComparison.Ordinal));
}
