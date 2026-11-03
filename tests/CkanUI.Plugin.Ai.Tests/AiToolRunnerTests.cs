using System.Linq;
using System.Threading.Tasks;

using CkanUI.Plugin.Ai.Chat;
using CkanUI.Plugin.Ai.Search;
using CkanUI.Plugin.Ai.Tests.Support;
using CkanUI.Services.Abstractions;

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
            "ckan_navigate",
            "web_search",
        }));
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
}
