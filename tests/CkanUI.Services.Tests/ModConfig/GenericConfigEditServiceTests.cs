using System.Text;
using CkanUI.Services.Abstractions.ModConfig;
using CkanUI.Services.ModConfig;
using NUnit.Framework;

namespace CkanUI.Services.Tests.ModConfig;

[TestFixture]
public sealed class GenericConfigEditServiceTests
{
    [Test]
    public async Task Discover_Load_Save_EndToEnd_ViaEditFacade()
    {
        using var scenario = GenericConfigServiceTests.Scenario.Create();
        var target = scenario.WriteConfigBytes(
            "MyMod/PluginData/settings.cfg",
            GenericConfigServiceTests.BomCrLf(@"SETTINGS
{
  // 音量
  volume = 0.5
  mute = false
}
"));

        var facade = new GenericConfigEditService(scenario.Service);

        var discover = await facade.DiscoverAsync(scenario.GameDir, "instance");
        Assert.That(discover.IsSuccess, Is.True, discover.Message);
        Assert.That(discover.Value!.Files, Has.Count.EqualTo(1));

        var loaded = await facade.LoadAsync(scenario.GameDir, target);
        Assert.That(loaded.IsSuccess, Is.True, loaded.Message);
        var view = loaded.Value!;
        Assert.That(view.CanWrite, Is.True);
        Assert.That(view.Rows, Has.Count.GreaterThan(0));

        var volumeRow = view.Rows.FirstOrDefault(r => r.Key == "volume");
        Assert.That(volumeRow, Is.Not.Null);
        Assert.That(volumeRow!.Value, Is.EqualTo("0.5"));
        Assert.That(volumeRow.Comment, Is.EqualTo("音量"));

        var saveResult = await facade.SaveAsync(
            scenario.GameDir,
            "instance",
            target,
            new[] { new GenericConfigChange(volumeRow.LineNumber, "0.95") },
            confirmedGameRunning: false);

        Assert.That(saveResult.IsSuccess, Is.True, saveResult.Message);
        Assert.That(saveResult.Value!.ChangedLineCount, Is.EqualTo(1));

        // 重新读取磁盘文本，验证修改生效
        var reloaded = await facade.LoadAsync(scenario.GameDir, target);
        Assert.That(reloaded.IsSuccess, Is.True);
        var newVolume = reloaded.Value!.Rows.First(r => r.Key == "volume");
        Assert.That(newVolume.Value, Is.EqualTo("0.95"));
    }

    [Test]
    public async Task Save_RejectsEmptyGameDir()
    {
        using var scenario = GenericConfigServiceTests.Scenario.Create();
        var facade = new GenericConfigEditService(scenario.Service);

        var res = await facade.SaveAsync(
            "",
            "instance",
            "dummy.cfg",
            new[] { new GenericConfigChange(1, "val") },
            confirmedGameRunning: false);

        Assert.That(res.IsSuccess, Is.False);
        Assert.That(res.Message, Does.Contain("未选定有效游戏实例目录"));
    }

    [Test]
    public async Task Save_RejectsEmptyChanges()
    {
        using var scenario = GenericConfigServiceTests.Scenario.Create();
        var facade = new GenericConfigEditService(scenario.Service);

        var res = await facade.SaveAsync(
            scenario.GameDir,
            "instance",
            "dummy.cfg",
            Array.Empty<GenericConfigChange>(),
            confirmedGameRunning: false);

        Assert.That(res.IsSuccess, Is.False);
        Assert.That(res.Message, Does.Contain("没有要写入的改动"));
    }
}
