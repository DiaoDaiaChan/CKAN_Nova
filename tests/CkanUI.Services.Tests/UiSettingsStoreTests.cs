using CkanUI.Services.Settings;
using CkanUI.TestSupport;
using NUnit.Framework;

namespace CkanUI.Services.Tests;

[TestFixture]
public sealed class UiSettingsStoreTests
{
    [Test]
    public void Save_IsAtomicAndLeavesNoTemporaryFiles()
    {
        using var temp = new TempWorkspace("ui-settings-save");
        var path = temp.Combine(UiSettingsStore.FileName);
        var store = new UiSettingsStore(path);
        var result = store.Save(new CkanUI.Services.Abstractions.UiSettings { LastPage = "settings" });
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(Directory.GetFiles(temp.Path, "*.tmp"), Is.Empty);
        Assert.That(store.Load().LastPage, Is.EqualTo("settings"));
    }

    [Test]
    public void Load_CorruptJson_ReturnsDefaultSettings()
    {
        using var temp = new TempWorkspace("ui-settings-corrupt");
        var path = temp.Combine(UiSettingsStore.FileName);
        File.WriteAllText(path, "{ definitely not json");
        var settings = new UiSettingsStore(path).Load();
        Assert.That(settings, Is.EqualTo(new CkanUI.Services.Abstractions.UiSettings()));
    }
}
