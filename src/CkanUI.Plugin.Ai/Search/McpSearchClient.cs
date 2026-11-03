using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;

namespace CkanUI.Plugin.Ai.Search;

/// <summary>
/// 通过 stdio 起一个 MCP 服务器进程，用 JSON-RPC 2.0（换行分帧）调它的 <c>tools/call</c>。
/// </summary>
/// <remarks>
/// <para><b>为什么自己写而不是引 SDK</b>：插件只允许依赖契约层 + Avalonia，多一个 MCP SDK 就是多一条
/// 依赖链；而这里需要的协议面很小——<c>initialize</c> → <c>notifications/initialized</c> → <c>tools/call</c>，
/// 三帧 JSON，不值得为此引包。</para>
/// <para><b>进程生命周期</b>：按需启动（第一次搜索时才 <see cref="Process.Start()"/>），
/// 空闲 <see cref="DefaultIdleTimeout"/> 后自动退出，<see cref="Dispose"/> 时 Kill 整棵进程树。
/// 一个聊天页留着常驻子进程，用户关掉页面后它还在吃内存——这是不可接受的。</para>
/// <para><b>降级纪律</b>：<see cref="SearchAsync"/> 永不抛异常。MCP 服务器没装、脚本路径写错、进程秒退、
/// 返回结构奇形怪状，全部收敛成 <see cref="WebSearchResult.Unavailable"/>，让模型自己降级成「没联网查到」。</para>
/// <para><b>解析纪律</b>：MCP 的 <c>tools/call</c> 结果没有强制的业务结构（content 里通常是一段 text，
/// 也可能有 structuredContent），所以这里做的是宽松解析：能认出 title/url/snippet 就认，认不出就当摘要文本。</para>
/// </remarks>
public sealed class McpSearchClient : IWebSearchTool, IDisposable
{
    /// <summary>空闲多久自动回收子进程（默认 5 分钟）。</summary>
    public static readonly TimeSpan DefaultIdleTimeout = TimeSpan.FromMinutes(5);

    /// <summary>单次 JSON-RPC 请求的等待上限。</summary>
    public static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(30);

    /// <summary>MCP 协议版本（客户端自报；服务器不接受会自己回它支持的版本）。</summary>
    private const string ProtocolVersion = "2024-11-05";

    /// <summary>宽松解析时按这些键名找「装结果的数组/对象」。</summary>
    private static readonly string[] ResultKeys =
    {
        "content", "structuredContent", "results", "items", "data", "hits", "documents", "entries", "pages", "text",
    };

    private static readonly JsonSerializerOptions WireOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly string _command;
    private readonly IReadOnlyList<string> _args;
    private readonly string _toolName;
    private readonly TimeSpan _idleTimeout;
    private readonly TimeSpan _requestTimeout;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _processGate = new();

    private Process? _process;
    private StreamWriter? _stdin;
    private StreamReader? _stdout;
    private Timer? _idleTimer;
    private int _nextId;
    private bool _disposed;

    /// <summary>创建客户端（此时不启动进程）。</summary>
    /// <param name="command">MCP 服务器命令，如 <c>node</c>。</param>
    /// <param name="args">命令参数（脚本路径等）。</param>
    /// <param name="toolName">要调的 MCP 工具名，默认 <c>search</c>。</param>
    /// <param name="idleTimeout">空闲回收时长；<c>null</c> 用 <see cref="DefaultIdleTimeout"/>。</param>
    /// <param name="requestTimeout">单次请求超时；<c>null</c> 用 <see cref="DefaultRequestTimeout"/>。</param>
    public McpSearchClient(
        string? command,
        IReadOnlyList<string>? args = null,
        string toolName = "search",
        TimeSpan? idleTimeout = null,
        TimeSpan? requestTimeout = null)
    {
        _command = (command ?? string.Empty).Trim();
        _args = args ?? Array.Empty<string>();
        _toolName = string.IsNullOrWhiteSpace(toolName) ? "search" : toolName.Trim();
        _idleTimeout = idleTimeout is { } idle && idle > TimeSpan.Zero ? idle : DefaultIdleTimeout;
        _requestTimeout = requestTimeout is { } request && request > TimeSpan.Zero ? request : DefaultRequestTimeout;
    }

    /// <summary>当前是否已经起过子进程（诊断/测试用）。</summary>
    public bool IsRunning
    {
        get
        {
            lock (_processGate)
            {
                return _process is { HasExited: false };
            }
        }
    }

