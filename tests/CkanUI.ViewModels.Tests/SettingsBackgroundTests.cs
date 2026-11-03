using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Pages;
using CkanUI.ViewModels.Pages.Settings;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

/// <summary>
/// 背景外观（20 篇 P-06 / THM-05）：设置页那十二行怎么读、怎么写、什么时候被拦。
/// </summary>
/// <remarks>
/// 只锁 VM 层的写入规则与作用范围解析；浏览器（<c>BackgroundHost</c>）与画笔工厂在
/// <c>tests/CkanUI.App.Tests/BackgroundHostTests.cs</c>。高对比度接管见 <see cref="SettingsHighContrastTests"/>。
/// </remarks>
[TestFixture]
public sealed class SettingsBackgroundTests
{
    [Test]
    public async Task Page_LoadsBackgroundRows_WithProfileDefaults()
    {
        var page = NewPage(out _, out _);
        await page.LoadAsync();

        Assert.That(Choice(page, SettingsKeyNames.BackgroundScene)?.Value, Is.EqualTo(BackgroundSceneKinds.Starfield));
        Assert.That(Choice(page, SettingsKeyNames.BackgroundKind)?.Value, Is.EqualTo("none"));
        Assert.That(Item(page, SettingsKeyNames.BackgroundColor).EditValue, Is.EqualTo("#1B1F27"));
        Assert.That(Item(page, SettingsKeyNames.BackgroundGradientStart).EditValue, Is.EqualTo("#1B1F27"));
        Assert.That(Item(page, SettingsKeyNames.BackgroundGradientEnd).EditValue, Is.EqualTo("#0B1220"));
        Assert.That(Item(page, SettingsKeyNames.BackgroundGradientAngle).EditValue, Is.EqualTo("90"));
        Assert.That(Item(page, SettingsKeyNames.BackgroundDim).EditValue, Is.EqualTo("35"));
        Assert.That(Item(page, SettingsKeyNames.BackgroundBlur).EditValue, Is.EqualTo("0"));
        Assert.That(Item(page, SettingsKeyNames.BackgroundOpacity).EditValue, Is.EqualTo("100"));
        Assert.That(Choice(page, SettingsKeyNames.BackgroundFill)?.Value, Is.EqualTo("fill"));
        Assert.That(Choice(page, SettingsKeyNames.BackgroundScope)?.Value, Is.EqualTo("global"));
    }

    [Test]
    public async Task Page_SaveScene_WritesOnlyTheSceneField()
    {
        var page = NewPage(out var store, out _);
        await page.LoadAsync();
        var scene = Item(page, SettingsKeyNames.BackgroundScene);
        scene.SelectedChoice = scene.Choices!.Single(choice => choice.Value == BackgroundSceneKinds.Lunar);

        await page.SaveItemAsync(scene);

        Assert.That(store.Saved, Has.Count.EqualTo(1));
        Assert.That(store.Saved[0].BackgroundScene, Is.EqualTo(BackgroundSceneKinds.Lunar));
        Assert.That(store.Saved[0].Background, Is.Null, "场景与自定义背景是两份独立设置");
        Assert.That(page.StatusText, Is.EqualTo("装饰场景：已保存"));
    }

    [Test]
    public async Task Page_LoadUnknownScene_FallsBackToStarfield()
    {
        var store = new FakeUiSettingsStore().With(new UiSettings
        {
            BackgroundScene = "not-a-scene",
        });
        var page = new SettingsPageViewModel(new FakeSettingsService(), store);

        await page.LoadAsync();

        Assert.That(Choice(page, SettingsKeyNames.BackgroundScene)?.Value, Is.EqualTo(BackgroundSceneKinds.Starfield));
    }

    [Test]
    public async Task Page_SaveSolidKind_WritesTheWholeProfileToGlobal()
    {
        var page = NewPage(out var store, out _);
        await page.LoadAsync();
        var kind = Item(page, SettingsKeyNames.BackgroundKind);
        kind.SelectedChoice = kind.Choices!.Single(choice => choice.Value == "solid");

        await page.SaveItemAsync(kind);

        Assert.That(store.Saved, Has.Count.EqualTo(1));
        var profile = store.Saved[0].Background;
        Assert.That(profile, Is.Not.Null);
        Assert.That(profile!.Kind, Is.EqualTo("solid"));
        // 改一个字段要带着整份 profile 落盘，否则其余十行会被重置成默认值。
        Assert.That(profile.Color, Is.EqualTo("#1B1F27"));
        Assert.That(profile.OpacityPercent, Is.EqualTo(100));
        Assert.That(page.StatusText, Is.EqualTo("背景来源：已保存"));
    }

