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

    [Test]
    public void Load_FileWithUtf8Bom_StillReadsTheUsersSettings()
    {
        // 外部编辑器（PowerShell 5.1 / 记事本）会写 BOM。按字节反序列化时 0xEF 是非法起始字符，
        // 整份设置会回落默认并被下一次保存覆盖——用户调好的缩放/主题会无声丢掉。
        using var temp = new TempWorkspace("ui-settings-bom");
        var path = temp.Combine(UiSettingsStore.FileName);
        File.WriteAllText(
            path,
            "{\n  \"PageZoom\": 1.5,\n  \"Theme\": \"dark\"\n}",
            new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        var settings = new UiSettingsStore(path).Load();

        Assert.That(settings.PageZoom, Is.EqualTo(1.5d));
        Assert.That(settings.Theme, Is.EqualTo("dark"));
    }

    [Test]
    public void SaveAndLoad_RoundTripsPersonalizationProperties()
    {
        using var temp = new TempWorkspace("ui-settings-personalization");
        var path = temp.Combine(UiSettingsStore.FileName);
        var store = new UiSettingsStore(path);
        var expected = new CkanUI.Services.Abstractions.UiSettings
        {
            AccentColor = "lime",
            RadiusScale = "round",
            BackdropKind = "mica",
            MotionIntensity = "reduced",
            Theme = "dark",
        };

        var saveResult = store.Save(expected);
        Assert.That(saveResult.IsSuccess, Is.True);

        var loaded = store.Load();
        Assert.That(loaded.AccentColor, Is.EqualTo("lime"));
        Assert.That(loaded.RadiusScale, Is.EqualTo("round"));
        Assert.That(loaded.BackdropKind, Is.EqualTo("mica"));
        Assert.That(loaded.MotionIntensity, Is.EqualTo("reduced"));
        Assert.That(loaded, Is.EqualTo(expected));
    }
}
