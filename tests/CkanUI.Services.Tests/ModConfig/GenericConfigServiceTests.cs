using System.Text;
using CkanUI.Services.ModConfig;
using NUnit.Framework;

namespace CkanUI.Services.Tests.ModConfig;

/// <summary>
/// <see cref="GenericConfigService"/> 的用例：加载/保存往返、备份、拒绝路径与只读/解析错误/游戏运行。
/// 全部用真的 <see cref="ConfigNodeDocument"/>，不做 mock。
/// </summary>
[TestFixture]
public sealed class GenericConfigServiceTests
{
    [Test]
    public async Task LoadThenSave_ChangesOnlyRequestedLine_AndKeepsBomAndCrlf()
    {
        using var scenario = Scenario.Create();
        var target = scenario.WriteConfigBytes(
            "MyMod/PluginData/settings.cfg",
            BomCrLf("SETTINGS\n{\n  // 音量\n  volume = 0.5\n  mute = false\n}\n"));
        var originalBytes = File.ReadAllBytes(target);

        var discover = await scenario.Service.DiscoverAsync(scenario.GameDir, "instance", CancellationToken.None);
        Assert.That(discover.IsSuccess, Is.True, discover.Message);
        Assert.That(discover.Value!.Files, Has.Count.EqualTo(1));
        Assert.That(discover.Value!.Files[0].RelativePath, Is.EqualTo("MyMod/PluginData/settings.cfg"));

        var loaded = await scenario.Service.LoadAsync(scenario.GameDir, target, CancellationToken.None);
        Assert.That(loaded.IsSuccess, Is.True, loaded.Message);
        var document = loaded.Value!.Document;
        Assert.That(loaded.Value.CanWrite, Is.True, loaded.Value.ReadonlyReason);

        var pair = document.FindPair("volume");
        Assert.That(pair, Is.Not.Null, "真解析出来的文档必须能找到 volume");
        Assert.That(document.TrySetValue(pair!.LineNumber, "0.9", out var error), Is.True, error);

        var saved = await scenario.Service.SaveAsync(
            scenario.GameDir, "instance", target, document, confirmedGameRunning: false, CancellationToken.None);

        Assert.That(saved.IsSuccess, Is.True, saved.Message);
        var result = saved.Value!;
        Assert.Multiple(() =>
        {
            Assert.That(result.ChangedLineCount, Is.EqualTo(1));
            Assert.That(result.FileWasCreated, Is.False);
            Assert.That(result.BackupPath, Is.Not.Empty);
            Assert.That(File.Exists(result.BackupPath), Is.True, "写前必须有备份");
            Assert.That(File.ReadAllBytes(result.BackupPath), Is.EqualTo(originalBytes), "备份必须是写前原样字节");
        });

        var afterBytes = File.ReadAllBytes(target);
        Assert.That(afterBytes.Take(3), Is.EqualTo(new byte[] { 0xEF, 0xBB, 0xBF }), "BOM 必须保留");

        var beforeLines = LinesOf(originalBytes);
        var afterLines = LinesOf(afterBytes);
        var changedIndex = Array.FindIndex(beforeLines, static line => line.Contains("volume", StringComparison.Ordinal));

        Assert.That(afterLines, Has.Length.EqualTo(beforeLines.Length), "行数不能变");
        Assert.That(changedIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(afterLines[changedIndex], Is.EqualTo("  volume = 0.9\r"), "只换值片段，缩进与行尾保留");
        for (var index = 0; index < beforeLines.Length; index++)
        {
            if (index == changedIndex)
            {
                continue;
            }

            Assert.That(afterLines[index], Is.EqualTo(beforeLines[index]), $"第 {index + 1} 行不应变化");
        }

        Assert.That(
            Decode(afterBytes).Replace("\r\n", string.Empty, StringComparison.Ordinal),
            Does.Not.Contain("\n"),
            "行尾必须仍然是 CRLF");
    }

    [Test]
    public async Task Save_SecondTimeWithSameTimestamp_DoesNotOverwriteExistingBackup()
    {
        var clock = () => new DateTime(2024, 5, 6, 7, 8, 9, DateTimeKind.Local);
        using var scenario = Scenario.Create(clock: clock);
        var target = scenario.WriteConfig("MyMod/PluginData/settings.cfg", "SETTINGS\n{\n  volume = 0.5\n}\n");
        var originalBytes = File.ReadAllBytes(target);
        var firstBackup = target + ".bak-20240506-070809";

        var firstLoad = await scenario.Service.LoadAsync(scenario.GameDir, target, CancellationToken.None);
        var firstDocument = firstLoad.Value!.Document;
        Assert.That(firstDocument.TrySetValue("volume", "0.9", null, out var firstError), Is.True, firstError);
        var firstSave = await scenario.Service.SaveAsync(
            scenario.GameDir, "instance", target, firstDocument, confirmedGameRunning: true, CancellationToken.None);

        Assert.That(firstSave.IsSuccess, Is.True, firstSave.Message);
        Assert.That(firstSave.Value!.BackupPath, Is.EqualTo(firstBackup));
        Assert.That(File.ReadAllBytes(firstBackup), Is.EqualTo(originalBytes));

        var secondLoad = await scenario.Service.LoadAsync(scenario.GameDir, target, CancellationToken.None);
        var secondDocument = secondLoad.Value!.Document;
        Assert.That(secondDocument.TrySetValue("volume", "0.8", null, out var secondError), Is.True, secondError);
        var secondSave = await scenario.Service.SaveAsync(
            scenario.GameDir, "instance", target, secondDocument, confirmedGameRunning: true, CancellationToken.None);

        Assert.That(secondSave.IsSuccess, Is.True, secondSave.Message);
        Assert.Multiple(() =>
        {
            Assert.That(secondSave.Value!.BackupPath, Is.EqualTo(firstBackup + "-1"), "同一秒的第二次备份要加序号");
            Assert.That(File.ReadAllBytes(firstBackup), Is.EqualTo(originalBytes), "已有备份不能被覆盖");
            Assert.That(File.ReadAllText(target), Does.Contain("volume = 0.8"));
        });
    }

    [Test]
    public async Task Save_RejectsPathEscapingGameDirectory()
    {
        using var scenario = Scenario.Create();
        var outside = scenario.Temp.File("outside.cfg", "SETTINGS\n{\n  a = 1\n}\n");
        var outsideBytes = File.ReadAllBytes(outside);

        var document = ConfigNodeDocument.Parse("SETTINGS\n{\n  a = 1\n}\n");
        Assert.That(document.TrySetValue("a", "2", null, out var error), Is.True, error);

        var escaped = Path.Combine(scenario.GameDir, "..", "outside.cfg");
        var save = await scenario.Service.SaveAsync(
            scenario.GameDir, "instance", escaped, document, confirmedGameRunning: true, CancellationToken.None);

        Assert.That(save.IsSuccess, Is.False);
        Assert.That(save.Message, Does.Contain("不在游戏目录内"));
        Assert.That(File.ReadAllBytes(outside), Is.EqualTo(outsideBytes), "拒绝时不能碰目标文件");

        var load = await scenario.Service.LoadAsync(scenario.GameDir, escaped, CancellationToken.None);
        Assert.That(load.IsSuccess, Is.False);
        Assert.That(load.Message, Does.Contain("不在游戏目录内"));
    }

    [Test]
    public async Task Save_RejectsReadonlyTargetFile()
    {
        using var scenario = Scenario.Create();
        var target = scenario.WriteConfig("MyMod/PluginData/settings.cfg", "SETTINGS\n{\n  volume = 0.5\n}\n");
        var originalBytes = File.ReadAllBytes(target);

        var loaded = await scenario.Service.LoadAsync(scenario.GameDir, target, CancellationToken.None);
        var document = loaded.Value!.Document;
        Assert.That(document.TrySetValue("volume", "0.9", null, out var error), Is.True, error);

        File.SetAttributes(target, FileAttributes.ReadOnly);
        try
        {
            var save = await scenario.Service.SaveAsync(
                scenario.GameDir, "instance", target, document, confirmedGameRunning: true, CancellationToken.None);

            Assert.That(save.IsSuccess, Is.False);
            Assert.That(save.Message, Does.Contain("只读"));
            Assert.That(File.ReadAllBytes(target), Is.EqualTo(originalBytes), "拒绝时文件不能被改");
        }
        finally
        {
            File.SetAttributes(target, FileAttributes.Normal);
        }
    }

    [Test]
    public async Task Save_RejectsDocumentWithParseErrors()
    {
        using var scenario = Scenario.Create();
        var target = scenario.WriteConfig(
            "MyMod/PluginData/settings.cfg",
            "SETTINGS\n{\n  volume = 0.5\n  这不是键值对也没有花括号\n}\n");
        var originalBytes = File.ReadAllBytes(target);

        var loaded = await scenario.Service.LoadAsync(scenario.GameDir, target, CancellationToken.None);
        Assert.That(loaded.IsSuccess, Is.True, loaded.Message);
        Assert.Multiple(() =>
        {
            Assert.That(loaded.Value!.CanWrite, Is.False);
            Assert.That(loaded.Value!.ReadonlyReason, Does.Contain("解析"));
        });

        var document = loaded.Value!.Document;
        var pair = document.FindPair("volume");
        Assert.That(pair, Is.Not.Null);
        Assert.That(document.TrySetValue(pair!.LineNumber, "0.9", out var error), Is.True, error);

        var save = await scenario.Service.SaveAsync(
            scenario.GameDir, "instance", target, document, confirmedGameRunning: true, CancellationToken.None);

        Assert.That(save.IsSuccess, Is.False);
        Assert.That(save.Message, Does.Contain("解析错误"));
        Assert.That(File.ReadAllBytes(target), Is.EqualTo(originalBytes), "有解析错误时必须原样不动");
    }

    [Test]
    public async Task Save_RejectsWhenGameIsRunningAndNotConfirmed()
    {
        using var scenario = Scenario.Create(gameRunning: true);
        var target = scenario.WriteConfig("MyMod/PluginData/settings.cfg", "SETTINGS\n{\n  volume = 0.5\n}\n");
        var originalBytes = File.ReadAllBytes(target);

        var loaded = await scenario.Service.LoadAsync(scenario.GameDir, target, CancellationToken.None);
        var document = loaded.Value!.Document;
        Assert.That(document.TrySetValue("volume", "0.9", null, out var error), Is.True, error);

        var rejected = await scenario.Service.SaveAsync(
            scenario.GameDir, "instance", target, document, confirmedGameRunning: false, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(rejected.IsSuccess, Is.False);
            Assert.That(rejected.Detail, Is.EqualTo("game-running"));
            Assert.That(rejected.Retryable, Is.True);
            Assert.That(rejected.Message, Does.Contain("KSP 正在运行"));
            Assert.That(File.ReadAllBytes(target), Is.EqualTo(originalBytes), "未确认时不能写盘");
        });

        var accepted = await scenario.Service.SaveAsync(
            scenario.GameDir, "instance", target, document, confirmedGameRunning: true, CancellationToken.None);

        Assert.That(accepted.IsSuccess, Is.True, accepted.Message);
        Assert.That(accepted.Value!.ChangedLineCount, Is.EqualTo(1));
        Assert.That(File.ReadAllText(target), Does.Contain("volume = 0.9"));
    }

