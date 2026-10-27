using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using CkanUI.App.Dialogs;
using CkanUI.App.Localization;
using CkanUI.App.Tray;
using CkanUI.Localization;
using CkanUI.Platform.Dialogs;
using CkanUI.Platform.Threading;
using CkanUI.Services;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Bootstrap;
using CkanUI.Services.Diagnostics;
using CkanUI.Services.Users;
using CkanUI.ViewModels;
using CkanUI.ViewModels.Navigation;
using CkanUI.ViewModels.Operations;
using CkanUI.ViewModels.Pages;
using CkanUI.ViewModels.Pages.Settings;
using CkanUI.ViewModels.Shell;
using Microsoft.Extensions.DependencyInjection;

namespace CkanUI.App;

/// <summary>
/// DI 组装根（02 篇 §2 / ARC-01）。这是全应用唯一允许 new 具体服务实现的地方。
/// 规则：本容器不与 Core 的 Autofac 容器合并，Core 类型由 Services 层的 AddCkanCoreServices 转接。
/// </summary>
public static class ServiceRegistration
{
    /// <summary>
    /// 构建 UI 容器。
    /// </summary>
    /// <param name="ownerProvider">返回当前主窗口，供模态对话框指定 owner（05 篇 §2 铁律：必须指定 owner）。</param>
    public static ServiceProvider Build(Func<Window?> ownerProvider)
    {
        ArgumentNullException.ThrowIfNull(ownerProvider);

        var services = new ServiceCollection();

        // 表现层基础设施：IDispatcher 与 IErrorReporter 是所有服务的共同依赖。
        services.AddSingleton<IDispatcher>(new AvaloniaDispatcher());

        // 剪贴板通道（UI-09 复制诊断信息）：VM 只拿到委托，不碰 Avalonia。
        services.AddSingleton<Func<string, Task>>(_ => CreateClipboardWriter(ownerProvider));

        // 对话框：契约在 Abstractions、实现留在 Platform、视图由 App 的呈现者接上（03 篇 §8）。
        services.AddSingleton<IDialogService>(sp => new DialogService(
            sp.GetRequiredService<IDispatcher>(),
            ownerProvider,
            new DialogPresenter(ownerProvider, sp.GetRequiredService<Func<string, Task>>())));

        // 服务层（Services 项目自有实现）。
        services.AddCkanServices();

        // 卸载收尾的残留目录对话框（F-43）：只在卸载后出现一次，故单独成呈现者，不塞进 DialogPresenter。
        services.AddSingleton<IResidualCleanupDialogService>(sp => new ResidualCleanupDialogPresenter(
            ownerProvider,
            sp.GetRequiredService<IResidualCleanupService>(),
            sp.GetService<IErrorReporter>()));

        // 模组对话框族（F-34..F-36）：推荐选择 / 提供者选择 / 冲突详情。VM 只认 IModDialogService。
        services.AddSingleton<IModDialogService>(sp => new ModDialogService(
            ownerProvider,
            sp.GetRequiredService<Func<string, Task>>()));

        // IUser 的 UI 形态：Core 的同步回调会在这里被拦到 UI 线程（INT-03）。
        services.AddCkanUser(sp => new AvaloniaUser(
            sp.GetRequiredService<IDispatcher>(),
            sp.GetRequiredService<IDialogService>(),
            sp.GetRequiredService<IErrorReporter>()));

        // Core 类型：由 Services 层从 Core 的 Autofac 容器转接过来。
        services.AddCkanCoreServices();

        // 页面 VM 一律登记为单例：导航切换不重建，避免丢状态（04 篇 §1）。
        // 各页面的构造依赖由容器注入，页面工厂只做"按 key 取实例"，不再 new 具体 VM。
        services.AddSingleton<OperationQueueViewModel>();
        // 变更集页同时充当「变更请求落点」：模组页把勾选灌进去再导航过去（IChangeRequestSink）。
        services.AddSingleton(sp => new ChangesPageViewModel(
            sp.GetService<IChangeSetService>(),
            sp.GetService<IInstallerService>(),
            sp.GetRequiredService<IErrorReporter>(),
            sp.GetService<IOperationQueue>(),
            sp.GetService<IDispatcher>(),
            sp.GetService<IResidualCleanupDialogService>(),
            sp.GetRequiredService<IInstanceService>(),
            sp.GetService<IModDialogService>()));
        services.AddSingleton<IChangeRequestSink>(sp => sp.GetRequiredService<ChangesPageViewModel>());
        services.AddSingleton(sp => new ModsPageViewModel(
            sp.GetRequiredService<IRepositoryService>(),
            sp.GetRequiredService<IRegistryService>(),
            sp.GetRequiredService<IInstanceService>(),
            sp.GetRequiredService<IErrorReporter>(),
            sp.GetService<IChangeSetService>(),
            sp.GetService<IUiSettingsStore>(),
            sp.GetService<IChangeRequestSink>(),
            sp.GetService<INavigationService>(),
            sp.GetService<IModSearchParser>(),
            sp.GetService<ILabelService>()));
        // 内容预览页要拿模组页的当前选中项（F-29）：只经 IContentTargetProvider 窄接口，不直接耦合。
        services.AddSingleton<IContentTargetProvider>(sp =>
            new ModsSelectionContentTarget(sp.GetRequiredService<ModsPageViewModel>()));
        services.AddSingleton<ContentPageViewModel>();
        services.AddSingleton<ReposPageViewModel>();
        services.AddSingleton<ModPacksPageViewModel>();
        services.AddSingleton<ModPackEditorPageViewModel>();
        // 实例页要拿资源释放服务与 UI 设置（18 篇 E6）：视图层的 ??= 注入拿不到容器，只能在这里挂。
        services.AddSingleton(sp =>
        {
            var page = ActivatorUtilities.CreateInstance<InstancesPageViewModel>(sp);
            page.ResourceRelease = sp.GetRequiredService<IResourceReleaseService>();
            page.UiSettings = sp.GetRequiredService<IUiSettingsStore>();
            return page;
        });
        services.AddSingleton<SavesPageViewModel>();
        services.AddSingleton<UnmanagedPageViewModel>();
        services.AddSingleton<CachePageViewModel>();
        services.AddSingleton<DownloadStatsPageViewModel>();
        services.AddSingleton<PlayTimePageViewModel>();
        services.AddSingleton<DedupPageViewModel>();
        services.AddSingleton<InstallHistoryPageViewModel>();
        services.AddSingleton<PluginsPageViewModel>();
        // 设置页的诊断区需要 CKAN 库版本（12 篇 F-55 同源）：VM 只收委托，CKAN 类型不越界。
        services.AddSingleton(sp =>
        {
            var page = ActivatorUtilities.CreateInstance<SettingsPageViewModel>(sp);
            page.Diagnostics.CoreVersionProvider = () => CoreVersionInfo.ReleaseVersion;
            return page;
        });

        // 本地化：磁盘语言包优先、内嵌兜底；App 侧把告警接到 log4net（docs/ops/i18n-guide.md §6.1）。
        services.AddSingleton<ILocalizer>(_ =>
        {
            var packs = Path.Combine(AppContext.BaseDirectory, "i18n", "packs");
            return LocalizerFactory.CreateFromDirectory(packs, new LocalizationOptions
            {
                Log = new Log4NetLocalizationLog(),
                FallbackLocale = "en-US",
                // 只切 CurrentUICulture，数字/日期仍跟随系统区域（16 篇 §2.3 / Q3）。
                ApplyThreadCulture = true,
            });
        });

        // 视图模型不引用本地化层（架构铁律 2），只拿一个「取译文」委托。
        services.AddSingleton<Func<string, string>>(sp => sp.GetRequiredService<ILocalizer>().Get);

        // 导航与外壳。
        services.AddSingleton<INavigationService>(sp => new NavigationService(CreatePageFactory(sp)));
        services.AddSingleton<ShellViewModel>();

        // 托盘（12 篇 F-51 / UI-26）：VM 只描述菜单，图标与原生菜单由 App 层的 TrayHost 装。
        services.AddSingleton<TrayHostActions>(sp => new TrayHostActions(
            ownerProvider,
            () => SettingsPaths.DefaultConfigDirectory,
            () => null,
            () => sp.GetService<IInstanceService>()?.Current?.GameDir,
            sp.GetService<IRepositoryService>(),
            () => (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.TryShutdown(0),
            sp.GetRequiredService<IDispatcher>(),
            sp.GetService<ISettingsService>()));
        services.AddSingleton<TrayMenuViewModel>(sp => new TrayMenuViewModel(sp.GetRequiredService<TrayHostActions>()));
        services.AddSingleton<TrayHost>(sp => new TrayHost(
            Application.Current!,
            sp.GetRequiredService<TrayMenuViewModel>(),
            sp.GetRequiredService<IUiSettingsStore>(),
            sp.GetRequiredService<IDispatcher>()));

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = false,
            ValidateScopes = true,
        });
    }

    /// <summary>剪贴板写入委托：没有可用 owner 或剪贴板时抛 <see cref="NotSupportedException"/>，由 VM 转成提示文案。</summary>
    private static Func<string, Task> CreateClipboardWriter(Func<Window?> ownerProvider)
    {
        return async text =>
        {
            var window = ownerProvider();
            var clipboard = window is null ? null : TopLevel.GetTopLevel(window)?.Clipboard;
            if (clipboard is null)
            {
                throw new NotSupportedException("没有可用的剪贴板通道");
            }

            await clipboard.SetTextAsync(text);
        };
    }

    // 页面 key ↔ 视图模型类型。新增页面时只改这张表与上方的单例登记。
    private static readonly Dictionary<string, Type> PageTypes = new(StringComparer.Ordinal)
    {
        ["mods"] = typeof(ModsPageViewModel),
        ["changes"] = typeof(ChangesPageViewModel),
        ["content"] = typeof(ContentPageViewModel),
        ["repos"] = typeof(ReposPageViewModel),
        ["modpacks"] = typeof(ModPacksPageViewModel),
        ["modpack-editor"] = typeof(ModPackEditorPageViewModel),
        ["instances"] = typeof(InstancesPageViewModel),
        ["saves"] = typeof(SavesPageViewModel),
        ["unmanaged"] = typeof(UnmanagedPageViewModel),
        ["cache"] = typeof(CachePageViewModel),
        ["downloadstats"] = typeof(DownloadStatsPageViewModel),
        ["playtime"] = typeof(PlayTimePageViewModel),
        ["dedup"] = typeof(DedupPageViewModel),
        ["installhistory"] = typeof(InstallHistoryPageViewModel),
        ["plugins"] = typeof(PluginsPageViewModel),
        ["settings"] = typeof(SettingsPageViewModel),
    };

    private static Func<string, object?> CreatePageFactory(IServiceProvider services)
    {
        return key => PageTypes.TryGetValue(key, out var type)
            ? services.GetService(type)
            : null;
    }
}
