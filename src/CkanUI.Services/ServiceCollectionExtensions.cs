using CkanUI.Services.Abstractions;
using CkanUI.Services.Bootstrap;
using CkanUI.Services.Errors;
using CkanUI.Services.Instances;
using CkanUI.Services.Operations;
using CkanUI.Services.Registry;
using CkanUI.Services.Repositories;
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
        services.TryAddSingleton<IRepositoryService, RepositoryService>();
        services.TryAddSingleton<IUiSettingsStore, Settings.UiSettingsStore>();
        services.TryAddSingleton<ISettingsService, Settings.SettingsService>();

        // 启动编排放在最后：它依赖上面三个领域服务。
        services.TryAddSingleton<IAppBootstrapper, AppBootstrapper>();

        return services;
    }
}
