using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;

using CkanUI.Plugin.Ai.Settings;

namespace CkanUI.Plugin.Ai.Chat;

/// <summary>
/// OpenAI 兼容的 <c>/chat/completions</c> 客户端：发一轮、看要不要调工具、把工具结果回灌、再发。
/// </summary>
/// <remarks>
/// <para><b>请求体编码</b>：这个端点对非 ASCII 请求体敏感，所以正文一律用
/// <see cref="JsonSerializer.SerializeToUtf8Bytes(JsonNode?, JsonSerializerOptions?)"/> 生成字节数组再交给
/// <see cref="ByteArrayContent"/>——从源头上就不存在 BOM，也不会出现 <c>Encoding.UTF8</c> 默认写 BOM 的老坑。</para>
/// <para><b>轮次上限</b>：模型可以连续多轮要工具，但必须有上限：一个「查不到就一直查」的模型会把用户的
/// 请求费和耐心一起烧掉。到 <see cref="AiChatOptions.MaxToolRounds"/> 就收口成
/// <see cref="AiChatOutcome.ToolRoundsExhaustedText"/>，而不是无限循环。</para>
/// <para><b>回灌协议</b>：assistant 那条带 <c>tool_calls</c> 的消息必须原样回给服务端，再追加 <c>role=tool</c>
/// 的消息（带 <c>tool_call_id</c>）；否则服务端会认为对话不完整而拒绝。</para>
/// </remarks>
public sealed class AiChatClient
{
    /// <summary>单次 HTTP 请求的默认超时（工具轮次多，120 秒比默认 100 秒宽松）。</summary>
    public static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(120);

    private static readonly JsonSerializerOptions WireOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly HttpClient _http;
    private readonly AiToolRunner _tools;
    private readonly TimeSpan _requestTimeout;

    /// <summary>创建客户端。</summary>
    /// <param name="http">复用的 <see cref="HttpClient"/>（页面级共享，不每次 new）。</param>
    /// <param name="tools">工具执行器。</param>
    /// <param name="requestTimeout">单次请求超时；<c>null</c> 用 <see cref="DefaultRequestTimeout"/>。</param>
    public AiChatClient(HttpClient http, AiToolRunner tools, TimeSpan? requestTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(tools);

        _http = http;
        _tools = tools;
        _requestTimeout = requestTimeout is { } timeout && timeout > TimeSpan.Zero ? timeout : DefaultRequestTimeout;
    }

    /// <summary>跑一次完整问答（含工具轮次）。</summary>
    /// <param name="options">端点参数。</param>
    /// <param name="history">对话历史（用户/助手消息，按时间顺序）。</param>
    /// <param name="onToolTrace">每次工具调用的回调（界面用来实时追加轨迹行）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>最终回答 + 工具轨迹。</returns>
    /// <exception cref="AiChatException">端点不可达、非 2xx、返回结构不认识。</exception>
    public async Task<AiChatOutcome> RunAsync(
        AiChatOptions options,
        IReadOnlyList<AiChatMessage>? history = null,
        Action<AiToolTrace>? onToolTrace = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var maxRounds = options.MaxToolRounds > 0 ? options.MaxToolRounds : AiSettings.DefaultMaxToolRounds;
        var endpoint = AiEndpoints.ChatCompletions(options.BaseUrl);
        var messages = BuildInitialMessages(options, history);

        var traces = new List<AiToolTrace>();
        var toolRounds = 0;

        // 防御性步数上限：服务端若一直返回「既没正文也没工具」的消息，也不能让循环转满 CPU。
        var guard = (maxRounds * 2) + 4;
        for (var step = 0; step < guard; step++)
        {
            var reply = await SendAsync(endpoint, options, messages, cancellationToken).ConfigureAwait(false);
            var content = ReadContent(reply);
            var calls = ReadToolCalls(reply);

            if (calls.Count == 0)
            {
                var text = content ?? string.Empty;
                return new AiChatOutcome(
                    text,
                    string.IsNullOrWhiteSpace(text) ? AiStopReason.EmptyAnswer : AiStopReason.Completed,
                    toolRounds,
                    traces);
            }

            messages.Add(BuildAssistantEcho(content, calls));

            if (toolRounds >= maxRounds)
            {
                // 轮次用尽不等于没答案：这一轮已经拿到的工具结果通常够模型回答了。
                // 所以最后再发一次 tool_choice=none 的请求，逼它「只用已有结果收口」。
                return await CloseOutAsync(endpoint, options, messages, toolRounds, traces, cancellationToken).ConfigureAwait(false);
            }

            toolRounds++;
            foreach (var call in calls)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = await _tools.InvokeAsync(call.Name, call.Arguments, cancellationToken).ConfigureAwait(false);
                var trace = new AiToolTrace(toolRounds, call.Name, call.Arguments, result.Json, result.Ok);
                traces.Add(trace);
                onToolTrace?.Invoke(trace);

                messages.Add(new JsonObject
                {
                    ["role"] = "tool",
                    ["tool_call_id"] = call.Id,
                    ["content"] = result.Json,
                });
            }
        }

