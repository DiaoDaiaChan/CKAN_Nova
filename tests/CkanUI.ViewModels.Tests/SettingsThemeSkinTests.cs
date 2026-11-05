using CkanUI.Services.Abstractions;
using CkanUI.ViewModels.Pages;
using CkanUI.ViewModels.Pages.Settings;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

/// <summary>主题皮肤设置：七项选择、只写皮肤字段、未知值回退、高对比度锁定。</summary>
[TestFixture]
public sealed class SettingsThemeSkinTests
{
    [Test]
    public async Task Page_LoadsEveryThemeSkinChoice()
    {
        var page = NewPage(new FakeUiSettingsStore());
        await page.LoadAsync();

        var item = Item(page, SettingsKeyNames.ThemeSkin);
        Assert.That(item.SelectedChoice?.Value, Is.EqualTo(ThemeSkinKinds.Default));
        Assert.That(item.Choices!.Select(choice => choice.Value), Is.EqualTo(ThemeSkinKinds.All));
    }

    [Test]
    public async Task Page_SaveThemeSkin_WritesOnlyTheSkinField()
    {
        var store = new FakeUiSettingsStore();
        var page = NewPage(store);
        await page.LoadAsync();
        var item = Item(page, SettingsKeyNames.ThemeSkin);
        item.SelectedChoice = item.Choices!.Single(choice => choice.Value == ThemeSkinKinds.CoronaGold);

        await page.SaveItemAsync(item);

        Assert.That(store.Saved, Has.Count.EqualTo(1));
        Assert.That(store.Saved[0].ThemeSkin, Is.EqualTo(ThemeSkinKinds.CoronaGold));
        Assert.That(store.Saved[0].Theme, Is.EqualTo(new UiSettings().Theme), "皮肤不能顺手改基础主题");
    }

    [Test]
    public async Task Page_UnknownThemeSkin_FallsBackToDefault()
    {
        var page = NewPage(new FakeUiSettingsStore().With(new UiSettings
        {
            ThemeSkin = "not-a-skin",
        }));

        await page.LoadAsync();

        Assert.That(Item(page, SettingsKeyNames.ThemeSkin).SelectedChoice?.Value,
            Is.EqualTo(ThemeSkinKinds.Default));
    }

    [Test]
    public async Task Page_HighContrast_LocksTheThemeSkinRow()
    {
        var page = NewPage(new FakeUiSettingsStore());
        page.HighContrastProbe = () => true;
        await page.LoadAsync();

        Assert.That(Item(page, SettingsKeyNames.ThemeSkin).IsEnabled, Is.False);
    }

    private static SettingsPageViewModel NewPage(FakeUiSettingsStore store) =>
        new(new FakeSettingsService(), store);

    private static SettingItemViewModel Item(SettingsPageViewModel page, string key) =>
        page.Items.Single(item => string.Equals(item.Key, key, StringComparison.Ordinal));
}