    [Test]
    public async Task Save_RejectsDocumentWithoutPendingEdits()
    {
        using var scenario = Scenario.Create();
        var target = scenario.WriteConfig("MyMod/PluginData/settings.cfg", "SETTINGS\n{\n  volume = 0.5\n}\n");
        var document = ConfigNodeDocument.Parse(File.ReadAllText(target));

        var save = await scenario.Service.SaveAsync(
            scenario.GameDir, "instance", target, document, confirmedGameRunning: true, CancellationToken.None);

        Assert.That(save.IsSuccess, Is.False);
        Assert.That(save.Message, Does.Contain("没有待写入的改动"));
    }

    [Test]
    public async Task Save_CreatesMissingFileInsideGameData()
    {
        using var scenario = Scenario.Create();
        var target = scenario.PathInGameData("NewMod/PluginData/new.cfg");
        var parent = Path.GetDirectoryName(target);
        Assert.That(parent, Is.Not.Null);
        Directory.CreateDirectory(parent!);

        var document = ConfigNodeDocument.Parse("SETTINGS\n{\n  a = 1\n}\n");
        Assert.That(document.TrySetValue("a", "2", null, out var error), Is.True, error);

        var save = await scenario.Service.SaveAsync(
            scenario.GameDir, "instance", target, document, confirmedGameRunning: true, CancellationToken.None);

        Assert.That(save.IsSuccess, Is.True, save.Message);
        Assert.Multiple(() =>
        {
            Assert.That(save.Value!.FileWasCreated, Is.True);
            Assert.That(save.Value!.BackupPath, Is.Empty, "新建文件没有写前内容可备份");
            Assert.That(File.ReadAllText(target), Does.Contain("a = 2"));
            Assert.That(Directory.GetFiles(parent!, "*.tmp-*"), Is.Empty, "临时文件必须清理干净");
        });
    }

