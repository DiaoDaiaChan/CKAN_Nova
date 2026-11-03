using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.KspConfig;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CkanUI.Services.KspConfig;

/// <summary>
/// KSP 原生配置（settings.cfg）读写与 Squad 官方本地化服务依赖注入扩展。
/// </summary>
public static class KspConfigServiceCollectionExtensions
{
    /// <summary>
    /// 注册 IKspConfigService 及其实现。
    /// </summary>
    public static IServiceCollection AddKspConfig(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IKspConfigService>(provider => new KspConfigService(
            provider.GetService<IInstanceService>(),
            provider.GetService<IErrorReporter>()));

        return services;
    }
}
