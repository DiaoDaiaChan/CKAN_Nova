using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.TestSupport;
using CkanUI.ViewModels.Pages;
using CkanUI.ViewModels.Pages.Settings;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

/// <summary>
/// 设置页（06 篇 UI-07 / VM-03）：八组读取、逐项保存、校验失败、脏标记、
/// 令牌脱敏与增删、Retryable 语义、异常收口、实例级容差文件、诊断区与打开目录。
/// </summary>
[TestFixture]
public sealed class SettingsPageViewModelTests
{
    private static SettingItemViewModel Item(SettingsPageViewModel page, string key) =>
        page.Items.Single(item => string.Equals(item.Key, key, StringComparison.Ordinal));

    private static SettingsPageViewModel CreatePage(
        FakeSettingsService settings,
        FakeUiSettingsStore store,
        IInstanceService? instances = null,
        Func<string, Task>? copyTextAsync = null) =>
        new(settings, store, instances, copyTextAsync);

    /// <summary>造一个带实例的假实例服务（实例级设置需要「当前实例」才有落脚点）。</summary>
    private static FakeInstanceService InstancesWith(string gameDir)
    {
        var instance = new InstanceInfo("Default", gameDir, "1.12.5", IsDefault: true, IsLocked: false, ModuleCount: 3);
        return new FakeInstanceService
        {
            Instances = new[] { instance },
            Current = instance,
        };
    }

    private static FakeSettingsService SeededSettings() => new FakeSettingsService()
        .Seed(SettingsKeyNames.DevBuilds, "true")
        .Seed(SettingsKeyNames.DownloadCacheDir, "D:/CKAN/cache")
        .Seed(SettingsKeyNames.CacheSizeLimit, "2097152")
        .Seed(SettingsKeyNames.RefreshRate, "30")
        .Seed(SettingsKeyNames.PreferredHosts, "[\"spacedock.info\",null]")
        .Seed(SettingsKeyNames.GlobalInstallFilters("KSP"), "[\"not:*.zip\"]")
        .Seed(SettingsKeyNames.AuthToken("spacedock.info"), "secret-token-1234");

    [Test]
    public async Task LoadAsync_PopulatesEightGroupsInSpecOrder()
    {
        var page = CreatePage(SeededSettings(), new FakeUiSettingsStore());

        await page.LoadAsync();

        Assert.That(
            page.Groups.Select(group => group.Title),
            Is.EqualTo(new[] { "通用", "实例", "下载缓存", "仓库与网络", "认证令牌", "外观", "兼容性", "高级与诊断" }));
        Assert.That(page.Groups[4].IsTokenGroup, Is.True);
        Assert.That(page.Groups[4].Items, Is.Empty);
        Assert.That(page.Groups[7].IsDiagnosticsGroup, Is.True);
        Assert.That(
            page.Groups[7].Items.Select(item => item.Key),
            Is.EqualTo(new[]
            {
                SettingsKeyNames.HideVersionEpoch,
                SettingsKeyNames.HideVersionVPrefix,
                SettingsKeyNames.AutoSortUpdates,
                SettingsKeyNames.SuppressRecommendations,
                SettingsKeyNames.MinimizeToTray,
                SettingsKeyNames.CloseToTray,
            }));
        Assert.That(page.Groups[7].Items.All(item => item.IsBooleanKind), Is.True);
    }

    [Test]
    public async Task LoadAsync_ReadsCoreValuesIntoItems()
    {
        var page = CreatePage(SeededSettings(), new FakeUiSettingsStore());

        await page.LoadAsync();

        Assert.That(Item(page, SettingsKeyNames.DevBuilds).BooleanValue, Is.True);
        Assert.That(Item(page, SettingsKeyNames.DownloadCacheDir).EditValue, Is.EqualTo("D:/CKAN/cache"));
        Assert.That(Item(page, SettingsKeyNames.RefreshRate).EditValue, Is.EqualTo("30"));
        // PreferredHosts 的 null 元素 = 「其余主机」的位置，读回时保留成空行。
        Assert.That(Item(page, SettingsKeyNames.PreferredHosts).EditValue, Is.EqualTo("spacedock.info\n"));
        Assert.That(Item(page, SettingsKeyNames.GlobalInstallFilters("KSP")).EditValue, Is.EqualTo("not:*.zip"));
    }

    [Test]
    public async Task LoadAsync_ConvertsCacheLimitFromBytesToMebibytes()
    {
        var page = CreatePage(SeededSettings(), new FakeUiSettingsStore());

        await page.LoadAsync();

        Assert.That(Item(page, SettingsKeyNames.CacheSizeLimit).EditValue, Is.EqualTo("2"));
    }

    [Test]
    public async Task LoadAsync_ReadsUiSettingsThemeLocaleAndDensity()
    {
        var store = new FakeUiSettingsStore().With(new UiSettings { Theme = "light", Locale = "en-US", Density = "compact" });
        var page = CreatePage(new FakeSettingsService(), store);

        await page.LoadAsync();

        Assert.That(Item(page, SettingsKeyNames.Theme).EditValue, Is.EqualTo("light"));
        Assert.That(Item(page, SettingsKeyNames.Locale).EditValue, Is.EqualTo("en-US"));
        Assert.That(Item(page, SettingsKeyNames.Density).EditValue, Is.EqualTo("compact"));
    }

    [Test]
    public async Task LoadAsync_DensityDefaultsToStandardWhenUnset()
    {
        var store = new FakeUiSettingsStore().With(new UiSettings { Density = null });
        var page = CreatePage(new FakeSettingsService(), store);

        await page.LoadAsync();

        Assert.That(Item(page, SettingsKeyNames.Density).EditValue, Is.EqualTo("standard"));
        Assert.That(Item(page, SettingsKeyNames.Density).IsDirty, Is.False);
    }

