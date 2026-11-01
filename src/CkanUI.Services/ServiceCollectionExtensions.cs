using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Assets;
using CkanUI.Services.Assets;
using CkanUI.Services.Bootstrap;
using CkanUI.Services.Cache;
using CkanUI.Services.ChangeSets;
using CkanUI.Services.Cleanup;
using CkanUI.Services.Compatibility;
using CkanUI.Services.Content;
using CkanUI.Services.Dedup;
using CkanUI.Services.Downloads;
using CkanUI.Services.Errors;
using CkanUI.Services.History;
using CkanUI.Services.Installers;
using CkanUI.Services.Instances;
using CkanUI.Services.Labels;
using CkanUI.Services.Lifecycle;
using CkanUI.Services.ModConfig;
using CkanUI.Services.ModPacks;
using CkanUI.Services.Operations;
using CkanUI.Services.PlayTime;
using CkanUI.Services.Plugins;
using CkanUI.Services.Registry;
using CkanUI.Services.Saves;
using CkanUI.Services.Repositories;
using CkanUI.Services.Search;
using CkanUI.Services.Stats;
using CkanUI.Services.Unmanaged;
using CKAN;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CkanUI.Services;

/// <summary>
/// 服务层注册入口。App 的 ServiceRegistration 调用本方法后再补注册 App 自己拥有的实现
/// （IDispatcher / IUser / IDialogService / IErrorReporter 的宿主形态，以及 Core 的四个类型）。
/// 之所以放在 Services 而不是全部写在 App：具体实现类型属于 Services 的私有细节（21 篇 §5 模块边界）。
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>注册服务层实现（INT-02..INT-10）。已存在的注册不会被覆盖。</summary>
    public static IServiceCollection AddCkanServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // 基础能力：错误上报与操作队列是所有其它服务的依赖，先注册。
        services.TryAddSingleton<IErrorReporter, ErrorReporter>();
        services.TryAddSingleton<IOperationQueue, OperationQueue>();

        // 领域服务（顺序无关，构造函数注入会按需解析）。
        services.TryAddSingleton<IInstanceService, InstanceService>();
        services.TryAddSingleton<IRegistryService, RegistryService>();

        // 主菜单「游戏截图」页签：只枚举 GameDir/Screenshots 里的图片文件，解码留给 App 层。
        services.TryAddSingleton<IGameScreenshotService, GameScreenshotService>();

        // 版本目录（F-30）：详情页「全部版本」的数据源。跨所有已加载仓库取同一标识符的全部版本，
        // 数据是内存里的 CkanModule 对象图（不联网、不读盘），因此和注册表快照一样始终在线。
        services.TryAddSingleton<Abstractions.Mods.IModVersionCatalog>(provider => new Mods.CkanModVersionCatalog(
            provider.GetRequiredService<GameInstanceManager>(),
            provider.GetRequiredService<RepositoryDataManager>()));

        // 未管理文件（F-42）：受管路径集合由 Core 注册表投影，扫描/删除是纯文件系统逻辑；
        // 接管（INT-09）只通过 IManagedFileRegistrar 写 installed_dlls，写前先生成注册表快照。
        services.TryAddSingleton<IManagedPathProvider, CkanManagedPathProvider>();
        services.TryAddSingleton<IManagedFileRegistrar, CkanManagedFileRegistrar>();
        services.TryAddSingleton<IUnmanagedService, UnmanagedService>();

        // 卸载残留目录清理（F-43）：候选来源可替换，删除边界（GameData 内、非符号链接、无受管文件）全在服务里。
        services.TryAddSingleton<IResidualDirectorySource, HeuristicResidualDirectorySource>();
        services.TryAddSingleton<IResidualCleanupService, ResidualCleanupService>();

        // 下载缓存面板与 ZIP 灌缓存（F-50/F-40）：Core 的 NetModuleCache/Registry 收在 ICacheStore 后面。
        services.TryAddSingleton<ICacheStore>(provider => new CkanCacheStore(
            provider.GetRequiredService<GameInstanceManager>(),
            provider.GetRequiredService<RepositoryDataManager>()));
        services.TryAddSingleton<ICacheService, CacheService>();

        // 跨实例去重（F-41）：硬链接走平台 API（IDedupLinker 接缝），扫描/执行边界全在服务里。
        services.TryAddSingleton<IDedupLinker, NativeDedupLinker>();
        services.TryAddSingleton<IInstanceDedupService, InstanceDedupService>();

        // Mod 元数据翻译服务（17 篇 E5）：纯内存查找器 + 语言包扫描与仓库联动。
        services.TryAddSingleton<CkanUI.ModText.IModTextService, ModText.ModTextService>();

        // SpaceDock 图片抓取服务：按需懒加载模组截图与背景预览。
        services.TryAddSingleton<ISpacedockImageService, Spacedock.SpacedockImageService>();

        // 标签组（F-24）：读写上游 ModuleLabelList，落盘交给 Core 的 Save(path)，不自写 JSON。
        services.TryAddSingleton<ILabelService>(provider => new LabelService(
            provider.GetRequiredService<GameInstanceManager>(),
            provider.GetRequiredService<IDispatcher>()));

        // 搜索语法解析：页面只认 ModSearchSpec，语法权威仍在上游 ModSearch（F-20..F-23）。
        services.TryAddSingleton<IModSearchParser, ModSearchParser>();
        // 反向格式化（F-20）：图形化筛选面板改字段，搜索框要显示同一套语法文本。
        services.TryAddSingleton<IModSearchFormatter, ModSearchFormatter>();
        services.TryAddSingleton<IRepositoryService, RepositoryService>();
        services.TryAddSingleton<IChangeSetPlanner, CoreChangeSetPlanner>();
        services.TryAddSingleton<IChangeSetService, ChangeSetService>();
        services.TryAddSingleton<IInstallerBackendFactory, CkanInstallerBackendFactory>();
        // 安装历史（F-44）：装饰器包住安装器，安装/卸载/升级成功后自动落一条 history。
        services.TryAddSingleton<InstallHistoryRecorder>();
        services.TryAddSingleton<IInstallHistoryService>(provider => new InstallHistoryService(
            provider.GetRequiredService<InstallHistoryRecorder>(),
            provider.GetRequiredService<IRegistryService>(),
            provider.GetRequiredService<IChangeSetService>(),
            provider.GetRequiredService<IInstallerService>()));
        services.TryAddSingleton<IInstallerService>(provider => new HistoryRecordingInstaller(
            new InstallerService(
                provider.GetRequiredService<IInstallerBackendFactory>(),
                provider.GetRequiredService<IOperationQueue>(),
                provider.GetRequiredService<IDispatcher>(),
                provider.GetService<IErrorReporter>()),
            provider.GetRequiredService<InstallHistoryRecorder>(),
            provider.GetRequiredService<IRegistryService>()));
        services.TryAddSingleton<IUiSettingsStore, Settings.UiSettingsStore>();

        // 插件系统（F-17）：插件目录默认 %LocalAppData%/CKAN/plugins，加载器可替换。
        services.TryAddSingleton<IPluginService, PluginService>();

        // 启动后的分层资源释放（18 篇 E6）：参与方集合由 MS DI 注入空集合，无需额外注册。
        services.TryAddSingleton<IPlatformMemory, PlatformMemory>();
        services.TryAddSingleton<IResourceReleaseService, ResourceReleaseService>();
        services.TryAddSingleton<ISettingsService, Settings.SettingsService>();

        // 游戏时长（F-48）：App 数据目录下的单文件，损坏回退空表。
        services.TryAddSingleton<IPlayTimeService, PlayTimeService>();

        // 会话计时窄接口（F-48）：实例页只认 IPlayTimeTracker。必须别名到同一个单例——
        // 它和 IPlayTimeService 共用一份落盘文件（累计时长 + 会话账本），注册成两个实例会让两次原子替换互相覆盖。
        services.TryAddSingleton<IPlayTimeTracker>(provider =>
            (IPlayTimeTracker)provider.GetRequiredService<IPlayTimeService>());

        // 存档工作台（M7/E1）：格式化与诊断建立在 Sfs 内核之上，磁盘语义（快照 + 原子写）全在服务里。
        services.TryAddSingleton<ISaveFileService, SaveFileService>();

        // 导入导出（F-39..F-42）：Core 能力收在 IModPackStore 后面，逻辑全在 ModPackCatalog。
        services.TryAddSingleton<IModPackStore>(provider => new CkanModPackStore(
            provider.GetRequiredService<GameInstanceManager>(),
            provider.GetRequiredService<RepositoryDataManager>()));
        services.TryAddSingleton<ModPackCatalog>();
        services.TryAddSingleton<IModPackService, ModPackService>();

        // 下载（M2）：字节搬运在 IDownloadTransport 后面，落盘/校验/取消语义在 DownloadService。
        services.TryAddSingleton<IDownloadTransport>(provider =>
            new NetAsyncDownloaderTransport(provider.GetRequiredService<IUser>()));
        // 重试策略显式注册：虽然 DownloadService 的构造参数有默认值，但依赖注入解析不到未注册的
        // 具体类型时会走默认值——显式注册让「生产用 3 次 / 400ms→1200ms」成为可替换的单点。
        services.TryAddSingleton(DownloadRetryPolicy.Default);
        services.TryAddSingleton<IDownloadService, DownloadService>();

        // 内容预览（F-29）：ZIP 只读中央目录；未缓存时先下到临时文件再原子 Store。
        services.TryAddSingleton<IContentZipStore>(provider => new NetModuleCacheZipStore(
            provider.GetRequiredService<GameInstanceManager>(),
            provider.GetRequiredService<RepositoryDataManager>(),
            provider.GetRequiredService<IDownloadService>()));
        services.TryAddSingleton<IContentService, ContentService>();

        // 下载统计（F-47）：纯本地聚合，输入是仓库快照 + 缓存清单 + 注册表快照。
        services.TryAddSingleton<IDownloadStatsService, DownloadStatsService>();

        // 自动更新与新版本检查器（F-52 / UI-27）。
        services.TryAddSingleton<IUpdateCheckService, Update.UpdateCheckService>();

        // 兼容版本服务（02 篇 §1 Ⅱ-⑪）：供兼容版本设置对话框与首启向导解析兼容性。
        services.TryAddSingleton<ICompatibleVersionsService>(provider => new CkanCompatibleVersionsService(
            new Lazy<GameInstanceManager>(() => provider.GetRequiredService<GameInstanceManager>()),
            provider.GetService<IRegistryService>(),
            provider.GetService<IRepositoryService>()));

        // 资产工作台（载具库、旗帜库、加载图库）。
        services.TryAddSingleton<ICraftLibraryService, CraftLibraryService>();
        services.TryAddSingleton<IFlagLibraryService, FlagLibraryService>();
        services.TryAddSingleton<ILoadingImageService, LoadingImageService>();

        // 实例可靠性处置（F-08）：构造参数全可选（探针/时钟/记录路径），
        // 所以必须用工厂注册 —— 直接 AddSingleton<接口,实现> 会让 DI 去解析可选参数而失败。
        services.TryAddSingleton<Abstractions.Instances.IInstanceRepairService>(_ => new InstanceRepairService());

        // 自动更新安装（F-52）：下载复用已注册的 IDownloadService（不新起 HttpClient），
        // 解包/备份/替换/回滚都在 UpdateInstaller 里，宿主只负责喂 UpdateInstallPlan。
        services.TryAddSingleton<Abstractions.Update.IUpdateInstaller>(provider =>
            new Update.UpdateInstaller(provider.GetRequiredService<IDownloadService>()));

        // Mod 配置适配层（M9）：目录即插件（内置 <App>/adapters + 用户目录），写盘走 IAdapterWriter 单一入口。
        // 用户目录与其它 UI 侧状态同处 %LocalAppData%\CKAN\ui，卸载实例不会连带删掉用户自写的 adapter。
        services.AddCkanModConfig();

        // adapter 导入（ADP-04）：本地文件 / 远程 URL 装 adapter 包，带 SHA-256 清单与 ETag 更新检查。
        // 必须在 AddCkanModConfig() 之后 —— 安装器要拿到 IAdapterCatalog 才能在装完后热重扫。
        services.AddCkanAdapterImport();

        // 启动编排放在最后：它依赖上面三个领域服务。
        services.TryAddSingleton<IAppBootstrapper, AppBootstrapper>();

        return services;
    }
}
