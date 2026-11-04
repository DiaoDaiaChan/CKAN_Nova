using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Services.Abstractions.Home;
using CkanUI.ViewModels.Navigation;
using CkanUI.ViewModels.Pages;
using CkanUI.ViewModels.Pages.Home;
using CommunityToolkit.Mvvm.Input;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

/// <summary>
/// 主菜单页（启动落地页）：实例概览、七张常用功能卡、截图枚举与切换实例时的丢弃语义。
/// </summary>
/// <remarks>
/// 口径：这一页只回答三件事——「我在哪个实例上」「常用功能怎么一键到」「这个实例截了哪些图」。
/// 截图解码在 App 层，所以这里用假枚举服务就能覆盖全部排版文案与边界（无实例、无服务、切走实例）。
/// </remarks>
[TestFixture]
public sealed class HomePageViewModelTests
{
    private static readonly string GameDir = Path.Combine(Path.GetTempPath(), "ksp-home-test");

    [Test]
    public void QuickActions_AreThreeTiers_InFixedOrder()
    {
        var vm = new HomePageViewModel();
        var all = vm.PrimaryActions.Concat(vm.PageActions).Concat(vm.ToolActions).ToArray();

        Assert.That(vm.PrimaryActions.Select(action => action.Title), Is.EqualTo(new[] { "模组库", "变更集" }),
            "第一层是两张大卡：模组库与变更集构成核心工作流。");
        Assert.That(vm.PageActions.Select(action => action.Title), Is.EqualTo(new[] { "实例管理", "存档", "模组仓库" }),
            "第二层是三张普通卡：进得去的常用页面。");
        Assert.That(vm.ToolActions.Select(action => action.Title), Is.EqualTo(new[] { "日志", "设置" }),
            "第三层是两个胶囊：工具页不该和功能页一样重。");
        Assert.That(all.Select(action => action.Glyph), Is.EqualTo(new[]
        {
            "Apps", "Sync", "Folder", "Archive", "Globe", "Bug", "Settings"
        }));
        Assert.That(all.Select(action => action.Command), Has.None.Null);
    }

    [Test]
    public async Task QuickAction_Executes_NavigatesToTargetPage()
    {
        var navigation = new NavigationService(key => key == "mods" ? new object() : null);
        var vm = new HomePageViewModel(null, null, navigation);

        await ((IAsyncRelayCommand)vm.PrimaryActions[0].Command).ExecuteAsync(null);

        Assert.That(navigation.CurrentKey, Is.EqualTo("mods"), "第一张卡是「模组库」，点了就要落到 mods 页。");
    }

    [Test]
    public async Task QuickAction_FallsBackToInstances_WhenTheTargetPageBelongsToADisabledPlugin()
    {
        // 存档页搬进了可停用的插件：停用后 "saves" 解析不出页面，卡片不能变成死按钮。
        var navigation = new RecordingNavigation(availableKey: "instances");
        var vm = new HomePageViewModel(null, null, navigation);
        var saves = vm.PageActions.Single(action => action.Title == "存档");

        await ((IAsyncRelayCommand)saves.Command).ExecuteAsync(null);

        Assert.That(navigation.Requests, Is.EqualTo(new[] { "saves", "instances" }),
            "目标页不可用时退到它的归属分节，让用户还能看到当前有哪些页签。");
    }

    [Test]
    public void WithoutInstance_ShowsEmptyState_AndDisablesInstanceCommands()
    {
        var vm = new HomePageViewModel();

        Assert.That(vm.HasInstance, Is.False);
        Assert.That(vm.ShowNoInstance, Is.True);
        Assert.That(vm.InstanceName, Is.EqualTo("还没有可用实例"));
        Assert.That(vm.CanLaunch, Is.False);
        Assert.That(vm.LaunchGameCommand.CanExecute(null), Is.False);
        Assert.That(vm.OpenGameFolderCommand.CanExecute(null), Is.False);
        Assert.That(vm.GameDirText, Is.Empty);
        Assert.That(vm.ScreenshotFolderText, Is.Empty);
    }

    [Test]
    public void LockedInstance_DisablesLaunch_AndExplainsWhy()
    {
        var instances = new InstancesFakeService();
        instances.Current = new InstanceInfo("Main", GameDir, "1.12.5", IsDefault: true, IsLocked: true, ModuleCount: 42);
        var vm = new HomePageViewModel(instances, null);

        Assert.That(vm.HasInstance, Is.True);
        Assert.That(vm.CanLaunch, Is.False, "实例被占用（游戏可能正在运行）时不能再启动一次。");
        Assert.That(vm.ShowLockedHint, Is.True);
        Assert.That(vm.ShowDefaultBadge, Is.True);
        Assert.That(vm.InstanceName, Does.Contain("1.12.5"));
        Assert.That(vm.ModuleCountText, Is.EqualTo("42 个模组"));
    }

