using CkanUI.Services.Abstractions.ModConfig;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CkanUI.Services.ModConfig;

/// <summary>
/// Mod 配置（M9）服务的注册入口。
/// <para>
/// 单独放一个扩展方法而不是塞进 <c>AddCkanServices</c>：那个文件是全项目共享的，
/// 由 Lead 统一接线（本次只新增本文件，需要 Lead 在 AddCkanServices 末尾加一行
/// <c>services.AddCkanModConfig();</c>）。
/// </para>
/// </summary>
public static class ModConfigServiceCollectionExtensions
{
    /// <summary>注册适配器目录、页面数据服务与写盘器（全部 TryAdd，允许调用方覆盖）。</summary>
    public static IServiceCollection AddCkanModConfig(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // 目录本身无状态依赖，但内置目录要看程序目录，所以用工厂而不是实例。
        services.TryAddSingleton<IAdapterCatalog>(_ => new AdapterCatalog());
        services.TryAddSingleton<IGameRunningProbe>(provider => new InstanceGameRunningProbe(
            provider.GetService<Abstractions.IInstanceService>()));
        services.TryAddSingleton<IAdapterWriter>(provider => new AdapterWriter(
            provider.GetRequiredService<IAdapterCatalog>(),
            provider.GetService<IGameRunningProbe>()));
        services.TryAddSingleton<IModConfigService>(provider => new ModConfigService(
            provider.GetRequiredService<IAdapterCatalog>(),
            provider.GetRequiredService<Abstractions.IInstanceService>(),
            provider.GetRequiredService<Abstractions.IRegistryService>(),
            provider.GetService<IGameRunningProbe>()));

        return services;
    }
}
