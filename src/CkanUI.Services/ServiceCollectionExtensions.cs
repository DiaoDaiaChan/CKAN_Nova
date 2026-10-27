using CkanUI.Services.Abstractions;
using CkanUI.Services.Bootstrap;
using CkanUI.Services.Cache;
using CkanUI.Services.ChangeSets;
using CkanUI.Services.Cleanup;
using CkanUI.Services.Content;
using CkanUI.Services.Dedup;
using CkanUI.Services.Downloads;
using CkanUI.Services.Errors;
using CkanUI.Services.History;
using CkanUI.Services.Installers;
using CkanUI.Services.Instances;
using CkanUI.Services.Labels;
using CkanUI.Services.Lifecycle;
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

        // 未管理文件（F-42）：受管路径集合由 Core 注册表投影，扫描/删除是纯文件系统逻辑。
        services.TryAddSingleton<IManagedPathProvider, CkanManagedPathProvider>();
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

        // 标签组（F-24）：读写上游 ModuleLabelList，落盘交给 Core 的 Save(path)，不自写 JSON。
        services.TryAddSingleton<ILabelService>(provider => new LabelService(
            provider.GetRequiredService<GameInstanceManager>(),
            provider.GetRequiredService<IDispatcher>()));

        // 搜索语法解析：页面只认 ModSearchSpec，语法权威仍在上游 ModSearch（F-20..F-23）。
        services.TryAddSingleton<IModSearchParser, ModSearchParser>();
        services.TryAddSingleton<IRepositoryService, RepositoryService>();
        services.TryAddSingleton<IChangeSetPlanner, CoreChangeSetPlanner>();
        services.TryAddSingleton<IChangeSetService, ChangeSetService>();
        services.TryAddSingleton<IInstallerBackendFactory, CkanInstallerBackendFactory>();
        // 安装历史（F-44）：装饰器包住安装器，安装/卸载/升级成功后自动落一条 history。
        services.TryAddSingleton<InstallHistoryRecorder>();
        services.TryAddSingleton<IInstallHistoryService, InstallHistoryService>();
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

        // 启动编排放在最后：它依赖上面三个领域服务。
        services.TryAddSingleton<IAppBootstrapper, AppBootstrapper>();

        return services;
    }
}