    [Test]
    public async Task LoadAsync_UnknownThemeFallsBackToDark()
    {
        var store = new FakeUiSettingsStore().With(new UiSettings { Theme = "solarized" });
        var page = CreatePage(new FakeSettingsService(), store);

        await page.LoadAsync();

        Assert.That(Item(page, SettingsKeyNames.Theme).EditValue, Is.EqualTo("dark"));
    }

    [Test]
    public async Task LoadAsync_LeavesPageCleanAndReportsCounts()
    {
        var page = CreatePage(SeededSettings(), new FakeUiSettingsStore());

        await page.LoadAsync();

        Assert.That(page.IsDirty, Is.False);
        Assert.That(page.IsLoading, Is.False);
        Assert.That(page.HasLoadError, Is.False);
        Assert.That(page.StatusText, Does.Contain("已读取"));
        Assert.That(page.TokenEditor.Tokens.Count, Is.EqualTo(1));
    }

    [Test]
    public async Task LoadAsync_ServiceFailure_ShowsLoadErrorWithoutThrowing()
    {
        var settings = new FakeSettingsService
        {
            GetAllResult = OperationResult<IReadOnlyDictionary<string, string?>>.Fail("配置文件不可读", "磁盘离线"),
        };
        var page = CreatePage(settings, new FakeUiSettingsStore());

        await page.LoadAsync();

        Assert.That(page.HasLoadError, Is.True);
        Assert.That(page.LoadErrorText, Does.Contain("配置文件不可读"));
        Assert.That(page.LoadErrorText, Does.Contain("磁盘离线"));
        Assert.That(page.StatusText, Is.EqualTo("读取设置失败"));
        Assert.That(page.IsLoading, Is.False);
    }

    [Test]
    public async Task LoadAsync_ServiceThrows_CapturesError()
    {
        var settings = new FakeSettingsService { GetAllException = new InvalidOperationException("核心配置已释放") };
        var page = CreatePage(settings, new FakeUiSettingsStore());

        await page.LoadAsync();

        Assert.That(page.HasLoadError, Is.True);
        Assert.That(page.LoadErrorText, Does.Contain("核心配置已释放"));
    }

    [Test]
    public async Task LoadAsync_UiStoreThrows_CapturesError()
    {
        var store = new FakeUiSettingsStore { LoadException = new IOException("ckan-ui.json 被占用") };
        var page = CreatePage(new FakeSettingsService(), store);

        await page.LoadAsync();

        Assert.That(page.HasLoadError, Is.True);
        Assert.That(page.LoadErrorText, Does.Contain("读取界面设置失败"));
        Assert.That(page.LoadErrorText, Does.Contain("ckan-ui.json 被占用"));
    }

    [Test]
    public async Task SaveItemAsync_WritesOnlyTheEditedKey()
    {
        var settings = new FakeSettingsService();
        var page = CreatePage(settings, new FakeUiSettingsStore());
        await page.LoadAsync();
        var refresh = Item(page, SettingsKeyNames.RefreshRate);
        refresh.EditValue = "60";

        await page.SaveItemAsync(refresh);

        Assert.That(settings.Attempts, Has.Count.EqualTo(1));
        Assert.That(settings.Writes[0].Key, Is.EqualTo(SettingsKeyNames.RefreshRate));
        Assert.That(settings.Writes[0].Value, Is.EqualTo("60"));
        Assert.That(refresh.IsDirty, Is.False);
        Assert.That(page.IsDirty, Is.False);
        Assert.That(page.StatusText, Is.EqualTo("自动刷新间隔（分钟）：已保存"));
    }

    [Test]
    public async Task SaveItemAsync_CacheLimit_WritesBytesToCore()
    {
        var settings = new FakeSettingsService();
        var page = CreatePage(settings, new FakeUiSettingsStore());
        await page.LoadAsync();
        var cacheLimit = Item(page, SettingsKeyNames.CacheSizeLimit);
        cacheLimit.EditValue = "100";

        await page.SaveItemAsync(cacheLimit);

        Assert.That(settings.Writes[0].Key, Is.EqualTo(SettingsKeyNames.CacheSizeLimit));
        Assert.That(settings.Writes[0].Value, Is.EqualTo("104857600"));
    }

    [Test]
    public async Task SaveItemAsync_ValidationFailure_DoesNotTouchService()
    {
        var settings = new FakeSettingsService();
        var page = CreatePage(settings, new FakeUiSettingsStore());
        await page.LoadAsync();
        var refresh = Item(page, SettingsKeyNames.RefreshRate);
        refresh.EditValue = "abc";

        await page.SaveItemAsync(refresh);

        Assert.That(settings.Attempts, Is.Empty);
        Assert.That(refresh.HasError, Is.True);
        Assert.That(refresh.ErrorText, Is.EqualTo("请输入整数"));
        Assert.That(refresh.IsDirty, Is.True);
        Assert.That(page.StatusText, Does.Contain("请先修正输入"));
    }

    [Test]
    public async Task SaveItemAsync_RequiresRestart_AppendsRestartHint()
    {
        var page = CreatePage(new FakeSettingsService(), new FakeUiSettingsStore(), InstancesWith("D:/KSP"));
        await page.LoadAsync();
        var autoStart = Item(page, SettingsKeyNames.AutoStartInstance);
        Assert.That(autoStart.RequiresRestart, Is.True);
        autoStart.SelectedChoice = autoStart.Choices.Single(choice => choice.Value == "Default");

        await page.SaveItemAsync(autoStart);

        Assert.That(page.StatusText, Is.EqualTo("自动打开的实例：已保存，重启后生效"));
    }