        return await CloseOutAsync(endpoint, options, messages, toolRounds, traces, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>轮次用尽时的收口：再要一次「不带工具」的最终回答。</summary>
    /// <remarks>
    /// 真实端点上（gemini 系）模型会连着几轮都只回 <c>tool_calls</c>、正文为空；直接返回
    /// <see cref="AiChatOutcome.ToolRoundsExhaustedText"/> 等于把「查到了却没回答」甩给用户。
    /// 改成最后发一次 <c>tool_choice=none</c> 的请求，模型只能拿已有工具结果写正文——这是同一份数据
    /// 下唯一能保证用户看到答案的做法。收口请求失败（网络/超时）不能把整次问答变成异常，
    /// 按用尽文案返回，界面照旧给红字。
    /// </remarks>
    private async Task<AiChatOutcome> CloseOutAsync(
        Uri endpoint,
        AiChatOptions options,
        JsonArray messages,
        int toolRounds,
        List<AiToolTrace> traces,
        CancellationToken cancellationToken)
    {
        const string Fallback = AiChatOutcome.ToolRoundsExhaustedText;

        try
        {
            var reply = await SendAsync(endpoint, options, messages, cancellationToken, "none", CloseOutInstruction).ConfigureAwait(false);
            var text = ReadContent(reply);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return new AiChatOutcome(text!, AiStopReason.Completed, toolRounds, traces);
            }
        }
        catch (AiChatException)
        {
            // 收口这一步失败不该让整次问答报错：工具轨迹已经在，用户能看到查到了什么。
        }

        return new AiChatOutcome(Fallback, AiStopReason.ToolRoundsExhausted, toolRounds, traces);
    }

    /// <summary>收口请求追加的指令：切断了工具，就必须明确告诉模型「用已有结果回答」。</summary>
    private const string CloseOutInstruction =
        "工具调用轮次已经用完。请只根据上面已经拿到的工具结果回答用户，不要再请求工具；" +
        "如果已有结果不足以回答，就说清楚缺什么。";

    private static JsonArray BuildInitialMessages(AiChatOptions options, IReadOnlyList<AiChatMessage>? history)
    {
        var messages = new JsonArray
        {
            new JsonObject
            {
                ["role"] = "system",
                ["content"] = string.IsNullOrWhiteSpace(options.SystemPrompt) ? AiSystemPrompt.Text : options.SystemPrompt,
            },
        };

        if (history is null)
        {
            return messages;
        }

        foreach (var message in history)
        {
            if (string.IsNullOrWhiteSpace(message.Text))
            {
                continue;
            }

            messages.Add(new JsonObject
            {
                ["role"] = message.IsUser ? "user" : "assistant",
                ["content"] = message.Text,
            });
        }

        return messages;
    }

