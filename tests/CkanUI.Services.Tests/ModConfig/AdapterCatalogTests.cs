using CkanUI.Services.Abstractions.ModConfig;
using CkanUI.Services.ModConfig;
using NUnit.Framework;

namespace CkanUI.Services.Tests.ModConfig;

/// <summary>
/// CFG-02：目录即插件（内置目录 + 用户目录 + 覆盖规则 + 坏件隔离）。
/// 关键不变式：一个坏 adapter 只能停用它自己（T-CFG-06），否则用户写错一份 JSON 会让整个功能消失。
/// </summary>
[TestFixture]
public sealed class AdapterCatalogTests
{
    private static string AdapterJson(string id, string name) => """
        {
          "schemaVersion": 1,
          "id": "§ID§",
          "name": "§NAME§",
          "match": { "ckanIdentifiers": ["§ID§"] },
          "files": [ { "path": "GameData/§ID§/settings.cfg", "layout": "flat", "mode": "editable", "id": "settings" } ]
        }
        """.Replace("§ID§", id, StringComparison.Ordinal).Replace("§NAME§", name, StringComparison.Ordinal);

    [Test]
    public void Adapters_LoadsEverySubdirectoryWithAdapterJson()
    {
        using var temp = ModConfigFixtures.CreateTempDirectory("catalog-ok");
        var builtin = Path.Combine(temp.Path, "builtin");
        Directory.CreateDirectory(builtin);
        temp.Adapter("builtin/beta-mod", AdapterJson("beta-mod", "Beta Mod"));
        temp.Adapter("builtin/alpha-mod", AdapterJson("alpha-mod", "Alpha Mod"));

        var catalog = new AdapterCatalog([builtin], [Path.Combine(temp.Path, "no-user")]);

        Assert.Multiple(() =>
        {
            Assert.That(catalog.Adapters, Has.Count.EqualTo(2));
            Assert.That(catalog.Adapters[0].Name, Is.EqualTo("Alpha Mod"), "结果按 Name 排序，列表才稳定");
            Assert.That(catalog.Adapters[1].Name, Is.EqualTo("Beta Mod"));
        });
    }

    [Test]
    public void Adapters_MissingBuiltinDirectory_WarnsButDoesNotThrow()
    {
        using var temp = ModConfigFixtures.CreateTempDirectory("catalog-missing");
        var catalog = new AdapterCatalog([Path.Combine(temp.Path, "nope")], [Path.Combine(temp.Path, "nope-user")]);

        Assert.Multiple(() =>
        {
            Assert.That(catalog.Adapters, Is.Empty);
            Assert.That(catalog.Diagnostics.Any(d => d.Code == "adapter-dir-missing"), Is.True,
                "内置目录缺失通常是打包漏了 adapters/，必须能看见");
        });
    }

    [Test]
    public void Adapters_MissingUserDirectory_IsSilent()
    {
        using var temp = ModConfigFixtures.CreateTempDirectory("catalog-user-silent");
        var builtin = Path.Combine(temp.Path, "builtin");
        Directory.CreateDirectory(builtin);
        temp.Adapter("builtin/one-mod", AdapterJson("one-mod", "One Mod"));

        var catalog = new AdapterCatalog([builtin], [Path.Combine(temp.Path, "user-does-not-exist")]);

        Assert.Multiple(() =>
        {
            Assert.That(catalog.Adapters, Has.Count.EqualTo(1));
            Assert.That(catalog.Diagnostics.Any(d => d.Code == "adapter-dir-missing"), Is.False,
                "用户目录不存在是常态，不该刷警告");
        });
    }