    [Test]
    public async Task Page_SaveColor_NormalizesHex()
    {
        var page = NewPage(out var store, out _);
        await page.LoadAsync();
        var color = Item(page, SettingsKeyNames.BackgroundColor);
        color.EditValue = "#00ff7f";

        await page.SaveItemAsync(color);

        Assert.That(store.Saved[0].Background!.Color, Is.EqualTo("#00FF7F"));
        Assert.That(color.EditValue, Is.EqualTo("#00FF7F"));
    }

    [Test]
    public async Task Page_InvalidColor_IsRejectedWithTheExpectedFormat()
    {
        var page = NewPage(out var store, out _);
        await page.LoadAsync();
        var color = Item(page, SettingsKeyNames.BackgroundGradientEnd);
        color.EditValue = "深蓝";

        await page.SaveItemAsync(color);

        Assert.That(store.Saved, Is.Empty);
        Assert.That(color.ErrorText, Does.Contain("#RRGGBB"));
        Assert.That(page.StatusText, Is.EqualTo("渐变止色：保存失败"));
    }

    [Test]
    public async Task Page_MissingImagePath_IsRejectedRetryably()
    {
        var page = NewPage(out var store, out _);
        await page.LoadAsync();
        var path = Item(page, SettingsKeyNames.BackgroundImagePath);
        path.EditValue = Path.Combine(Path.GetTempPath(), "ckan-ui-missing-" + Guid.NewGuid().ToString("N") + ".png");

        await page.SaveItemAsync(path);

        Assert.That(store.Saved, Is.Empty);
        Assert.That(path.ErrorText, Does.Contain("找不到图片文件"));
        Assert.That(path.IsRetryable, Is.True, "文件稍后才出现，属于可重试失败");
    }

    [Test]
    public async Task Page_ClearingImagePath_SavesNull()
    {
        var store = new FakeUiSettingsStore().With(new UiSettings
        {
            Background = new BackgroundProfile { Kind = "image", ImagePath = "/tmp/a.png" },
        });
        var page = new SettingsPageViewModel(new FakeSettingsService(), store);
        await page.LoadAsync();
        var path = Item(page, SettingsKeyNames.BackgroundImagePath);
        // 注意留空要写成空串：SettingValidator.ValidatePath 只放行「长度为 0」的路径，
        // 一串空格会被判成「请输入绝对路径」而在保存前就被拦下（这也合理，不该靠 trim 兜底）。
        path.EditValue = string.Empty;

        await page.SaveItemAsync(path);

        Assert.That(store.Saved[0].Background!.ImagePath, Is.Null);
        // 清空路径不等于关掉背景来源，Kind 由用户自己决定。
        Assert.That(store.Saved[0].Background!.Kind, Is.EqualTo("image"));
    }

    [Test]
    public async Task Page_LoadClampsOutOfRangeNumbers()
    {
        var store = new FakeUiSettingsStore().With(new UiSettings
        {
            Background = new BackgroundProfile
            {
                Kind = "gradient",
                DimPercent = 250,
                BlurRadius = -8,
                OpacityPercent = 3_000,
                GradientAngle = -90,
            },
        });
        var page = new SettingsPageViewModel(new FakeSettingsService(), store);

        await page.LoadAsync();

        Assert.That(Item(page, SettingsKeyNames.BackgroundDim).EditValue, Is.EqualTo("100"));
        Assert.That(Item(page, SettingsKeyNames.BackgroundBlur).EditValue, Is.EqualTo("0"));
        Assert.That(Item(page, SettingsKeyNames.BackgroundOpacity).EditValue, Is.EqualTo("100"));
        Assert.That(Item(page, SettingsKeyNames.BackgroundGradientAngle).EditValue, Is.EqualTo("270"));
    }

    [Test]
    public async Task Page_InstanceScope_WritesBackgroundByInstance()
    {
        var page = NewPage(out var store, out var instances, currentName: "默认");
        instances.Current = new InstanceInfo("默认", "D:/KSP", "1.12.5", IsDefault: true, IsLocked: false, ModuleCount: 0);
        await page.LoadAsync();
        var scope = Item(page, SettingsKeyNames.BackgroundScope);
        scope.SelectedChoice = scope.Choices!.Single(choice => choice.Value == "instance");

        await page.SaveItemAsync(scope);

        Assert.That(store.Saved, Has.Count.EqualTo(1));
        Assert.That(store.Saved[0].BackgroundByInstance.ContainsKey("默认"), Is.True);
        Assert.That(store.Saved[0].BackgroundByInstance["默认"].Kind, Is.EqualTo("none"));
        Assert.That(store.Saved[0].Background, Is.Null, "按实例保存不该顺手写全局");
    }

