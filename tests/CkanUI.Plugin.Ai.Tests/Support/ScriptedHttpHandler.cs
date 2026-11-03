using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CkanUI.Plugin.Ai.Tests.Support;

/// <summary>
/// 按脚本逐次回放的假 <see cref="HttpMessageHandler"/>：把「模型这一轮说什么」写成队列，
/// 同时把每次真实发出去的请求体原封不动录下来——测「回灌了什么」靠的就是这份录像。
/// </summary>
internal sealed class ScriptedHttpHandler : HttpMessageHandler
{
    private readonly Queue<string> _scripted;

    /// <summary>创建处理器。</summary>
    /// <param name="replies">按顺序回放的响应体（OpenAI 兼容 JSON）。</param>
    public ScriptedHttpHandler(params string[] replies)
    {
        _scripted = new Queue<string>(replies);
    }

    /// <summary>录下来的请求（按发送顺序）。</summary>
    public List<RecordedRequest> Requests { get; } = new();

    /// <summary>造一个「直接回答」响应。</summary>
    /// <param name="content">助手正文。</param>
    /// <returns>响应 JSON。</returns>
    public static string Reply(string content) => JsonSerializer.Serialize(new
    {
        choices = new[]
        {
            new
            {
                index = 0,
                message = new { role = "assistant", content },
                finish_reason = "stop",
            },
        },
    });

    /// <summary>造一个「要求调工具」响应。</summary>
    /// <param name="id">tool_call id（回灌时必须原样带回）。</param>
    /// <param name="name">工具名。</param>
    /// <param name="argumentsJson">参数 JSON 字符串（模型给的就是字符串）。</param>
    /// <returns>响应 JSON。</returns>
    public static string ToolCallReply(string id, string name, string argumentsJson) => JsonSerializer.Serialize(new
    {
        choices = new[]
        {
            new
            {
                index = 0,
                message = new
                {
                    role = "assistant",
                    content = (string?)null,
                    tool_calls = new[]
                    {
                        new
                        {
                            id,
                            type = "function",
                            function = new { name, arguments = argumentsJson },
                        },
                    },
                },
                finish_reason = "tool_calls",
            },
        },
    });

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var bytes = request.Content is null
            ? Array.Empty<byte>()
            : await request.Content.ReadAsByteArrayAsync(cancellationToken);

        Requests.Add(new RecordedRequest(
            request.RequestUri,
            Encoding.UTF8.GetString(bytes),
            bytes,
            request.Headers.Authorization?.ToString(),
            request.Content?.Headers.ContentType?.MediaType));

        if (_scripted.Count == 0)
        {
            throw new InvalidOperationException("脚本里的响应已经用完：被测代码多发了一次请求。");
        }

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(_scripted.Dequeue(), Encoding.UTF8, "application/json"),
        };
    }
}

/// <summary>一份录下来的请求。</summary>
/// <param name="Uri">请求地址。</param>
/// <param name="Body">请求体（UTF-8 解码后的文本）。</param>
/// <param name="Bytes">请求体原始字节（用来验没有 BOM）。</param>
/// <param name="Authorization">Authorization 头。</param>
/// <param name="ContentType">Content-Type 的媒体类型。</param>
internal sealed record RecordedRequest(Uri? Uri, string Body, byte[] Bytes, string? Authorization, string? ContentType);