    [Test]
    public void Adapters_BrokenAdapterOnlyDisablesItself()
    {
        // T-CFG-06：一份坏 JSON 不能连累其它 adapter。
        using var temp = ModConfigFixtures.CreateTempDirectory("catalog-broken");
        var builtin = Path.Combine(temp.Path, "builtin");
        Directory.CreateDirectory(builtin);
        temp.Adapter("builtin/good-mod", AdapterJson("good-mod", "Good Mod"));
        temp.Adapter("builtin/broken-mod", "{ this is not json ");

        var catalog = new AdapterCatalog([builtin], [Path.Combine(temp.Path, "user")]);

        Assert.Multiple(() =>
        {
            Assert.That(catalog.Adapters, Has.Count.EqualTo(1));
            Assert.That(catalog.Adapters[0].Id, Is.EqualTo("good-mod"));
            Assert.That(catalog.Diagnostics.Any(d => d.IsError), Is.True, "坏件要留下错误诊断");
        });
    }

    [Test]
    public void Adapters_UserDirectoryOverridesBuiltinWithSameId()
    {
        using var temp = ModConfigFixtures.CreateTempDirectory("catalog-override");
        var builtin = Path.Combine(temp.Path, "builtin");
        var user = Path.Combine(temp.Path, "user");
        Directory.CreateDirectory(builtin);
        Directory.CreateDirectory(user);
        temp.Adapter("builtin/same-mod", AdapterJson("same-mod", "Builtin Name"));
        temp.Adapter("user/same-mod", AdapterJson("same-mod", "User Name"));

        var catalog = new AdapterCatalog([builtin], [user]);

        Assert.Multiple(() =>
        {
            Assert.That(catalog.Adapters, Has.Count.EqualTo(1));
            Assert.That(catalog.Adapters[0].Name, Is.EqualTo("User Name"), "用户目录优先");
            Assert.That(catalog.Adapters[0].IsUserProvided, Is.True);
            Assert.That(catalog.Diagnostics.Any(d => d.Code == "adapter-overridden"), Is.True);
        });
    }

    [Test]
    public void Adapters_DuplicateIdInsideBuiltin_KeepsFirstAndWarns()
    {
        using var temp = ModConfigFixtures.CreateTempDirectory("catalog-dup");
        var first = Path.Combine(temp.Path, "builtin-a");
        var second = Path.Combine(temp.Path, "builtin-b");
        Directory.CreateDirectory(first);
        Directory.CreateDirectory(second);
        temp.Adapter("builtin-a/same-mod", AdapterJson("same-mod", "A Name"));
        temp.Adapter("builtin-b/same-mod", AdapterJson("same-mod", "B Name"));

        var catalog = new AdapterCatalog([first, second], [Path.Combine(temp.Path, "user")]);

        Assert.Multiple(() =>
        {
            Assert.That(catalog.Adapters, Has.Count.EqualTo(1));
            Assert.That(catalog.Adapters[0].Name, Is.EqualTo("A Name"));
            Assert.That(catalog.Diagnostics.Any(d => d.Code == "adapter-duplicate"), Is.True);
        });
    }

    [Test]
    public void Reload_PicksUpNewlyAddedAdapter()
    {
        using var temp = ModConfigFixtures.CreateTempDirectory("catalog-reload");
        var builtin = Path.Combine(temp.Path, "builtin");
        Directory.CreateDirectory(builtin);
        temp.Adapter("builtin/one-mod", AdapterJson("one-mod", "One Mod"));

        var catalog = new AdapterCatalog([builtin], [Path.Combine(temp.Path, "user")]);
        Assert.That(catalog.Adapters, Has.Count.EqualTo(1));

        temp.Adapter("builtin/two-mod", AdapterJson("two-mod", "Two Mod"));
        catalog.Reload();

        Assert.That(catalog.Adapters, Has.Count.EqualTo(2), "Reload 后必须重新扫描磁盘");
    }

    [Test]
    public void GetDefaultUserDirectory_FollowsCkanAppDataConvention()
    {
        var expected = Path.Combine("CKAN", "ui", "adapters");
        Assert.That(AdapterCatalog.GetDefaultUserDirectory(), Does.EndWith(expected));
    }

