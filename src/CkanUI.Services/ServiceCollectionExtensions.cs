using CkanUI.Services.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace CkanUI.Services;

/// <summary>
/// 服务层注册入口。App 的 ServiceRegistration 调用本方法后再补注册 App 自己拥有的实现
/// （IDispatcher / IUser / IDialogService / IErrorReporter）。
/// 之所以放在 Services 而不是全部写在 App：具体实现类型属于 Services 的私有细节（21 篇 §5 模块边界）。
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>注册服务层实现（INT-02..INT-10 逐个补齐）。</summary>
    public static IServiceCollection AddCkanServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services;
    }
}
