using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Services.Abstractions.Home;
using CkanUI.Services.Plugins;
using CkanUI.TestSupport;
using NUnit.Framework;

namespace CkanUI.Services.Tests;

/// <summary>插件主页部件：宿主只转发声明与数据请求，插件停用后一律不可见。</summary>
[TestFixture]
public sealed class HomeWidgetPluginServiceTests
{
    private TempWorkspace? _workspace;
    private string _root = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _workspace = new TempWorkspace("homewidgets");
        _root = _workspace.Path;
    }

    [TearDown]
    public void TearDown()
    {
        _workspace?.Dispose();
        _workspace = null;
    }

    [Test]
    public async Task ActiveHomeWidgets_ExposeDeclarations_OnlyWhileActive()
    {
        var plugin = new HomeWidgetPlugin();
        var service = CreateService(plugin);
        await service.LoadAsync(Path.Combine(_root, "home.dll"), activate: true);

        Assert.That(service.ActiveHomeWidgets, Has.Count.EqualTo(1));
        var binding = service.ActiveHomeWidgets[0];
        Assert.That(binding.PluginId, Is.EqualTo("com.example.home"));
        Assert.That(binding.Widget.Key, Is.EqualTo("career"));
        Assert.That(binding.Widget.PageKey, Is.EqualTo("career-page"));

        await service.DeactivateAsync("com.example.home");

        Assert.That(service.ActiveHomeWidgets, Is.Empty, "停用后不能再把部件露给主页。");
    }

    [Test]
    public async Task GetHomeWidgetAsync_ForwardsTheContext_AndRejectsUnknownKeys()
    {
        var plugin = new HomeWidgetPlugin();
        var service = CreateService(plugin);
        await service.LoadAsync(Path.Combine(_root, "home.dll"), activate: true);
        var context = new PluginHomeWidgetContext("Main", @"D:\KSP", "1.12.5");

        var result = await service.GetHomeWidgetAsync("com.example.home", "career", context);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Title, Is.EqualTo("生涯日志"));
        Assert.That(plugin.LastContext, Is.SameAs(context), "宿主不能改写插件看到的实例上下文。");

        var unknown = await service.GetHomeWidgetAsync("com.example.home", "missing", context);
        Assert.That(unknown.IsSuccess, Is.False);
        Assert.That(unknown.Message, Does.Contain("没有声明"));
    }

    [Test]
    public async Task GetHomeWidgetAsync_PluginThrows_BecomesRetryableFailure()
    {
        var plugin = new HomeWidgetPlugin { Throw = new InvalidOperationException("存档读坏了") };
        var service = CreateService(plugin);
        await service.LoadAsync(Path.Combine(_root, "home.dll"), activate: true);

        var result = await service.GetHomeWidgetAsync(
            "com.example.home",
            "career",
            new PluginHomeWidgetContext("Main", @"D:\KSP", "1.12.5"));

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("存档读坏了"));
        Assert.That(result.Retryable, Is.True, "读一次失败不代表插件坏了，主页应该允许重试。");
    }

    private PluginService CreateService(ICkanUiPlugin plugin) =>
        new(Path.Combine(_root, "plugins"), new SinglePluginLoader(plugin), "9.9.9");
}

internal sealed class HomeWidgetPlugin : ICkanUiPlugin
{
    public string Id => "com.example.home";

    public string Name => "home";

    public string Version => "1.0.0";

    public string Author => "测试";

    public string Description => "主页部件测试插件。";

    public string MinimumHostVersion => string.Empty;

    public IReadOnlyList<PluginHomeWidgetDescriptor> HomeWidgets { get; } = new[]
    {
        new PluginHomeWidgetDescriptor("career", "生涯日志", "RP-1 生涯", "History", "career-page"),
    };

    public PluginHomeWidgetContext? LastContext { get; private set; }

    public Exception? Throw { get; set; }

    public Task<OperationResult<PluginHomeWidgetSnapshot>> GetHomeWidgetAsync(
        string key,
        PluginHomeWidgetContext context,
        CancellationToken ct = default)
    {
        if (Throw is not null)
        {
            throw Throw;
        }

        LastContext = context;
        return Task.FromResult(OperationResult<PluginHomeWidgetSnapshot>.Ok(
            new PluginHomeWidgetSnapshot("生涯日志", "DSC")));
    }

    public void Initialize()
    {
    }

    public void Deinitialize()
    {
    }
}
