using CkanUI.Services.Abstractions.Dtos;
using NUnit.Framework;

namespace CkanUI.Plugin.SpaceDock.Tests;

[TestFixture]
public sealed class SpaceDockPluginTests
{
    [Test]
    public void Descriptor_IsPlacedInModsSection()
    {
        var plugin = new SpaceDockPlugin();

        Assert.That(plugin.Id, Is.EqualTo("com.ckanui.spacedock"));
        Assert.That(plugin.Name, Is.EqualTo("SpaceDock"));
        Assert.That(plugin.Version, Is.EqualTo("1.0.0"));
        Assert.That(plugin.Pages, Has.Count.EqualTo(1));

        var page = plugin.Pages[0];
        Assert.That(page.Key, Is.EqualTo("spacedock"));
        Assert.That(page.Section, Is.EqualTo("mods"));
        Assert.That(page.Order, Is.EqualTo(60));
        Assert.That(page.TitleKey, Is.EqualTo("Ui_NavRail_SpaceDock"));
        Assert.That(page.Glyph, Is.EqualTo("Globe"));
        Assert.That(page.ViewModelTypeName, Is.EqualTo(typeof(SpaceDockPageViewModel).FullName));
    }

    [Test]
    public void Lifecycle_CanRunTwiceWithoutState()
    {
        var plugin = new SpaceDockPlugin();

        Assert.DoesNotThrow(plugin.Initialize);
        Assert.DoesNotThrow(plugin.Deinitialize);
        Assert.DoesNotThrow(plugin.Initialize);
        Assert.DoesNotThrow(plugin.Deinitialize);
    }
}