    /// <inheritdoc />
    public async Task<WebSearchResult> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return WebSearchResult.Unavailable("MCP 搜索客户端已释放。");
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            return WebSearchResult.Unavailable("缺少查询词。");
        }

        if (_command.Length == 0)
        {
            return WebSearchResult.Unavailable("设置里的 MCP 命令为空：先在 AI 助手的设置面板填命令与脚本路径。");
        }

        try
        {
            if (!await _gate.WaitAsync(_requestTimeout, cancellationToken).ConfigureAwait(false))
            {
                return WebSearchResult.Unavailable("MCP 搜索正忙（上一次调用还没返回）。");
            }
        }
        catch (OperationCanceledException)
        {
            return WebSearchResult.Unavailable("MCP 搜索已取消。");
        }

        try
        {
            await EnsureStartedAsync(cancellationToken).ConfigureAwait(false);
            var reply = await CallAsync(
                "tools/call",
                new JsonObject
                {
                    ["name"] = _toolName,
                    ["arguments"] = new JsonObject { ["query"] = query },
                },
                cancellationToken).ConfigureAwait(false);

            return reply.Ok
                ? ParseHits(reply.Result)
                : WebSearchResult.Unavailable(reply.Error ?? "MCP tools/call 失败。");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            StopProcess();
            return WebSearchResult.Unavailable("MCP 搜索已取消。");
        }
        catch (OperationCanceledException)
        {
            // 走到这里说明是 _requestTimeout 触发的：进程可能卡在半帧上，杀掉重来比留着一个僵住的孩子强。
            StopProcess();
            return WebSearchResult.Unavailable($"MCP 服务器 {_requestTimeout.TotalSeconds:0} 秒内没有响应。");
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or ObjectDisposedException or JsonException or System.ComponentModel.Win32Exception)
        {
            StopProcess();
            return WebSearchResult.Unavailable("MCP 搜索不可用：" + ex.Message);
        }
        finally
        {
            TouchIdleTimer();
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Timer? timer;
        lock (_processGate)
        {
            timer = _idleTimer;
            _idleTimer = null;
        }

        timer?.Dispose();
        StopProcess();
    }

    /// <summary>起进程并完成 MCP 握手（幂等：进程还活着就直接返回）。</summary>
    private async Task EnsureStartedAsync(CancellationToken cancellationToken)
    {
        if (IsRunning)
        {
            return;
        }

        StopProcess();

        var startInfo = new ProcessStartInfo(_command)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardInputEncoding = Encoding.UTF8,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var arg in _args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动 MCP 服务器进程：" + _command);

        // stderr 必须被消费掉：重定向却不读，管道填满后子进程会卡死在写日志上。
        process.ErrorDataReceived += static (_, _) => { };
        process.BeginErrorReadLine();

        var stdin = process.StandardInput;
        stdin.AutoFlush = true;

        lock (_processGate)
        {
            _process = process;
            _stdin = stdin;
            _stdout = process.StandardOutput;
        }

        var init = await CallAsync(
            "initialize",
            new JsonObject
            {
                ["protocolVersion"] = ProtocolVersion,
                ["capabilities"] = new JsonObject(),
                ["clientInfo"] = new JsonObject
                {
                    ["name"] = "ckan-ui-ai-assistant",
                    ["version"] = "1.0.0",
                },
            },
            cancellationToken).ConfigureAwait(false);

        if (!init.Ok)
        {
            throw new InvalidOperationException("MCP initialize 失败：" + (init.Error ?? "未知原因"));
        }

        await NotifyAsync("notifications/initialized", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>发一帧请求并等同一 id 的响应。</summary>
    private async Task<McpReply> CallAsync(string method, JsonObject parameters, CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref _nextId);
        var request = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["method"] = method,
            ["params"] = parameters,
        };

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_requestTimeout);

        await WriteLineAsync(request.ToJsonString(WireOptions), timeout.Token).ConfigureAwait(false);

        var expected = id.ToString(CultureInfo.InvariantCulture);
        while (true)
        {
            var line = await ReadLineAsync(timeout.Token).ConfigureAwait(false);
            if (line is null)
            {
                throw new InvalidOperationException("MCP 服务器进程已退出。");
            }

            if (line.Length == 0)
            {
                continue;
            }

            JsonNode? node;
            try
            {
                node = JsonNode.Parse(line);
            }
            catch (JsonException)
            {
                // 服务器把自己的日志打到 stdout 是常见现象：跳过不可解析的行，继续等真正的响应。
                continue;
            }

            if (node is not JsonObject message || message["id"]?.ToString() != expected)
            {
                continue;
            }

            if (message["error"] is JsonObject error)
            {
                var errorText = error["message"]?.ToString();
                return new McpReply(false, null, string.IsNullOrWhiteSpace(errorText) ? "MCP 调用返回错误。" : errorText);
            }

            return new McpReply(true, message["result"], null);
        }
    }

    /// <summary>发一帧通知（不需要响应）。</summary>
    private Task NotifyAsync(string method, CancellationToken cancellationToken)
    {
        var notification = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = method,
        };
        return WriteLineAsync(notification.ToJsonString(WireOptions), cancellationToken);
    }

    private async Task WriteLineAsync(string line, CancellationToken cancellationToken)
    {
        var stdin = _stdin ?? throw new InvalidOperationException("MCP 服务器未启动。");
        await stdin.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
        await stdin.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        var stdout = _stdout ?? throw new InvalidOperationException("MCP 服务器未启动。");
        return await stdout.ReadLineAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>把 <c>tools/call</c> 的 result 压成 [title, url, snippet] 列表。</summary>
    private static WebSearchResult ParseHits(JsonNode? result)
    {
        if (result is null)
        {
            return WebSearchResult.Success(Array.Empty<WebSearchHit>());
        }

        if (result is JsonObject root
            && root["isError"] is JsonValue flag
            && flag.TryGetValue<bool>(out var isError)
            && isError)
        {
            return WebSearchResult.Unavailable("MCP 工具返回错误：" + (FirstContentText(root) ?? "无详情"));
        }

        var hits = new List<WebSearchHit>();
        Collect(result, hits, depth: 0);
        return WebSearchResult.Success(hits);
    }

    /// <summary>宽松遍历：数组摊平、对象认 title/url/snippet、字符串当 JSON 或纯文本。</summary>
    private static void Collect(JsonNode? node, List<WebSearchHit> hits, int depth)
    {
        if (node is null || depth > 4)
        {
            return;
        }

        switch (node)
        {
            case JsonArray array:
                foreach (var item in array)
                {
                    Collect(item, hits, depth + 1);
                }

                break;

            case JsonObject obj:
                if (BuildHit(obj) is { } hit)
                {
                    hits.Add(hit);
                    break;
                }

                foreach (var key in ResultKeys)
                {
                    if (obj[key] is not { } child)
                    {
                        continue;
                    }

                    Collect(child, hits, depth + 1);
                    if (hits.Count > 0)
                    {
                        break;
                    }
                }

                break;

            case JsonValue value:
                if (value.GetValueKind() != JsonValueKind.String)
                {
                    break;
                }

                var text = value.GetValue<string>();
                if (string.IsNullOrWhiteSpace(text))
                {
                    break;
                }

                if (TryParseJson(text) is { } parsed)
                {
                    Collect(parsed, hits, depth + 1);
                }
                else
                {
                    hits.Add(new WebSearchHit(string.Empty, string.Empty, text.Trim()));
                }

                break;
        }
    }

    /// <summary>把一个对象认成一条命中；认不出返回 <c>null</c>（调用方继续往下钻）。</summary>
    private static WebSearchHit? BuildHit(JsonObject obj)
    {
        var url = FirstString(obj, "url", "link", "href", "uri", "source");
        var title = FirstString(obj, "title", "name", "heading", "label");
        var snippet = FirstString(obj, "snippet", "description", "summary", "abstract", "text", "content", "body");

        // 只有 url 一个字段也值得回给模型（它要靠链接说话）；纯标题没有出处等于噪声。
        if (string.IsNullOrWhiteSpace(url) && string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(url) && string.IsNullOrWhiteSpace(snippet))
        {
            return null;
        }

        return new WebSearchHit(title ?? string.Empty, url ?? string.Empty, snippet ?? string.Empty);
    }

    private static string? FirstString(JsonObject obj, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (obj[key] is JsonValue value && value.GetValueKind() == JsonValueKind.String)
            {
                var text = value.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text.Trim();
                }
            }
        }

        return null;
    }

    private static string? FirstContentText(JsonObject root)
    {
        if (root["content"] is not JsonArray content)
        {
            return null;
        }

        var builder = new StringBuilder();
        foreach (var item in content)
        {
            if (item is JsonObject entry && FirstString(entry, "text") is { } text)
            {
                if (builder.Length > 0)
                {
                    builder.Append('\n');
                }

                builder.Append(text);
            }
        }

        return builder.Length == 0 ? null : Chat.AiText.Truncate(builder.ToString(), 300);
    }

    private static JsonNode? TryParseJson(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0 || (trimmed[0] != '{' && trimmed[0] != '['))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(trimmed);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>重置空闲计时器：每次用完都重新计时。</summary>
    private void TouchIdleTimer()
    {
        if (_disposed)
        {
            return;
        }

        lock (_processGate)
        {
            if (_idleTimer is null)
            {
                _idleTimer = new Timer(static state => ((McpSearchClient)state!).StopProcess(), this, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            }

            _idleTimer.Change(_idleTimeout, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>杀掉子进程并清空句柄（可重复调用）。</summary>
    private void StopProcess()
    {
        Process? process;
        lock (_processGate)
        {
            process = _process;
            _process = null;
            _stdin = null;
            _stdout = null;
        }

        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception or AggregateException)
        {
            // 进程已经自己退了 / 已经被别人杀了：没有可补救的，继续走清理。
        }

        try
        {
            process.Dispose();
        }
        catch (InvalidOperationException)
        {
            // 同上：Dispose 失败不影响下一个实例。
        }
    }

    /// <summary>JSON-RPC 响应（成功带 result，失败带 error.message）。</summary>
    private sealed record McpReply(bool Ok, JsonNode? Result, string? Error);
}
