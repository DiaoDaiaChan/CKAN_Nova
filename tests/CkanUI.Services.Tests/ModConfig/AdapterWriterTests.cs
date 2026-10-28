using CkanUI.Services.Abstractions.ModConfig;
using CkanUI.Services.ModConfig;
using NUnit.Framework;

namespace CkanUI.Services.Tests.ModConfig;

/// <summary>
/// CFG-05：写盘唯一入口（快照 → 原子替换 → 写后自校验 → 失败回滚；KSP 运行中禁写；高风险二次确认）。
/// 这些用例覆盖 docs/plan/15 §9 的 T-CFG-05 / T-CFG-07 / T-CFG-08，以及"拒绝未知键/未知文件"这类安全边界。
/// </summary>
[TestFixture]
public sealed class AdapterWriterTests
{
    private const string AdapterJsonText = """
        {
          "schemaVersion": 1,
          "id": "demo-mod",
          "name": "Demo Mod",
          "match": { "ckanIdentifiers": ["DemoMod"] },
          "files": [
            {
              "id": "settings",
              "path": "GameData/Demo/settings.cfg",
              "layout": "node",
              "rootNode": "ROOT",
              "mode": "editable",
              "onMissing": "create-from-template",
              "template": "fixtures/default.cfg",
              "lineEnding": "preserve",
              "separator": " = "
            }
          ],
          "fields": [
            { "file": "settings", "key": "enabled", "type": "bool", "default": true, "requiresRestart": true },
            { "file": "settings", "key": "mode", "type": "enum", "values": ["normal", "expert"], "default": "normal" },
            { "file": "settings", "key": "size", "type": "float", "min": 0.01, "max": 0.2, "default": 0.05 },
            { "file": "settings", "key": "danger", "type": "int", "risk": "high", "default": 1 },
            { "file": "settings", "key": "note", "type": "string", "default": "hello" }
          ]
        }
        """;

    private const string SettingsText = """
        ROOT
        {
          // 开关
          enabled = true
          mode = normal
          size = 0.05
          danger = 1
          note = hello
        }
        """;

    [Test]
    public async Task Write_BoolChange_UpdatesOnlyThatLineAndKeepsTheRest()
    {
        using var scenario = Scenario.Create();
        var before = scenario.ReadSettingsLines();

        var result = await scenario.Writer.WriteAsync(scenario.Request("enabled", "false"));

        Assert.That(result.IsSuccess, Is.True, result.Message + " / " + result.Detail);
        var after = scenario.ReadSettingsLines();

        Assert.Multiple(() =>
        {
            Assert.That(after, Has.Length.EqualTo(before.Length), "只改值，不许增删行");
            Assert.That(after[2], Is.EqualTo("  // 开关"), "注释行原样保留");
            Assert.That(after[3], Is.EqualTo("  enabled = false"), "目标行只换值，缩进与分隔符不动");
            for (var i = 0; i < before.Length; i++)
            {
                if (i == 3)
                {
                    continue;
                }

                Assert.That(after[i], Is.EqualTo(before[i]), "第 " + (i + 1) + " 行不该被碰");
            }
        });
    }

    [Test]
    public async Task Write_ReportsOldAndNewValue()
    {
        using var scenario = Scenario.Create();

        var result = await scenario.Writer.WriteAsync(scenario.Request("mode", "expert"));

        Assert.That(result.IsSuccess, Is.True, result.Message);
        var change = result.Value!.Changes.Single();

        Assert.Multiple(() =>
        {
            Assert.That(change.Key, Is.EqualTo("mode"));
            Assert.That(change.OldValue, Is.EqualTo("normal"));
            Assert.That(change.NewValue, Is.EqualTo("expert"));
            Assert.That(change.FilePath, Is.EqualTo(scenario.SettingsPath).IgnoreCase);
        });
    }