    [Test]
    public void ModuleCountAndVersion_MissingValues_FallBackToReadableText()
    {
        var instances = new InstancesFakeService();
        instances.Current = new InstanceInfo("Bare", GameDir, null, false, false, null);
        var vm = new HomePageViewModel(instances, null);

        Assert.That(vm.InstanceVersionText, Is.EqualTo("版本未知"));
        Assert.That(vm.ModuleCountText, Is.EqualTo("模组数未知"));
    }

    [Test]
    public async Task Activate_LoadsScreenshots_AsReadableRows()
    {
        var instances = new InstancesFakeService();
        instances.Current = new InstanceInfo("Main", GameDir, "1.12.5", true, false, 3);
        var screenshots = new FakeScreenshotService();
        screenshots.Items.Add(new GameScreenshotInfo(
            Path.Combine(GameDir, "Screenshots", "shot1.png"),
            "shot1.png",
            1572864,
            new DateTimeOffset(2024, 5, 1, 10, 0, 0, TimeSpan.Zero)));
        var vm = new HomePageViewModel(instances, screenshots);

        vm.Activate();
        await vm.RefreshScreenshotsCommand.ExecuteAsync(null);

        Assert.That(vm.Screenshots, Has.Count.EqualTo(1));
        Assert.That(vm.Screenshots[0].FileName, Is.EqualTo("shot1.png"));
        Assert.That(vm.Screenshots[0].CapturedText, Does.StartWith("拍摄于 "));
        Assert.That(vm.Screenshots[0].SizeText, Is.EqualTo("1.5 MB"));
        Assert.That(vm.ScreenshotCountText, Is.EqualTo("1 张截图"));
        Assert.That(vm.ShowEmptyScreenshots, Is.False);
        Assert.That(vm.ShowScreenshotRail, Is.True, "有图才占胶片带的位置。");
        Assert.That(screenshots.LastLimit, Is.EqualTo(12), "一次只取一屏的量，别把几十张图整个读进来。");
    }

    [Test]
    public void NoScreenshotService_ShowsEmptyStateWithoutTouchingDisk()
    {
        var instances = new InstancesFakeService();
        instances.Current = new InstanceInfo("Main", GameDir, "1.12.5", true, false, 0);
        var vm = new HomePageViewModel(instances, null);

        vm.Activate();

        Assert.That(vm.Screenshots, Is.Empty);
        Assert.That(vm.ShowEmptyScreenshots, Is.True);
        Assert.That(vm.ShowScreenshotRail, Is.False, "没图就收起胶片带，别在页面上留一条空带。");
        Assert.That(vm.ScreenshotCountText, Is.EqualTo("0 张截图"));
    }

    [Test]
    public void ScreenshotFolderText_ComesFromService()
    {
        var instances = new InstancesFakeService();
        instances.Current = new InstanceInfo("Main", GameDir, "1.12.5", true, false, 0);
        var screenshots = new FakeScreenshotService();
        var vm = new HomePageViewModel(instances, screenshots);

        Assert.That(vm.ScreenshotFolderText, Is.EqualTo(Path.Combine(GameDir, "Screenshots")));
    }

    [Test]
    public async Task InstanceSwitchedWhileLoading_DiscardsTheOtherInstancesRows()
    {
        var instances = new InstancesFakeService();
        instances.Current = new InstanceInfo("Main", GameDir, "1.12.5", true, false, 0);
        var screenshots = new FakeScreenshotService { Gate = new TaskCompletionSource<bool>() };
        screenshots.Items.Add(new GameScreenshotInfo("stale.png", "stale.png", 1024, DateTimeOffset.UtcNow));
        screenshots.OnlyGameDir = GameDir;
        var vm = new HomePageViewModel(instances, screenshots);
        // 激活后才订阅 CurrentChanged，实例切换才会走到“重新枚举”这条路径。
        vm.Activate();

        var loading = vm.RefreshScreenshotsCommand.ExecuteAsync(null);
        // 枚举还没回来，用户已经在实例页切走了——这批结果属于上一个实例。
        instances.Current = new InstanceInfo("Other", GameDir + "-other", "1.12.5", false, false, 0);
        // Current 是自动属性，事件要显式发，才等于实例页真的切走了。
        instances.RaiseCurrentChanged();
        screenshots.Gate.SetResult(true);
        await loading;

        Assert.That(vm.Screenshots, Is.Empty, "切走实例后旧的截图不能落到新实例的页签里。");
        Assert.That(vm.IsBusy, Is.False);
    }

