using CkanUI.Services.Abstractions.ModConfig;
using CkanUI.Services.ModConfig;
using NUnit.Framework;

namespace CkanUI.Services.Tests.ModConfig;

/// <summary>
/// CFG-02：adapter.json 的解析与 schema 校验。
/// 这些用例是"信任模型"的第一道闸门（docs/plan/15 §4.4）：用户目录里的 adapter 是纯声明式 JSON，
/// 校验不过必须整份停用，不能半可用——否则 UI 会拿到半截字段表去写别人的配置。
/// </summary>
[TestFixture]
public sealed class AdapterJsonReaderTests
{
    private const string MinimalAdapter = """
        {
          "schemaVersion": 1,
          "id": "demo-mod",
          "name": "Demo Mod",
          "match": { "ckanIdentifiers": ["DemoMod"] },
          "files": [
            { "path": "GameData/Demo/settings.cfg", "layout": "flat", "mode": "editable", "id": "settings" }
          ],
          "fields": [
            { "file": "settings", "key": "enabled", "type": "bool", "default": true }
          ]
        }
        """;

    private static AdapterLoadResult Read(string json) => AdapterJsonReader.Read(json, "adapter.json");

    [Test]
    public void Read_MinimalAdapter_Succeeds()
    {
        var result = Read(MinimalAdapter);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, "最小合法 adapter 必须能加载：" + Describe(result));
            Assert.That(result.HasErrors, Is.False);
        });

        var adapter = result.Adapter!;
        Assert.Multiple(() =>
        {
            Assert.That(adapter.Id, Is.EqualTo("demo-mod"));
            Assert.That(adapter.Name, Is.EqualTo("Demo Mod"));
            Assert.That(adapter.SchemaVersion, Is.EqualTo(1));
            Assert.That(adapter.Files, Has.Count.EqualTo(1));
            Assert.That(adapter.Match.CkanIdentifiers, Has.Count.EqualTo(1));
            Assert.That(adapter.Unlisted, Is.EqualTo(ModConfigUnlistedPolicy.Preserve));
        });
    }

    [Test]
    public void Read_FieldByFileId_BindsIndexAndId()
    {
        var adapter = Read(MinimalAdapter).Adapter!;
        var field = adapter.Fields[0];

        Assert.Multiple(() =>
        {
            Assert.That(field.FileIndex, Is.EqualTo(0), "字符串 file 引用应解析成下标");
            Assert.That(field.FileId, Is.EqualTo("settings"));
            Assert.That(adapter.Files[0].EffectiveId, Is.EqualTo("settings"));
        });
    }

    [Test]
    public void Read_FieldByNumericIndex_BindsIndex()
    {
        var json = MinimalAdapter.Replace("\"file\": \"settings\"", "\"file\": 0", StringComparison.Ordinal);
        var adapter = Read(json).Adapter!;

        Assert.Multiple(() =>
        {
            Assert.That(adapter.Fields[0].FileIndex, Is.EqualTo(0));
            Assert.That(adapter.Fields[0].FileId, Is.Null);
        });
    }

    [Test]
    public void Read_DefaultValues_NormalizedToSourceText()
    {
        var json = """
            {
              "schemaVersion": 1,
              "id": "demo-defaults",
              "name": "Demo Defaults",
              "match": { "ckanIdentifiers": ["Demo"] },
              "files": [ { "path": "a.cfg", "layout": "flat", "mode": "editable", "id": "a" } ],
              "fields": [
                { "file": "a", "key": "flag", "type": "bool", "default": false },
                { "file": "a", "key": "size", "type": "float", "default": 0.05 },
                { "file": "a", "key": "lang", "type": "string", "default": "zh-cn" }
              ]
            }
            """;
        var adapter = Read(json).Adapter!;

        Assert.Multiple(() =>
        {
            Assert.That(adapter.Fields[0].DefaultValue, Is.EqualTo("false"));
            Assert.That(adapter.Fields[1].DefaultValue, Is.EqualTo("0.05"));
            Assert.That(adapter.Fields[2].DefaultValue, Is.EqualTo("zh-cn"));
        });
    }

    [Test]
    public void Read_AllowsCommentsAndTrailingCommas()
    {
        // jsonc 的注释与尾逗号是允许的：用户手写 adapter 时几乎必然会加注释。
        var json = """
            {
              // schema 版本
              "schemaVersion": 1,
              "id": "demo-jsonc",
              "name": "Demo Jsonc",
              "match": { "ckanIdentifiers": ["Demo"], },
              "files": [ { "path": "a.cfg", "layout": "node", "rootNode": "ROOT", "mode": "editable", }, ],
            }
            """;
        var result = Read(json);

        Assert.That(result.IsSuccess, Is.True, Describe(result));
        Assert.Multiple(() =>
        {
            Assert.That(result.Adapter!.Files[0].Layout, Is.EqualTo(ModConfigFileLayout.Node));
            Assert.That(result.Adapter!.Files[0].RootNode, Is.EqualTo("ROOT"));
        });
    }

    [Test]
    public void Read_MalformedJson_ReportsErrorInsteadOfThrowing()
    {
        // 注意不能拿 "{ \"schemaVersion\": 1, }" 当"坏 JSON"：jsonc 允许尾逗号，它是合法输入。
        // 这里用真正截断的文本，验证解析失败降级成诊断而不是抛异常。
        var result = Read("{ \"schemaVersion\": 1 ");

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.HasErrors, Is.True);
            Assert.That(result.Diagnostics[0].Code, Is.EqualTo("adapter-json-invalid"));
        });
    }

    [Test]
    public void Read_RootIsArray_IsRejected()
    {
        var result = Read("[ { \"schemaVersion\": 1 } ]");

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False, "根必须是对象");
            Assert.That(result.HasErrors, Is.True);
        });
    }

    [Test]
    public void Read_MissingSchemaVersion_IsRejected()
    {
        var json = MinimalAdapter.Replace("\"schemaVersion\": 1,", string.Empty, StringComparison.Ordinal);
        var result = Read(json);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(string.Join(" ", result.Diagnostics.Select(d => d.Message)), Does.Contain("schemaVersion"));
        });
    }

    [Test]
    public void Read_UnsupportedSchemaVersion_IsRejected()
    {
        var json = MinimalAdapter.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 2", StringComparison.Ordinal);
        var result = Read(json);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(string.Join(" ", result.Diagnostics.Select(d => d.Message)), Does.Contain("schemaVersion"));
        });
    }

    [TestCase("Demo_Mod", TestName = "Read_IdWithUnderscore_IsRejected")]
    [TestCase("Demo Mod", TestName = "Read_IdWithSpace_IsRejected")]
    [TestCase("-demo", TestName = "Read_IdStartingWithDash_IsRejected")]
    public void Read_BadId_IsRejected(string id)
    {
        var json = MinimalAdapter.Replace("\"id\": \"demo-mod\"", "\"id\": \"" + id + "\"", StringComparison.Ordinal);
        var result = Read(json);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False, "id 必须是 kebab-case：" + id);
            Assert.That(result.HasErrors, Is.True);
        });
    }

    [Test]
    public void Read_MissingName_IsRejected()
    {
        var json = MinimalAdapter.Replace("\"name\": \"Demo Mod\",", string.Empty, StringComparison.Ordinal);
        var result = Read(json);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(string.Join(" ", result.Diagnostics.Select(d => d.Message)), Does.Contain("name"));
        });
    }

    [Test]
    public void Read_MissingMatch_IsRejected()
    {
        var json = MinimalAdapter.Replace("\"match\": { \"ckanIdentifiers\": [\"DemoMod\"] },", string.Empty, StringComparison.Ordinal);
        var result = Read(json);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(string.Join(" ", result.Diagnostics.Select(d => d.Message)), Does.Contain("match"));
        });
    }

    [Test]
    public void Read_EmptyCkanIdentifiers_IsRejected()
    {
        var json = MinimalAdapter.Replace("[\"DemoMod\"]", "[]", StringComparison.Ordinal);
        var result = Read(json);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False, "至少要有 1 个 CKAN 标识才能判定已装");
            Assert.That(result.HasErrors, Is.True);
        });
    }

    [Test]
    public void Read_NonHttpHomepage_IsRejected()
    {
        var json = MinimalAdapter.Replace(
            "\"name\": \"Demo Mod\",",
            "\"name\": \"Demo Mod\", \"homepage\": \"ftp://example.com\",",
            StringComparison.Ordinal);
        var result = Read(json);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(string.Join(" ", result.Diagnostics.Select(d => d.Message)), Does.Contain("homepage"));
        });
    }

    [Test]
    public void Read_EmptyFiles_IsRejected()
    {
        var json = """
            {
              "schemaVersion": 1,
              "id": "demo-empty",
              "name": "Demo Empty",
              "match": { "ckanIdentifiers": ["Demo"] },
              "files": []
            }
            """;
        var result = Read(json);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.HasErrors, Is.True);
        });
    }

    [Test]
    public void Read_AbsoluteFilePath_IsRejected()
    {
        var json = MinimalAdapter.Replace(
            "\"path\": \"GameData/Demo/settings.cfg\"",
            "\"path\": \"/etc/settings.cfg\"",
            StringComparison.Ordinal);
        var result = Read(json);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False, "adapter 只能声明游戏目录内的相对路径");
            Assert.That(string.Join(" ", result.Diagnostics.Select(d => d.Message)), Does.Contain("path"));
        });
    }

    [Test]
    public void Read_UnknownLayout_IsRejected()
    {
        var json = MinimalAdapter.Replace("\"layout\": \"flat\"", "\"layout\": \"tree\"", StringComparison.Ordinal);
        var result = Read(json);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(string.Join(" ", result.Diagnostics.Select(d => d.Message)), Does.Contain("layout"));
        });
    }

    [Test]
    public void Read_UnknownMode_IsRejected()
    {
        var json = MinimalAdapter.Replace("\"mode\": \"editable\"", "\"mode\": \"writable\"", StringComparison.Ordinal);
        var result = Read(json);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(string.Join(" ", result.Diagnostics.Select(d => d.Message)), Does.Contain("mode"));
        });
    }

    [Test]
    public void Read_CustomLayoutMustBeReadonly()
    {
        // 本项目规则：custom 布局意味着我方不理解它的语法，绝不允许写。
        var json = MinimalAdapter.Replace("\"layout\": \"flat\"", "\"layout\": \"custom\"", StringComparison.Ordinal);
        var result = Read(json);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(string.Join(" ", result.Diagnostics.Select(d => d.Message)), Does.Contain("custom"));
        });
    }

    [Test]
    public void Read_CreateFromTemplateWithoutTemplate_IsRejected()
    {
        var json = MinimalAdapter.Replace(
            "\"mode\": \"editable\"",
            "\"mode\": \"editable\", \"onMissing\": \"create-from-template\"",
            StringComparison.Ordinal);
        var result = Read(json);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(string.Join(" ", result.Diagnostics.Select(d => d.Message)), Does.Contain("template"));
        });
    }

    [Test]
    public void Read_DuplicateFileId_IsRejected()
    {
        var json = """
            {
              "schemaVersion": 1,
              "id": "demo-dup",
              "name": "Demo Dup",
              "match": { "ckanIdentifiers": ["Demo"] },
              "files": [
                { "path": "a.cfg", "layout": "flat", "mode": "editable", "id": "same" },
                { "path": "b.cfg", "layout": "flat", "mode": "editable", "id": "same" }
              ]
            }
            """;
        var result = Read(json);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False, "文件 id 必须唯一，否则 fields 的引用会歧义");
            Assert.That(result.HasErrors, Is.True);
        });
    }

    [Test]
    public void Read_FieldFileIndexOutOfRange_IsRejected()
    {
        var json = MinimalAdapter.Replace("\"file\": \"settings\"", "\"file\": 7", StringComparison.Ordinal);
        var result = Read(json);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(string.Join(" ", result.Diagnostics.Select(d => d.Message)), Does.Contain("file"));
        });
    }

    [Test]
    public void Read_FieldFileIdNotFound_IsRejected()
    {
        var json = MinimalAdapter.Replace("\"file\": \"settings\"", "\"file\": \"nope\"", StringComparison.Ordinal);
        var result = Read(json);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(string.Join(" ", result.Diagnostics.Select(d => d.Message)), Does.Contain("nope"));
        });
    }

