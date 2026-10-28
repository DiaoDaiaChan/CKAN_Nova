using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CkanUI.App.Plugins;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Navigation;
using NUnit.Framework;

namespace CkanUI.App.Tests;

/// <summary>
/// 插件工具项的宿主接线（12 篇 F-17）：按页分发、跟插件状态刷新、坏插件不拖垮工具条。
/// </summary>
[TestFixture]
public sealed class PluginToolItemHostTests
{
    [Test]
    public void Install_PushesButtonsToTheHostOfTheTargetPage()
    {
        var plugins = new ToolFakePlugins();
        plugins.Items.Add(Plugin("a", "演示", Item("mods", "m", "模组工具"), Item("content", "c", "内容工具")));
        var mods = new RecordingToolHost(PluginToolCatalog.ModsPageKey);
        var content = new RecordingToolHost("content");

        using var host = NewHost(plugins, mods, content);

        Assert.That(mods.Tools.Select(t => t.Key), Is.EqualTo(new[] { "m" }));
        Assert.That(content.Tools.Select(t => t.Key), Is.EqualTo(new[] { "c" }));
    }

    [Test]
    public void Install_WithAnUnknownPageKey_PushesNothingThere()
    {
        var plugins = new ToolFakePlugins();
        plugins.Items.Add(Plugin("a", "演示", Item("mods", "m", "模组工具")));
        var other = new RecordingToolHost("repos");

        using var host = NewHost(plugins, other);

        Assert.That(other.Tools, Is.Empty);
    }

    [Test]
    public void Changed_RefreshesTheButtons()
    {
        var plugins = new ToolFakePlugins();
        var mods = new RecordingToolHost(PluginToolCatalog.ModsPageKey);
        using var host = NewHost(plugins, mods);
        Assert.That(mods.Tools, Is.Empty);

        plugins.Items.Add(Plugin("a", "演示", Item("mods", "m", "模组工具")));
        plugins.RaiseChanged();

        Assert.That(mods.Tools.Select(t => t.Key), Is.EqualTo(new[] { "m" }));
    }

    [Test]
    public void ActiveThrowing_DoesNotBreakTheToolbar()
    {
        var plugins = new ToolFakePlugins { ThrowOnActive = true };
        var mods = new RecordingToolHost(PluginToolCatalog.ModsPageKey);

        Assert.DoesNotThrow(() => NewHost(plugins, mods).Dispose());
        Assert.That(mods.Tools, Is.Empty);
    }

    [Test]
    public void Dispose_StopsFollowingPluginChanges()
    {
        var plugins = new ToolFakePlugins();
        var mods = new RecordingToolHost(PluginToolCatalog.ModsPageKey);
        var host = NewHost(plugins, mods);
        host.Dispose();

        plugins.Items.Add(Plugin("a", "演示", Item("mods", "m", "模组工具")));
        plugins.RaiseChanged();

        Assert.That(mods.Tools, Is.Empty);
    }

    [Test]
    public async Task Buttons_InvokeTheServiceWithTheDeclaringPluginAndKey()
    {
        var plugins = new ToolFakePlugins();
        plugins.Items.Add(Plugin("a", "演示", Item("mods", "rescan", "重新扫描")));
        var mods = new RecordingToolHost(PluginToolCatalog.ModsPageKey);
        using var host = NewHost(plugins, mods);

        await mods.Tools[0].ExecuteCommand.ExecuteAsync(null);

        Assert.That(plugins.Invoked, Is.EqualTo(new[] { "com.example.a/rescan" }));
        Assert.That(mods.Tools[0].LastMessage, Does.Contain("已执行"));
    }

    [Test]
    public void Install_RejectsANullPluginService()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new PluginToolItemHost(null!, Array.Empty<IPluginToolHost>(), new ToolInlineDispatcher()));
    }

    private static PluginToolItemHost NewHost(ToolFakePlugins plugins, params IPluginToolHost[] hosts)
    {
        var host = new PluginToolItemHost(plugins, hosts, new ToolInlineDispatcher());
        host.Install();
        return host;
    }

    private static PluginToolItemDescriptor Item(string page, string key, string title) => new(page, key, title);

    private static PluginInfo Plugin(string id, string name, params PluginToolItemDescriptor[] items) =>
        new(
            "com.example." + id,
            name,
            "1.0.0",
            "测试",
            "演示用插件。",
            string.Empty,
            "C:/plugins/" + id + ".dll",
            PluginState.Active)
        {
            ToolItems = items,
        };
}

/// <summary>只实现工具项相关成员的假插件服务。</summary>
internal sealed class ToolFakePlugins : IPluginService
{
    public List<PluginInfo> Items { get; } = [];

    public List<string> Invoked { get; } = [];

    public bool ThrowOnActive { get; set; }

    public string PluginsDirectory => "C:/plugins";

    public IReadOnlyList<PluginInfo> Active => ThrowOnActive
        ? throw new InvalidOperationException("插件服务炸了")
        : Items;

    public IReadOnlyList<PluginInfo> Dormant => Array.Empty<PluginInfo>();

    public IReadOnlyList<PluginInfo> Failures => Array.Empty<PluginInfo>();

    public event EventHandler? Changed;

    public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    public Task<OperationResult> InvokeToolItemAsync(string pluginId, string itemKey, CancellationToken ct = default)
    {
        Invoked.Add(pluginId + "/" + itemKey);
        return Task.FromResult(OperationResult.Ok());
    }

    public Task<OperationResult<IReadOnlyList<PluginInfo>>> DiscoverAsync(CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<PluginLoadResult> LoadAsync(string pluginPath, bool activate = false, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<PluginLoadResult> ActivateAsync(string pluginId, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<OperationResult> DeactivateAsync(string pluginId, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<PluginLoadResult> ReloadAsync(string pluginId, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<OperationResult> UnloadAsync(string pluginId, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<PluginLoadResult> AddFromPathAsync(string sourcePath, bool activate = false, CancellationToken ct = default) =>
        throw new NotSupportedException();
}

/// <summary>记录收到的工具项按钮的假页面。</summary>
internal sealed class RecordingToolHost : IPluginToolHost
{
    public RecordingToolHost(string key) => ToolHostKey = key;

    public string ToolHostKey { get; }

    public IReadOnlyList<PluginToolButtonViewModel> Tools { get; private set; } = Array.Empty<PluginToolButtonViewModel>();

    public void SetPluginTools(IReadOnlyList<PluginToolButtonViewModel> tools) => Tools = tools;
}

/// <summary>同步执行 Post 的假调度器。</summary>
internal sealed class ToolInlineDispatcher : IDispatcher
{
    public bool IsOnUiThread => true;

    public void Post(Action action) => action();

    public Task InvokeAsync(Action action)
    {
        action();
        return Task.CompletedTask;
    }

    public Task<T> InvokeAsync<T>(Func<T> func) => Task.FromResult(func());
}