    [Test]
    public void Widgets_StartWithTheDefaultThree_AndAddingRemovingPersists()
    {
        var settings = new FakeUiSettingsStore();
        var vm = new HomePageViewModel(null, null, null, null, settings);
        vm.Activate();

        Assert.That(vm.Widgets.Select(widget => widget.Id), Is.EqualTo(new[]
        {
            HomePageViewModel.InstanceWidgetId,
            HomePageViewModel.QuickActionsWidgetId,
            HomePageViewModel.ScreenshotsWidgetId,
        }), "第一次打开主页应该是实例概览、快捷操作、最近截图三块。");
        Assert.That(vm.AvailableWidgets.Select(option => option.Id), Is.EquivalentTo(new[]
        {
            HomePageViewModel.ModUpdatesWidgetId,
            HomePageViewModel.InstallHistoryWidgetId,
            HomePageViewModel.CacheWidgetId,
            HomePageViewModel.SavesWidgetId,
            HomePageViewModel.OperationsWidgetId,
        }), "模板自带的数据部件一开始全部在「可添加」里。");

        vm.RemoveWidgetCommand.Execute(HomePageViewModel.ScreenshotsWidgetId);

        Assert.That(vm.Widgets.Select(widget => widget.Id), Is.EqualTo(new[]
        {
            HomePageViewModel.InstanceWidgetId,
            HomePageViewModel.QuickActionsWidgetId,
        }));
        Assert.That(vm.AvailableWidgets.Select(option => option.Id), Does.Contain(HomePageViewModel.ScreenshotsWidgetId));
        Assert.That(settings.Current.HomeWidgets, Is.EqualTo(new[]
        {
            HomePageViewModel.InstanceWidgetId,
            HomePageViewModel.QuickActionsWidgetId,
        }), "布局要立刻落盘，不能等退出应用。");

        vm.AddWidgetById(HomePageViewModel.ScreenshotsWidgetId);

        Assert.That(vm.Widgets[^1].Id, Is.EqualTo(HomePageViewModel.ScreenshotsWidgetId), "添加的部件放在末尾。");
    }

    [Test]
    public void Widgets_AddBuiltInDataWidget_ProducesAFeedWidget()
    {
        var settings = new FakeUiSettingsStore();
        var vm = new HomePageViewModel(null, null, null, null, settings);
        vm.Activate();

        vm.AddWidgetById(HomePageViewModel.CacheWidgetId);

        var widget = vm.Widgets.OfType<FeedHomeWidgetViewModel>().SingleOrDefault();
        Assert.That(widget, Is.Not.Null);
        Assert.That(widget!.Id, Is.EqualTo(HomePageViewModel.CacheWidgetId));
        Assert.That(widget.Title, Is.EqualTo("下载缓存"));
        Assert.That(widget.CanOpen, Is.True);
        Assert.That(settings.Current.HomeWidgets, Does.Contain(HomePageViewModel.CacheWidgetId));
        Assert.That(vm.AvailableWidgets.Select(option => option.Id), Does.Not.Contain(HomePageViewModel.CacheWidgetId));
    }

    [Test]
    public void Widgets_MoveBeforeAndAfter_RewritesTheSavedOrder()
    {
        var settings = new FakeUiSettingsStore();
        var vm = new HomePageViewModel(null, null, null, null, settings);
        vm.Activate();

        vm.MoveWidgetAfter(HomePageViewModel.InstanceWidgetId, HomePageViewModel.ScreenshotsWidgetId);

        Assert.That(vm.Widgets.Select(widget => widget.Id), Is.EqualTo(new[]
        {
            HomePageViewModel.QuickActionsWidgetId,
            HomePageViewModel.ScreenshotsWidgetId,
            HomePageViewModel.InstanceWidgetId,
        }));
        Assert.That(settings.Current.HomeWidgets![^1], Is.EqualTo(HomePageViewModel.InstanceWidgetId));

        vm.MoveWidgetBefore(HomePageViewModel.InstanceWidgetId, HomePageViewModel.QuickActionsWidgetId);

        Assert.That(vm.Widgets[0].Id, Is.EqualTo(HomePageViewModel.InstanceWidgetId));
        Assert.That(settings.Current.HomeWidgets![0], Is.EqualTo(HomePageViewModel.InstanceWidgetId));
    }

