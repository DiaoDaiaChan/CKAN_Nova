using Avalonia.Controls;
using CkanUI.Platform.Dialogs;
using CkanUI.Platform.Threading;
using CkanUI.Services;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Bootstrap;
using CkanUI.Services.Users;
using CkanUI.ViewModels;
using CkanUI.ViewModels.Navigation;
using CkanUI.ViewModels.Pages;
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
        services.AddSingleton<IDialogService>(sp => new DialogService(
            sp.GetRequiredService<IDispatcher>(),
            ownerProvider));

        // 服务层（Services 项目自有实现）。
        services.AddCkanServices();

        // IUser 的 UI 形态：Core 的同步回调会在这里被拦到 UI 线程（INT-03）。
        services.AddCkanUser(sp => new AvaloniaUser(
            sp.GetRequiredService<IDispatcher>(),
            sp.GetRequiredService<IDialogService>(),
            sp.GetRequiredService<IErrorReporter>()));

        // Core 类型：由 Services 层从 Core 的 Autofac 容器转接过来。
        services.AddCkanCoreServices();

        // 导航与外壳：页面 VM 首次访问时创建，之后复用同一个实例（切换不重建，避免丢状态）。
        services.AddSingleton<INavigationService>(_ => new NavigationService(CreatePageFactory()));
        services.AddSingleton<ShellViewModel>();

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = false,
            ValidateScopes = true,
        });
    }

    private static Func<string, object?> CreatePageFactory()
    {
        var pages = new Dictionary<string, object?>(StringComparer.Ordinal);
        return key =>
        {
            if (pages.TryGetValue(key, out var cached)) return cached;

            object? page = key switch
            {
                "mods" => new ModsPageViewModel(),
                "instances" => new InstancesPageViewModel(),
                "settings" => new SettingsPageViewModel(),
                _ => null,
            };
            if (page is not null) pages[key] = page;
            return page;
        };
    }
}
