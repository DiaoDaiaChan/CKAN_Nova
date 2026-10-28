using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Services.Plugins;
using CkanUI.TestSupport;
using NUnit.Framework;

namespace CkanUI.Services.Tests;

/// <summary>
/// 插件工具项（12 篇 F-17「往现有工具条插按钮」）：元数据透传 + 回调执行 + 失败隔离。
/// </summary>
/// <remarks>
/// 口径与页签一致：插件是第三方代码，宿主只认「声明了什么」和「回调有没有抛」，
/// 其余（页面挂在哪、按钮长什么样）都不是服务层的事。
/// </remarks>
[TestFixture]
public sealed class PluginToolItemTests
{
    private TempWorkspace? _workspace;
    private string _root = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _workspace = new TempWorkspace("plugin-tools");
        _root = _workspace.Path;
    }

    [TearDown]
    public void TearDown()
    {
        _workspace?.Dispose();
        _workspace = null;
    }

    [Test]
    public async Task ToolItems_AreExposedOnThePluginInfoAfterLoading()
    {
        var plugin = new ToolItemPlugin
        {
            ToolItems =
            [
                new PluginToolItemDescriptor("mods", "rescan", "重新扫描", "把 GameData 再扫一遍"),
            ],
        };
        var service = CreateService(plugin);

        var result = await service.LoadAsync(Path.Combine(_root, "tools.dll"));

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(result.Plugin!.ToolItems, Has.Count.EqualTo(1));
        Assert.That(result.Plugin.ToolItems[0].TargetPageKey, Is.EqualTo("mods"));
        Assert.That(result.Plugin.ToolItems[0].Key, Is.EqualTo("rescan"));
    }

    [Test]
    public async Task InvokeToolItemAsync_CallsThePluginWithTheDeclaredKey()
    {
        var plugin = new ToolItemPlugin
        {
            ToolItems = [new PluginToolItemDescriptor("mods", "rescan", "重新扫描")],
        };
        var service = CreateService(plugin);
        await service.LoadAsync(Path.Combine(_root, "tools.dll"), activate: true);

        var result = await service.InvokeToolItemAsync("com.example.tools", "rescan");

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(plugin.Executed, Is.EqualTo(new[] { "rescan" }));
    }

    [Test]
    public async Task InvokeToolItemAsync_MatchesTheKeyIgnoringCase()
    {
        var plugin = new ToolItemPlugin
        {
            ToolItems = [new PluginToolItemDescriptor("mods", "Rescan", "重新扫描")],
        };
        var service = CreateService(plugin);
        await service.LoadAsync(Path.Combine(_root, "tools.dll"), activate: true);

        var result = await service.InvokeToolItemAsync("com.example.tools", "  rescan  ");

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(plugin.Executed, Is.EqualTo(new[] { "Rescan" }));
    }

    [Test]
    public async Task InvokeToolItemAsync_RejectsAnUnknownPlugin()
    {
        var service = CreateService(new ToolItemPlugin());

        var result = await service.InvokeToolItemAsync("com.example.missing", "rescan");

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("还没有加载"));
    }

    [Test]
    public async Task InvokeToolItemAsync_RequiresAnActivePlugin()
    {
        var plugin = new ToolItemPlugin
        {
            ToolItems = [new PluginToolItemDescriptor("mods", "rescan", "重新扫描")],
        };
        var service = CreateService(plugin);
        await service.LoadAsync(Path.Combine(_root, "tools.dll"));

        var result = await service.InvokeToolItemAsync("com.example.tools", "rescan");

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("未激活"));
        Assert.That(plugin.Executed, Is.Empty);
    }

    [Test]
    public async Task InvokeToolItemAsync_RejectsAKeyThePluginDidNotDeclare()
    {
        var plugin = new ToolItemPlugin
        {
            ToolItems = [new PluginToolItemDescriptor("mods", "rescan", "重新扫描")],
        };
        var service = CreateService(plugin);
        await service.LoadAsync(Path.Combine(_root, "tools.dll"), activate: true);

        var result = await service.InvokeToolItemAsync("com.example.tools", "delete-everything");

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("没有声明键"));
        Assert.That(plugin.Executed, Is.Empty);
    }

    [Test]
    public async Task InvokeToolItemAsync_TurnsPluginExceptionsIntoRetryableFailures()
    {
        var plugin = new ToolItemPlugin
        {
            ToolItems = [new PluginToolItemDescriptor("mods", "rescan", "重新扫描")],
            ExecuteException = new InvalidOperationException("插件内部炸了"),
        };
        var service = CreateService(plugin);
        await service.LoadAsync(Path.Combine(_root, "tools.dll"), activate: true);

        var result = await service.InvokeToolItemAsync("com.example.tools", "rescan");

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("插件内部炸了"));
        Assert.That(result.Retryable, Is.True);
        Assert.That(result.Detail, Does.Contain("InvalidOperationException"));
    }

    [Test]
    public async Task InvokeToolItemAsync_ExplainsWhenThePluginDoesNotImplementTheCallback()
    {
        var plugin = new MinimalToolItemPlugin
        {
            ToolItems = [new PluginToolItemDescriptor("mods", "rescan", "重新扫描")],
        };
        var service = CreateService(plugin);
        await service.LoadAsync(Path.Combine(_root, "minimal.dll"), activate: true);

        var result = await service.InvokeToolItemAsync("com.example.minimal", "rescan");

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("未实现工具项执行"));
    }

    private PluginService CreateService(ICkanUiPlugin plugin) =>
        new(Path.Combine(_root, "plugins"), new SinglePluginLoader(plugin), "9.9.9");
}

