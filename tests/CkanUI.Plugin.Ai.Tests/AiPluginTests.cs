using CkanUI.Plugin.Ai;

using NUnit.Framework;

namespace CkanUI.Plugin.Ai.Tests;

/// <summary>插件入口：宿主认的就是这几个元数据，字段漂移等于页签不出现在界面里。</summary>
[TestFixture]
public sealed class AiPluginTests
{
    [Test]
    public void DescriptorMatchesHostContract()
    {
        var plugin = new AiPlugin();

        Assert.Multiple(() =>
        {
            Assert.That(plugin.Id, Is.EqualTo("com.ckanui.ai"));
            Assert.That(plugin.Name, Is.EqualTo("AI 助手"));
            Assert.That(plugin.Version, Is.EqualTo("1.0.0"));
            Assert.That(plugin.Pages, Has.Count.EqualTo(1));
        });

        var page = plugin.Pages[0];
        Assert.Multiple(() =>
        {
            Assert.That(page.Key, Is.EqualTo("ai-assistant"));
            Assert.That(page.Title, Is.EqualTo("AI 助手"));
            Assert.That(page.Glyph, Is.EqualTo("Sparkle"));
            Assert.That(page.Section, Is.Null);
            Assert.That(page.Order, Is.EqualTo(10));
            Assert.That(page.ViewModelTypeName, Is.EqualTo("CkanUI.Plugin.Ai.AiAssistantPageViewModel"));
        });
    }

    [Test]
    public void LifecycleCallbacksAreIdempotent()
    {
        var plugin = new AiPlugin();

        Assert.DoesNotThrow(() =>
        {
            plugin.Initialize();
            plugin.Initialize();
            plugin.Deinitialize();
            plugin.Deinitialize();
        });
    }
}
