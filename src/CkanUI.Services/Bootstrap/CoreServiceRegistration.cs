using Autofac;
using CKAN;
using CKAN.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CkanUI.Services.Bootstrap;

/// <summary>
/// Core 类型注册（02 篇 §2）。App 层被架构测试禁止引用 CKAN 命名空间，
/// 所以"从 Core 的 Autofac 容器取那四个类型"这件事必须留在 Services 层完成。
/// </summary>
public static class CoreServiceRegistration
{
    /// <summary>
    /// 把 Core 的 IConfiguration / RepositoryDataManager / GameInstanceManager 注册进 UI 容器。
    /// 前置条件：调用方已注册 <see cref="IUser"/>（UID 侧的 AvaloniaUser）。
    /// </summary>
    /// <summary>
    /// 注册 UI 侧的 <see cref="IUser"/> 实现。App 层不能引用 CKAN 命名空间，
    /// 所以由这里把具体的 <c>AvaloniaUser</c> 转成 Core 的 IUser 契约。
    /// </summary>
    public static IServiceCollection AddCkanUser(this IServiceCollection services,
                                                Func<IServiceProvider, Users.AvaloniaUser> factory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(factory);
        services.TryAddSingleton<IUser>(sp => factory(sp));
        return services;
    }

    public static IServiceCollection AddCkanCoreServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // IConfiguration 与 RepositoryDataManager 都是 Core Autofac 容器里的单例，
        // 直接复用可以保证与 Core 内部持有的实例是同一个。
        services.TryAddSingleton(_ => CKAN.ServiceLocator.Container.Resolve<IConfiguration>());
        services.TryAddSingleton(_ => CKAN.ServiceLocator.Container.Resolve<RepositoryDataManager>());

        // GameInstanceManager 不在 Core 容器里（GUI 与 ConsoleUI 各自 new），需要 IUser 参与构造。
        services.TryAddSingleton(sp => new GameInstanceManager(
            sp.GetRequiredService<IUser>(),
            sp.GetRequiredService<IConfiguration>()));

        return services;
    }
}
