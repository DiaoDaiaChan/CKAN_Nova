using CkanUI.ViewModels.Pages;
using CkanUI.ViewModels.Pages.Settings;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

/// <summary>
/// 设置页「界面语言」候选（F-53）：候选必须只列实际发过包的语种。
/// 背景缺陷：原来硬编码 6 项（含 ja-JP/zh-TW/de-DE），但包只发了 zh-CN/en-US，
/// 用户选完写进 Locale 后重启静默退回系统语言 —— 设置了却没效果，属于假 UI。
/// </summary>
[TestFixture]
public sealed class SettingsLocaleChoicesTests
{
    private static SettingItemViewModel Locale(SettingsPageViewModel page) =>
        page.Items.Single(item => string.Equals(item.Key, SettingsKeyNames.Locale, StringComparison.Ordinal));

    private static SettingsPageViewModel CreatePage(FakeUiSettingsStore store) =>
        new(new FakeSettingsService(), store);

    [Test]
    public void WithoutProvider_ListsOnlyShippedLocales()
    {
        var page = CreatePage(new FakeUiSettingsStore());

        var locale = Locale(page);

        Assert.Multiple(() =>
        {
            Assert.That(
                locale.Choices.Select(choice => choice.Value),
                Is.EqualTo(new[] { string.Empty, "zh-CN", "en-US" }));
            Assert.That(
                locale.Choices.Select(choice => choice.Value),
                Has.None.EqualTo("ja-JP").And.None.EqualTo("zh-TW").And.None.EqualTo("de-DE"));
            Assert.That(locale.Choices[0].Label, Is.EqualTo("跟随系统"));
        });
    }

    [Test]
    public async Task Provider_RebuildsChoicesInOrderAndStillSavesSelectedLocale()
    {
        var store = new FakeUiSettingsStore();
        var page = CreatePage(store);
        await page.LoadAsync();
        page.LocaleChoicesProvider = () => new[]
        {
            ("zh-CN", "简体中文"),
            ("en-US", "English"),
            ("ja-JP", "日本語"),
        };

        page.RefreshLocaleChoices();
        var locale = Locale(page);

        Assert.That(
            locale.Choices.Select(choice => choice.Value),
            Is.EqualTo(new[] { string.Empty, "zh-CN", "en-US", "ja-JP" }));
        Assert.That(locale.Choices[3].Label, Is.EqualTo("日本語"));

        locale.SelectedChoice = locale.Choices.Single(choice => choice.Value == "ja-JP");
        await page.SaveItemAsync(locale);

        Assert.That(store.Saved, Has.Count.EqualTo(1));
        Assert.That(store.Saved[0].Locale, Is.EqualTo("ja-JP"));
    }

    [Test]
    public void Provider_DeduplicatesAndSkipsEmptyCodes()
    {
        var page = CreatePage(new FakeUiSettingsStore());
        page.LocaleChoicesProvider = () => new[]
        {
            ("zh-CN", "简体中文"),
            (string.Empty, "重复的跟随系统"),
            ("zh-CN", "重复的简体中文"),
            ("ja-JP", string.Empty),
        };

        page.RefreshLocaleChoices();
        var locale = Locale(page);

        Assert.Multiple(() =>
        {
            Assert.That(
                locale.Choices.Select(choice => choice.Value),
                Is.EqualTo(new[] { string.Empty, "zh-CN", "ja-JP" }));
            // 缺显示名时退回 code，不显示空白项。
            Assert.That(locale.Choices[2].Label, Is.EqualTo("ja-JP"));
        });
    }

    [Test]
    public void EmptyProviderResult_FallsBackToShippedLocales()
    {
        var page = CreatePage(new FakeUiSettingsStore());
        page.LocaleChoicesProvider = () => Array.Empty<(string Code, string Display)>();

        page.RefreshLocaleChoices();

        Assert.That(
            Locale(page).Choices.Select(choice => choice.Value),
            Is.EqualTo(new[] { string.Empty, "zh-CN", "en-US" }));
    }
}