/// <summary>声明工具项并记录调用的插件替身。</summary>
internal sealed class ToolItemPlugin : ICkanUiPlugin
{
    /// <inheritdoc />
    public string Id { get; set; } = "com.example.tools";

    /// <inheritdoc />
    public string Name { get; set; } = "tools";

    /// <inheritdoc />
    public string Version { get; set; } = "1.0.0";

    /// <inheritdoc />
    public string Author { get; set; } = "测试";

    /// <inheritdoc />
    public string Description { get; set; } = "工具项测试插件。";

    /// <inheritdoc />
    public string MinimumHostVersion { get; set; } = string.Empty;

    /// <inheritdoc />
    public IReadOnlyList<PluginToolItemDescriptor> ToolItems { get; set; } = Array.Empty<PluginToolItemDescriptor>();

    /// <summary>被回调过的工具项键（按调用顺序）。</summary>
    public List<string> Executed { get; } = [];

    /// <summary>非 null 时回调抛这个异常。</summary>
    public Exception? ExecuteException { get; set; }

    /// <inheritdoc />
    public Task ExecuteToolItemAsync(string key, CancellationToken ct)
    {
        if (ExecuteException is not null)
        {
            throw ExecuteException;
        }

        Executed.Add(key);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Initialize()
    {
    }

    /// <inheritdoc />
    public void Deinitialize()
    {
    }
}

/// <summary>声明了工具项但没实现回调的插件（用来验证默认接口实现的提示）。</summary>
internal sealed class MinimalToolItemPlugin : ICkanUiPlugin
{
    /// <inheritdoc />
    public string Id => "com.example.minimal";

    /// <inheritdoc />
    public string Name => "minimal";

    /// <inheritdoc />
    public string Version => "1.0.0";

    /// <inheritdoc />
    public string Author => "测试";

    /// <inheritdoc />
    public string Description => "只声明不实现。";

    /// <inheritdoc />
    public string MinimumHostVersion => string.Empty;

    /// <inheritdoc />
    public IReadOnlyList<PluginToolItemDescriptor> ToolItems { get; set; } = Array.Empty<PluginToolItemDescriptor>();

    /// <inheritdoc />
    public void Initialize()
    {
    }

    /// <inheritdoc />
    public void Deinitialize()
    {
    }
}

/// <summary>只加载一个已知插件实例的假加载器。</summary>
internal sealed class SinglePluginLoader : IPluginAssemblyLoader
{
    private readonly ICkanUiPlugin _plugin;

    public SinglePluginLoader(ICkanUiPlugin plugin) => _plugin = plugin;

    public PluginAssemblyLoadAttempt TryLoad(string assemblyPath) =>
        PluginAssemblyLoadAttempt.Ok(new PluginAssemblyLoad(_plugin, _plugin.GetType().Name, () => { }));
}
