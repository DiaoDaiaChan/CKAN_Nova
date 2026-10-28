using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Pages;
using CkanUI.ViewModels.Pages.Settings;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

/// <summary>
/// 按实例强调色（20 篇 P-07）：设置页那两行在实例作用域下怎么写、切回全局怎么搬值。
/// </summary>
/// <remarks>
/// 只锁 VM 层的写入规则与作用范围解析；宿主（<c>AccentHost</c>）的生效值解析在
/// <c>tests/CkanUI.App.Tests/AccentInstanceThemeTests.cs</c>。
/// </remarks>
[TestFixture]
public sealed class SettingsAccentInstanceTests
{
    [Test]
    public async Task Page_LoadsGlobalScope_ByDefault()
    {
        var page = NewPage(out _, out _, "A");
        await page.LoadAsync();

        Assert.That(Choice(page, SettingsKeyNames.AccentScope)?.Value, Is.EqualTo("global"));
        Assert.That(Choice(page, SettingsKeyNames.AccentColor)?.Value, Is.EqualTo("default"));
        Assert.That(Item(page, SettingsKeyNames.AccentColorCustom).EditValue, Is.Empty);
    }

    [Test]
    public async Task Page_SwitchToInstance_CopiesTheEffectiveValueIntoTheInstanceMap()
    {
        var page = NewPage(out var store, out _, "A");
        await page.LoadAsync();
        var scope = Item(page, SettingsKeyNames.AccentScope);
        scope.SelectedChoice = scope.Choices!.Single(choice => choice.Value == "instance");

        await page.SaveItemAsync(scope);

        Assert.That(store.Saved, Has.Count.EqualTo(1));
        Assert.That(store.Saved[0].AccentByInstance["A"], Is.EqualTo("default"));
        // 切到实例只是「开始单独记账」，原来的全局值不该被顺手改掉。
        Assert.That(store.Saved[0].AccentColor, Is.Null);
        Assert.That(page.StatusText, Is.EqualTo("强调色作用范围：已保存"));
    }

    [Test]
    public async Task Page_EditingAccentInInstanceScope_StaysInTheInstanceMap()
    {
        var page = NewPage(out var store, out _, "A");
        await page.LoadAsync();
        var scope = Item(page, SettingsKeyNames.AccentScope);
        scope.SelectedChoice = scope.Choices!.Single(choice => choice.Value == "instance");
        await page.SaveItemAsync(scope);

        var accent = Item(page, SettingsKeyNames.AccentColor);
        accent.SelectedChoice = accent.Choices!.Single(choice => choice.Value == "blue");
        await page.SaveItemAsync(accent);

        Assert.That(store.Saved, Has.Count.EqualTo(2));
        var last = store.Saved[1];
        Assert.That(last.AccentByInstance["A"], Is.EqualTo("blue"));
        Assert.That(last.AccentColor, Is.Null, "实例作用域下不该写全局字段");
        // 作用范围仍在实例上，保存强调色以后不能悄悄跳回全局。
        Assert.That(Choice(page, SettingsKeyNames.AccentScope)?.Value, Is.EqualTo("instance"));
    }

    [Test]
    public async Task Page_SaveCustomHexInInstanceScope_StaysInTheInstanceMap()
    {
        var page = NewPage(out var store, out _, "A");
        await page.LoadAsync();
        var scope = Item(page, SettingsKeyNames.AccentScope);
        scope.SelectedChoice = scope.Choices!.Single(choice => choice.Value == "instance");
        await page.SaveItemAsync(scope);

        var custom = Item(page, SettingsKeyNames.AccentColorCustom);
        custom.EditValue = "#4C9AFF";
        await page.SaveItemAsync(custom);

        Assert.That(store.Saved, Has.Count.EqualTo(2));
        Assert.That(store.Saved[1].AccentByInstance["A"], Is.EqualTo("#4C9AFF"));
        Assert.That(store.Saved[1].AccentColor, Is.Null);
    }

