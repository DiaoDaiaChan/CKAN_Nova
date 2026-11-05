using System.Linq;
using System.Threading.Tasks;

using CkanUI.Plugin.Ai.Chat;
using CkanUI.Plugin.Ai.Search;
using CkanUI.Plugin.Ai.Tests.Support;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Ai;

using NUnit.Framework;

namespace CkanUI.Plugin.Ai.Tests;

/// <summary>工具层：坏输入不抛、网页搜索不可用给结构化错误、MCP 结果压缩成清单。</summary>
[TestFixture]
public sealed class AiToolRunnerTests
{
    [Test]
    public async Task UnknownToolReturnsStructuredErrorInsteadOfThrowing()
    {
        var runner = new AiToolRunner(new FakeCkanBridge());

        var result = await runner.InvokeAsync("ckan_delete_everything", "{}");

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.False);
            Assert.That(result.Json, Does.Contain("unknown_tool"));
            Assert.That(result.Json, Does.Contain("ckan_search_mods"), "错误里要列出可用工具，模型才有机会改用别的");
        });
    }

    [Test]
    public async Task BadArgumentsAndMissingNameDoNotThrow()
    {
        var runner = new AiToolRunner(new FakeCkanBridge());

        var broken = await runner.InvokeAsync("ckan_search_mods", "{ 这不是 JSON");
        var missing = await runner.InvokeAsync(null, "{}");
        var wrongShape = await runner.InvokeAsync("ckan_search_mods", "[1,2,3]");

        Assert.Multiple(() =>
        {
            Assert.That(broken.Ok, Is.False);
            Assert.That(broken.Json, Does.Contain("bad_arguments"));
            Assert.That(missing.Ok, Is.False);
            Assert.That(missing.Json, Does.Contain("missing_tool_name"));
            Assert.That(wrongShape.Ok, Is.False);
        });
    }

    [Test]
    public async Task WebSearchUnavailableReturnsStructuredErrorWithHint()
    {
        var runner = new AiToolRunner(new FakeCkanBridge(), search: null);

        var result = await runner.InvokeAsync("web_search", "{\"query\":\"KSP 1.12 最新版本\"}");

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.False);
            Assert.That(result.Json, Does.Contain("web_search_unavailable"));
            Assert.That(result.Json, Does.Contain("\"hint\""), "必须给模型一句降级指引，否则它会开始编");
        });
    }

    [Test]
    public async Task WebSearchHitsAreCompactedToList()
    {
        var search = new FakeWebSearch(WebSearchResult.Success(new[]
        {
            new WebSearchHit("KSP 官网", "https://example.invalid/ksp", "官方站点"),
        }));
        var runner = new AiToolRunner(new FakeCkanBridge(), search);

        var result = await runner.InvokeAsync("web_search", "{\"query\":\"KSP\"}");

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.True);
            Assert.That(result.Json, Does.Contain("https://example.invalid/ksp"));
            Assert.That(result.Json, Does.Contain("KSP 官网"));
            Assert.That(result.Json, Does.Contain("\"count\":1"));
        });
    }

    [Test]
    public async Task BridgeMissingGivesBridgeUnavailableForCkanTools()
    {
        var runner = new AiToolRunner(bridge: null);

        var result = await runner.InvokeAsync("ckan_context", "{}");

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.False);
            Assert.That(result.Json, Does.Contain("bridge_unavailable"));
        });
    }

    [Test]
    public void ToolSchemaContainsEveryAdvertisedTool()
    {
        var names = AiToolRunner.BuildToolSchema()
            .Select(node => node?["function"]?["name"]?.GetValue<string>())
            .ToArray();

        Assert.That(names, Is.EquivalentTo(new[]
        {
            "ckan_context",
            "ckan_search_mods",
            "ckan_mod_detail",
            "ckan_installed",
            "ckan_list_pages",
            "ckan_ui_state",
            "ckan_page_actions",
            "ckan_run_page_action",
            "ckan_set_ui_field",
            "ckan_settings",
            "ckan_navigate",
            "ckan_install_mod",
            "ckan_set_theme",
            "ckan_filter_mods",
            "ckan_switch_instance",
            "ckan_launch_game",
            "ckan_set_setting",
            "web_search",
        }));
    }

    [Test]
    public void InstallToolIsAdvertisedWithRequiredIdentifier()
    {
        var install = AiToolRunner.BuildToolSchema()
            .Select(node => node?["function"])
            .Single(function => function!["name"]?.GetValue<string>() == "ckan_install_mod")!;
        var required = install["parameters"]!["required"]!.AsArray()
            .Select(node => node!.GetValue<string>())
            .ToArray();

        Assert.That(required, Does.Contain("identifier"));
    }

    [Test]
    public async Task InstallApprovedReturnsAppliedOutcome()
    {
        var bridge = new FakeCkanBridge
        {
            InstallResult = OperationResult<AiInstallOutcome>.Ok(new AiInstallOutcome(
                "TestMod",
                true,
                "已安装 TestMod。",
                new[] { "TestMod", "ModuleManager" },
                Array.Empty<string>(),
                Array.Empty<string>(),
                new[] { "自动安装了依赖 ModuleManager" })),
        };
        var runner = new AiToolRunner(bridge);

        var result = await runner.InvokeAsync("ckan_install_mod", "{\"identifier\":\"TestMod\"}");

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.True);
            Assert.That(result.Json, Does.Contain("\"applied\":true"));
            Assert.That(result.Json, Does.Contain("\"installed\":[\"TestMod\",\"ModuleManager\"]"));
            Assert.That(bridge.LastInstallIdentifier, Is.EqualTo("TestMod"));
        });
    }

    [Test]
    public async Task InstallRefusedIsSuccessWithAppliedFalse()
    {
        var bridge = new FakeCkanBridge
        {
            InstallResult = OperationResult<AiInstallOutcome>.Ok(new AiInstallOutcome(
                "TestMod",
                false,
                "用户拒绝了安装，未改动磁盘。",
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>())),
        };
        var runner = new AiToolRunner(bridge);

        var result = await runner.InvokeAsync("ckan_install_mod", "{\"identifier\":\"TestMod\"}");

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.True, "用户拒绝不是工具错误，模型要能如实转述");
            Assert.That(result.Json, Does.Contain("\"applied\":false"));
            Assert.That(result.Json, Does.Contain("拒绝"));
        });
    }

    [Test]
    public async Task InstallFailureIsReportedAsInstallFailed()
    {
        var bridge = new FakeCkanBridge
        {
            InstallResult = OperationResult<AiInstallOutcome>.Fail("下载失败：网络不可达"),
        };
        var runner = new AiToolRunner(bridge);

        var result = await runner.InvokeAsync("ckan_install_mod", "{\"identifier\":\"TestMod\"}");

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.False);
            Assert.That(result.Json, Does.Contain("\"error\":\"install_failed\""));
            Assert.That(result.Json, Does.Contain("下载失败"));
        });
    }

    [Test]
    public async Task InstallWithoutIdentifierIsBadArgumentsAndNeverCallsBridge()
    {
        var bridge = new FakeCkanBridge();
        var runner = new AiToolRunner(bridge);

        var result = await runner.InvokeAsync("ckan_install_mod", "{\"identifier\":\"   \"}");

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.False);
            Assert.That(result.Json, Does.Contain("bad_arguments"));
            Assert.That(bridge.InstallCalls, Is.Zero, "参数没通过校验就不该碰桥");
        });
    }

    [Test]
    public async Task NavigateFailureIsDataNotException()
    {
        var bridge = new FakeCkanBridge
        {
            NavigateResult = OperationResult<bool>.Fail("导航被宿主拒绝", "目标是弹窗页"),
        };
        var runner = new AiToolRunner(bridge);

        var result = await runner.InvokeAsync("ckan_navigate", "{\"page_key\":\"settings\"}");

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.False);
            Assert.That(result.Json, Does.Contain("navigate_failed"));
            Assert.That(result.Json, Does.Contain("导航被宿主拒绝"));
            Assert.That(bridge.LastNavigateKey, Is.EqualTo("settings"));
        });
    }

    [Test]
    public async Task SetThemeSuccessAndFailureTranslateCorrectly()
    {
        var bridge = new FakeCkanBridge();
        var runner = new AiToolRunner(bridge);

        var success = await runner.InvokeAsync("ckan_set_theme", """{"theme":"dark","skin":"coronal-gold"}""");
        Assert.Multiple(() =>
        {
            Assert.That(success.Ok, Is.True);
            Assert.That(success.Json, Does.Contain("已应用主题设置"));
            Assert.That(bridge.LastSetTheme, Is.EqualTo("dark"));
            Assert.That(bridge.LastSetSkin, Is.EqualTo("coronal-gold"));
        });

        var badArgs = await runner.InvokeAsync("ckan_set_theme", "{}");
        Assert.Multiple(() =>
        {
            Assert.That(badArgs.Ok, Is.False);
            Assert.That(badArgs.Json, Does.Contain("bad_arguments"));
        });

        bridge.SetThemeResult = OperationResult<bool>.Fail("主题不存在");
        var failed = await runner.InvokeAsync("ckan_set_theme", """{"theme":"nonexistent"}""");
        Assert.Multiple(() =>
        {
            Assert.That(failed.Ok, Is.False);
            Assert.That(failed.Json, Does.Contain("set_theme_failed"));
            Assert.That(failed.Json, Does.Contain("主题不存在"));
        });
    }

    [Test]
    public async Task FilterModsSuccessAndFailureTranslateCorrectly()
    {
        var bridge = new FakeCkanBridge();
        var runner = new AiToolRunner(bridge);

        var success = await runner.InvokeAsync("ckan_filter_mods", """{"query":"warp","filter":"installed"}""");
        Assert.Multiple(() =>
        {
            Assert.That(success.Ok, Is.True);
            Assert.That(success.Json, Does.Contain("已应用模组筛选"));
            Assert.That(bridge.LastFilterQuery, Is.EqualTo("warp"));
            Assert.That(bridge.LastFilterKind, Is.EqualTo("installed"));
        });

        var badArgs = await runner.InvokeAsync("ckan_filter_mods", "{}");
        Assert.Multiple(() =>
        {
            Assert.That(badArgs.Ok, Is.False);
            Assert.That(badArgs.Json, Does.Contain("bad_arguments"));
        });

        bridge.FilterModsResult = OperationResult<bool>.Fail("导航模组页失败");
        var failed = await runner.InvokeAsync("ckan_filter_mods", """{"query":"test"}""");
        Assert.Multiple(() =>
        {
            Assert.That(failed.Ok, Is.False);
            Assert.That(failed.Json, Does.Contain("filter_mods_failed"));
            Assert.That(failed.Json, Does.Contain("导航模组页失败"));
        });
    }

    [Test]
    public async Task SwitchInstanceSuccessAndFailureTranslateCorrectly()
    {
        var bridge = new FakeCkanBridge();
        var runner = new AiToolRunner(bridge);

        var success = await runner.InvokeAsync("ckan_switch_instance", """{"name":"KSP-1.12"}""");
        Assert.Multiple(() =>
        {
            Assert.That(success.Ok, Is.True);
            Assert.That(success.Json, Does.Contain("已切换至实例"));
            Assert.That(bridge.LastSwitchedInstance, Is.EqualTo("KSP-1.12"));
        });

        var badArgs = await runner.InvokeAsync("ckan_switch_instance", """{"name":"   "}""");
        Assert.Multiple(() =>
        {
            Assert.That(badArgs.Ok, Is.False);
            Assert.That(badArgs.Json, Does.Contain("bad_arguments"));
        });

        bridge.SwitchInstanceResult = OperationResult<bool>.Fail("实例未找到");
        var failed = await runner.InvokeAsync("ckan_switch_instance", """{"name":"Missing"}""");
        Assert.Multiple(() =>
        {
            Assert.That(failed.Ok, Is.False);
            Assert.That(failed.Json, Does.Contain("switch_instance_failed"));
            Assert.That(failed.Json, Does.Contain("实例未找到"));
        });
    }

    [Test]
    public async Task LaunchGameSuccessAndFailureTranslateCorrectly()
    {
        var bridge = new FakeCkanBridge();
        var runner = new AiToolRunner(bridge);

        var success = await runner.InvokeAsync("ckan_launch_game", "{}");
        Assert.Multiple(() =>
        {
            Assert.That(success.Ok, Is.True);
            Assert.That(success.Json, Does.Contain("游戏已启动"));
            Assert.That(bridge.LaunchGameCalls, Is.EqualTo(1));
        });

        bridge.LaunchGameResult = OperationResult<bool>.Fail("游戏启动文件丢失");
        var failed = await runner.InvokeAsync("ckan_launch_game", "{}");
        Assert.Multiple(() =>
        {
            Assert.That(failed.Ok, Is.False);
            Assert.That(failed.Json, Does.Contain("launch_game_failed"));
            Assert.That(failed.Json, Does.Contain("游戏启动文件丢失"));
        });
    }

    [Test]
    public async Task SetSettingSuccessAndFailureTranslateCorrectly()
    {
        var bridge = new FakeCkanBridge();
        var runner = new AiToolRunner(bridge);

        var success = await runner.InvokeAsync("ckan_set_setting", """{"key":"Locale","value":"zh-CN"}""");
        Assert.Multiple(() =>
        {
            Assert.That(success.Ok, Is.True);
            Assert.That(success.Json, Does.Contain("已更新"));
            Assert.That(bridge.LastSettingKey, Is.EqualTo("Locale"));
            Assert.That(bridge.LastSettingValue, Is.EqualTo("zh-CN"));
        });

        var badArgs = await runner.InvokeAsync("ckan_set_setting", """{"key":"Locale"}""");
        Assert.Multiple(() =>
        {
            Assert.That(badArgs.Ok, Is.False);
            Assert.That(badArgs.Json, Does.Contain("bad_arguments"));
        });

        bridge.SetSettingResult = OperationResult<bool>.Fail("无效键名");
        var failed = await runner.InvokeAsync("ckan_set_setting", """{"key":"Invalid","value":"val"}""");
        Assert.Multiple(() =>
        {
            Assert.That(failed.Ok, Is.False);
            Assert.That(failed.Json, Does.Contain("set_setting_failed"));
            Assert.That(failed.Json, Does.Contain("无效键名"));
        });
    }

    [Test]
    public async Task UiStateFlattensFieldsAndActions()
    {
        var bridge = new FakeCkanBridge();
        var runner = new AiToolRunner(bridge);

        var result = await runner.InvokeAsync("ckan_ui_state", "{}");

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.True);
            Assert.That(result.Json, Does.Contain("\"page\":\"mods\""));
            Assert.That(result.Json, Does.Contain("\"view_model\":\"ModsPageViewModel\""));
            Assert.That(result.Json, Does.Contain("\"name\":\"SearchText\""), "字段要摊平成 name/kind/value，模型才读得懂");
            Assert.That(result.Json, Does.Contain("\"can_execute\":true"));
            Assert.That(result.Json, Does.Contain("ApplyCommand"));
        });
    }

    [Test]
    public async Task PageActionsPassPageKeyAndReportCount()
    {
        var bridge = new FakeCkanBridge();
        var runner = new AiToolRunner(bridge);

        var result = await runner.InvokeAsync("ckan_page_actions", """{"page_key":"mods"}""");

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.True);
            Assert.That(bridge.LastPageActionsQuery, Is.EqualTo("mods"));
            Assert.That(result.Json, Does.Contain("\"count\":2"));
            Assert.That(result.Json, Does.Contain("SelectCommand"));
            Assert.That(result.Json, Does.Contain("\"parameter\":\"Int32\""));
        });
    }

    [Test]
    public async Task RunPageActionRequiresActionNameAndNeverCallsBridge()
    {
        var bridge = new FakeCkanBridge();
        var runner = new AiToolRunner(bridge);

        var result = await runner.InvokeAsync("ckan_run_page_action", """{"argument":"3"}""");

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.False);
            Assert.That(result.Json, Does.Contain("missing_action"));
            Assert.That(bridge.RunPageActionCalls, Is.Zero);
        });
    }

    [Test]
    public async Task RunPageActionPassesArgumentAndPageKey()
    {
        var bridge = new FakeCkanBridge();
        var runner = new AiToolRunner(bridge);

        var result = await runner.InvokeAsync(
            "ckan_run_page_action",
            """{"action":"SelectCommand","argument":"3","page_key":"mods"}""");

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.True);
            Assert.That(bridge.LastPageAction, Is.EqualTo("SelectCommand"));
            Assert.That(bridge.LastPageActionArgument, Is.EqualTo("3"));
            Assert.That(bridge.LastPageActionPageKey, Is.EqualTo("mods"));
            Assert.That(result.Json, Does.Contain("已执行"));
        });
    }

    [Test]
    public async Task RunPageActionFailureIsDataNotException()
    {
        var bridge = new FakeCkanBridge
        {
            RunPageActionResult = OperationResult<string>.Fail("用户拒绝了这次操作。", "confirm_denied"),
        };
        var runner = new AiToolRunner(bridge);

        var result = await runner.InvokeAsync("ckan_run_page_action", """{"action":"UninstallCommand"}""");

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.False);
            Assert.That(result.Json, Does.Contain("run_page_action_failed"));
            Assert.That(result.Json, Does.Contain("用户拒绝了这次操作。"));
            Assert.That(result.Json, Does.Contain("confirm_denied"));
        });
    }

    [Test]
    public async Task SetUiFieldRequiresFieldAndNeverCallsBridge()
    {
        var bridge = new FakeCkanBridge();
        var runner = new AiToolRunner(bridge);

        var result = await runner.InvokeAsync("ckan_set_ui_field", """{"value":"realchute"}""");

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.False);
            Assert.That(result.Json, Does.Contain("missing_field"));
            Assert.That(bridge.SetUiFieldCalls, Is.Zero);
        });
    }

    [Test]
    public async Task SetUiFieldPassesValueAndPageKey()
    {
        var bridge = new FakeCkanBridge();
        var runner = new AiToolRunner(bridge);

        var result = await runner.InvokeAsync(
            "ckan_set_ui_field",
            """{"field":"SearchText","value":"realchute","page_key":"mods"}""");

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.True);
            Assert.That(bridge.LastUiField, Is.EqualTo("SearchText"));
            Assert.That(bridge.LastUiFieldValue, Is.EqualTo("realchute"));
            Assert.That(bridge.LastUiFieldPageKey, Is.EqualTo("mods"));
            Assert.That(result.Json, Does.Contain("「SearchText」"), "把桥返回的旧值→新值原样转给模型");
        });
    }

    [Test]
    public async Task SetUiFieldFailureIsDataNotException()
    {
        var bridge = new FakeCkanBridge
        {
            SetUiFieldResult = OperationResult<string>.Fail("字段「ApiKey」属于密钥类值，拒绝从对话写入。"),
        };
        var runner = new AiToolRunner(bridge);

        var result = await runner.InvokeAsync("ckan_set_ui_field", """{"field":"ApiKey","value":"x"}""");

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.False);
            Assert.That(result.Json, Does.Contain("set_ui_field_failed"));
            Assert.That(result.Json, Does.Contain("密钥"));
        });
    }

    [Test]
    public async Task SettingsListKindsAndChoices()
    {
        var runner = new AiToolRunner(new FakeCkanBridge());

        var result = await runner.InvokeAsync("ckan_settings", "{}");

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.True);
            Assert.That(result.Json, Does.Contain("\"count\":2"));
            Assert.That(result.Json, Does.Contain("\"key\":\"Density\""));
            Assert.That(result.Json, Does.Contain("\"choices\":[\"standard\",\"compact\"]"));
            Assert.That(
                result.Json,
                Does.Contain("\"key\":\"Theme\",\"kind\":\"text\",\"value\":\"dark\"}"),
                "无枚举取值的项不要带空 choices 数组，否则模型会以为枚举为空");
        });
    }
}