    [Test]
    public async Task PluginWidget_ShowsTheSnapshot_AndKeepsItsSlotWhenThePluginGoesAway()
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        try
        {
            var settings = new FakeUiSettingsStore().With(new UiSettings
            {
                HomeWidgets = new[] { "plugin:com.example.rp1:career" },
            });
            var plugins = new FakePluginService();
            plugins.HomeWidgetBindings.Add(new PluginHomeWidgetBinding(
                "com.example.rp1",
                "RP-1 示例",
                new PluginHomeWidgetDescriptor("career", "生涯日志", "desc", "History", "rp1-career")));
            plugins.HomeWidgetHandler = (_, _, _) => Task.FromResult(OperationResult<PluginHomeWidgetSnapshot>.Ok(
                new PluginHomeWidgetSnapshot(
                    "生涯日志",
                    "DSC · RP-1 4.7.0.0",
                    new[] { new PluginHomeWidgetMetric("当前日期", "1988-08-01") },
                    new[] { new PluginHomeWidgetLine("第一次轨道飞行", "1961-04-12", "里程碑") },
                    "存档更新于 2026-11-04 07:10")));
            var vm = new HomePageViewModel(null, null, null, plugins, settings);

            vm.Activate();
            var widget = await WaitForPluginWidgetAsync(vm, HasSnapshot);

            Assert.That(widget.IsPlugin, Is.True);
            Assert.That(widget.PluginName, Is.EqualTo("RP-1 示例"));
            Assert.That(widget.Metrics.Select(metric => metric.Label), Is.EqualTo(new[] { "当前日期" }));
            Assert.That(widget.Lines[0].Badge, Is.EqualTo("里程碑"));
            Assert.That(plugins.HomeWidgetRequests, Is.EqualTo(new[] { "com.example.rp1:career" }));

            // 插件被停用：声明消失，但布局里的 plugin: 槽位要留着，重新启用后回原位。
            plugins.HomeWidgetBindings.Clear();
            plugins.RaiseChanged();

            Assert.That(vm.Widgets.OfType<PluginHomeWidgetViewModel>(), Is.Empty, "停用后不再显示插件部件。");
            Assert.That(settings.Current.HomeWidgets, Does.Contain("plugin:com.example.rp1:career"),
                "停用不等于删布局，槽位要留给重新启用。");
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    private static bool HasSnapshot(PluginHomeWidgetViewModel widget)
        => !widget.IsLoading && (widget.HasMetrics || widget.HasLines || widget.HasError || !string.IsNullOrWhiteSpace(widget.StatusText));

    private static async Task<PluginHomeWidgetViewModel> WaitForPluginWidgetAsync(
        HomePageViewModel page,
        Func<PluginHomeWidgetViewModel, bool> ready)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            var widget = page.Widgets.OfType<PluginHomeWidgetViewModel>().FirstOrDefault();
            if (widget is not null && ready(widget))
            {
                return widget;
            }

            await Task.Delay(10);
        }

        Assert.Fail("插件部件没有在 2 秒内拿到数据。");
        throw new InvalidOperationException();
    }

    /// <summary>只记录跳转请求的导航替身：可以精确断言"先试目标页、失败再退分节"。</summary>
    private sealed class RecordingNavigation : INavigationService
    {
        private readonly string _availableKey;

        public RecordingNavigation(string availableKey) => _availableKey = availableKey;

        public List<string> Requests { get; } = new();

        public object? CurrentPage => null;

        public string CurrentKey => string.Empty;

        public event Action<object?, string>? Navigated
        {
            add { }
            remove { }
        }

        public Task<bool> NavigateAsync(string pageKey, object? parameter = null, CancellationToken ct = default)
        {
            Requests.Add(pageKey);
            return Task.FromResult(string.Equals(pageKey, _availableKey, StringComparison.Ordinal));
        }
    }

    private sealed class FakeScreenshotService : IGameScreenshotService
    {
        public List<GameScreenshotInfo> Items { get; } = new();

        public TaskCompletionSource<bool>? Gate { get; set; }

        public int? LastLimit { get; private set; }

        /// <summary>只对这一个游戏目录返回 <see cref="Items"/>，用来区分“切走前那批”和“切走后这批”。</summary>
        public string? OnlyGameDir { get; set; }

        public string GetScreenshotsDirectory(string gameDir)
            => string.IsNullOrWhiteSpace(gameDir) ? string.Empty : Path.Combine(gameDir, "Screenshots");

        public async Task<OperationResult<IReadOnlyList<GameScreenshotInfo>>> ListAsync(
            string gameDir,
            int limit = 12,
            CancellationToken cancellationToken = default)
        {
            LastLimit = limit;
            if (Gate is not null)
            {
                await Gate.Task.ConfigureAwait(false);
            }

            var rows = OnlyGameDir is null || string.Equals(OnlyGameDir, gameDir, StringComparison.Ordinal)
                ? Items.ToArray()
                : Array.Empty<GameScreenshotInfo>();
            return OperationResult<IReadOnlyList<GameScreenshotInfo>>.Ok(rows);
        }
    }
}