    [Test]
    public async Task SaveItemAsync_PermanentFailure_ShowsPermanentHint()
    {
        var settings = new FakeSettingsService
        {
            SetAndVerifyHandler = (_, _) => OperationResult<string?>.Fail("配置项被核心拒绝", retryable: false),
        };
        var page = CreatePage(settings, new FakeUiSettingsStore());
        await page.LoadAsync();
        var refresh = Item(page, SettingsKeyNames.RefreshRate);
        refresh.EditValue = "60";

        await page.SaveItemAsync(refresh);

        Assert.That(refresh.HasError, Is.True);
        Assert.That(refresh.ErrorText, Does.Contain("配置项被核心拒绝"));
        Assert.That(refresh.IsRetryable, Is.False);
        Assert.That(refresh.FailureHint, Does.Contain("不会被接受"));
        Assert.That(page.HasRetryableFailures, Is.False);
        Assert.That(page.StatusText, Does.Not.Contain("可重试"));
    }

    [Test]
    public async Task SaveItemAsync_RetryableFailure_AllowsRetry()
    {
        var settings = new FakeSettingsService
        {
            SetAndVerifyHandler = (_, _) => OperationResult<string?>.Fail("写配置超时", "文件被占用", retryable: true),
        };
        var page = CreatePage(settings, new FakeUiSettingsStore());
        await page.LoadAsync();
        var refresh = Item(page, SettingsKeyNames.RefreshRate);
        refresh.EditValue = "60";

        await page.SaveItemAsync(refresh);

        Assert.That(refresh.IsRetryable, Is.True);
        Assert.That(refresh.ErrorText, Does.Contain("写配置超时"));
        Assert.That(refresh.ErrorText, Does.Contain("文件被占用"));
        Assert.That(refresh.FailureHint, Does.Contain("可重试"));
        Assert.That(page.HasRetryableFailures, Is.True);
        Assert.That(page.StatusText, Does.Contain("可重试"));
        Assert.That(refresh.IsDirty, Is.True);
    }

    [Test]
    public async Task RetryFailedCommand_ResavesOnlyRetryableItems()
    {
        var settings = new FakeSettingsService();
        var page = CreatePage(settings, new FakeUiSettingsStore());
        await page.LoadAsync();
        var refresh = Item(page, SettingsKeyNames.RefreshRate);
        var cacheLimit = Item(page, SettingsKeyNames.CacheSizeLimit);
        refresh.EditValue = "60";
        cacheLimit.EditValue = "100";
        settings.SetAndVerifyHandler = (key, _) => string.Equals(key, SettingsKeyNames.RefreshRate, StringComparison.Ordinal)
            ? OperationResult<string?>.Fail("写配置超时", retryable: true)
            : OperationResult<string?>.Fail("值被拒绝", retryable: false);
        await page.SaveItemAsync(refresh);
        await page.SaveItemAsync(cacheLimit);

        settings.SetAndVerifyHandler = null;
        await page.RetryFailedCommand.ExecuteAsync(null);

        Assert.That(settings.Attempts, Has.Count.EqualTo(3));
        Assert.That(refresh.IsDirty, Is.False);
        Assert.That(cacheLimit.IsDirty, Is.True);
        Assert.That(page.StatusText, Is.EqualTo("已重试 1 项"));
    }

    [Test]
    public async Task SaveItemAsync_ServiceThrows_MarksRetryableFailure()
    {
        var settings = new FakeSettingsService();
        var page = CreatePage(settings, new FakeUiSettingsStore());
        await page.LoadAsync();
        var refresh = Item(page, SettingsKeyNames.RefreshRate);
        refresh.EditValue = "60";
        settings.SetAndVerifyException = new IOException("配置文件被锁定");

        await page.SaveItemAsync(refresh);

        Assert.That(refresh.HasError, Is.True);
        Assert.That(refresh.ErrorText, Does.Contain("配置文件被锁定"));
        Assert.That(refresh.IsRetryable, Is.True);
        Assert.That(refresh.IsSaving, Is.False);
    }

    [Test]
    public async Task SaveItemAsync_Theme_WritesUiSettingsOnly()
    {
        var settings = new FakeSettingsService();
        var store = new FakeUiSettingsStore();
        var page = CreatePage(settings, store);
        await page.LoadAsync();
        var theme = Item(page, SettingsKeyNames.Theme);
        theme.SelectedChoice = theme.Choices.Single(choice => choice.Value == "light");

        await page.SaveItemAsync(theme);

        Assert.That(settings.Attempts, Is.Empty);
        Assert.That(store.Saved, Has.Count.EqualTo(1));
        Assert.That(store.Saved[0].Theme, Is.EqualTo("light"));
        // 窗口几何等其它 UI 设置必须原样保留。
        Assert.That(store.Saved[0].WindowWidth, Is.EqualTo(1280));
        Assert.That(page.StatusText, Is.EqualTo("主题：已保存"));
        Assert.That(page.IsDirty, Is.False);
    }

    [Test]
    public async Task SaveItemAsync_LocaleEmpty_WritesNullLocale()
    {
        var store = new FakeUiSettingsStore().With(new UiSettings { Locale = "en-US" });
        var page = CreatePage(new FakeSettingsService(), store);
        await page.LoadAsync();
        var locale = Item(page, SettingsKeyNames.Locale);
        locale.SelectedChoice = locale.Choices.Single(choice => choice.Value.Length == 0);

        await page.SaveItemAsync(locale);

        Assert.That(store.Saved, Has.Count.EqualTo(1));
        Assert.That(store.Saved[0].Locale, Is.Null);
        Assert.That(page.StatusText, Is.EqualTo("界面语言：已保存，重启后生效"));
    }

