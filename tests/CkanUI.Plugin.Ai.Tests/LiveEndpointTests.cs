using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

using CkanUI.Plugin.Ai.Chat;
using CkanUI.Plugin.Ai.Tests.Support;

using NUnit.Framework;

namespace CkanUI.Plugin.Ai.Tests;

/// <summary>
/// 对真实 AI 端点跑一次完整问答（含工具轮次）。
/// </summary>
/// <remarks>
/// <para><b>为什么不进日常回归</b>：这条用例依赖内网 Tailscale 端点与网络出口，跑在 CI 或别人机器上只会红。
/// 默认 <see cref="Assert.Ignore(string)"/>，把 <c>CKANUI_AI_LIVE=1</c> 与 <c>CKANUI_AI_KEY</c> 设上才跑。</para>
/// <para><b>它证明什么</b>：假 HTTP 用例只能证明「解析器认得我们伪造的回复」；这一条证明真实端点接受我们的请求体
/// （<c>tools</c> / <c>tool_choice=auto</c> / 无 BOM 的 UTF-8 字节）、真的回 <c>tool_calls</c>、工具结果回灌后还能收口成正文。</para>
/// </remarks>
[TestFixture]
public sealed class LiveEndpointTests
{
    private const string LiveFlagVariable = "CKANUI_AI_LIVE";
    private const string KeyVariable = "CKANUI_AI_KEY";
    private const string DefaultBaseUrl = "http://100.111.24.121:8317/v1";
    private const string DefaultModel = "gemini-3.8-flash-high";

    [Test]
    public async Task RealEndpoint_AsksForACkanTool_ThenAnswersWithText()
    {
        var options = BuildOptions();
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(180) };
        var bridge = new FakeCkanBridge();
        var runner = new AiToolRunner(bridge);
        var client = new AiChatClient(http, runner, TimeSpan.FromSeconds(150));

        var outcome = await client
            .RunAsync(options, new[] { AiChatMessage.User("有什么好用的火箭组件 mod？先查 CKAN 仓库再回答。") })
            .ConfigureAwait(false);

        TestContext.Progress.WriteLine(
            "stop=" + outcome.StopReason
            + " rounds=" + outcome.ToolRounds
            + " traces=" + outcome.Traces.Count
            + " chars=" + (outcome.Text ?? string.Empty).Length);
        foreach (var trace in outcome.Traces)
        {
            TestContext.Progress.WriteLine("trace #" + trace.Step + " " + trace.Tool + " ok=" + trace.Ok);
        }

        Assert.That(
            outcome.Traces.Any(trace => trace.Tool.StartsWith("ckan_", StringComparison.Ordinal)),
            Is.True,
            "真实端点必须至少调一次 ckan_* 工具，否则说明 tools 字段没被采纳。");
        Assert.Multiple(() =>
        {
            // 轮次用尽也必须有正文：收口请求会把已查到的结果写成回答。
            Assert.That(outcome.StopReason, Is.Not.EqualTo(AiStopReason.EmptyAnswer));
            Assert.That(outcome.Text, Is.Not.Empty);
            Assert.That(outcome.Text, Is.Not.EqualTo(AiChatOutcome.ToolRoundsExhaustedText));
        });
    }

    [Test]
    public async Task RealEndpoint_ListsTheConfiguredModel()
    {
        var options = BuildOptions();
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        using var request = new HttpRequestMessage(HttpMethod.Get, AiEndpoints.Models(options.BaseUrl));
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", options.ApiKey);
        using var response = await http.SendAsync(request).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.That((int)response.StatusCode, Is.EqualTo(200), body);
        Assert.That(body, Does.Contain(options.Model));
    }

    private static AiChatOptions BuildOptions()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable(LiveFlagVariable), "1", StringComparison.Ordinal))
        {
            Assert.Ignore("真实端点用例默认不跑：设 " + LiveFlagVariable + "=1 与 " + KeyVariable + " 后手动运行。");
        }

        var key = Environment.GetEnvironmentVariable(KeyVariable);
        if (string.IsNullOrWhiteSpace(key))
        {
            Assert.Ignore("缺少 " + KeyVariable + "（API Key 不入库，用环境变量传）。");
        }

        return new AiChatOptions(
            Environment.GetEnvironmentVariable("CKANUI_AI_BASE_URL") ?? DefaultBaseUrl,
            Environment.GetEnvironmentVariable("CKANUI_AI_MODEL") ?? DefaultModel,
            key!.Trim(),
            MaxToolRounds: 3);
    }
}
