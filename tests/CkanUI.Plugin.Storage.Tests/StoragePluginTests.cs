namespace CkanUI.Plugin.Storage.Tests;

[TestFixture]
public sealed class StoragePluginTests
{
    [Test]
    public void Descriptor_MatchesContract()
    {
        var plugin = new StoragePlugin();

        Assert.That(plugin.Id, Is.EqualTo("com.ckanui.storage"));
        Assert.That(plugin.Name, Is.EqualTo("存储管理"));
        Assert.That(plugin.Version, Is.EqualTo("1.0.0"));
        Assert.That(plugin.MinimumHostVersion, Is.Empty);
        Assert.That(plugin.Pages, Has.Count.EqualTo(1));

        var page = plugin.Pages[0];
        Assert.That(page.Key, Is.EqualTo("storage"));
        Assert.That(page.Title, Is.EqualTo("存储"));
        Assert.That(page.Section, Is.EqualTo("cache"));
        Assert.That(page.Glyph, Is.EqualTo("Storage"));
        Assert.That(page.Order, Is.EqualTo(20));
        Assert.That(page.ViewModelTypeName, Is.EqualTo("CkanUI.Plugin.Storage.UI.StoragePageViewModel"));
    }

    [Test]
    public void ViewResolvesByHostNamingConvention()
    {
        // 宿主按“同命名空间、PageViewModel 换成 PageView”找视图，且要求无参构造；
        // 这里用反射锁死契约，避免改名后只在运行时才发现页面空白。
        var assembly = typeof(StoragePageViewModel).Assembly;
        var viewModelName = typeof(StoragePageViewModel).FullName;

        Assert.That(viewModelName, Does.EndWith(".UI.StoragePageViewModel"));

        var viewName = viewModelName!.Replace("ViewModel", "View", StringComparison.Ordinal);
        var viewType = assembly.GetType(viewName, throwOnError: false);

        Assert.That(viewType, Is.Not.Null, $"{viewName} 应该存在");
        Assert.That(typeof(Avalonia.Controls.Control).IsAssignableFrom(viewType), Is.True);
        Assert.That(viewType!.GetConstructor(Type.EmptyTypes), Is.Not.Null);
        Assert.That(new StoragePlugin().Pages[0].ViewModelTypeName, Is.EqualTo(viewModelName));
    }

    [Test]
    public void InitializeAndDeinitialize_AreReentrant()
    {
        var plugin = new StoragePlugin();

        Assert.DoesNotThrow(plugin.Initialize);
        Assert.DoesNotThrow(plugin.Initialize);
        Assert.DoesNotThrow(plugin.Deinitialize);
        Assert.DoesNotThrow(plugin.Deinitialize);
    }
}
