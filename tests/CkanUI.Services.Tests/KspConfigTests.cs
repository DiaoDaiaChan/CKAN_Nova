using System.Text;
using CkanUI.Services.Abstractions.KspConfig;
using CkanUI.Services.KspConfig;
using CkanUI.TestSupport;
using NUnit.Framework;

namespace CkanUI.Services.Tests;

[TestFixture]
public sealed class KspConfigTests
{
    private TempWorkspace? _workspace;
    private string _tempDir = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _workspace = new TempWorkspace("ksp_config");
        _tempDir = _workspace.Path;
    }

    [TearDown]
    public void TearDown()
    {
        _workspace?.Dispose();
        _workspace = null;
    }

    [Test]
    public async Task LoadOfficialLocalizationAsync_ExtractsCleanStrings()
    {
        // 准备 Mock Squad 本地化文件
        var squadLocDir = Path.Combine(_tempDir, "GameData", "Squad", "Localization");
        Directory.CreateDirectory(squadLocDir);

        var dictContent = @"Localization
{
	en-us
	{
		#autoLOC_149894 = Screen Resolution:
		#autoLOC_149901 = Fullscreen
	}
	zh-cn
	{
		#autoLOC_149894 = 屏幕分辨率：
		#autoLOC_149901 = <color=#ffffffff>全屏</color>
		#autoLOC_146154 = 主音量：
	}
}";
        await File.WriteAllTextAsync(Path.Combine(squadLocDir, "dictionary.cfg"), dictContent, Encoding.UTF8);

        var service = new KspConfigService();
        var locDict = await service.LoadOfficialLocalizationAsync(_tempDir, "zh-cn");

        Assert.That(locDict, Is.Not.Null);
        Assert.That(locDict.ContainsKey("#autoLOC_149894"), Is.True);
        Assert.That(locDict["#autoLOC_149894"], Is.EqualTo("屏幕分辨率")); // 冒号已清理
        Assert.That(locDict["#autoLOC_149901"], Is.EqualTo("全屏")); // 富文本标签已清理
        Assert.That(locDict["#autoLOC_146154"], Is.EqualTo("主音量"));
    }

    [Test]
    public async Task LoadConfigAsync_ParsesRootKeys_AndMatchesLocalization()
    {
        var squadLocDir = Path.Combine(_tempDir, "GameData", "Squad", "Localization");
        Directory.CreateDirectory(squadLocDir);

        var dictContent = @"Localization
{
	zh-cn
	{
		#autoLOC_149894 = 屏幕分辨率
		#autoLOC_149901 = 全屏
		#autoLOC_146154 = 主音量
	}
}";
        await File.WriteAllTextAsync(Path.Combine(squadLocDir, "dictionary.cfg"), dictContent, Encoding.UTF8);

        var settingsContent = @"// KSP Game Settings
LANGUAGE = zh-cn
SCREEN_RESOLUTION_WIDTH = 1920
SCREEN_RESOLUTION_HEIGHT = 1080
FULLSCREEN = True
MASTER_VOLUME = 0.8
CUSTOM_UNKNOWN_FLOAT = 1.25
CUSTOM_UNKNOWN_BOOL = False
CUSTOM_UNKNOWN_STRING = HelloSpace

INPUT_DEVICES
{
	Pitch = Axis(1)
}
";
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "settings.cfg"), settingsContent, Encoding.UTF8);

        var service = new KspConfigService();
        var doc = await service.LoadConfigAsync(_tempDir);

        Assert.That(doc, Is.Not.Null);
        Assert.That(doc.Language, Is.EqualTo("zh-cn"));
        Assert.That(doc.Settings.Count, Is.GreaterThanOrEqualTo(8));

        // 检查精选配置项解析与本地化关联
        var widthItem = doc.Settings.FirstOrDefault(s => s.Key == "SCREEN_RESOLUTION_WIDTH");
        Assert.That(widthItem, Is.Not.Null);
        Assert.That(widthItem!.DisplayName, Is.EqualTo("屏幕分辨率"));
        Assert.That(widthItem.Value, Is.EqualTo("1920"));
        Assert.That(widthItem.Category, Is.EqualTo(KspSettingCategory.Graphics));
        Assert.That(widthItem.SettingType, Is.EqualTo(KspSettingType.Integer));

        var fullScreenItem = doc.Settings.FirstOrDefault(s => s.Key == "FULLSCREEN");
        Assert.That(fullScreenItem, Is.Not.Null);
        Assert.That(fullScreenItem!.DisplayName, Is.EqualTo("全屏"));
        Assert.That(fullScreenItem.Value, Is.EqualTo("True"));
        Assert.That(fullScreenItem.SettingType, Is.EqualTo(KspSettingType.Boolean));

        var masterVolumeItem = doc.Settings.FirstOrDefault(s => s.Key == "MASTER_VOLUME");
        Assert.That(masterVolumeItem, Is.Not.Null);
        Assert.That(masterVolumeItem!.DisplayName, Is.EqualTo("主音量"));
        Assert.That(masterVolumeItem.Category, Is.EqualTo(KspSettingCategory.Audio));

        // 检查自动类型推断的自定义键
        var customFloat = doc.Settings.FirstOrDefault(s => s.Key == "CUSTOM_UNKNOWN_FLOAT");
        Assert.That(customFloat, Is.Not.Null);
        Assert.That(customFloat!.SettingType, Is.EqualTo(KspSettingType.Float));
        Assert.That(customFloat.Category, Is.EqualTo(KspSettingCategory.Other));

        var customBool = doc.Settings.FirstOrDefault(s => s.Key == "CUSTOM_UNKNOWN_BOOL");
        Assert.That(customBool, Is.Not.Null);
        Assert.That(customBool!.SettingType, Is.EqualTo(KspSettingType.Boolean));

        var customStr = doc.Settings.FirstOrDefault(s => s.Key == "CUSTOM_UNKNOWN_STRING");
        Assert.That(customStr, Is.Not.Null);
        Assert.That(customStr!.SettingType, Is.EqualTo(KspSettingType.Text));
    }

    [Test]
    public async Task SaveConfigAsync_PreservesFormatAndCreatesBackup()
    {
        var settingsContent = @"// Header Comment
SCREEN_RESOLUTION_WIDTH = 1920
FULLSCREEN = True

// Section
INPUT_DEVICES
{
	key = 1
}
";
        var settingsPath = Path.Combine(_tempDir, "settings.cfg");
        await File.WriteAllTextAsync(settingsPath, settingsContent, Encoding.UTF8);

        var service = new KspConfigService();
        var doc = await service.LoadConfigAsync(_tempDir);

        var widthItem = doc.Settings.First(s => s.Key == "SCREEN_RESOLUTION_WIDTH");
        widthItem.Value = "2560";
        var fullScreenItem = doc.Settings.First(s => s.Key == "FULLSCREEN");
        fullScreenItem.Value = "False";

        var saved = await service.SaveConfigAsync(doc, [widthItem, fullScreenItem]);
        Assert.That(saved, Is.True);

        // 验证 settings.cfg.bak 备份已生成且为原始内容
        var backupPath = settingsPath + ".bak";
        Assert.That(File.Exists(backupPath), Is.True);
        var backupText = await File.ReadAllTextAsync(backupPath);
        Assert.That(backupText.Contains("SCREEN_RESOLUTION_WIDTH = 1920"), Is.True);

        // 验证新文件已更新并保留注释与结构
        var newText = await File.ReadAllTextAsync(settingsPath);
        Assert.That(newText.Contains("// Header Comment"), Is.True);
        Assert.That(newText.Contains("SCREEN_RESOLUTION_WIDTH = 2560"), Is.True);
        Assert.That(newText.Contains("FULLSCREEN = False"), Is.True);
        Assert.That(newText.Contains("INPUT_DEVICES"), Is.True);
        Assert.That(newText.Contains("key = 1"), Is.True);

        // 验证恢复备份
        var restored = await service.RestoreBackupAsync(_tempDir);
        Assert.That(restored, Is.True);
        var restoredText = await File.ReadAllTextAsync(settingsPath);
        Assert.That(restoredText.Contains("SCREEN_RESOLUTION_WIDTH = 1920"), Is.True);
    }
}