    [Test]
    public async Task SaveItemAsync_Density_WritesUiSettingsOnly()
    {
        var settings = new FakeSettingsService();
        var store = new FakeUiSettingsStore().With(new UiSettings { Theme = "light", WindowWidth = 1280 });
        var page = CreatePage(settings, store);
        await page.LoadAsync();
        var density = Item(page, SettingsKeyNames.Density);
        density.SelectedChoice = density.Choices.Single(choice => choice.Value == "compact");

        await page.SaveItemAsync(density);

        // 密度是外观类设置：只进 ckan-ui.json，不碰 Core；也不标「重启后生效」。
        Assert.That(settings.Attempts, Is.Empty);
        Assert.That(store.Saved, Has.Count.EqualTo(1));
        Assert.That(store.Saved[0].Density, Is.EqualTo("compact"));
        Assert.That(store.Saved[0].Theme, Is.EqualTo("light"));
        Assert.That(store.Saved[0].WindowWidth, Is.EqualTo(1280));
        Assert.That(density.IsDirty, Is.False);
        Assert.That(density.HintText, Is.Null);
        Assert.That(page.StatusText, Is.EqualTo("界面密度：已保存"));
    }

    [Test]
    public async Task LoadAsync_ReadsPersonalizationFieldsFromUiSettings()
    {
        var store = new FakeUiSettingsStore().With(new UiSettings
        {
            AccentColor = "purple",
            RadiusScale = "round",
            BackdropKind = "mica",
            MotionIntensity = "reduced",
        });
        var page = CreatePage(new FakeSettingsService(), store);

        await page.LoadAsync();

        Assert.That(Item(page, SettingsKeyNames.AccentColor).EditValue, Is.EqualTo("purple"));
        Assert.That(Item(page, SettingsKeyNames.RadiusScale).EditValue, Is.EqualTo("round"));
        Assert.That(Item(page, SettingsKeyNames.BackdropKind).EditValue, Is.EqualTo("mica"));
        Assert.That(Item(page, SettingsKeyNames.MotionIntensity).EditValue, Is.EqualTo("reduced"));
    }

    [Test]
    public async Task SaveItemAsync_AccentColor_WritesUiSettingsOnly()
    {
        var settings = new FakeSettingsService();
        var store = new FakeUiSettingsStore().With(new UiSettings { Theme = "dark" });
        var page = CreatePage(settings, store);
        await page.LoadAsync();
        var accent = Item(page, SettingsKeyNames.AccentColor);
        accent.SelectedChoice = accent.Choices.Single(choice => choice.Value == "lime");

        await page.SaveItemAsync(accent);

        Assert.That(settings.Attempts, Is.Empty);
        Assert.That(store.Saved, Has.Count.EqualTo(1));
        Assert.That(store.Saved[0].AccentColor, Is.EqualTo("lime"));
        Assert.That(accent.IsDirty, Is.False);
        Assert.That(page.StatusText, Is.EqualTo("强调色：已保存"));
    }

    [Test]
    public async Task SaveItemAsync_RadiusScale_WritesUiSettingsOnly()
    {
        var settings = new FakeSettingsService();
        var store = new FakeUiSettingsStore().With(new UiSettings { Theme = "dark" });
        var page = CreatePage(settings, store);
        await page.LoadAsync();
        var radius = Item(page, SettingsKeyNames.RadiusScale);
        radius.SelectedChoice = radius.Choices.Single(choice => choice.Value == "round");

        await page.SaveItemAsync(radius);

        Assert.That(settings.Attempts, Is.Empty);
        Assert.That(store.Saved, Has.Count.EqualTo(1));
        Assert.That(store.Saved[0].RadiusScale, Is.EqualTo("round"));
        Assert.That(radius.IsDirty, Is.False);
        Assert.That(page.StatusText, Is.EqualTo("圆角尺度：已保存"));
    }

    [Test]
    public async Task SaveItemAsync_BackdropKind_WritesUiSettingsOnly()
    {
        var settings = new FakeSettingsService();
        var store = new FakeUiSettingsStore().With(new UiSettings { Theme = "dark" });
        var page = CreatePage(settings, store);
        await page.LoadAsync();
        var backdrop = Item(page, SettingsKeyNames.BackdropKind);
        backdrop.SelectedChoice = backdrop.Choices.Single(choice => choice.Value == "acrylic");

        await page.SaveItemAsync(backdrop);

        Assert.That(settings.Attempts, Is.Empty);
        Assert.That(store.Saved, Has.Count.EqualTo(1));
        Assert.That(store.Saved[0].BackdropKind, Is.EqualTo("acrylic"));
        Assert.That(backdrop.IsDirty, Is.False);
        Assert.That(page.StatusText, Is.EqualTo("窗口材质与透明：已保存"));
    }

    [Test]
    public async Task SaveItemAsync_MotionIntensity_WritesUiSettingsOnly()
    {
        var settings = new FakeSettingsService();
        var store = new FakeUiSettingsStore().With(new UiSettings { Theme = "dark" });
        var page = CreatePage(settings, store);
        await page.LoadAsync();
        var motion = Item(page, SettingsKeyNames.MotionIntensity);
        motion.SelectedChoice = motion.Choices.Single(choice => choice.Value == "none");

        await page.SaveItemAsync(motion);

        Assert.That(settings.Attempts, Is.Empty);
        Assert.That(store.Saved, Has.Count.EqualTo(1));
        Assert.That(store.Saved[0].MotionIntensity, Is.EqualTo("none"));
        Assert.That(motion.IsDirty, Is.False);
        Assert.That(page.StatusText, Is.EqualTo("动效强度：已保存"));
    }

