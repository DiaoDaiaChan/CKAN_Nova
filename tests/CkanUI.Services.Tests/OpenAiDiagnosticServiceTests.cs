using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using CkanUI.Services.Abstractions.Ai;
using CkanUI.Services.Ai;

using NUnit.Framework;

namespace CkanUI.Services.Tests;

/// <summary>
/// OpenAI 兼容客户端的解析面（14 篇 §6.2）：模型永远可能返回代码围栏、多余外壳或干脆不是 JSON，
/// 这里把「哪种坏法会被识别成哪种失败」钉死，界面才不会把一段报错原文当诊断结果显示给玩家。
/// </summary>
[TestFixture]
public sealed class OpenAiDiagnosticServiceTests
{
    private const string ContentJson = """
        {"summary":"某个零件缩放在 1.12 下失效","severity":"high","hypotheses":[
          {"cause":"旧版 TweakScale","confidence":0.9,"evidenceSeq":[12,13],"explain":"日志里该模块连续抛异常","fix":"升级 TweakScale 到 2.4.7"},
          {"cause":"内存不足","confidence":0.3,"evidenceSeq":["40"],"explain":"加载时接近 32 位上限","fix":"换 64 位版本"}],
         "needMoreInfo":["完整的 KSP.log"]}
        """;

    [Test]
    public void Parse_SortsHypothesesByConfidence()
    {
        var result = OpenAiCompatibleDiagnosticService.ParseDiagnosis(ContentJson);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Summary, Is.EqualTo("某个零件缩放在 1.12 下失效"));
        Assert.That(result.Value.Hypotheses[0].Cause, Is.EqualTo("旧版 TweakScale"));
        Assert.That(result.Value.Hypotheses[0].EvidenceSeq, Is.EqualTo(new[] { 12, 13 }));
        Assert.That(result.Value.Hypotheses[1].EvidenceSeq, Is.EqualTo(new[] { 40 }));
        Assert.That(result.Value.NeedMoreInfo, Has.Count.EqualTo(1));
    }

    [Test]
    public void Parse_StripsCodeFences()
    {
        var fenced = "```json\n" + ContentJson + "\n```";

        Assert.That(OpenAiCompatibleDiagnosticService.ParseDiagnosis(fenced).IsSuccess, Is.True);
    }

    [Test]
    public void Parse_RejectsNonJson()
    {
        var result = OpenAiCompatibleDiagnosticService.ParseDiagnosis("我觉得是模组冲突");

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("不是合法 JSON"));
    }

    [Test]
    public void Parse_ClampsConfidenceAndFillsDefaults()
    {
        var result = OpenAiCompatibleDiagnosticService.ParseDiagnosis(
            "{\"hypotheses\":[{\"cause\":\"x\",\"confidence\":7}]}");

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Hypotheses[0].Confidence, Is.EqualTo(1d));
        Assert.That(result.Value.Severity, Is.EqualTo("medium"));
    }

    [Test]
    public async Task Diagnose_PostsPromptAndReturnsParsedDiagnosis()
    {
        HttpRequestMessage? seen = null;
        string? seenBody = null;
        var handler = new StubHandler(async request =>
        {
            seen = request;
            seenBody = await request.Content!.ReadAsStringAsync();
            return Json(HttpStatusCode.OK, $"{{\"choices\":[{{\"message\":{{\"content\":{Quote(ContentJson)}}}}}]}}");
        });

        var service = new OpenAiCompatibleDiagnosticService(new HttpClient(handler));
        var result = await service.DiagnoseAsync(new LlmDiagnosisRequest
        {
            BaseUrl = "https://api.example.com/v1/",
            Model = "test-model",
            ApiKey = "sk-test",
            Prompt = "#1 12:00:00.000 ERR boom",
        });

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(seen!.RequestUri!.ToString(), Is.EqualTo("https://api.example.com/v1/chat/completions"));
        Assert.That(seen.Headers.Authorization!.Scheme, Is.EqualTo("Bearer"));
        Assert.That(seen.Headers.Authorization.Parameter, Is.EqualTo("sk-test"));
        Assert.That(seenBody, Does.Contain("test-model"));
        Assert.That(seenBody, Does.Contain("#1 12:00:00.000 ERR boom"));
        Assert.That(result.Value!.Hypotheses, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task Diagnose_SurfacesHttpErrorWithStatus()
    {
        var handler = new StubHandler(_ => Task.FromResult(Json(HttpStatusCode.Unauthorized, "{\"error\":\"bad key\"}")));
        var service = new OpenAiCompatibleDiagnosticService(new HttpClient(handler));

        var result = await service.DiagnoseAsync(Request());

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("401"));
        Assert.That(result.Retryable, Is.False);
    }

    [Test]
    public async Task Diagnose_FailsWhenContentIsMissing()
    {
        var handler = new StubHandler(_ => Task.FromResult(Json(HttpStatusCode.OK, "{\"choices\":[]}")));
        var service = new OpenAiCompatibleDiagnosticService(new HttpClient(handler));

        var result = await service.DiagnoseAsync(Request());

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("choices[0].message.content"));
    }

    [Test]
    public void Diagnose_RequiresConfiguration()
    {
        var service = new OpenAiCompatibleDiagnosticService(new HttpClient(new StubHandler(_ => Task.FromResult(Json(HttpStatusCode.OK, "{}")))));

        Assert.That(service.DiagnoseAsync(new LlmDiagnosisRequest { Prompt = "x" }).Result.IsSuccess, Is.False);
        Assert.That(service.DiagnoseAsync(new LlmDiagnosisRequest { BaseUrl = "https://a/v1", Prompt = "x" }).Result.IsSuccess, Is.False);
        Assert.That(service.DiagnoseAsync(new LlmDiagnosisRequest { BaseUrl = "https://a/v1", Model = "m", Prompt = "x" }).Result.IsSuccess, Is.False);
    }

    private static LlmDiagnosisRequest Request() => new()
    {
        BaseUrl = "https://api.example.com/v1",
        Model = "test-model",
        ApiKey = "sk-test",
        Prompt = "x",
    };

    private static string Quote(string text) =>
        System.Text.Json.JsonSerializer.Serialize(text);

    private static HttpResponseMessage Json(HttpStatusCode code, string body) => new(code)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _responder;

        public StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            _responder(request);
    }
}
