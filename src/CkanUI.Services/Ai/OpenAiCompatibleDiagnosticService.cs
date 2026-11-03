using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Ai;

namespace CkanUI.Services.Ai;

/// <summary>
/// OpenAI 兼容 Chat Completions 客户端（DeepSeek / OpenAI / 本地 Ollama 的 /v1 都吃这一套）。
/// 只做一件事：把提示词发出去，把返回的 JSON 解析成 <see cref="LlmDiagnosis"/>。
/// 失败一律转成 <see cref="OperationResult{T}"/>，界面负责告诉玩家「下次该检查哪个字段」。
/// </summary>
public sealed class OpenAiCompatibleDiagnosticService : ILlmDiagnosticService
{
    private const string Fence = "```";

    private static readonly JsonSerializerOptions PayloadOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;

    /// <summary>构造。<paramref name="http"/> 仅供测试注入；不传时自建 90 秒超时的客户端。</summary>
    public OpenAiCompatibleDiagnosticService(HttpClient? http = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
    }

    /// <inheritdoc />
    public async Task<OperationResult<LlmDiagnosis>> DiagnoseAsync(
        LlmDiagnosisRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.BaseUrl))
        {
            return OperationResult<LlmDiagnosis>.Fail("还没有填接口地址（例如 https://api.deepseek.com/v1）");
        }

        if (string.IsNullOrWhiteSpace(request.Model))
        {
            return OperationResult<LlmDiagnosis>.Fail("还没有填模型名（例如 deepseek-chat）");
        }

        if (string.IsNullOrWhiteSpace(request.ApiKey))
        {
            return OperationResult<LlmDiagnosis>.Fail("还没有填 API Key");
        }

        if (!Uri.TryCreate(request.BaseUrl.Trim().TrimEnd('/') + "/chat/completions", UriKind.Absolute, out var endpoint))
        {
            return OperationResult<LlmDiagnosis>.Fail("接口地址不是合法的 URL：" + request.BaseUrl);
        }

        using var message = new HttpRequestMessage(HttpMethod.Post, endpoint);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", request.ApiKey.Trim());
        message.Content = JsonContent.Create(
            new
            {
                model = request.Model.Trim(),
                messages = new[]
                {
                    new { role = "system", content = "你只输出严格 JSON，不输出 Markdown。" },
                    new { role = "user", content = request.Prompt },
                },
                temperature = 0.1,
                max_tokens = request.MaxOutputTokens,
                response_format = new { type = "json_object" },
            },
            options: PayloadOptions);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(message, cancellationToken).ConfigureAwait(false);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return OperationResult<LlmDiagnosis>.Fail("请求超时：模型 90 秒内没有返回", retryable: true);
        }
        catch (HttpRequestException ex)
        {
            return OperationResult<LlmDiagnosis>.Fail("连不上接口：" + ex.Message, ex.ToString(), retryable: true);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return OperationResult<LlmDiagnosis>.Fail(
                    string.Create(CultureInfo.InvariantCulture, $"接口返回 {(int)response.StatusCode}：{Truncate(body, 400)}"),
                    retryable: (int)response.StatusCode is >= 500 or 429);
            }

            var content = ExtractContent(body);
            if (content is null)
            {
                return OperationResult<LlmDiagnosis>.Fail("接口返回里没有 choices[0].message.content", Truncate(body, 400));
            }

            return ParseDiagnosis(content);
        }
    }

    /// <summary>从 Chat Completions 外壳里取出模型正文。</summary>
    public static string? ExtractContent(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("choices", out var choices) ||
                choices.ValueKind != JsonValueKind.Array ||
                choices.GetArrayLength() == 0)
            {
                return null;
            }

            var first = choices[0];
            return first.TryGetProperty("message", out var msg) && msg.TryGetProperty("content", out var content)
                ? content.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>解析模型正文。容忍三件事：代码围栏、数组外面多套一层、字段缺失。</summary>
    public static OperationResult<LlmDiagnosis> ParseDiagnosis(string content)
    {
        var json = StripFences(content);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
            {
                root = root[0];
            }

            if (root.ValueKind != JsonValueKind.Object)
            {
                return OperationResult<LlmDiagnosis>.Fail("模型没有按约定返回 JSON 对象", Truncate(content, 400));
            }

            var summary = ReadString(root, "summary") ?? "模型没有给出结论";
            var severity = ReadString(root, "severity") ?? "medium";
            var hypotheses = new List<LlmHypothesis>();
            if (root.TryGetProperty("hypotheses", out var array) && array.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in array.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    hypotheses.Add(new LlmHypothesis(
                        ReadString(item, "cause") ?? "（未命名原因）",
                        ReadConfidence(item),
                        ReadSeq(item),
                        ReadString(item, "explain") ?? string.Empty,
                        ReadString(item, "fix") ?? string.Empty));
                }
            }

            var need = new List<string>();
            if (root.TryGetProperty("needMoreInfo", out var extra) && extra.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in extra.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } text)
                    {
                        need.Add(text);
                    }
                }
            }

            hypotheses.Sort((a, b) => b.Confidence.CompareTo(a.Confidence));
            return OperationResult<LlmDiagnosis>.Ok(new LlmDiagnosis(summary, severity, hypotheses, need));
        }
        catch (JsonException ex)
        {
            return OperationResult<LlmDiagnosis>.Fail("模型返回的不是合法 JSON：" + ex.Message, Truncate(content, 600));
        }
    }

    private static string StripFences(string content)
    {
        var text = content.Trim();
        if (!text.StartsWith(Fence, StringComparison.Ordinal))
        {
            return text;
        }

        var firstNewline = text.IndexOf('\n');
        var lastFence = text.LastIndexOf(Fence, StringComparison.Ordinal);
        if (firstNewline < 0 || lastFence <= firstNewline)
        {
            return text;
        }

        return text[(firstNewline + 1)..lastFence].Trim();
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static double ReadConfidence(JsonElement element)
    {
        if (!element.TryGetProperty("confidence", out var value))
        {
            return 0.5;
        }

        var raw = value.ValueKind switch
        {
            JsonValueKind.Number => value.GetDouble(),
            JsonValueKind.String => double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0.5,
            _ => 0.5,
        };

        return Math.Clamp(raw, 0d, 1d);
    }

    private static int[] ReadSeq(JsonElement element)
    {
        if (!element.TryGetProperty("evidenceSeq", out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<int>();
        }

        var list = new List<int>();
        foreach (var item in value.EnumerateArray())
        {
            switch (item.ValueKind)
            {
                case JsonValueKind.Number when item.TryGetInt32(out var number):
                    list.Add(number);
                    break;
                case JsonValueKind.String when int.TryParse(item.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed):
                    list.Add(parsed);
                    break;
            }
        }

        return list.ToArray();
    }

    private static string Truncate(string? text, int max) =>
        string.IsNullOrEmpty(text) ? string.Empty : text.Length <= max ? text : text[..max] + "…";
}
