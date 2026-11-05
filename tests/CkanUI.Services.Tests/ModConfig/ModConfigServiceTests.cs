using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Services.Abstractions.ModConfig;
using CkanUI.Services.ModConfig;
using CkanUI.Services.Tests.Profiles;
using NUnit.Framework;

namespace CkanUI.Services.Tests.ModConfig;

/// <summary>
/// CFG-02 的读侧：adapter 声明 + 磁盘现状 → 页面模型。
/// 重点覆盖 fields[].nodePath —— 子节点里的键必须从子节点取值，
/// 不能拿根层同名键顶替（那是静默显示错值，比空白更危险）。
/// </summary>
[TestFixture]
public sealed class ModConfigServiceTests
{
    private const string NestedAdapter = """
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
              "onMissing": "readonly",
              "lineEnding": "preserve",
              "separator": " = "
            }
          ],
          "fields": [
            { "file": "settings", "key": "enabled", "type": "bool", "default": true },
            { "file": "settings", "key": "gain", "type": "float", "default": 1 },
            { "file": "settings", "nodePath": "CHILD_A", "key": "gain", "type": "float", "default": 1 }
          ]
        }
        """;

    /// <summary>注册表里查不到的 mod（手工装 / fork 换标识符），只能靠 match.paths 认出来。</summary>
    private const string PathMatchedAdapter = """
        {
          "schemaVersion": 1,
          "id": "path-matched",
          "name": "Path Matched",
          "match": {
            "ckanIdentifiers": ["PathMatchedMod"],
            "paths": ["GameData/Demo/settings.cfg"]
          },
          "files": [
            {
              "id": "settings",
              "path": "GameData/Demo/settings.cfg",
              "layout": "node",
              "rootNode": "ROOT",
              "mode": "editable",
              "onMissing": "readonly",
              "lineEnding": "preserve",
              "separator": " = "
            }
          ],
          "fields": [
            { "file": "settings", "key": "enabled", "type": "bool", "default": true }
          ]
        }
        """;

    private const string SettingsText = """
        ROOT
        {
          enabled = true
          gain = 1
          CHILD_A
          {
            gain = 4.5
          }
        }
        """;

    [Test]
    public async Task LoadAsync_ReadsChildNodeKeyFromItsOwnNode()
    {
        using var temp = ModConfigFixtures.CreateTempDirectory("service-nested");
        var gameDir = CreateGameDir(temp, SettingsText);
        temp.Adapter("adapters/demo-mod", NestedAdapter);

        var service = CreateService(temp, gameDir);
        var result = await service.LoadAsync("test");

        Assert.That(result.IsSuccess, Is.True, result.Message);
        var mod = result.Value!.Mods.Single();
        var fields = mod.Files.Single().Fields;

        var rootGain = fields.Single(f => f.Key == "gain" && f.NodePath == "ROOT");
        var childGain = fields.Single(f => f.Key == "gain" && f.NodePath == "ROOT/CHILD_A");
        Assert.Multiple(() =>
        {
            Assert.That(rootGain.Value, Is.EqualTo("1"), "根层的 gain 要读根层那行");
            Assert.That(childGain.Value, Is.EqualTo("4.5"), "子节点的 gain 要读子节点那行");
            Assert.That(fields.Single(f => f.Key == "enabled").Value, Is.EqualTo("true"));
        });
    }

    [Test]
    public async Task LoadAsync_MatchesByDeclaredPath_WhenRegistryHasNoModule()
    {
        using var temp = ModConfigFixtures.CreateTempDirectory("service-match-path");
        var gameDir = CreateGameDir(temp, SettingsText);
        temp.Adapter("adapters/path-matched", PathMatchedAdapter);

        var service = CreateService(temp, gameDir);
        var result = await service.LoadAsync("test");

        Assert.That(result.IsSuccess, Is.True, result.Message);
        var mod = result.Value!.Mods.Single();

        Assert.Multiple(() =>
        {
            Assert.That(mod.State, Is.EqualTo(ModConfigAdapterState.Editable));
            var codes = (mod.Diagnostics ?? []).Select(d => d.Code).ToArray();
            Assert.That(codes, Does.Contain("matched-by-path"),
                "靠路径命中的 mod 必须留痕：页面上要提示版本无法确认");
        });
    }

    [Test]
    public async Task LoadAsync_IgnoresDeclaredPath_WhenNothingOnDisk()
    {
        // 反向用例：路径写了但磁盘上没东西，不能凭空冒出卡片（§5 不占位的规则）。
        using var temp = ModConfigFixtures.CreateTempDirectory("service-match-path-miss");
        var gameDir = Path.Combine(temp.Path, "empty-game");
        Directory.CreateDirectory(gameDir);
        temp.Adapter("adapters/path-matched", PathMatchedAdapter);

        var service = CreateService(temp, gameDir);
        var result = await service.LoadAsync("test");

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(result.Value!.Mods, Is.Empty);
    }

    private static ModConfigService CreateService(TempDirectory temp, string gameDir)
    {
        var catalog = new AdapterCatalog([Path.Combine(temp.Path, "adapters")], [Path.Combine(temp.Path, "user")]);
        var instances = new ProfileFakeInstanceService(
            new InstanceInfo("test", gameDir, "1.12.5", IsDefault: true, IsLocked: false, ModuleCount: 0))
        {
            Current = null,
        };

        return new ModConfigService(catalog, instances, new ProfileFakeRegistryService(), new FixedGameRunningProbe(false));
    }

    private static string CreateGameDir(TempDirectory temp, string text)
    {
        var gameDir = Path.Combine(temp.Path, "game");
        var path = Path.Combine(gameDir, "GameData", "Demo", "settings.cfg");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text + Environment.NewLine);
        return gameDir;
    }
}
