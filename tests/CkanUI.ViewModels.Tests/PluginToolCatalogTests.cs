using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Navigation;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

/// <summary>
/// 插件工具项（12 篇 F-17）：收集规则与按钮执行。
/// </summary>
/// <remarks>
/// 收集规则和页签同一套口径——只收已激活、键不能空、重名留第一个；
/// 按钮侧的关注点是「插件抛异常也只坏一次点击」「连点不会执行两次」。
/// </remarks>
[TestFixture]
public sealed class PluginToolCatalogTests
{
    [Test]
    public void Collect_KeepsOnlyActivePlugins()
    {
        var plugins = new[]
        {
            Plugin("a", PluginState.Active, Item("mods", "a-1", "A 工具")),
            Plugin("d", PluginState.Dormant, Item("mods", "d-1", "D 工具")),
            Plugin("f", PluginState.Failed, Item("mods", "f-1", "F 工具")),
        };

        var entries = PluginToolCatalog.Collect(plugins);

        Assert.That(entries, Has.Count.EqualTo(1));
        Assert.That(entries[0].Key, Is.EqualTo("a-1"));
    }

    [Test]
    public void Collect_DropsEntriesWithBlankFields()
    {
        var plugins = new[]
        {
            Plugin(
                "a",
                PluginState.Active,
                new PluginToolItemDescriptor(" ", "k", "标题"),
                new PluginToolItemDescriptor("mods", "  ", "标题"),
                new PluginToolItemDescriptor("mods", "k2", "  ")),
        };

        Assert.That(PluginToolCatalog.Collect(plugins), Is.Empty);
    }

    [Test]
    public void Collect_DeduplicatesByTargetPageAndKey_KeepingTheFirstByPluginName()
    {
        var plugins = new[]
        {
            Plugin("zeta", PluginState.Active, Item("mods", "shared", "Z 的工具")),
            Plugin("alpha", PluginState.Active, Item("mods", "shared", "A 的工具")),
        };

        var entries = PluginToolCatalog.Collect(plugins);

        Assert.That(entries, Has.Count.EqualTo(1));
        // 按插件显示名排序后 alpha 先到，重名键留它。
        Assert.That(entries[0].PluginId, Is.EqualTo("com.example.alpha"));
    }

    [Test]
    public void Collect_TrimsWhitespaceAndFallsBackToNoToolTip()
    {
        var plugins = new[]
        {
            Plugin("a", PluginState.Active, new PluginToolItemDescriptor(" mods ", " k ", " 标题 ", "   ")),
        };

        var entries = PluginToolCatalog.Collect(plugins);

        Assert.That(entries[0].TargetPageKey, Is.EqualTo("mods"));
        Assert.That(entries[0].Key, Is.EqualTo("k"));
        Assert.That(entries[0].Title, Is.EqualTo("标题"));
        Assert.That(entries[0].ToolTip, Is.Null);
    }

    [Test]
    public void Collect_WithoutPlugins_ReturnsEmpty()
    {
        Assert.That(PluginToolCatalog.Collect(null), Is.Empty);
        Assert.That(PluginToolCatalog.Collect(Array.Empty<PluginInfo>()), Is.Empty);
    }

    [Test]
    public void ForPage_FiltersIgnoringCase()
    {
        var entries = PluginToolCatalog.Collect(new[]
        {
            Plugin("a", PluginState.Active, Item("mods", "m", "M"), Item("content", "c", "C")),
        });

        var forMods = PluginToolCatalog.ForPage(entries, "MODS");

        Assert.That(forMods, Has.Count.EqualTo(1));
        Assert.That(forMods[0].Key, Is.EqualTo("m"));
        Assert.That(PluginToolCatalog.ForPage(entries, " "), Is.Empty);
        Assert.That(PluginToolCatalog.ForPage(entries, "nowhere"), Is.Empty);
    }

    [Test]
    public async Task Button_ReportsSuccessInItsLastMessage()
    {
        var button = new PluginToolButtonViewModel(Entry(), _ => Task.FromResult(OperationResult.Ok()));

        await button.ExecuteCommand.ExecuteAsync(null);

        Assert.That(button.LastMessage, Does.Contain("已执行"));
        Assert.That(button.ToolTipText, Does.Contain("已执行"));
        Assert.That(button.IsBusy, Is.False);
    }

    [Test]
    public async Task Button_ReportsTheServiceFailureMessage()
    {
        var button = new PluginToolButtonViewModel(
            Entry(),
            _ => Task.FromResult(OperationResult.Fail("插件没激活。")));

        await button.ExecuteCommand.ExecuteAsync(null);

        Assert.That(button.LastMessage, Is.EqualTo("插件没激活。"));
    }

    [Test]
    public async Task Button_SwallowsDelegateExceptions()
    {
        var button = new PluginToolButtonViewModel(
            Entry(),
            _ => Task.FromException<OperationResult>(new InvalidOperationException("炸了")));

        await button.ExecuteCommand.ExecuteAsync(null);

        Assert.That(button.LastMessage, Does.Contain("炸了"));
    }

    [Test]
    public async Task Button_IgnoresClicksWhileBusy()
    {
        var gate = new TaskCompletionSource<OperationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var button = new PluginToolButtonViewModel(Entry(), _ =>
        {
            calls++;
            return gate.Task;
        });

        var first = button.ExecuteCommand.ExecuteAsync(null);
        Assert.That(button.IsBusy, Is.True);
        await button.ExecuteCommand.ExecuteAsync(null);
        gate.SetResult(OperationResult.Ok());
        await first;

        Assert.That(calls, Is.EqualTo(1));
        Assert.That(button.IsBusy, Is.False);
    }

    [Test]
    public void Button_ToolTipFallsBackToPluginNameAndKey()
    {
        var button = new PluginToolButtonViewModel(Entry(), _ => Task.FromResult(OperationResult.Ok()));

        Assert.That(button.ToolTipText, Is.EqualTo("演示插件 - 1.0.0 · rescan"));
    }

    [Test]
    public void Button_ToolTipUsesTheDeclaredHintWhenGiven()
    {
        var entry = Entry() with { ToolTip = "把 GameData 再扫一遍" };
        var button = new PluginToolButtonViewModel(entry, _ => Task.FromResult(OperationResult.Ok()));

        Assert.That(button.ToolTipText, Is.EqualTo("把 GameData 再扫一遍"));
    }

    private static PluginToolItemEntry Entry() =>
        new("com.example.tools", "演示插件 - 1.0.0", "mods", "rescan", "重新扫描", null);

    private static PluginToolItemDescriptor Item(string page, string key, string title) => new(page, key, title);

    private static PluginInfo Plugin(string id, PluginState state, params PluginToolItemDescriptor[] items) =>
        new(
            "com.example." + id,
            // 显示名排序决定去重「留谁」，这里让显示名与 id 一致，断言才好读。
            id,
            "1.0.0",
            "测试",
            "演示用插件。",
            string.Empty,
            "C:/plugins/" + id + ".dll",
            state)
        {
            ToolItems = items,
        };
}