    [Test]
    public async Task LoadAsync_ReadsF54FlagsFromUiSettings()
    {
        var store = new FakeUiSettingsStore().With(new UiSettings
        {
            HideVersionEpoch = true,
            HideVersionVPrefix = true,
            AutoSortUpdates = false,
            SuppressRecommendations = true,
        });
        var page = CreatePage(new FakeSettingsService(), store);

        await page.LoadAsync();

        Assert.That(Item(page, SettingsKeyNames.HideVersionEpoch).EditValue, Is.EqualTo("true"));
        Assert.That(Item(page, SettingsKeyNames.HideVersionVPrefix).EditValue, Is.EqualTo("true"));
        Assert.That(Item(page, SettingsKeyNames.AutoSortUpdates).EditValue, Is.EqualTo("false"));
        Assert.That(Item(page, SettingsKeyNames.SuppressRecommendations).EditValue, Is.EqualTo("true"));
        Assert.That(Item(page, SettingsKeyNames.HideVersionEpoch).IsDirty, Is.False);
    }

    [Test]
    public async Task SaveItemAsync_HideVersionEpoch_WritesUiSettingsAndKeepsOtherFields()
    {
        var settings = new FakeSettingsService();
        var store = new FakeUiSettingsStore().With(new UiSettings { Theme = "light", WindowWidth = 1280 });
        var page = CreatePage(settings, store);
        await page.LoadAsync();
        var item = Item(page, SettingsKeyNames.HideVersionEpoch);

        item.BooleanValue = true;
        Assert.That(item.IsDirty, Is.True);
        await page.SaveItemAsync(item);

        Assert.That(settings.Attempts, Is.Empty);
        Assert.That(store.Saved, Has.Count.EqualTo(1));
        Assert.That(store.Saved[0].HideVersionEpoch, Is.True);
        Assert.That(store.Saved[0].AutoSortUpdates, Is.False);
        Assert.That(store.Saved[0].Theme, Is.EqualTo("light"));
        Assert.That(store.Saved[0].WindowWidth, Is.EqualTo(1280));
        Assert.That(item.IsDirty, Is.False);
        Assert.That(page.StatusText, Is.EqualTo("隐藏版本 epoch 前缀：已保存"));
    }

    [Test]
    public async Task SaveItemAsync_UiSettingsSaveFailure_ReportsRetryableFailure()
    {
        var store = new FakeUiSettingsStore
        {
            SaveResult = OperationResult<bool>.Fail("ckan-ui.json 只读", retryable: true),
        };
        var page = CreatePage(new FakeSettingsService(), store);
        await page.LoadAsync();
        var theme = Item(page, SettingsKeyNames.Theme);
        theme.SelectedChoice = theme.Choices.Single(choice => choice.Value == "light");

        await page.SaveItemAsync(theme);

        Assert.That(theme.HasError, Is.True);
        Assert.That(theme.ErrorText, Does.Contain("ckan-ui.json 只读"));
        Assert.That(theme.IsRetryable, Is.True);
        Assert.That(page.HasRetryableFailures, Is.True);
    }

    [Test]
    public async Task SaveItemAsync_WithoutChanges_ReportsNoChange()
    {
        var settings = new FakeSettingsService();
        var page = CreatePage(settings, new FakeUiSettingsStore());
        await page.LoadAsync();
        var refresh = Item(page, SettingsKeyNames.RefreshRate);

        await page.SaveItemAsync(refresh);

        Assert.That(settings.Attempts, Is.Empty);
        Assert.That(page.StatusText, Is.EqualTo("自动刷新间隔（分钟）：没有修改"));
    }

    [Test]
    public async Task SaveItemAsync_NullItem_IsIgnored()
    {
        var settings = new FakeSettingsService();
        var page = CreatePage(settings, new FakeUiSettingsStore());

        await page.SaveItemAsync(null);

        Assert.That(settings.Attempts, Is.Empty);
    }

    [Test]
    public async Task DirtyTracking_FollowsEditsAndRevert()
    {
        var page = CreatePage(new FakeSettingsService(), new FakeUiSettingsStore());
        await page.LoadAsync();
        var refresh = Item(page, SettingsKeyNames.RefreshRate);
        var devBuilds = Item(page, SettingsKeyNames.DevBuilds);
        Assert.That(page.IsDirty, Is.False);

        refresh.EditValue = "45";
        Assert.That(refresh.IsDirty, Is.True);
        Assert.That(page.IsDirty, Is.True);

        devBuilds.BooleanValue = true;
        Assert.That(page.IsDirty, Is.True);

        devBuilds.Revert();
        Assert.That(devBuilds.BooleanValue, Is.False);
        Assert.That(page.IsDirty, Is.True);

        refresh.Revert();
        Assert.That(page.IsDirty, Is.False);
    }

    [Test]
    public async Task Discard_RestoresSavedValuesAndClearsDirty()
    {
        var page = CreatePage(SeededSettings(), new FakeUiSettingsStore());
        await page.LoadAsync();
        var refresh = Item(page, SettingsKeyNames.RefreshRate);
        var devBuilds = Item(page, SettingsKeyNames.DevBuilds);
        refresh.EditValue = "45";
        devBuilds.BooleanValue = false;
        Assert.That(page.IsDirty, Is.True);

        page.DiscardCommand.Execute(null);

        Assert.That(refresh.EditValue, Is.EqualTo("30"));
        Assert.That(devBuilds.BooleanValue, Is.True);
        Assert.That(page.IsDirty, Is.False);
        Assert.That(page.StatusText, Is.EqualTo("已放弃未保存的修改"));
    }

    [Test]
    public async Task TokenEditor_LoadFrom_MasksExistingTokens()
    {
        var page = CreatePage(SeededSettings(), new FakeUiSettingsStore());

        await page.LoadAsync();

        var token = page.TokenEditor.Tokens.Single();
        Assert.That(token.Host, Is.EqualTo("spacedock.info"));
        Assert.That(token.MaskedPreview, Is.EqualTo("脱敏预览：secr****1234"));
        Assert.That(token.MaskedPreview, Does.Not.Contain("secret-token-1234"));
        Assert.That(token.IsDirty, Is.False);
    }