    [Test]
    public async Task Page_InstanceScope_ThenEdits_StayInTheInstanceMap()
    {
        var store = new FakeUiSettingsStore().With(new UiSettings
        {
            Background = new BackgroundProfile { Kind = "solid", Color = "#123456" },
            BackgroundByInstance = new Dictionary<string, BackgroundProfile>(StringComparer.Ordinal)
            {
                ["默认"] = new BackgroundProfile { Kind = "gradient", GradientStart = "#101010", GradientEnd = "#202020" },
            },
        });
        var instances = new FakeInstanceService
        {
            Current = new InstanceInfo("默认", "D:/KSP", "1.12.5", IsDefault: true, IsLocked: false, ModuleCount: 0),
        };
        var page = new SettingsPageViewModel(new FakeSettingsService(), store, instances);
        await page.LoadAsync();

        // 有覆盖时作用范围行必须显示「仅当前实例」，否则用户以为改的是全局。
        Assert.That(Choice(page, SettingsKeyNames.BackgroundScope)?.Value, Is.EqualTo("instance"));
        Assert.That(Item(page, SettingsKeyNames.BackgroundGradientStart).EditValue, Is.EqualTo("#101010"));

        var dim = Item(page, SettingsKeyNames.BackgroundDim);
        dim.EditValue = "60";
        await page.SaveItemAsync(dim);

        var saved = store.Saved[0];
        Assert.That(saved.BackgroundByInstance["默认"].DimPercent, Is.EqualTo(60));
        Assert.That(saved.BackgroundByInstance["默认"].Kind, Is.EqualTo("gradient"));
        Assert.That(saved.Background!.Kind, Is.EqualTo("solid"), "全局那份不能被实例的改动污染");
    }

    [Test]
    public async Task Page_SwitchingBackToGlobal_RemovesTheInstanceOverride()
    {
        var store = new FakeUiSettingsStore().With(new UiSettings
        {
            BackgroundByInstance = new Dictionary<string, BackgroundProfile>(StringComparer.Ordinal)
            {
                ["默认"] = new BackgroundProfile { Kind = "image", ImagePath = "/tmp/a.png" },
            },
        });
        var instances = new FakeInstanceService
        {
            Current = new InstanceInfo("默认", "D:/KSP", "1.12.5", IsDefault: true, IsLocked: false, ModuleCount: 0),
        };
        var page = new SettingsPageViewModel(new FakeSettingsService(), store, instances);
        await page.LoadAsync();
        var scope = Item(page, SettingsKeyNames.BackgroundScope);
        scope.SelectedChoice = scope.Choices!.Single(choice => choice.Value == "global");

        await page.SaveItemAsync(scope);

        var saved = store.Saved[0];
        Assert.That(saved.BackgroundByInstance.ContainsKey("默认"), Is.False);
        // 切全局时把当前生效的那份（实例覆盖）搬过去，用户看到的画面不变。
        Assert.That(saved.Background!.Kind, Is.EqualTo("image"));
        Assert.That(saved.Background!.ImagePath, Is.EqualTo("/tmp/a.png"));
    }

    [Test]
    public async Task Page_InstanceScope_WithoutInstance_IsRejected()
    {
        var page = NewPage(out var store, out _, currentName: null);
        await page.LoadAsync();
        var scope = Item(page, SettingsKeyNames.BackgroundScope);
        scope.SelectedChoice = scope.Choices!.Single(choice => choice.Value == "instance");

        await page.SaveItemAsync(scope);

        Assert.That(store.Saved, Is.Empty);
        Assert.That(scope.ErrorText, Does.Contain("没有当前实例"));
        Assert.That(scope.IsRetryable, Is.False);
    }

    [Test]
    public async Task Page_HighContrast_LocksAndRejectsBackgroundRows()
    {
        var page = NewPage(out var store, out _);
        page.HighContrastProbe = () => true;
        await page.LoadAsync();

        Assert.That(Item(page, SettingsKeyNames.BackgroundScene).IsEnabled, Is.False);
        Assert.That(Item(page, SettingsKeyNames.BackgroundKind).IsEnabled, Is.False);
        Assert.That(Item(page, SettingsKeyNames.BackgroundOpacity).IsEnabled, Is.False);
        Assert.That(Item(page, SettingsKeyNames.Density).IsEnabled, Is.True, "非外观行照旧可改");

        var kind = Item(page, SettingsKeyNames.BackgroundKind);
        kind.SelectedChoice = kind.Choices!.Single(choice => choice.Value == "gradient");
        await page.SaveItemAsync(kind);

        Assert.That(store.Saved, Is.Empty);
        Assert.That(page.StatusText, Does.Contain("高对比度"));
        Assert.That(page.AppearanceLockNotice, Does.Contain("背景"));
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

    private static SettingItemViewModel Item(SettingsPageViewModel page, string key) =>
        page.Items.Single(item => string.Equals(item.Key, key, StringComparison.Ordinal));

    private static SettingChoice? Choice(SettingsPageViewModel page, string key) => Item(page, key).SelectedChoice;
}
