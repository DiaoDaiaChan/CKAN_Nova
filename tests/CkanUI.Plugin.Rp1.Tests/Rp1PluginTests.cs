using CkanUI.Plugin.Rp1.UI;
using NUnit.Framework;

namespace CkanUI.Plugin.Rp1.Tests;

[TestFixture]
public sealed class Rp1PluginTests
{
    [Test]
    public void Plugin_DeclaresOneToolsPageWithHistoryGlyph()
    {
        var plugin = new global::CkanUI.Plugin.Rp1.Rp1Plugin();

        Assert.Multiple(() =>
        {
            Assert.That(plugin.Id, Is.EqualTo("com.ckanui.rp1"));
            Assert.That(plugin.Name, Is.EqualTo("RP-1 生涯日志"));
            Assert.That(plugin.Pages, Has.Count.EqualTo(1));
            Assert.That(plugin.Pages[0].Key, Is.EqualTo("rp1-career"));
            Assert.That(plugin.Pages[0].Title, Is.EqualTo("RP-1 生涯日志"));
            Assert.That(plugin.Pages[0].Glyph, Is.EqualTo("History"));
            Assert.That(plugin.Pages[0].Section, Is.Null, "独立侧栏入口：生涯日志页签独立成Tab");
            Assert.That(plugin.Pages[0].Order, Is.EqualTo(40));
            Assert.That(plugin.Pages[0].ViewModelTypeName, Is.EqualTo(typeof(Rp1CareerPageViewModel).FullName));
        });
    }

    [Test]
    public async Task Plugin_DeclaresOneHomeWidget_ForTheCareerTimeline()
    {
        var plugin = new global::CkanUI.Plugin.Rp1.Rp1Plugin();

        Assert.That(plugin.HomeWidgets, Has.Count.EqualTo(1));
        var widget = plugin.HomeWidgets[0];
        Assert.That(widget.Key, Is.EqualTo(global::CkanUI.Plugin.Rp1.Rp1Plugin.CareerWidgetKey));
        Assert.That(widget.Title, Is.EqualTo("生涯日志"));
        Assert.That(widget.Glyph, Is.EqualTo("History"));
        Assert.That(widget.PageKey, Is.EqualTo(global::CkanUI.Plugin.Rp1.Rp1Plugin.PageKey));

        var unknown = await plugin.GetHomeWidgetAsync("missing", new CkanUI.Services.Abstractions.Home.PluginHomeWidgetContext());
        Assert.That(unknown.IsSuccess, Is.False);
    }

    [Test]
    public async Task HomeWidget_WithoutAnInstance_SaysWhatToDoInsteadOfShowingZeros()
    {
        var plugin = new global::CkanUI.Plugin.Rp1.Rp1Plugin();
        var result = await plugin.GetHomeWidgetAsync(
            global::CkanUI.Plugin.Rp1.Rp1Plugin.CareerWidgetKey,
            new CkanUI.Services.Abstractions.Home.PluginHomeWidgetContext());

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Error, Does.Contain("选中实例"));
        Assert.That(result.Value.Metrics, Is.Null.Or.Empty, "没有数据时不能编一条 0 资金出来。");
    }

    [Test]
    public void Plugin_InitializeAndDeinitializeAreReentrant()
    {
        var plugin = new global::CkanUI.Plugin.Rp1.Rp1Plugin();
        plugin.Initialize();
        plugin.Initialize();
        plugin.Deinitialize();
        plugin.Deinitialize();
    }
}