    [Test]
    public async Task TokenEditor_Add_WritesAuthTokenKeyAndKeepsHostOrder()
    {
        var settings = new FakeSettingsService();
        var page = CreatePage(settings, new FakeUiSettingsStore());
        await page.LoadAsync();

        page.TokenEditor.NewHost = "spacedock.info";
        page.TokenEditor.NewToken = "token-bbbb-2222";
        await page.TokenEditor.AddAsync();
        page.TokenEditor.NewHost = "ksp.sarbian.com";
        page.TokenEditor.NewToken = "token-aaaa-1111";
        await page.TokenEditor.AddAsync();

        Assert.That(settings.Attempts, Has.Count.EqualTo(2));
        Assert.That(settings.Attempts[0].Key, Is.EqualTo("AuthToken:spacedock.info"));
        Assert.That(settings.Attempts[0].Value, Is.EqualTo("token-bbbb-2222"));
        Assert.That(
            page.TokenEditor.Tokens.Select(token => token.Host),
            Is.EqualTo(new[] { "ksp.sarbian.com", "spacedock.info" }));
        Assert.That(page.TokenEditor.NewHost, Is.Empty);
        Assert.That(page.TokenEditor.NewToken, Is.Empty);
        Assert.That(page.TokenEditor.IsEmpty, Is.False);
    }