    [Test]
    public void Adapters_RepoBuiltinDirectory_LoadsEveryBundledAdapter()
    {
        using var temp = ModConfigFixtures.CreateTempDirectory("catalog-repo");
        var catalog = new AdapterCatalog([ModConfigFixtures.BuiltinAdapterDirectory], [Path.Combine(temp.Path, "user")]);

        var ids = catalog.Adapters.Select(a => a.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray();

        Assert.Multiple(() =>
        {
            // 内置目录是"随包发出去的默认能力"，少一份就等于发行版缺功能；新增 adapter 必须同步这条清单。
            Assert.That(ids, Is.EqualTo(new[]
            {
                "dynamic-battery-storage",
                "ferram-aerospace-research",
                "kerbalism",
                "krpc",
                "ksp-community-fixes",
                "ksp-wheel",
                "lunar-transfer-planner",
                "memgraph",
                "parallax-continued",
                "rcs-build-aid",
                "real-chute",
                "real-fuels-mfs",
                "space-dust",
                "system-heat",
                "textures-unlimited",
                "time-control",
                "too-many-orbits",
                "vab-organizer",
                "waypoint-manager",
            }));
            Assert.That(catalog.Diagnostics.Any(d => d.IsError), Is.False,
                "仓库内置 adapter 必须全部能加载：" + string.Join(" | ", catalog.Diagnostics.Select(d => d.Message)));
        });
    }

    [Test]
    public void Adapters_CreateFromTemplateFixtures_ContainEveryDeclaredKey()
    {
        // onMissing=create-from-template 的模板就是"文件不存在时"造出来的配置；
        // 模板里漏一个键，用户拿到的新文件就会缺项，页面再写这一项就报「文件里没有键」。
        using var temp = ModConfigFixtures.CreateTempDirectory("catalog-template");
        var catalog = new AdapterCatalog([ModConfigFixtures.BuiltinAdapterDirectory], [Path.Combine(temp.Path, "user")]);

        var problems = new List<string>();
        foreach (var adapter in catalog.Adapters)
        {
            foreach (var file in adapter.Files.Where(f => f.Template is not null))
            {
                var templatePath = Path.Combine(
                    Path.GetDirectoryName(adapter.SourcePath!) ?? string.Empty, file.Template!);
                if (!File.Exists(templatePath))
                {
                    problems.Add(adapter.Id + " 的模板不存在：" + templatePath);
                    continue;
                }

                var template = File.ReadAllText(templatePath);
                foreach (var field in adapter.Fields.Where(f => adapter.FindFile(f.FileIndex, f.FileId) == file))
                {
                    if (!System.Text.RegularExpressions.Regex.IsMatch(
                            template, "\\b" + System.Text.RegularExpressions.Regex.Escape(field.Key) + "\\s*="))
                    {
                        problems.Add(adapter.Id + " 的模板缺键：" + field.Key);
                    }
                }
            }
        }

        Assert.That(problems, Is.Empty, string.Join(" | ", problems));
    }

    [Test]
    public void Adapters_BundledFields_PointAtKeysThatExistInRealConfigFiles()
    {
        // 幽灵键（adapter 声明了、真实文件里没有）是这个功能最阴的缺陷：页面显示空白，
        // 用户以为"这项没设置"，真去写却报「文件里没有键」。这里只断言"每份 adapter 至少
        // 声明了字段"这种能离线证明的部分；键是否真实存在靠 fixtures 与真机取证。
        using var temp = ModConfigFixtures.CreateTempDirectory("catalog-repo-fields");
        var catalog = new AdapterCatalog([ModConfigFixtures.BuiltinAdapterDirectory], [Path.Combine(temp.Path, "user")]);

        Assert.Multiple(() =>
        {
            foreach (var adapter in catalog.Adapters.Where(a => a.Id != "kerbalism"))
            {
                Assert.That(adapter.Fields, Is.Not.Empty,
                    adapter.Id + " 声明了文件却没有任何字段，等于只读卡片，没有存在意义");
            }
        });
    }
}
