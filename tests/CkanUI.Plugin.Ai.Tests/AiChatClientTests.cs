using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

using CkanUI.Plugin.Ai.Chat;
using CkanUI.Plugin.Ai.Tests.Support;
using CkanUI.Services.Abstractions;

using NUnit.Framework;

namespace CkanUI.Plugin.Ai.Tests;

/// <summary>问答循环：一次直答、一次工具往返、轮次用尽收口、导航失败回灌。</summary>
[TestFixture]
public sealed class AiChatClientTests
{
    private static AiChatOptions Options(int maxRounds = 6) => new(
        "http://localhost:8317/v1",
        "test-model",
        "sk-test",
        MaxToolRounds: maxRounds);

    [Test]
    public async Task DirectAnswerSendsOneRequestWithToolsAndUtf8Body()
    {
        var handler = new ScriptedHttpHandler(ScriptedHttpHandler.Reply("你好，我是 CKAN 助手。"));
        using var http = new HttpClient(handler);
        var client = new AiChatClient(http, new AiToolRunner(new FakeCkanBridge()));

        var outcome = await client.RunAsync(Options(), new[] { AiChatMessage.User("嗨，你是谁？") });

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Text, Is.EqualTo("你好，我是 CKAN 助手。"));
            Assert.That(outcome.StopReason, Is.EqualTo(AiStopReason.Completed));
            Assert.That(outcome.ToolRounds, Is.Zero);
            Assert.That(outcome.Traces, Is.Empty);
            Assert.That(handler.Requests, Has.Count.EqualTo(1));
        });

        var request = handler.Requests[0];
        Assert.Multiple(() =>
        {
            Assert.That(request.Uri?.ToString(), Is.EqualTo("http://localhost:8317/v1/chat/completions"));
            Assert.That(request.Authorization, Is.EqualTo("Bearer sk-test"));
            Assert.That(request.ContentType, Is.EqualTo("application/json"));

            // 工具清单与 tool_choice 必须在每次请求里：多轮往返时第二轮丢了 tools，模型就没法再调工具。
            Assert.That(request.Body, Does.Contain("\"tools\""));
            Assert.That(request.Body, Does.Contain("\"tool_choice\":\"auto\""));
            Assert.That(request.Body, Does.Contain("ckan_navigate"));
            Assert.That(request.Body, Does.Contain("web_search"));

            // 端点对非 ASCII 请求体敏感：必须 UTF-8 无 BOM，中文得原样发出去。
            Assert.That(request.Bytes, Is.Not.Empty);
            Assert.That(request.Bytes[0], Is.Not.EqualTo((byte)0xEF), "请求体不能带 UTF-8 BOM");
            Assert.That(request.Body, Does.Contain("嗨，你是谁？"));
        });
    }

    [Test]
    public async Task ToolCallThenAnswerReplaysToolMessageWithResult()
    {
        var handler = new ScriptedHttpHandler(
            ScriptedHttpHandler.ToolCallReply("call-1", "ckan_search_mods", "{\"query\":\"kerbal\",\"limit\":3}"),
            ScriptedHttpHandler.Reply("仓库里有 TestMod。"));
        using var http = new HttpClient(handler);
        var bridge = new FakeCkanBridge();
        var traces = new List<AiToolTrace>();
        var client = new AiChatClient(http, new AiToolRunner(bridge));

        var outcome = await client.RunAsync(Options(), new[] { AiChatMessage.User("帮我找 kerbal 模组") }, traces.Add);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Text, Is.EqualTo("仓库里有 TestMod。"));
            Assert.That(outcome.StopReason, Is.EqualTo(AiStopReason.Completed));
            Assert.That(outcome.ToolRounds, Is.EqualTo(1));
            Assert.That(traces, Has.Count.EqualTo(1));
            Assert.That(traces[0].Tool, Is.EqualTo("ckan_search_mods"));
            Assert.That(traces[0].Ok, Is.True);
            Assert.That(traces[0].Step, Is.EqualTo(1));
        });

        Assert.That(handler.Requests, Has.Count.EqualTo(2));
        var second = handler.Requests[1].Body;
        Assert.Multiple(() =>
        {
            Assert.That(second, Does.Contain("\"role\":\"tool\""));
            Assert.That(second, Does.Contain("\"tool_call_id\":\"call-1\""));

            // 工具结果必须真的回灌（模组标识符得出现在 tool 消息里），而不是只回一句「已执行」。
            Assert.That(second, Does.Contain("TestMod"));

            // 工具结果是塞进 content 字符串里的 JSON，所以请求体里看到的是转义后的 \"ok\":true。
            Assert.That(second, Does.Contain("\\\"ok\\\":true"));
        });
    }

    [Test]
    public async Task ToolRoundsExhaustedClosesOutWithFixedText()
    {
        var handler = new ScriptedHttpHandler(
            ScriptedHttpHandler.ToolCallReply("call-1", "ckan_context", "{}"),
            ScriptedHttpHandler.ToolCallReply("call-2", "ckan_context", "{}"),
            ScriptedHttpHandler.ToolCallReply("call-3", "ckan_context", "{}"),
            ScriptedHttpHandler.ToolCallReply("call-4", "ckan_context", "{}"));
        using var http = new HttpClient(handler);
        var client = new AiChatClient(http, new AiToolRunner(new FakeCkanBridge()));

        var outcome = await client.RunAsync(Options(maxRounds: 2), new[] { AiChatMessage.User("一直调工具") });

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Text, Is.EqualTo(AiChatOutcome.ToolRoundsExhaustedText));
            Assert.That(outcome.StopReason, Is.EqualTo(AiStopReason.ToolRoundsExhausted));
            Assert.That(outcome.ToolRounds, Is.EqualTo(2));

            // 上限是 2 轮：第 3 次回复发现超限后，只能再发一次「不带工具」的收口请求（第 4 次），不能无限要工具。
            Assert.That(handler.Requests, Has.Count.EqualTo(4));
            Assert.That(handler.Requests[3].Body, Does.Contain("\"tool_choice\":\"none\""));
            Assert.That(handler.Requests[3].Body, Does.Contain("不要再请求工具"));
        });
    }

    [Test]
    public async Task RoundsExhaustedStillAnswersFromWhatTheToolsAlreadyReturned()
    {
        // 真实端点上模型会连着几轮只回 tool_calls：收口请求必须把已有结果变成正文，而不是「轮次用尽」四个字。
        var handler = new ScriptedHttpHandler(
            ScriptedHttpHandler.ToolCallReply("call-1", "ckan_search_mods", "{\"query\":\"restock\"}"),
            ScriptedHttpHandler.ToolCallReply("call-2", "ckan_search_mods", "{\"query\":\"restock\"}"),
            ScriptedHttpHandler.Reply("推荐 Restock：它是原版部件的高清重制。"));
        using var http = new HttpClient(handler);
        var client = new AiChatClient(http, new AiToolRunner(new FakeCkanBridge()));

        var outcome = await client.RunAsync(Options(maxRounds: 2), new[] { AiChatMessage.User("推荐个部件包") });

        Assert.Multiple(() =>
        {
            Assert.That(outcome.StopReason, Is.EqualTo(AiStopReason.Completed));
            Assert.That(outcome.Text, Is.EqualTo("推荐 Restock：它是原版部件的高清重制。"));
            Assert.That(outcome.ToolRounds, Is.EqualTo(2));
            Assert.That(outcome.Traces, Has.Count.EqualTo(2));
            Assert.That(handler.Requests, Has.Count.EqualTo(3));
        });
    }

    [Test]
    public async Task NavigateFailureReasonGoesBackToModel()
    {
        var handler = new ScriptedHttpHandler(
            ScriptedHttpHandler.ToolCallReply("call-1", "ckan_navigate", "{\"page_key\":\"nope\"}"),
            ScriptedHttpHandler.Reply("那个页面打不开。"));
        using var http = new HttpClient(handler);
        var bridge = new FakeCkanBridge
        {
            NavigateResult = OperationResult<bool>.Fail("页面不存在：nope"),
        };
        var traces = new List<AiToolTrace>();
        var client = new AiChatClient(http, new AiToolRunner(bridge));

        var outcome = await client.RunAsync(Options(), new[] { AiChatMessage.User("打开 nope 页") }, traces.Add);

        Assert.Multiple(() =>
        {
            Assert.That(bridge.NavigateCalls, Is.EqualTo(1));
            Assert.That(bridge.LastNavigateKey, Is.EqualTo("nope"));
            Assert.That(outcome.Text, Is.EqualTo("那个页面打不开。"));
            Assert.That(traces, Has.Count.EqualTo(1));
            Assert.That(traces[0].Ok, Is.False);
        });

        // 失败原因（而不是异常）必须回到模型手上，它才有机会换页面键或如实告诉用户。
        var second = handler.Requests[1].Body;
        Assert.Multiple(() =>
        {
            Assert.That(second, Does.Contain("navigate_failed"));
            Assert.That(second, Does.Contain("页面不存在：nope"));
        });
    }

    [Test]
    public async Task EmptyAnswerIsReportedAsEmptyNotSuccess()
    {
        var handler = new ScriptedHttpHandler(ScriptedHttpHandler.Reply(string.Empty));
        using var http = new HttpClient(handler);
        var client = new AiChatClient(http, new AiToolRunner(new FakeCkanBridge()));

        var outcome = await client.RunAsync(Options(), new[] { AiChatMessage.User("说点什么") });

        Assert.That(outcome.StopReason, Is.EqualTo(AiStopReason.EmptyAnswer));
        Assert.That(outcome.Text, Is.Empty);
    }
}