    [Test]
    public async Task Page_LoadsPerInstanceAccentIntoBothRows()
    {
        var store = new FakeUiSettingsStore().With(new UiSettings
        {
            AccentColor = "blue",
            AccentByInstance = new Dictionary<string, string>(StringComparer.Ordinal) { ["A"] = "#4C9AFF" },
        });
        var page = NewPage(store, out _, "A");
        await page.LoadAsync();

        Assert.That(Choice(page, SettingsKeyNames.AccentScope)?.Value, Is.EqualTo("instance"));
        Assert.That(Choice(page, SettingsKeyNames.AccentColor)?.Value, Is.EqualTo("#4C9AFF"));
        Assert.That(Item(page, SettingsKeyNames.AccentColorCustom).EditValue, Is.EqualTo("#4C9AFF"));
    }

    [Test]
    public async Task Page_SwitchingBackToGlobal_MovesTheValueAndDropsTheKey()
    {
        var store = new FakeUiSettingsStore().With(new UiSettings
        {
            AccentColor = "blue",
            AccentByInstance = new Dictionary<string, string>(StringComparer.Ordinal) { ["A"] = "purple" },
        });
        var page = NewPage(store, out _, "A");
        await page.LoadAsync();

        var scope = Item(page, SettingsKeyNames.AccentScope);
        scope.SelectedChoice = scope.Choices!.Single(choice => choice.Value == "global");
        await page.SaveItemAsync(scope);

        Assert.That(store.Saved, Has.Count.EqualTo(1));
        // 切回全局时，当前看得见的色要接着用，而不是跳回那个没人记得的旧全局色。
        Assert.That(store.Saved[0].AccentColor, Is.EqualTo("purple"));
        Assert.That(store.Saved[0].AccentByInstance, Is.Empty);
        Assert.That(Choice(page, SettingsKeyNames.AccentColor)?.Value, Is.EqualTo("purple"));
    }

    [Test]
    public async Task Page_InstanceScope_WithoutInstance_IsRejected()
    {
        var page = NewPage(out var store, out _, currentName: null);
        await page.LoadAsync();
        var scope = Item(page, SettingsKeyNames.AccentScope);
        scope.SelectedChoice = scope.Choices!.Single(choice => choice.Value == "instance");

        await page.SaveItemAsync(scope);

        Assert.That(store.Saved, Is.Empty);
        Assert.That(scope.ErrorText, Does.Contain("没有当前实例"));
        Assert.That(page.StatusText, Is.EqualTo("强调色作用范围：保存失败"));
    }

    [Test]
    public async Task Page_HighContrast_LocksTheScopeRowToo()
    {
        var page = NewPage(out var store, out _, "A");
        page.HighContrastProbe = () => true;
        await page.LoadAsync();

        Assert.That(Item(page, SettingsKeyNames.AccentScope).IsEnabled, Is.False);

        var scope = Item(page, SettingsKeyNames.AccentScope);
        scope.SelectedChoice = scope.Choices!.Single(choice => choice.Value == "instance");
        await page.SaveItemAsync(scope);

        Assert.That(store.Saved, Is.Empty);
    }

    private static SettingsPageViewModel NewPage(
        out FakeUiSettingsStore store,
        out FakeInstanceService instances,
        string? currentName = null)
    {
        store = new FakeUiSettingsStore();
        instances = new FakeInstanceService
        {
            Current = currentName is null
                ? null
                : new InstanceInfo(currentName, "D:/KSP", "1.12.5", IsDefault: true, IsLocked: false, ModuleCount: 0),
        };
        return new SettingsPageViewModel(new FakeSettingsService(), store, instances);
    }

    private static SettingsPageViewModel NewPage(
        FakeUiSettingsStore store,
        out FakeInstanceService instances,
        string? currentName = null)
    {
        instances = new FakeInstanceService
        {
            Current = currentName is null
                ? null
                : new InstanceInfo(currentName, "D:/KSP", "1.12.5", IsDefault: true, IsLocked: false, ModuleCount: 0),
        };
        return new SettingsPageViewModel(new FakeSettingsService(), store, instances);
    }

    private static SettingItemViewModel Item(SettingsPageViewModel page, string key) =>
        page.Items.Single(item => string.Equals(item.Key, key, StringComparison.Ordinal));

    private static SettingChoice? Choice(SettingsPageViewModel page, string key) => Item(page, key).SelectedChoice;
}