    [Test]
    public async Task TokenEditor_Add_RejectsEmptyHostEmptyTokenAndDuplicates()
    {
        var settings = new FakeSettingsService();
        var page = CreatePage(settings, new FakeUiSettingsStore());
        await page.LoadAsync();
        var editor = page.TokenEditor;

        await editor.AddAsync();
        Assert.That(editor.ErrorText, Is.EqualTo("请输入主机名"));

        editor.NewHost = "spacedock.info";
        await editor.AddAsync();
        Assert.That(editor.ErrorText, Is.EqualTo("请输入令牌"));

        editor.NewToken = "token-bbbb-2222";
        await editor.AddAsync();
        Assert.That(editor.ErrorText, Is.Null);
        Assert.That(settings.Attempts, Has.Count.EqualTo(1));

        editor.NewHost = "spacedock.info";
        editor.NewToken = "token-cccc-3333";
        await editor.AddAsync();
        Assert.That(editor.ErrorText, Does.Contain("已有令牌"));
        Assert.That(settings.Attempts, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task TokenEditor_Remove_WritesNullAndDropsRow()
    {
        var settings = new FakeSettingsService().Seed(SettingsKeyNames.AuthToken("spacedock.info"), "secret-token-1234");
        var page = CreatePage(settings, new FakeUiSettingsStore());
        await page.LoadAsync();
        var token = page.TokenEditor.Tokens.Single();

        await page.TokenEditor.RemoveAsync(token);

        Assert.That(settings.Attempts, Has.Count.EqualTo(1));
        Assert.That(settings.Attempts[0].Key, Is.EqualTo("AuthToken:spacedock.info"));
        Assert.That(settings.Attempts[0].Value, Is.Null);
        Assert.That(page.TokenEditor.Tokens, Is.Empty);
        Assert.That(page.TokenEditor.IsEmpty, Is.True);
    }

    [Test]
    public async Task TokenEditor_Save_WritesNewValueAndClearsDirty()
    {
        var settings = new FakeSettingsService().Seed(SettingsKeyNames.AuthToken("spacedock.info"), "secret-token-1234");
        var page = CreatePage(settings, new FakeUiSettingsStore());
        await page.LoadAsync();
        var token = page.TokenEditor.Tokens.Single();
        token.Token = "token-dddd-4444";
        Assert.That(page.TokenEditor.HasChanges, Is.True);

        await page.TokenEditor.SaveAsync(token);

        Assert.That(settings.Writes[0].Key, Is.EqualTo("AuthToken:spacedock.info"));
        Assert.That(settings.Writes[0].Value, Is.EqualTo("token-dddd-4444"));
        Assert.That(token.IsDirty, Is.False);
        Assert.That(page.TokenEditor.HasChanges, Is.False);
    }

    [Test]
    public void KeyNames_MirrorCoreSettingKeys()
    {
        // 分层规则不允许 ViewModels 引用 CkanUI.Services，键名在这里复制了一份；
        // 这条测试盯住复制品别跟 src/CkanUI.Services/Settings/SettingKeys.cs 漂移。
        Assert.That(SettingsKeyNames.DevBuilds, Is.EqualTo("DevBuilds"));
        Assert.That(SettingsKeyNames.AutoStartInstance, Is.EqualTo("AutoStartInstance"));
        Assert.That(SettingsKeyNames.DownloadCacheDir, Is.EqualTo("DownloadCacheDir"));
        Assert.That(SettingsKeyNames.CacheSizeLimit, Is.EqualTo("CacheSizeLimit"));
        Assert.That(SettingsKeyNames.RefreshRate, Is.EqualTo("RefreshRate"));
        Assert.That(SettingsKeyNames.Language, Is.EqualTo("Language"));
        Assert.That(SettingsKeyNames.PreferredHosts, Is.EqualTo("PreferredHosts"));
        Assert.That(SettingsKeyNames.AuthTokenPrefix, Is.EqualTo("AuthToken:"));
        Assert.That(SettingsKeyNames.GlobalInstallFiltersPrefix, Is.EqualTo("GlobalInstallFilters:"));
        Assert.That(SettingsKeyNames.AuthToken("h"), Is.EqualTo("AuthToken:h"));
        Assert.That(SettingsKeyNames.GlobalInstallFilters("KSP"), Is.EqualTo("GlobalInstallFilters:KSP"));
    }

    [Test]
    public void KeyNames_UiSettingKeysAreLiteralAndDistinctFromCoreKeys()
    {
        // 外观三项只写 ckan-ui.json，不在 Core 的键名空间里（02 篇 §3、04 篇 §17）；
        // 这里把字面量钉住，Lead 会在 tests/CkanUI.Services.Tests 侧补反向镜像。
        Assert.That(SettingsKeyNames.Theme, Is.EqualTo("Theme"));
        Assert.That(SettingsKeyNames.Locale, Is.EqualTo("Locale"));
        Assert.That(SettingsKeyNames.Density, Is.EqualTo("Density"));

        var coreKeys = new[]
        {
            SettingsKeyNames.DevBuilds,
            SettingsKeyNames.AutoStartInstance,
            SettingsKeyNames.DownloadCacheDir,
            SettingsKeyNames.CacheSizeLimit,
            SettingsKeyNames.RefreshRate,
            SettingsKeyNames.Language,
            SettingsKeyNames.PreferredHosts,
        };
        Assert.That(SettingsKeyNames.HideVersionEpoch, Is.EqualTo("HideVersionEpoch"));
        Assert.That(SettingsKeyNames.HideVersionVPrefix, Is.EqualTo("HideVersionVPrefix"));
        Assert.That(SettingsKeyNames.AutoSortUpdates, Is.EqualTo("AutoSortUpdates"));
        Assert.That(SettingsKeyNames.SuppressRecommendations, Is.EqualTo("SuppressRecommendations"));

        var uiKeys = new[]
        {
            SettingsKeyNames.Theme,
            SettingsKeyNames.Locale,
            SettingsKeyNames.Density,
            SettingsKeyNames.HideVersionEpoch,
            SettingsKeyNames.HideVersionVPrefix,
            SettingsKeyNames.AutoSortUpdates,
            SettingsKeyNames.SuppressRecommendations,
        };

        foreach (var uiKey in uiKeys)
        {
            Assert.That(coreKeys, Has.No.Member(uiKey), $"外观键 {uiKey} 不应与 Core 键重名");
            Assert.That(SettingsKeyNames.AuthToken(uiKey), Is.Not.EqualTo(uiKey));
        }

        // 稳定版容差落在实例级文件 <实例目录>/CKAN/stability_tolerance.json，不在 Core 键空间里。
        Assert.That(SettingsKeyNames.StabilityTolerance, Is.EqualTo("StabilityTolerance"));
        Assert.That(coreKeys, Has.No.Member(SettingsKeyNames.StabilityTolerance));
    }

    [Test]
    public async Task LoadAsync_AutoStartInstance_OffersInstanceChoices()
    {
        var page = CreatePage(new FakeSettingsService(), new FakeUiSettingsStore(), InstancesWith("D:/KSP"));
        await page.LoadAsync();

        var autoStart = Item(page, SettingsKeyNames.AutoStartInstance);
        Assert.That(autoStart.Kind, Is.EqualTo(SettingItemKind.Choice));
        Assert.That(
            autoStart.Choices.Select(choice => choice.Value),
            Is.EqualTo(new[] { string.Empty, "Default" }));
        Assert.That(autoStart.Choices[1].Label, Is.EqualTo("Default (1.12.5)"));
        Assert.That(autoStart.SelectedChoice?.Value, Is.EqualTo(string.Empty));
    }

    [Test]
    public async Task LoadAsync_AutoStartInstance_KeepsSavedValueMissingFromInstanceList()
    {
        // 已落盘但当前实例列表里没有：保留成候选，别让一次进设置页把用户配置冲成空值。
        var settings = new FakeSettingsService().Seed(SettingsKeyNames.AutoStartInstance, "OldSave");
        var page = CreatePage(settings, new FakeUiSettingsStore(), InstancesWith("D:/KSP"));

        await page.LoadAsync();

        var autoStart = Item(page, SettingsKeyNames.AutoStartInstance);
        Assert.That(autoStart.SelectedChoice?.Value, Is.EqualTo("OldSave"));
        Assert.That(autoStart.SelectedChoice?.Label, Does.Contain("不在当前实例列表中"));
        Assert.That(autoStart.IsDirty, Is.False);
    }

    [Test]
    public async Task StabilityTolerance_ReadsDefaultAndWritesInstanceFile()
    {
        // 系统临时目录在受限沙箱里不可写，统一落在仓库根的 artifacts/tmp（见 TempWorkspace 注释）。
        using var workspace = new TempWorkspace("settings-page-tol");
        var gameDir = workspace.Path;
        {
            var page = CreatePage(new FakeSettingsService(), new FakeUiSettingsStore(), InstancesWith(gameDir));
            await page.LoadAsync();

            var tolerance = Item(page, SettingsKeyNames.StabilityTolerance);
            Assert.That(tolerance.IsEnabled, Is.True);
            Assert.That(tolerance.EditValue, Is.EqualTo("stable"));
            Assert.That(tolerance.IsDirty, Is.False);

            tolerance.SelectedChoice = tolerance.Choices.Single(choice => choice.Value == "testing");
            await page.SaveItemAsync(tolerance);

            Assert.That(page.StatusText, Is.EqualTo("稳定版容差：已保存"));
            Assert.That(tolerance.IsDirty, Is.False);
            var json = File.ReadAllText(StabilityToleranceFile.PathFor(gameDir));
            Assert.That(json, Does.Contain("\"overall_stability_tolerance\": \"testing\""));
        }
    }

    [Test]
    public async Task StabilityTolerance_WithoutCurrentInstance_IsDisabledAndRefusesToSave()
    {
        var page = CreatePage(new FakeSettingsService(), new FakeUiSettingsStore());
        await page.LoadAsync();

        var tolerance = Item(page, SettingsKeyNames.StabilityTolerance);
        Assert.That(tolerance.IsEnabled, Is.False);
        Assert.That(tolerance.EditValue, Is.EqualTo("stable"));

        tolerance.SelectedChoice = tolerance.Choices.Single(choice => choice.Value == "development");
        await page.SaveItemAsync(tolerance);

        Assert.That(tolerance.HasError, Is.True);
        Assert.That(tolerance.ErrorText, Does.StartWith("尚未打开实例"));
        Assert.That(tolerance.IsRetryable, Is.False);
    }

    [Test]
    public async Task Diagnostics_ListsApplicationVersionAndPaths()
    {
        var page = CreatePage(SeededSettings(), new FakeUiSettingsStore(), InstancesWith("D:/KSP"));
        await page.LoadAsync();

        var rows = page.Diagnostics.Rows;
        Assert.That(rows.Select(row => row.Name), Does.Contain("应用版本"));
        Assert.That(rows.Select(row => row.Name), Does.Contain("配置目录"));
        Assert.That(rows.Single(row => row.Name == "当前实例").Value, Is.EqualTo("Default"));
        Assert.That(rows.Single(row => row.Name == "实例目录").Value, Is.EqualTo("D:/KSP"));
        Assert.That(rows.Single(row => row.Name == "缓存目录").Value, Is.EqualTo("D:/CKAN/cache"));
        // Core 版本由宿主注入，未注入时整行不显示（不写「未知」）。
        Assert.That(rows.Select(row => row.Name), Has.No.Member("CKAN Core 版本"));
        Assert.That(page.Diagnostics.HasRows, Is.True);
    }

    [Test]
    public async Task Diagnostics_Copy_WithoutClipboard_ReportsMissingChannel()
    {
        var page = CreatePage(new FakeSettingsService(), new FakeUiSettingsStore());
        await page.LoadAsync();

        await page.Diagnostics.CopyCommand.ExecuteAsync(null);

        Assert.That(page.Diagnostics.CopyStatusText, Is.EqualTo("当前环境没有剪贴板通道，无法复制"));
        Assert.That(page.Diagnostics.HasCopyStatus, Is.True);
    }

    [Test]
    public async Task Diagnostics_Copy_WritesTextToClipboard()
    {
        string? captured = null;
        var page = CreatePage(
            new FakeSettingsService(),
            new FakeUiSettingsStore(),
            InstancesWith("D:/KSP"),
            text =>
            {
                captured = text;
                return Task.CompletedTask;
            });
        await page.LoadAsync();

        await page.Diagnostics.CopyCommand.ExecuteAsync(null);

        Assert.That(page.Diagnostics.CopyStatusText, Is.EqualTo("已复制到剪贴板"));
        Assert.That(captured, Does.Contain("诊断信息"));
        Assert.That(captured, Does.Contain("应用版本"));
    }

    [Test]
    public async Task OpenCacheFolder_WithoutLauncher_ReportsMissingChannel()
    {
        var page = CreatePage(SeededSettings(), new FakeUiSettingsStore());
        await page.LoadAsync();

        await page.OpenCacheFolderCommand.ExecuteAsync(null);

        Assert.That(page.StatusText, Is.EqualTo("当前环境没有打开目录的通道"));
    }

    [Test]
    public async Task OpenCacheFolder_PassesSavedDirectoryToLauncher()
    {
        var page = CreatePage(SeededSettings(), new FakeUiSettingsStore());
        await page.LoadAsync();
        var launcher = new RecordingShellLauncher();
        page.ShellLauncher = launcher;

        await page.OpenCacheFolderCommand.ExecuteAsync(null);

        Assert.That(launcher.Paths, Is.EqualTo(new[] { "D:/CKAN/cache" }));
        Assert.That(page.StatusText, Is.EqualTo("已打开缓存目录"));
    }

    [Test]
    public async Task OpenConfigFolder_UsesDefaultConfigDirectory()
    {
        var page = CreatePage(new FakeSettingsService(), new FakeUiSettingsStore());
        await page.LoadAsync();
        var launcher = new RecordingShellLauncher();
        page.ShellLauncher = launcher;

        await page.OpenConfigFolderCommand.ExecuteAsync(null);

        Assert.That(launcher.Paths, Is.EqualTo(new[] { SettingsPaths.DefaultConfigDirectory }));
        Assert.That(page.StatusText, Is.EqualTo("已打开配置目录"));
    }

    [Test]
    public async Task OpenCacheFolder_FailureIsReported()
    {
        var page = CreatePage(SeededSettings(), new FakeUiSettingsStore());
        await page.LoadAsync();
        page.ShellLauncher = new RecordingShellLauncher
        {
            Result = OperationResult.Fail("目录不存在：D:/CKAN/cache"),
        };

        await page.OpenCacheFolderCommand.ExecuteAsync(null);

        Assert.That(page.StatusText, Is.EqualTo("打开缓存目录失败：目录不存在：D:/CKAN/cache"));
    }

    /// <summary>记录型「打开目录」宿主实现。</summary>
    private sealed class RecordingShellLauncher : ISettingsShellLauncher
    {
        public List<string> Paths { get; } = new();

        public OperationResult Result { get; set; } = OperationResult.Ok();

        public Task<OperationResult> OpenFolderAsync(string path)
        {
            Paths.Add(path);
            return Task.FromResult(Result);
        }
    }
    [Test]
    public async Task SecondActivation_KeepsInFlightEdits()
    {
        var page = CreatePage(SeededSettings(), new FakeUiSettingsStore(), InstancesWith(@"C:\Games\KSP"));

        page.Activate();
        var item = Item(page, SettingsKeyNames.DevBuilds);
        item.EditValue = "false";
        Assert.That(item.IsDirty, Is.True);

        page.Deactivate();

        // 再次进入只补实例相关项，不能把还没保存的编辑值冲回磁盘上的旧值，也不该再闪一次加载态。
        page.Activate();

        Assert.That(item.EditValue, Is.EqualTo("false"));
        Assert.That(page.IsLoading, Is.False);
    }

}
