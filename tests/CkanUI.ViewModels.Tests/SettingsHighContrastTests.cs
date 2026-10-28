using CkanUI.Services.Abstractions;
using CkanUI.ViewModels.Pages;
using CkanUI.ViewModels.Pages.Settings;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

/// <summary>
/// 系统高对比度接管外观（20 篇 P-02 / THM-10）：探针说开了，主题与强调色两行就置灰、写入被拒，
/// 别的设置项照旧能存。
/// </summary>
/// <remarks>
/// 真正的主题变体压制（<c>ThemeHost</c>）与强调色覆盖清退在
/// <c>tests/CkanUI.App.Tests/HighContrastThemeTests.cs</c>；这里只锁 VM 层的呈现与写入规则。
/// </remarks>
[TestFixture]
public sealed class SettingsHighContrastTests
{
    [Test]
    public async Task RefreshAppearanceLock_LocksOnlyAppearanceRows()
    {
        var forced = true;
        var page = new SettingsPageViewModel(new FakeSettingsService(), new FakeUiSettingsStore())
        {
            HighContrastProbe = () => forced,
        };
        await page.LoadAsync();

        Assert.That(page.IsAppearanceLocked, Is.True);
        Assert.That(page.HasAppearanceLockNotice, Is.True);
        Assert.That(page.AppearanceLockNotice, Does.Contain("高对比度"));
        Assert.That(Item(page, SettingsKeyNames.Theme).IsEnabled, Is.False);
        Assert.That(Item(page, SettingsKeyNames.AccentColor).IsEnabled, Is.False);
        Assert.That(Item(page, SettingsKeyNames.AccentColorCustom).IsEnabled, Is.False);

        // 不相干的行不能被连坐：下载缓存目录与外观无关。
        Assert.That(Item(page, SettingsKeyNames.DownloadCacheDir).IsEnabled, Is.True);

        // 关掉之后必须弹回来，否则设置页会永远停在「已被接管」。
        forced = false;
        page.RefreshAppearanceLock();

        Assert.That(page.IsAppearanceLocked, Is.False);
        Assert.That(page.HasAppearanceLockNotice, Is.False);
        Assert.That(page.AppearanceLockNotice, Is.Empty);
        Assert.That(Item(page, SettingsKeyNames.Theme).IsEnabled, Is.True);
        Assert.That(Item(page, SettingsKeyNames.AccentColorCustom).IsEnabled, Is.True);
    }

    [Test]
    public async Task LoadAsync_AppliesTheLockWithoutWaitingForTheView()
    {
        var page = new SettingsPageViewModel(new FakeSettingsService(), new FakeUiSettingsStore())
        {
            HighContrastProbe = () => true,
        };

        await page.LoadAsync();

        Assert.That(page.IsAppearanceLocked, Is.True);
        Assert.That(Item(page, SettingsKeyNames.Theme).IsEnabled, Is.False);
    }

    [Test]
    public async Task SaveItemAsync_RejectsAppearanceChange_WhileLocked()
    {
        var store = new FakeUiSettingsStore();
        var page = new SettingsPageViewModel(new FakeSettingsService(), store) { HighContrastProbe = () => true };
        await page.LoadAsync();
        var theme = Item(page, SettingsKeyNames.Theme);
        theme.EditValue = "light";

        await page.SaveItemAsync(theme);

        Assert.That(store.Saved, Is.Empty);
        Assert.That(page.StatusText, Does.Contain("高对比度"));
        Assert.That(page.StatusText, Does.Contain("已接管"));
    }

    [Test]
    public async Task SaveItemAsync_StillSavesNonAppearanceRows_WhileLocked()
    {
        var store = new FakeUiSettingsStore();
        var page = new SettingsPageViewModel(new FakeSettingsService(), store) { HighContrastProbe = () => true };
        await page.LoadAsync();
        var density = Item(page, SettingsKeyNames.Density);
        density.SelectedChoice = density.Choices!.Single(choice => choice.Value == "compact");

        await page.SaveItemAsync(density);

        Assert.That(store.Saved, Has.Count.EqualTo(1));
        Assert.That(store.Saved[0].Density, Is.EqualTo("compact"));
    }

    private static SettingItemViewModel Item(SettingsPageViewModel page, string key) =>
        page.Items.Single(item => string.Equals(item.Key, key, StringComparison.Ordinal));
}