    private async Task<JsonObject> SendAsync(
        Uri endpoint,
        AiChatOptions options,
        JsonArray messages,
        CancellationToken cancellationToken,
        string toolChoice = "auto",
        string? tailInstruction = null)
    {
        var wireMessages = messages.DeepClone().AsArray();
        if (!string.IsNullOrWhiteSpace(tailInstruction))
        {
            wireMessages.Add(new JsonObject
            {
                ["role"] = "system",
                ["content"] = tailInstruction,
            });
        }

        var payload = new JsonObject
        {
            ["model"] = options.Model,
            ["temperature"] = options.Temperature,
            ["stream"] = false,
            ["messages"] = wireMessages,
            ["tools"] = AiToolRunner.BuildToolSchema(),
            ["tool_choice"] = toolChoice,
        };

        // 无 BOM 的 UTF-8 字节：这个端点对非 ASCII 请求体敏感，走字符串 + 默认编码容易踩坑。
        var body = JsonSerializer.SerializeToUtf8Bytes(payload, WireOptions);

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        var content = new ByteArrayContent(body);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
        request.Content = content;

        if (!string.IsNullOrWhiteSpace(options.ApiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey.Trim());
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_requestTimeout);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AiChatException($"请求 AI 端点超时（{_requestTimeout.TotalSeconds:0} 秒）。");
        }
        catch (HttpRequestException ex)
        {
            throw new AiChatException("连不上 AI 端点：" + ex.Message, ex);
        }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new AiChatException($"AI 端点返回 HTTP {(int)response.StatusCode}：{AiText.Truncate(text, 300)}");
            }

            return ParseReply(text);
        }
    }

    private static JsonObject ParseReply(string text)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(text);
        }
        catch (JsonException ex)
        {
            throw new AiChatException("AI 端点返回的不是合法 JSON。", ex);
        }

        if (node is not JsonObject root)
        {
            throw new AiChatException("AI 端点返回的顶层不是 JSON 对象。");
        }

        if (root["error"] is JsonObject error)
        {
            throw new AiChatException("AI 端点报错：" + (error["message"]?.ToString() ?? error.ToJsonString()));
        }

        if (root["choices"] is not JsonArray choices || choices.Count == 0)
        {
            throw new AiChatException("AI 端点返回里没有 choices。");
        }

        if (choices[0] is not JsonObject choice || choice["message"] is not JsonObject message)
        {
            throw new AiChatException("AI 端点返回里没有 choices[0].message。");
        }

        return message;
    }

    private static string? ReadContent(JsonObject message)
    {
        switch (message["content"])
        {
            case JsonValue value when value.GetValueKind() == JsonValueKind.String:
                return value.GetValue<string>();

            // 少数兼容实现把 content 塞成分段数组：拼起来当纯文本用。
            case JsonArray parts:
                var builder = new System.Text.StringBuilder();
                foreach (var part in parts)
                {
                    if (part is JsonObject entry && entry["text"] is JsonValue partText && partText.GetValueKind() == JsonValueKind.String)
                    {
                        builder.Append(partText.GetValue<string>());
                    }
                    else if (part is JsonValue partValue && partValue.GetValueKind() == JsonValueKind.String)
                    {
                        builder.Append(partValue.GetValue<string>());
                    }
                }

                return builder.Length == 0 ? null : builder.ToString();

            default:
                return null;
        }
    }

    private static List<(string Id, string Name, string Arguments)> ReadToolCalls(JsonObject message)
    {
        var calls = new List<(string Id, string Name, string Arguments)>();
        if (message["tool_calls"] is not JsonArray array)
        {
            return calls;
        }

        var index = 0;
        foreach (var item in array)
        {
            index++;
            if (item is not JsonObject call)
            {
                continue;
            }

            var function = call["function"] as JsonObject;
            var name = function?["name"] is JsonValue nameValue && nameValue.GetValueKind() == JsonValueKind.String
                ? nameValue.GetValue<string>()
                : null;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var id = call["id"] is JsonValue idValue ? idValue.ToString() : string.Empty;
            if (string.IsNullOrWhiteSpace(id))
            {
                // 少数兼容实现不回 id：用序号兜底，否则回灌的 role=tool 消息没法配对。
                id = "call_" + index.ToString(CultureInfo.InvariantCulture);
            }

            calls.Add((id, name.Trim(), ReadArguments(function?["arguments"])));
        }

        return calls;
    }

    private static string ReadArguments(JsonNode? arguments)
    {
        if (arguments is null)
        {
            return "{}";
        }

        return arguments.GetValueKind() == JsonValueKind.String
            ? arguments.GetValue<string>()
            : arguments.ToJsonString();
    }

    private static JsonObject BuildAssistantEcho(string? content, IReadOnlyList<(string Id, string Name, string Arguments)> calls)
    {
        var echo = new JsonObject { ["role"] = "assistant" };
        if (content is not null)
        {
            echo["content"] = content;
        }

        var array = new JsonArray();
        foreach (var (id, name, arguments) in calls)
        {
            array.Add(new JsonObject
            {
                ["id"] = id,
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = name,
                    ["arguments"] = arguments,
                },
            });
        }

        echo["tool_calls"] = array;
        return echo;
    }
}
