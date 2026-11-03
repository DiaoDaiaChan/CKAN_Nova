using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Plugin.Soundtrack;
using NUnit.Framework;

namespace CkanUI.Plugin.Soundtrack.Tests;

/// <summary>
/// 插件入口的契约面：页签键/标题/图标/分节/顺序与视图模型类型。
/// </summary>
/// <remarks>
/// 这些字符串是宿主装配页签时的唯一依据：图标名写错只表现为退化成通用图标，
/// 分节写错则整页跑到侧栏去，用户找不到「音乐」这个入口。
/// </remarks>
[TestFixture]
public sealed class SoundtrackPluginTests
{
    [Test]
    public void DeclaresOnePageInTheToolsSection()
    {
        ICkanUiPlugin plugin = new SoundtrackPlugin();

        Assert.That(plugin.Id, Is.EqualTo("com.ckanui.soundtrack"));
        Assert.That(plugin.Name, Is.EqualTo("音乐"));
        Assert.That(plugin.Pages, Has.Count.EqualTo(1));

        var page = plugin.Pages[0];
        Assert.That(page.Key, Is.EqualTo("music"));
        Assert.That(page.Title, Is.EqualTo("音乐"));
        Assert.That(page.Glyph, Is.EqualTo("Music"));
        Assert.That(page.Section, Is.EqualTo("plugins"), "工具分节：这一页不能跑到侧栏顶格");
        Assert.That(page.Order, Is.EqualTo(20));
    }

    [Test]
    public void PageViewModelTypeResolvesToAnIPluginPage()
    {
        var descriptor = new SoundtrackPlugin().Pages[0];

        var type = typeof(SoundtrackPlugin).Assembly.GetType(descriptor.ViewModelTypeName, throwOnError: false);

        Assert.That(type, Is.Not.Null, "页签声明的视图模型必须真的在插件程序集里");
        Assert.That(typeof(IPluginPage).IsAssignableFrom(type), Is.True);
    }

    [Test]
    public void InitializeAndDeinitializeAreReentrant()
    {
        var plugin = new SoundtrackPlugin();

        Assert.DoesNotThrow(() => plugin.Initialize());
        Assert.DoesNotThrow(() => plugin.Deinitialize());
        Assert.DoesNotThrow(() => plugin.Initialize());
    }
}
