using System;
using System.IO;
using CkanUI.Services;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Plugins;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace CkanUI.App.Tests;

/// <summary>
/// 插件系统的 DI 接线（12 篇 F-17）：宿主启动时是「按类型取 <see cref="PluginBootstrapper"/>」，
/// 少注册一条就是启动期静默没有首方页签，而服务层自己的用例全绿——所以这条必须在容器层面钉住。
/// </summary>
[TestFixture]
public sealed class PluginRegistrationTests
{
    [Test]
    public void AddCkanServices_ResolvesBootstrapperAndPluginService()
    {
        var services = new ServiceCollection();
        services.AddCkanServices();
        using var provider = services.BuildServiceProvider();

        // App.axaml.cs 的后台引导拿不到 bootstrapper 就只写一条警告、界面从此没有首方页签。
        // 这条断言是那条静默降级的对侧：容器里必须有它，而且必须是同一份激活存储。
        Assert.That(provider.GetService<PluginBootstrapper>(), Is.Not.Null);
        Assert.That(provider.GetService<IPluginActivationStore>(), Is.Not.Null);
        var pluginService = provider.GetService<IPluginService>();
        Assert.That(pluginService, Is.Not.Null);
        Assert.That(pluginService!.BundledDirectory, Is.EqualTo(Path.Combine(AppContext.BaseDirectory, "plugins")));
    }

    [Test]
    public void AddCkanServices_RegistersBootstrapperAsSingleton()
    {
        var services = new ServiceCollection();
        services.AddCkanServices();
        services.AddCkanServices();
        using var provider = services.BuildServiceProvider();

        var a = provider.GetRequiredService<PluginBootstrapper>();
        var b = provider.GetRequiredService<PluginBootstrapper>();
        Assert.That(ReferenceEquals(a, b), Is.True, "bootstrapper 必须是单例：重复引导会把同一份激活记录写两遍。");
    }
}
