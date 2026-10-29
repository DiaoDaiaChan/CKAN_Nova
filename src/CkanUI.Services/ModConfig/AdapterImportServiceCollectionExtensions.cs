using CkanUI.Services.Abstractions.ModConfig;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CkanUI.Services.ModConfig;

/// <summary>adapter 导入（ADP-04）的 DI 注册：独立扩展方法，不改既有的 AddCkanModConfig()。</summary>
public static class AdapterImportServiceCollectionExtensions
{
    /// <summary>
    /// 注册 <see cref="IAdapterPackageFetcher"/> 与 <see cref="IAdapterInstaller"/>。
    /// </summary>
    /// <remarks>
    /// 需要宿主已经注册了 <see cref="IAdapterCatalog"/>（AddCkanModConfig() 会注册）；
    /// 没注册时 catalog 参数为 null，安装完不会自动 Reload，由调用方自己刷新。
    /// </remarks>
    /// <param name="services">服务集合。</param>
    public static IServiceCollection AddCkanAdapterImport(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IAdapterPackageFetcher>(static _ => new HttpAdapterPackageFetcher());
        services.TryAddSingleton<IAdapterInstaller>(static provider => new AdapterInstaller(
            provider.GetService<IAdapterPackageFetcher>(),
            provider.GetService<IAdapterCatalog>()));
        return services;
    }
}