    [Test]
    public async Task Write_CreatesSnapshotWithOriginalBytes()
    {
        using var scenario = Scenario.Create();
        var originalBytes = File.ReadAllBytes(scenario.SettingsPath);

        var result = await scenario.Writer.WriteAsync(scenario.Request("enabled", "false"));

        Assert.That(result.IsSuccess, Is.True, result.Message);
        var backup = result.Value!.BackupPaths.Single();

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(backup), Is.True, "快照必须真的落盘");
            Assert.That(backup, Does.EndWith(".bak"));
            Assert.That(File.ReadAllBytes(backup), Is.EqualTo(originalBytes), "快照=写前原文件，一个字节都不能差");
        });
    }

    [Test]
    public async Task Write_LeavesNoTemporaryFiles()
    {
        using var scenario = Scenario.Create();

        var result = await scenario.Writer.WriteAsync(scenario.Request("enabled", "false"));

        Assert.That(result.IsSuccess, Is.True, result.Message);
        var leftovers = Directory.GetFiles(scenario.SettingsDirectory, "*.tmp-*");
        Assert.That(leftovers, Is.Empty, "原子写的临时文件必须清理干净");
    }

    [Test]
    public async Task Write_TwiceWithinSameSecond_DoesNotOverwriteSnapshot()
    {
        // 固定时钟：第二次写会算出同名快照，必须自动加序号而不是覆盖第一份原件。
        using var scenario = Scenario.Create(fixedClock: true);

        var first = await scenario.Writer.WriteAsync(scenario.Request("enabled", "false"));
        var second = await scenario.Writer.WriteAsync(scenario.Request("enabled", "true"));

        Assert.That(first.IsSuccess, Is.True, first.Message);
        Assert.That(second.IsSuccess, Is.True, second.Message);

        var firstBackup = first.Value!.BackupPaths.Single();
        var secondBackup = second.Value!.BackupPaths.Single();

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(firstBackup), Is.True);
            Assert.That(File.Exists(secondBackup), Is.True);
            Assert.That(secondBackup, Does.Contain("-2.bak"), "同名快照要加序号");
            Assert.That(File.ReadAllText(firstBackup), Does.Contain("enabled = true"), "第一份快照仍是第一次写前的原文");
            Assert.That(File.ReadAllText(secondBackup), Does.Contain("enabled = false"));
        });
    }

    [Test]
    public async Task Write_CountsRequiresRestartFields()
    {
        using var scenario = Scenario.Create();

        var result = await scenario.Writer.WriteAsync(scenario.Request(
            ("enabled", "false"),
            ("mode", "expert")));

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(result.Value!.RequiresRestartCount, Is.EqualTo(1), "只有 enabled 声明了 requiresRestart");
    }

    [Test]
    public async Task Write_EmptyChanges_Fails()
    {
        using var scenario = Scenario.Create();

        var result = await scenario.Writer.WriteAsync(new AdapterWriteRequest("test", scenario.GameDir, "demo-mod", []));

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Message, Does.Contain("没有要写入的变更"));
        });
    }

    [Test]
    public async Task Write_UnknownAdapter_Fails()
    {
        using var scenario = Scenario.Create();

        var result = await scenario.Writer.WriteAsync(new AdapterWriteRequest(
            "test", scenario.GameDir, "no-such-adapter",
            [new ModConfigFieldChange(scenario.SettingsPath, "enabled", "false")]));

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Message, Does.Contain("no-such-adapter"));
        });
    }

    [Test]
    public async Task Write_UnknownKey_IsRefused()
    {
        using var scenario = Scenario.Create();

        var result = await scenario.Writer.WriteAsync(scenario.Request("notDeclared", "1"));

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False, "不认识的键一律拒绝，否则等于盲写");
            Assert.That(result.Message, Does.Contain("未知键"));
            Assert.That(scenario.ReadSettings(), Is.EqualTo(SettingsText + NewLine), "拒绝时文件必须保持原样");
        });
    }

    [Test]
    public async Task Write_UnknownFile_IsRefused()
    {
        using var scenario = Scenario.Create();
        var other = Path.Combine(scenario.GameDir, "GameData", "Demo", "other.cfg");
        File.WriteAllText(other, "ROOT\n{\n  enabled = true\n}\n");

        var result = await scenario.Writer.WriteAsync(new AdapterWriteRequest(
            "test", scenario.GameDir, "demo-mod",
            [new ModConfigFieldChange(other, "enabled", "false")]));

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Message, Does.Contain("没有声明文件"));
        });
    }

    [Test]
    public async Task Write_FileOutsideGameDir_IsRefused()
    {
        using var scenario = Scenario.Create();
        var outside = Path.Combine(scenario.Temp.Path, "outside.cfg");
        File.WriteAllText(outside, "ROOT\n{\n  enabled = true\n}\n");

        var result = await scenario.Writer.WriteAsync(new AdapterWriteRequest(
            "test", scenario.GameDir, "demo-mod",
            [new ModConfigFieldChange(outside, "enabled", "false")]));

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Message, Does.Contain("不在实例游戏目录内"));
        });
    }

    [Test]
    public async Task Write_ReadonlyFile_IsRefusedWithReason()
    {
        using var scenario = Scenario.Create(readonlySettingsFile: true);

        var result = await scenario.Writer.WriteAsync(scenario.Request("enabled", "false"));

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Message, Does.Contain("堆垫片"), "只读理由要透出来，用户才知道为什么不能改");
        });
    }

    [Test]
    public async Task Write_GameRunningWithoutConfirmation_IsRefused()
    {
        using var scenario = Scenario.Create(gameRunning: true);

        var result = await scenario.Writer.WriteAsync(scenario.Request("enabled", "false"));

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Detail, Is.EqualTo("game-running"));
            Assert.That(result.Retryable, Is.True);
            Assert.That(result.Message, Does.Contain("重启"));
            Assert.That(scenario.ReadSettings(), Is.EqualTo(SettingsText + NewLine));
        });
    }

    [Test]
    public async Task Write_GameRunningWithConfirmation_Succeeds()
    {
        // §7 Q1=(b)：运行中允许写，但必须二次确认（UI 负责确认弹窗）。
        using var scenario = Scenario.Create(gameRunning: true);

        var result = await scenario.Writer.WriteAsync(scenario.Request("enabled", "false", confirmRunning: true));

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(scenario.ReadSettings(), Does.Contain("enabled = false"));
    }

    [Test]
    public async Task Write_HighRiskField_NeedsConfirmation()
    {
        using var scenario = Scenario.Create();

        var refused = await scenario.Writer.WriteAsync(scenario.Request("danger", "2"));
        Assert.Multiple(() =>
        {
            Assert.That(refused.IsSuccess, Is.False);
            Assert.That(refused.Detail, Is.EqualTo("risk-high"));
        });

        var accepted = await scenario.Writer.WriteAsync(scenario.Request("danger", "2", confirmRiskHigh: true));
        Assert.That(accepted.IsSuccess, Is.True, accepted.Message);
        Assert.That(scenario.ReadSettings(), Does.Contain("danger = 2"));
    }

    [Test]
    public async Task Write_InvalidEnumValue_IsRefused()
    {
        using var scenario = Scenario.Create();

        var result = await scenario.Writer.WriteAsync(scenario.Request("mode", "impossible"));

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Message, Does.Contain("只能取"));
        });
    }

    [Test]
    public async Task Write_OutOfRangeFloat_IsRefused()
    {
        using var scenario = Scenario.Create();

        var result = await scenario.Writer.WriteAsync(scenario.Request("size", "0.9"));

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Message, Does.Contain("不能大于"));
        });
    }

    [Test]
    public async Task Write_MissingFile_IsCreatedFromTemplate()
    {
        using var scenario = Scenario.Create(createSettingsFile: false);

        var result = await scenario.Writer.WriteAsync(scenario.Request("enabled", "false"));

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(scenario.SettingsPath), Is.True, "缺失文件应按模板创建（T-CFG-05）");
            Assert.That(result.Value!.BackupPaths, Is.Empty, "新建的文件没有「写前」可备份");
            Assert.That(File.ReadAllText(scenario.SettingsPath), Does.Contain("enabled = false"));
            Assert.That(File.ReadAllText(scenario.SettingsPath), Does.Contain("mode = normal"), "模板里未改的键要保留");
        });
    }

    [Test]
    public async Task Write_NonUtf8File_IsRefused()
    {
        using var scenario = Scenario.Create();
        File.WriteAllBytes(scenario.SettingsPath, [0xFF, 0xFE, 0x52, 0x4F, 0x4F, 0x54, 0x0A]);

        var result = await scenario.Writer.WriteAsync(scenario.Request("enabled", "false"));

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False, "非 UTF-8 文件按 UTF-8 重写会静默毁内容");
            Assert.That(result.Message, Does.Contain("UTF-8"));
        });
    }

    [Test]
    public async Task Write_ValueWithSpaces_IsQuotedAndReParsesToSameValue()
    {
        using var scenario = Scenario.Create();

        var result = await scenario.Writer.WriteAsync(scenario.Request("note", "hello world"));

        Assert.That(result.IsSuccess, Is.True, result.Message);
        var text = scenario.ReadSettings();
        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("note = \"hello world\""));
            var reparsed = ConfigNodeDocument.Parse(text);
            Assert.That(reparsed.FindPair("note", "ROOT")!.Value.Trim('"'), Is.EqualTo("hello world"));
        });
    }

    [Test]
    public async Task Write_ValueWithNewline_IsRefused()
    {
        using var scenario = Scenario.Create();

        var result = await scenario.Writer.WriteAsync(scenario.Request("note", "a\nb"));

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Message, Does.Contain("换行"));
        });
    }

    [Test]
    public async Task Write_NodeLayout_DoesNotTouchSameKeyInOtherNode()
    {
        using var scenario = Scenario.Create();
        var text = SettingsText + NewLine + "OTHER" + NewLine + "{" + NewLine + "  enabled = true" + NewLine + "}" + NewLine;
        File.WriteAllText(scenario.SettingsPath, text);

        var result = await scenario.Writer.WriteAsync(scenario.Request("enabled", "false"));

        Assert.That(result.IsSuccess, Is.True, result.Message);
        var lines = scenario.ReadSettingsLines();
        Assert.Multiple(() =>
        {
            Assert.That(lines[3], Is.EqualTo("  enabled = false"), "ROOT 里的键要改");
            Assert.That(lines[^2], Is.EqualTo("  enabled = true"), "其它节点里的同名键不许动");
        });
    }

    [Test]
    public async Task Write_ConcurrentWrites_AreSerialized()
    {
        using var scenario = Scenario.Create();

        var first = scenario.Writer.WriteAsync(scenario.Request("enabled", "false"));
        var second = scenario.Writer.WriteAsync(scenario.Request("mode", "expert"));
        var results = await Task.WhenAll(first, second);

        Assert.Multiple(() =>
        {
            Assert.That(results[0].IsSuccess, Is.True, results[0].Message);
            Assert.That(results[1].IsSuccess, Is.True, results[1].Message);
            var text = scenario.ReadSettings();
            Assert.That(text, Does.Contain("enabled = false"));
            Assert.That(text, Does.Contain("mode = expert"));
        });
    }

    private static readonly string NewLine = Environment.NewLine;

    /// <summary>每个用例一套独立环境：临时 adapter 目录 + 临时游戏目录 + 可注入的探针与时钟。</summary>
    private sealed class Scenario : IDisposable
    {
        private Scenario(TempDirectory temp, string gameDir, string settingsPath, AdapterWriter writer, bool gameRunning)
        {
            Temp = temp;
            GameDir = gameDir;
            SettingsPath = settingsPath;
            Writer = writer;
            GameRunning = gameRunning;
        }

        public TempDirectory Temp { get; }

        public string GameDir { get; }

        public string SettingsPath { get; }

        public AdapterWriter Writer { get; }

        public bool GameRunning { get; }

        public string SettingsDirectory => Path.GetDirectoryName(SettingsPath)!;

        public static Scenario Create(
            bool gameRunning = false,
            bool fixedClock = false,
            bool readonlySettingsFile = false,
            bool createSettingsFile = true)
        {
            var temp = new TempDirectory("writer");
            string gameDir = Path.Combine(temp.Path, "game");
            Directory.CreateDirectory(gameDir);

            var adapterJson = AdapterJsonText;
            if (readonlySettingsFile)
            {
                adapterJson = adapterJson
                    .Replace("\"mode\": \"editable\"", "\"mode\": \"readonly\"", StringComparison.Ordinal)
                    .Replace(
                        "\"separator\": \" = \"",
                        "\"separator\": \" = \", \"readonlyReason\": \"该文件控制堆垫片总量，写错会直接影响内存\"",
                        StringComparison.Ordinal);
            }

            temp.Adapter("adapters/demo-mod", adapterJson);
            temp.File("adapters/demo-mod/fixtures/default.cfg", SettingsText + NewLine);

            var settingsPath = Path.Combine(gameDir, "GameData", "Demo", "settings.cfg");
            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
            if (createSettingsFile)
            {
                File.WriteAllText(settingsPath, SettingsText + NewLine);
            }

            var catalog = new AdapterCatalog([Path.Combine(temp.Path, "adapters")], [Path.Combine(temp.Path, "user")]);
            var clock = fixedClock
                ? () => new DateTime(2026, 10, 28, 12, 0, 0, DateTimeKind.Local)
                : (Func<DateTime>?)null;
            var writer = new AdapterWriter(catalog, new FixedGameRunningProbe(gameRunning), clock);

            return new Scenario(temp, gameDir, settingsPath, writer, gameRunning);
        }

        public AdapterWriteRequest Request(string key, string value, bool confirmRunning = false, bool confirmRiskHigh = false)
            => new(
                "test",
                GameDir,
                "demo-mod",
                [new ModConfigFieldChange(SettingsPath, key, value)],
                ConfirmedRiskHigh: confirmRiskHigh,
                ConfirmedGameRunning: confirmRunning);

        public AdapterWriteRequest Request(params (string Key, string Value)[] changes)
            => new(
                "test",
                GameDir,
                "demo-mod",
                changes.Select(c => new ModConfigFieldChange(SettingsPath, c.Key, c.Value)).ToArray());

        public string ReadSettings() => File.ReadAllText(SettingsPath);

        public string[] ReadSettingsLines() => File.ReadAllLines(SettingsPath);

        public void Dispose() => Temp.Dispose();
    }
}
