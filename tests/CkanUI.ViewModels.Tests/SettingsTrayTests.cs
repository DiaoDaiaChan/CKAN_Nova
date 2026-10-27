using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using CkanUI.Services.Abstractions;
using CkanUI.ViewModels.Pages;
using CkanUI.ViewModels.Pages.Settings;

using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

/// <summary>
/// 设置页「高级与诊断」组的两个托盘开关（12 篇 F-51）：读写 UiSettings、
/// 只写自己那一项、不污染其它字段，以及保存失败时的中文错误与可重试标记。
/// </summary>
[TestFixture]
public sealed class SettingsTrayTests
{
    private static SettingItemViewModel Item(SettingsPageViewModel page, string key) =>
        page.Items.Single(item => string.Equals(item.Key, key, StringComparison.Ordinal));

    private static SettingsPageViewModel CreatePage(FakeSettingsService settings, FakeUiSettingsStore store) =>
        new(settings, store);

    [Test]
    public async Task AdvancedGroup_EndsWithTheTwoTrayToggles()
    {
        var page = CreatePage(new FakeSettingsService(), new FakeUiSettingsStore());

        await page.LoadAsync();

        var advanced = page.Groups.Single(group => group.IsDiagnosticsGroup);
        Assert.That(
            advanced.Items.Select(item => item.Key),
            Is.EqualTo(new[]
            {
                SettingsKeyNames.HideVersionEpoch,
                SettingsKeyNames.HideVersionVPrefix,
                SettingsKeyNames.AutoSortUpdates,
                SettingsKeyNames.SuppressRecommendations,
                SettingsKeyNames.MinimizeToTray,
                SettingsKeyNames.CloseToTray,
            }));
        Assert.That(Item(page, SettingsKeyNames.MinimizeToTray).DisplayName, Is.EqualTo("最小化到托盘"));
        Assert.That(Item(page, SettingsKeyNames.CloseToTray).DisplayName, Is.EqualTo("关闭到托盘"));
        Assert.That(Item(page, SettingsKeyNames.MinimizeToTray).IsBooleanKind, Is.True);
        Assert.That(Item(page, SettingsKeyNames.CloseToTray).IsBooleanKind, Is.True);
    }

    [Test]
    public async Task LoadAsync_TrayTogglesDefaultToOff()
    {
        var page = CreatePage(new FakeSettingsService(), new FakeUiSettingsStore());

        await page.LoadAsync();

        Assert.That(Item(page, SettingsKeyNames.MinimizeToTray).EditValue, Is.EqualTo("false"));
        Assert.That(Item(page, SettingsKeyNames.CloseToTray).EditValue, Is.EqualTo("false"));
        Assert.That(Item(page, SettingsKeyNames.MinimizeToTray).IsDirty, Is.False);
        Assert.That(Item(page, SettingsKeyNames.CloseToTray).IsDirty, Is.False);
    }

    [Test]
    public async Task LoadAsync_ReadsBothTogglesFromUiSettings()
    {
        var store = new FakeUiSettingsStore().With(new UiSettings
        {
            MinimizeToTray = true,
            CloseToTray = true,
        });
        var page = CreatePage(new FakeSettingsService(), store);

        await page.LoadAsync();

        Assert.That(Item(page, SettingsKeyNames.MinimizeToTray).BooleanValue, Is.True);
        Assert.That(Item(page, SettingsKeyNames.CloseToTray).BooleanValue, Is.True);
        Assert.That(page.IsDirty, Is.False);
    }

    [Test]
    public async Task SaveItemAsync_MinimizeToTray_WritesUiSettingsOnly()
    {
        var settings = new FakeSettingsService();
        var store = new FakeUiSettingsStore().With(new UiSettings
        {
            Theme = "light",
            WindowWidth = 1280,
            AutoSortUpdates = false,
            ColumnWidths = new Dictionary<string, double>(StringComparer.Ordinal) { ["mods"] = 320 },
        });
        var page = CreatePage(settings, store);
        await page.LoadAsync();
        var item = Item(page, SettingsKeyNames.MinimizeToTray);

        item.BooleanValue = true;
        Assert.That(item.IsDirty, Is.True);
        await page.SaveItemAsync(item);

        Assert.That(settings.Attempts, Is.Empty);
        Assert.That(store.Saved, Has.Count.EqualTo(1));
        Assert.That(store.Saved[0].MinimizeToTray, Is.True);
        Assert.That(store.Saved[0].CloseToTray, Is.False);
        Assert.That(store.Saved[0].Theme, Is.EqualTo("light"));
        Assert.That(store.Saved[0].WindowWidth, Is.EqualTo(1280));
        Assert.That(store.Saved[0].ColumnWidths["mods"], Is.EqualTo(320));
        Assert.That(item.IsDirty, Is.False);
    }

    [Test]
    public async Task SaveItemAsync_CloseToTray_KeepsMinimizeToTray()
    {
        var store = new FakeUiSettingsStore().With(new UiSettings { MinimizeToTray = true });
        var page = CreatePage(new FakeSettingsService(), store);
        await page.LoadAsync();
        var item = Item(page, SettingsKeyNames.CloseToTray);

        item.BooleanValue = true;
        await page.SaveItemAsync(item);

        Assert.That(store.Saved, Has.Count.EqualTo(1));
        Assert.That(store.Saved[0].MinimizeToTray, Is.True);
        Assert.That(store.Saved[0].CloseToTray, Is.True);
        Assert.That(item.IsDirty, Is.False);
    }

    [Test]
    public async Task SaveItemAsync_TrayToggle_RaisesStoreChangedForTrayHost()
    {
        var store = new FakeUiSettingsStore();
        UiSettings? notified = null;
        store.Changed += value => notified = value;
        var page = CreatePage(new FakeSettingsService(), store);
        await page.LoadAsync();
        var item = Item(page, SettingsKeyNames.CloseToTray);

        item.BooleanValue = true;
        await page.SaveItemAsync(item);

        // TrayHost 只订阅 Changed 来切换两个开关，事件没带上新值就等于没生效。
        Assert.That(notified, Is.Not.Null);
        Assert.That(notified!.CloseToTray, Is.True);
    }

    [Test]
    public async Task SaveItemAsync_TrayToggleFailure_ReportsRetryableAndKeepsDirty()
    {
        var store = new FakeUiSettingsStore
        {
            SaveResult = OperationResult<bool>.Fail("ckan-ui.json 只读", retryable: true),
        };
        var page = CreatePage(new FakeSettingsService(), store);
        await page.LoadAsync();
        var item = Item(page, SettingsKeyNames.MinimizeToTray);

        item.BooleanValue = true;
        await page.SaveItemAsync(item);

        Assert.That(item.HasError, Is.True);
        Assert.That(item.ErrorText, Does.Contain("ckan-ui.json 只读"));
        Assert.That(item.IsRetryable, Is.True);
        Assert.That(item.IsDirty, Is.True);
        Assert.That(page.HasRetryableFailures, Is.True);
    }

    [Test]
    public void TraySettingKeyNames_AreStable()
    {
        // 键名落进 ckan-ui.json，改动等于丢掉老用户的勾选状态。
        Assert.That(SettingsKeyNames.MinimizeToTray, Is.EqualTo("MinimizeToTray"));
        Assert.That(SettingsKeyNames.CloseToTray, Is.EqualTo("CloseToTray"));
    }
}
