using CkanUI.App.Plugins;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace CkanUI.App.Tests;

/// <summary>
/// 插件页签的类型解析与构造（12 篇 F-17、ARC-03）：契约只给字符串，宿主负责落地成页面实例。
/// </summary>
[TestFixture]
public sealed class PluginPageFactoryTests
{
    /// <summary>无依赖的插件页视图模型。</summary>
    public sealed class PlainPluginViewModel
    {
    }

    /// <summary>需要注入宿主服务的插件页视图模型。</summary>
    public sealed class InjectedPluginViewModel
    {
        public InjectedPluginViewModel(TestDependency dependency) => Dependency = dependency;

        public TestDependency Dependency { get; }
    }

    /// <summary>依赖不可满足的插件页视图模型。</summary>
    public sealed class UnsatisfiablePluginViewModel
    {
        public UnsatisfiablePluginViewModel(ICkanUiPlugin missing) => Plugin = missing;

        public ICkanUiPlugin Plugin { get; }
    }

    /// <summary>可注入的测试依赖。</summary>
    public sealed class TestDependency
    {
    }

    [Test]
    public void ResolveViewModelType_ByAssemblyQualifiedName()
    {
        var resolved = PluginPageFactory.ResolveViewModelType(typeof(PlainPluginViewModel).AssemblyQualifiedName!);

        Assert.That(resolved, Is.EqualTo(typeof(PlainPluginViewModel)));
    }

    [Test]
    public void ResolveViewModelType_ByFullName()
    {
        var resolved = PluginPageFactory.ResolveViewModelType(typeof(PlainPluginViewModel).FullName);

        Assert.That(resolved, Is.EqualTo(typeof(PlainPluginViewModel)));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("No.Such.Plugin.PageViewModel")]
    public void ResolveViewModelType_Unknown_ReturnsNull(string? typeName)
    {
        Assert.That(PluginPageFactory.ResolveViewModelType(typeName), Is.Null);
    }

    [Test]
    public void Create_BuildsInstanceWithDependencies()
    {
        using var provider = new ServiceCollection()
            .AddSingleton<TestDependency>()
            .BuildServiceProvider();

        var page = PluginPageFactory.Create(provider, typeof(InjectedPluginViewModel));

        Assert.That(page, Is.TypeOf<InjectedPluginViewModel>());
        Assert.That(((InjectedPluginViewModel)page!).Dependency, Is.Not.Null);
    }

    [Test]
    public void Create_UnsatisfiableDependency_ReturnsNull()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();

        Assert.That(PluginPageFactory.Create(provider, typeof(UnsatisfiablePluginViewModel)), Is.Null);
    }

    [Test]
    public void CreateFor_ActivePluginPage_ReturnsViewModel()
    {
        var plugins = new StubPluginService();
        plugins.Active.Add(ActivePlugin("hello", typeof(PlainPluginViewModel).AssemblyQualifiedName!));
        using var provider = new ServiceCollection()
            .AddSingleton<IPluginService>(plugins)
            .BuildServiceProvider();

        var page = PluginPageFactory.CreateFor(provider, "hello", new[] { "mods", "settings" });

        Assert.That(page, Is.TypeOf<PlainPluginViewModel>());
    }

    [Test]
    public void CreateFor_UnknownKey_ReturnsNull()
    {
        var plugins = new StubPluginService();
        plugins.Active.Add(ActivePlugin("hello", typeof(PlainPluginViewModel).AssemblyQualifiedName!));
        using var provider = new ServiceCollection()
            .AddSingleton<IPluginService>(plugins)
            .BuildServiceProvider();

        Assert.That(PluginPageFactory.CreateFor(provider, "nope", Array.Empty<string>()), Is.Null);
    }

    [Test]
    public void CreateFor_ReservedKey_ReturnsNull()
    {
        var plugins = new StubPluginService();
        plugins.Active.Add(ActivePlugin("settings", typeof(PlainPluginViewModel).AssemblyQualifiedName!));
        using var provider = new ServiceCollection()
            .AddSingleton<IPluginService>(plugins)
            .BuildServiceProvider();

        Assert.That(PluginPageFactory.CreateFor(provider, "settings", new[] { "settings" }), Is.Null);
    }

    [Test]
    public void CreateFor_UnresolvableType_ReturnsNull()
    {
        var plugins = new StubPluginService();
        plugins.Active.Add(ActivePlugin("hello", "No.Such.Plugin.PageViewModel"));
        using var provider = new ServiceCollection()
            .AddSingleton<IPluginService>(plugins)
            .BuildServiceProvider();

        Assert.That(PluginPageFactory.CreateFor(provider, "hello", Array.Empty<string>()), Is.Null);
    }

    [Test]
    public void CreateFor_WithoutPluginService_ReturnsNull()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();

        Assert.That(PluginPageFactory.CreateFor(provider, "hello", Array.Empty<string>()), Is.Null);
    }

    private static PluginInfo ActivePlugin(string key, string typeName) =>
        new(
            "com.example." + key,
            key,
            "1.0",
            "tester",
            string.Empty,
            string.Empty,
            @"C:\CKAN\plugins\" + key + ".dll",
            PluginState.Active)
        {
            Pages = new[] { new PluginPageDescriptor(key, "示例页 - " + key, typeName) },
        };

    /// <summary>只提供列表的插件服务替身。</summary>
    private sealed class StubPluginService : IPluginService
    {
        public List<PluginInfo> Active { get; } = new();

        public string PluginsDirectory => @"C:\CKAN\plugins";

        IReadOnlyList<PluginInfo> IPluginService.Active => Active;

        public IReadOnlyList<PluginInfo> Dormant => Array.Empty<PluginInfo>();

        public IReadOnlyList<PluginInfo> Failures => Array.Empty<PluginInfo>();

        public event EventHandler? Changed
        {
            add { }
            remove { }
        }

        public Task<OperationResult<IReadOnlyList<PluginInfo>>> DiscoverAsync(CancellationToken ct = default) =>
            Task.FromResult(OperationResult<IReadOnlyList<PluginInfo>>.Ok(Active.ToArray()));

        public Task<PluginLoadResult> LoadAsync(string pluginPath, bool activate = false, CancellationToken ct = default) =>
            Task.FromResult(PluginLoadResult.Fail(PluginLoadFailure.LoadFailed, "stub"));

        public Task<PluginLoadResult> ActivateAsync(string pluginId, CancellationToken ct = default) =>
            Task.FromResult(PluginLoadResult.Fail(PluginLoadFailure.LoadFailed, "stub"));

        public Task<OperationResult> DeactivateAsync(string pluginId, CancellationToken ct = default) =>
            Task.FromResult(OperationResult.Fail("stub"));

        public Task<PluginLoadResult> ReloadAsync(string pluginId, CancellationToken ct = default) =>
            Task.FromResult(PluginLoadResult.Fail(PluginLoadFailure.LoadFailed, "stub"));

        public Task<OperationResult> UnloadAsync(string pluginId, CancellationToken ct = default) =>
            Task.FromResult(OperationResult.Fail("stub"));

        public Task<PluginLoadResult> AddFromPathAsync(string sourcePath, bool activate = false, CancellationToken ct = default) =>
            Task.FromResult(PluginLoadResult.Fail(PluginLoadFailure.LoadFailed, "stub"));
    }
}