private const string NodeAdapter = """
        {
          "schemaVersion": 1,
          "id": "demo-node",
          "name": "Demo Node",
          "match": { "ckanIdentifiers": ["Demo"] },
          "files": [
            { "path": "GameData/Demo/settings.cfg", "layout": "node", "rootNode": "ROOT", "mode": "editable", "id": "settings" }
          ],
          "fields": [
            { "file": "settings", "key": "enabled", "type": "bool", "default": true },
            { "file": "settings", "nodePath": "CHILD", "key": "enabled", "type": "bool", "default": false },
            { "file": "settings", "nodePath": "ROOT/GRANDCHILD", "key": "value", "type": "string" }
          ]
        }
        """;

    [Test]
    public void Read_NodePath_ResolvesRelativeToRootNodeUnlessAbsolute()
    {
        var adapter = Read(NodeAdapter).Adapter!;

        Assert.Multiple(() =>
        {
            Assert.That(adapter.Fields[0].NodePath, Is.Null, "不写 nodePath 就是根节点");
            Assert.That(adapter.Fields[0].ResolveNodePath(adapter.Files[0]), Is.EqualTo("ROOT"));
            Assert.That(adapter.Fields[1].ResolveNodePath(adapter.Files[0]), Is.EqualTo("ROOT/CHILD"),
                "相对名链要拼在 rootNode 后面");
            Assert.That(adapter.Fields[2].ResolveNodePath(adapter.Files[0]), Is.EqualTo("ROOT/GRANDCHILD"),
                "以 / 开头或不以 / 开头但已含根名，都解析成同一条绝对名链");
        });
    }

    [Test]
    public void Read_NodePath_AllowsSameKeyInDifferentNodes()
    {
        // 同一个键名出现在根层和子节点里是合法配置，判重必须按节点分域，
        // 否则真实文件里 "enabled 在 ROOT 与 ROOT/CHILD 各有一份" 会被误判成重复声明。
        var result = Read(NodeAdapter);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, Describe(result));
            Assert.That(result.Adapter!.Fields, Has.Count.EqualTo(3));
            Assert.That(result.Diagnostics.Any(d => d.Code == "adapter-unknown-property"), Is.False,
                "nodePath 是已知属性，不该触发未知属性告警：" + Describe(result));
        });
    }

    [Test]
    public void Read_NodePath_DuplicateWithinSameNode_IsRejected()
    {
        var json = NodeAdapter.Replace(
            "\"nodePath\": \"CHILD\", \"key\": \"enabled\"",
            "\"nodePath\": \"CHILD\", \"key\": \"enabled\"",
            StringComparison.Ordinal)
            .Replace(
                "{ \"file\": \"settings\", \"key\": \"enabled\", \"type\": \"bool\", \"default\": true }",
                "{ \"file\": \"settings\", \"key\": \"enabled\", \"type\": \"bool\" }, { \"file\": \"settings\", \"key\": \"enabled\", \"type\": \"bool\" }",
                StringComparison.Ordinal);
        var result = Read(json);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False, "同一节点里同名键重复声明只会互相打架");
            Assert.That(string.Join(" ", result.Diagnostics.Select(d => d.Message)), Does.Contain("重复声明"));
        });
    }

    [Test]
    public void Read_NodePath_WithEmptySegment_IsRejected()
    {
        var json = NodeAdapter.Replace("\"nodePath\": \"CHILD\"", "\"nodePath\": \"A//B\"", StringComparison.Ordinal);
        var result = Read(json);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False, "A//B 定位不到任何真实节点，写盘时会静默落空");
            Assert.That(string.Join(" ", result.Diagnostics.Select(d => d.Message)), Does.Contain("nodePath"));
        });
    }

    [Test]
    public void Read_NodePath_OnNonNodeLayout_IsRejected()
    {
        var json = MinimalAdapter.Replace(
            "\"file\": \"settings\", \"key\": \"enabled\"",
            "\"file\": \"settings\", \"nodePath\": \"CHILD\", \"key\": \"enabled\"",
            StringComparison.Ordinal);
        var result = Read(json);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False, "flat 文件的键全在根层，nodePath 只会让查找永远落空");
            Assert.That(string.Join(" ", result.Diagnostics.Select(d => d.Message)), Does.Contain("nodePath"));
            Assert.That(string.Join(" ", result.Diagnostics.Select(d => d.Message)), Does.Contain("flat"));
        });
    }

    [Test]
    public void Read_NodePath_BlankString_CountsAsRootAndCollides()
    {
        // 空白 nodePath 按"没写"处理，于是这个 enabled 与根层那个撞车；
        // 用报错来证明它确实被归一成了根节点，而不是被当成一个叫 "   " 的节点。
        var json = NodeAdapter.Replace("\"nodePath\": \"CHILD\"", "\"nodePath\": \"   \"", StringComparison.Ordinal);
        var result = Read(json);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(string.Join(" ", result.Diagnostics.Select(d => d.Message)), Does.Contain("重复声明"));
        });
    }

    [Test]
    public void Read_DuplicateKeyInSameFile_IsRejected()
    {
        var json = MinimalAdapter.Replace(
            "{ \"file\": \"settings\", \"key\": \"enabled\", \"type\": \"bool\", \"default\": true }",
            "{ \"file\": \"settings\", \"key\": \"enabled\", \"type\": \"bool\" }, { \"file\": \"settings\", \"key\": \"enabled\", \"type\": \"bool\" }",
            StringComparison.Ordinal);
        var result = Read(json);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False, "同一文件里同一个键重复声明只会互相打架");
            Assert.That(result.HasErrors, Is.True);
        });
    }

    [Test]
    public void Read_UnknownFieldType_IsRejected()
    {
        var json = MinimalAdapter.Replace("\"type\": \"bool\"", "\"type\": \"toggle\"", StringComparison.Ordinal);
        var result = Read(json);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(string.Join(" ", result.Diagnostics.Select(d => d.Message)), Does.Contain("toggle"));
        });
    }

    [Test]
    public void Read_EnumWithoutValues_IsRejected()
    {
        var json = MinimalAdapter.Replace("\"type\": \"bool\"", "\"type\": \"enum\"", StringComparison.Ordinal);
        var result = Read(json);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False, "enum 没有 values 就等于没有白名单，必须报错");
            Assert.That(string.Join(" ", result.Diagnostics.Select(d => d.Message)), Does.Contain("values"));
        });
    }

    [Test]
    public void Read_MinGreaterThanMax_IsRejected()
    {
        var json = """
            {
              "schemaVersion": 1,
              "id": "demo-range",
              "name": "Demo Range",
              "match": { "ckanIdentifiers": ["Demo"] },
              "files": [ { "path": "a.cfg", "layout": "flat", "mode": "editable", "id": "a" } ],
              "fields": [ { "file": "a", "key": "dt", "type": "float", "min": 2.0, "max": 1.0 } ]
            }
            """;
        var result = Read(json);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.HasErrors, Is.True);
        });
    }

    [Test]
    public void Read_InvalidPattern_IsRejected()
    {
        var json = """
            {
              "schemaVersion": 1,
              "id": "demo-pattern",
              "name": "Demo Pattern",
              "match": { "ckanIdentifiers": ["Demo"] },
              "files": [ { "path": "a.cfg", "layout": "flat", "mode": "editable", "id": "a" } ],
              "fields": [ { "file": "a", "key": "s", "type": "string", "pattern": "[" } ]
            }
            """;
        var result = Read(json);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(string.Join(" ", result.Diagnostics.Select(d => d.Message)), Does.Contain("pattern"));
        });
    }

    [Test]
    public void Read_BadVersionRange_IsRejected()
    {
        var json = MinimalAdapter.Replace(
            "\"name\": \"Demo Mod\",",
            "\"name\": \"Demo Mod\", \"modVersionRange\": [\">=not-a-version\"],",
            StringComparison.Ordinal);
        var result = Read(json);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(string.Join(" ", result.Diagnostics.Select(d => d.Message)), Does.Contain("modVersionRange"));
        });
    }

    [Test]
    public void Read_UnknownProperty_IsWarningNotFatal()
    {
        // 拼写错误的未知属性只提醒，不整份停用：用户手写 adapter 时常有额外注释字段。
        var json = MinimalAdapter.Replace(
            "\"name\": \"Demo Mod\",",
            "\"name\": \"Demo Mod\", \"colour\": \"red\",",
            StringComparison.Ordinal);
        var result = Read(json);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, Describe(result));
            Assert.That(result.Diagnostics.Any(d => d.Code == "adapter-unknown-property"), Is.True,
                "未知属性应当留下 Warning：" + Describe(result));
            Assert.That(result.Diagnostics.Any(d => d.IsError), Is.False);
        });
    }

    [Test]
    public void Read_BuiltinKspCommunityFixes_LoadsExpectedShape()
    {
        var path = Path.Combine(ModConfigFixtures.BuiltinAdapterDirectory, "ksp-community-fixes", "adapter.json");
        var result = AdapterJsonReader.ReadFile(path);

        Assert.That(result.IsSuccess, Is.True, Describe(result));

        var adapter = result.Adapter!;
        Assert.Multiple(() =>
        {
            Assert.That(adapter.Id, Is.EqualTo("ksp-community-fixes"));
            Assert.That(adapter.Files, Has.Count.EqualTo(1));
            Assert.That(adapter.Files[0].Layout, Is.EqualTo(ModConfigFileLayout.Node));
            Assert.That(adapter.Files[0].RootNode, Is.EqualTo("KSP_COMMUNITY_FIXES"));
            Assert.That(adapter.Files[0].EffectiveSeparator, Is.EqualTo(" = "));
            Assert.That(adapter.Files[0].LineEnding, Is.EqualTo(ModConfigLineEndingPolicy.Preserve));
            Assert.That(adapter.Files[0].OnMissing, Is.EqualTo(ModConfigMissingFilePolicy.CreateFromTemplate));
            Assert.That(adapter.Files[0].Template, Is.EqualTo("fixtures/settings.default.cfg"));
            Assert.That(adapter.Fields, Has.Count.EqualTo(29), "21 个根层键 + 3 个设置子节点里的 8 个键（见 README）");
            Assert.That(adapter.Match.CaseInsensitive, Is.True);
            Assert.That(adapter.ModVersionRange, Has.Count.EqualTo(2));
            Assert.That(adapter.IsUserProvided, Is.False);
        });

        var root = adapter.Fields.Single(f => f.Key == "RefundingOnRecovery");
        Assert.Multiple(() =>
        {
            Assert.That(root.Type, Is.EqualTo(ModConfigFieldType.Bool));
            Assert.That(root.DefaultValue, Is.EqualTo("true"));
            Assert.That(root.NodePath, Is.Null, "根层键不写 nodePath");
            Assert.That(root.ResolveNodePath(adapter.Files[0]), Is.EqualTo("KSP_COMMUNITY_FIXES"));
        });

        var nested = adapter.Fields.Single(f => f.Key == "ManeuverToolEnabledByDefault");
        Assert.Multiple(() =>
        {
            Assert.That(nested.NodePath, Is.EqualTo("MANEUVER_TOOL_DEFAULTS"));
            Assert.That(nested.ResolveNodePath(adapter.Files[0]), Is.EqualTo("KSP_COMMUNITY_FIXES/MANEUVER_TOOL_DEFAULTS"),
                "nodePath 相对 rootNode 解析");
        });
    }

    [Test]
    public void Read_BuiltinKerbalism_DeclaresEditableSettingsNode()
    {
        var path = Path.Combine(ModConfigFixtures.BuiltinAdapterDirectory, "kerbalism", "adapter.json");
        var result = AdapterJsonReader.ReadFile(path);

        Assert.That(result.IsSuccess, Is.True, Describe(result));

        var adapter = result.Adapter!;
        Assert.Multiple(() =>
        {
            Assert.That(adapter.Id, Is.EqualTo("kerbalism"));
            Assert.That(adapter.Files, Has.Count.EqualTo(1));
            Assert.That(adapter.Files[0].Layout, Is.EqualTo(ModConfigFileLayout.Node));
            Assert.That(adapter.Files[0].Mode, Is.EqualTo(ModConfigWriteMode.Editable));
            Assert.That(adapter.Files[0].IsWritable, Is.True);
            Assert.That(adapter.Files[0].ReadonlyReason, Is.Null);
            Assert.That(adapter.Files[0].RootNode, Is.EqualTo("Kerbalism"));
            Assert.That(adapter.Fields, Has.Count.EqualTo(41), "语料核对过 KerbalismConfig/Settings.cfg 的 41 个根键");

            var profile = adapter.Fields.Single(f => f.Key == "Profile");
            Assert.That(profile.Type, Is.EqualTo(ModConfigFieldType.String));
            Assert.That(profile.Risk, Is.EqualTo(ModConfigFieldRisk.High), "换档案会整套替换玩法参数");

            var threshold = adapter.Fields.Single(f => f.Key == "PressureThreshold");
            Assert.That(threshold.Type, Is.EqualTo(ModConfigFieldType.Float));
            Assert.That(threshold.Min, Is.EqualTo(0));
        });
    }

    [Test]
    public void ReadFile_MissingFile_ReportsError()
    {
        using var temp = ModConfigFixtures.CreateTempDirectory("missing-adapter");
        var result = AdapterJsonReader.ReadFile(Path.Combine(temp.Path, "nope", "adapter.json"));

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.HasErrors, Is.True);
        });
    }

    private static string Describe(AdapterLoadResult result)
        => string.Join(" | ", result.Diagnostics.Select(d => d.Level + ":" + d.Code + ":" + d.Message));
}
