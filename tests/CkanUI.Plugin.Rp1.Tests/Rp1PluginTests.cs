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
            Assert.That(plugin.Pages[0].Section, Is.EqualTo("plugins"));
            Assert.That(plugin.Pages[0].Order, Is.EqualTo(40));
            Assert.That(plugin.Pages[0].ViewModelTypeName, Is.EqualTo(typeof(Rp1CareerPageViewModel).FullName));
        });
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