    internal sealed class Scenario : IDisposable
    {
        private Scenario(TempDirectory temp, string gameDir, GenericConfigService service)
        {
            Temp = temp;
            GameDir = gameDir;
            Service = service;
        }

        public TempDirectory Temp { get; }

        public string GameDir { get; }

        public GenericConfigService Service { get; }

        public static Scenario Create(bool gameRunning = false, Func<DateTime>? clock = null)
        {
            var temp = new TempDirectory("generic-service");
            var gameDir = System.IO.Path.Combine(temp.Path, "game");
            Directory.CreateDirectory(System.IO.Path.Combine(gameDir, "GameData"));
            var service = new GenericConfigService(
                instances: null,
                gameRunningProbe: new FixedGameRunningProbe(gameRunning),
                clock: clock);
            return new Scenario(temp, gameDir, service);
        }

        public string PathInGameData(string relativePath)
            => System.IO.Path.Combine(
                GameDir,
                "GameData",
                relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));

        public string WriteConfig(string relativePath, string content)
            => WriteConfigBytes(relativePath, Encoding.UTF8.GetBytes(content));

        public string WriteConfigBytes(string relativePath, byte[] bytes)
        {
            var full = PathInGameData(relativePath);
            var parent = System.IO.Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(parent))
            {
                Directory.CreateDirectory(parent);
            }

            File.WriteAllBytes(full, bytes);
            return full;
        }

        public void Dispose() => Temp.Dispose();
    }

    internal static byte[] BomCrLf(string text)
        => Encoding.UTF8.GetBytes("\uFEFF" + text.Replace("\n", "\r\n", StringComparison.Ordinal));

    private static string Decode(byte[] bytes)
        => Encoding.UTF8.GetString(bytes);

    /// <summary>按 '\n' 切行（行尾的 '\r' 留在行内，方便逐字比 CRLF）。</summary>
    private static string[] LinesOf(byte[] bytes)
    {
        var text = Decode(bytes);
        if (text.Length > 0 && text[0] == '\uFEFF')
        {
            text = text[1..];
        }

        return text.Split('\n');
    }
}
