using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;

using CkanUI.Plugin.Ai.Search;
using CkanUI.Services.Abstractions.Ai;

namespace CkanUI.Plugin.Ai.Chat;

/// <summary>
/// 把模型发来的 <c>tool_calls</c> 变成真实的宿主查询，并把结果压成紧凑 JSON。
/// </summary>
/// <remarks>
/// <para><b>绝不抛</b>：AI 是外部输入，工具名拼错、参数是坏 JSON、页面键不存在都是正常故障，
/// 一律收敛成 <c>{"ok":false,...}</c> 回给模型，让它自己换个办法——抛异常只会让用户看到一句红字，
/// 而模型连「我错在哪」都不知道。</para>
/// <para><b>紧凑</b>：CKAN 的 abstract/description 里混着 BBCode 与 HTML，整段回给模型既贵又容易跑偏；
/// 这里统一走 <see cref="AiText.Compact"/> 压成单行并截断。</para>
/// <para><b>只读 + 一个动作</b>：六个 <c>ckan_*</c> 工具里只有 <c>ckan_navigate</c> 会动界面，
/// 其余都读快照；失败也照实回给模型（导航失败不是异常，是「这个页面键不存在」这条信息）。</para>
/// </remarks>
public sealed class AiToolRunner
{
    /// <summary>工具名，顺序与下发给模型的 <c>tools</c> 数组一致。</summary>
    public static readonly string[] ToolNames =
    {
        "ckan_context",
        "ckan_search_mods",
        "ckan_mod_detail",
        "ckan_installed",
        "ckan_list_pages",
        "ckan_navigate",
        "web_search",
    };

    private const int MaxSearchResults = 20;
    private const int MaxInstalledRows = 200;
    private const int MaxWebResults = 8;
    private const int AbstractChars = 240;
    private const int DescriptionChars = 800;

    private static readonly JsonSerializerOptions PayloadOptions = new()
    {
        // 中文原样输出：\u4e2d\u6587 会让每段结果膨胀一倍，还更难读。
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly (string Name, string Description, string Parameters)[] ToolSpecs =
    {
        (
            "ckan_context",
            "读取当前 KSP 实例与仓库索引的概况：实例名、游戏目录、KSP 版本、已安装模组数、可选模组数、索引更新时间与是否来自缓存。无参数。",
            """{"type":"object","properties":{},"additionalProperties":false}"""),
        (
            "ckan_search_mods",
            "按关键词搜索模组仓库（匹配标识符/名称/作者/摘要/标签，按相关度与下载量排序）。返回带标识符的候选列表。",
            """{"type":"object","properties":{"query":{"type":"string","description":"关键词；留空表示按下载量取热门"},"limit":{"type":"integer","description":"返回条数，1-20，默认 10"}},"additionalProperties":false}"""),
        (
            "ckan_mod_detail",
            "取一个模组的详情：作者、摘要、描述、许可证、主页、已装版本、最新版本、下载量、标签、依赖/推荐/冲突。",
            """{"type":"object","properties":{"identifier":{"type":"string","description":"模组标识符，如 ModuleManager"}},"required":["identifier"],"additionalProperties":false}"""),
        (
            "ckan_installed",
            "列出当前实例已安装的模组（标识符、版本、是否自动安装）。",
            """{"type":"object","properties":{"limit":{"type":"integer","description":"返回条数，1-200，默认 50"}},"additionalProperties":false}"""),
        (
            "ckan_list_pages",
            "列出可以导航过去的页面键与标题（配合 ckan_navigate 使用）。无参数。",
            """{"type":"object","properties":{},"additionalProperties":false}"""),
        (
            "ckan_navigate",
            "在 CKAN UI 里打开一个页面。只能用在 ckan_list_pages 里出现过的页面键。",
            """{"type":"object","properties":{"page_key":{"type":"string","description":"页面键，如 mods / instances / settings"}},"required":["page_key"],"additionalProperties":false}"""),
        (
            "web_search",
            "联网搜索网页（MCP 搜索服务器）。用于查最新版本、公告、论坛帖等仓库里没有的信息。不可用时会返回结构化错误，请据此降级。",
            """{"type":"object","properties":{"query":{"type":"string","description":"搜索词"}},"required":["query"],"additionalProperties":false}"""),
    };

    private static readonly JsonArray ToolSchema = BuildToolSchemaCore();

    private readonly IAiCkanBridge? _bridge;
    private readonly IWebSearchTool? _search;

    /// <summary>创建执行器。</summary>
    /// <param name="bridge">宿主桥；<c>null</c> 时所有 <c>ckan_*</c> 工具都回「宿主未连接」的结构化错误。</param>
    /// <param name="search">网页搜索；<c>null</c> 时 <c>web_search</c> 回 <c>web_search_unavailable</c>。</param>
    public AiToolRunner(IAiCkanBridge? bridge, IWebSearchTool? search = null)
    {
        _bridge = bridge;
        _search = search;
    }

    /// <summary>给 <c>/chat/completions</c> 的 <c>tools</c> 数组（每次返回一份深拷贝，避免共享可变节点）。</summary>
    /// <returns>OpenAI 兼容的工具声明数组。</returns>
    public static JsonArray BuildToolSchema() => (JsonArray)ToolSchema.DeepClone();

    /// <summary>执行一次工具调用。</summary>
    /// <param name="name">工具名。</param>
    /// <param name="argumentsJson">模型给的参数（JSON 字符串，可能是坏 JSON）。</param>
    /// <param name="cancellationToken">取消令牌（用户点「停止」时中断导航等操作）。</param>
    /// <returns>紧凑 JSON；永不抛异常。</returns>
    public async Task<AiToolResult> InvokeAsync(string? name, string? argumentsJson, CancellationToken cancellationToken = default)
    {
        var tool = (name ?? string.Empty).Trim();
        if (tool.Length == 0)
        {
            return Failure("missing_tool_name", "工具调用里没有函数名。");
        }

        if (!ToolNames.Contains(tool, StringComparer.Ordinal))
        {
            return Failure("unknown_tool", $"没有名为 {tool} 的工具。", new { available_tools = ToolNames });
        }

        if (!TryParseArguments(argumentsJson, out var args, out var argumentError))
        {
            return Failure("bad_arguments", argumentError);
        }

        if (tool != "web_search" && _bridge is null)
        {
            return Failure("bridge_unavailable", "AI 助手没有连接到宿主（缺少 IAiCkanBridge 实现）。");
        }

        try
        {
            return tool switch
            {
                "ckan_context" => Ok(ContextPayload()),
                "ckan_search_mods" => Ok(SearchModsPayload(args)),
                "ckan_mod_detail" => DetailResult(args),
                "ckan_installed" => Ok(InstalledPayload(args)),
                "ckan_list_pages" => Ok(PagesPayload()),
                "ckan_navigate" => await NavigateAsync(args, cancellationToken).ConfigureAwait(false),
                _ => await WebSearchAsync(args, cancellationToken).ConfigureAwait(false),
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Failure("tool_failed", ex.Message);
        }
    }

    private object ContextPayload()
    {
        var context = _bridge!.GetContext();
        return new
        {
            ok = true,
            instance = context.InstanceName,
            game_dir = context.GameDir,
            ksp_version = context.KspVersion,
            installed_count = context.InstalledCount,
            available_count = context.AvailableCount,
            repository_updated_at = context.RepositoryUpdatedAt?.ToString("O", CultureInfo.InvariantCulture),
            repository_from_cache = context.RepositoryFromCache,
        };
    }

    private object SearchModsPayload(JsonElement args)
    {
        var query = GetString(args, "query");
        var limit = GetInt(args, "limit", 10, 1, MaxSearchResults);
        var mods = _bridge!.SearchMods(query, limit) ?? Array.Empty<AiModSummary>();
        return new
        {
            ok = true,
            query = string.IsNullOrWhiteSpace(query) ? null : query,
            count = mods.Count,
            mods = mods.Select(mod => new
            {
                id = mod.Identifier,
                name = mod.Name,
                author = mod.Author,
                summary = AiText.Compact(mod.Abstract, AbstractChars),
                installed = mod.InstalledVersion,
                latest = mod.LatestVersion,
                downloads = mod.DownloadCount,
                compatible = mod.IsCompatible,
                has_update = mod.HasUpdate,
                tags = mod.Tags,
            }),
        };
    }

    private AiToolResult DetailResult(JsonElement args)
    {
        var identifier = GetString(args, "identifier");
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return Failure("missing_identifier", "缺少 identifier；先用 ckan_search_mods 拿到标识符。");
        }

        var detail = _bridge!.GetModDetail(identifier);
        if (detail is null)
        {
            return Failure("mod_not_found", $"仓库里没有标识符为 {identifier} 的模组。");
        }

        return Ok(new
        {
            ok = true,
            id = detail.Identifier,
            name = detail.Name,
            author = detail.Author,
            summary = AiText.Compact(detail.Abstract, AbstractChars),
            description = AiText.Compact(detail.Description, DescriptionChars),
            licenses = detail.Licenses,
            homepage = detail.Homepage,
            installed = detail.InstalledVersion,
            latest = detail.LatestVersion,
            downloads = detail.DownloadCount,
            tags = detail.Tags,
            compatible = detail.IsCompatible,
            depends_on = detail.DependsOn,
            recommends = detail.Recommends,
            conflicts_with = detail.ConflictsWith,
        });
    }

    private object InstalledPayload(JsonElement args)
    {
        var limit = GetInt(args, "limit", 50, 1, MaxInstalledRows);
        var mods = _bridge!.ListInstalled(limit) ?? Array.Empty<AiInstalledMod>();
        return new
        {
            ok = true,
            count = mods.Count,
            mods = mods.Select(mod => new
            {
                id = mod.Identifier,
                version = mod.Version,
                auto = mod.IsAutoInstalled,
            }),
        };
    }

    private object PagesPayload()
    {
        var pages = _bridge!.ListPages() ?? Array.Empty<AiPageRef>();
        return new
        {
            ok = true,
            count = pages.Count,
            pages = pages.Select(page => new { key = page.Key, title = page.Title }),
        };
    }

    private async Task<AiToolResult> NavigateAsync(JsonElement args, CancellationToken cancellationToken)
    {
        var pageKey = GetString(args, "page_key");
        if (string.IsNullOrWhiteSpace(pageKey))
        {
            return Failure("missing_page_key", "缺少 page_key；先调 ckan_list_pages 拿到可用页面键。");
        }

        var result = await _bridge!.NavigateAsync(pageKey, cancellationToken).ConfigureAwait(false);
        if (result.IsSuccess)
        {
            return Ok(new { ok = true, page_key = pageKey });
        }

        // 导航失败不是异常，是信息：把原因原样交给模型，让它换页面键或如实告诉用户。
        return new AiToolResult(
            false,
            JsonSerializer.Serialize(
                new
                {
                    ok = false,
                    error = "navigate_failed",
                    page_key = pageKey,
                    message = result.Message ?? "导航失败",
                    detail = result.Detail,
                },
                PayloadOptions));
    }

    private async Task<AiToolResult> WebSearchAsync(JsonElement args, CancellationToken cancellationToken)
    {
        var query = GetString(args, "query");
        if (string.IsNullOrWhiteSpace(query))
        {
            return Failure("missing_query", "web_search 需要 query 参数。");
        }

        if (_search is null)
        {
            return WebSearchUnavailable("没有配置 MCP 搜索服务器（设置里的 MCP 命令为空）。", query);
        }

        var result = await _search.SearchAsync(query, cancellationToken).ConfigureAwait(false);
        if (!result.Ok)
        {
            return WebSearchUnavailable(result.Error ?? "MCP 搜索服务器不可用。", query);
        }

        var hits = result.Hits.Take(MaxWebResults).Select(hit => new
        {
            title = AiText.Compact(hit.Title, AbstractChars),
            url = hit.Url,
            snippet = AiText.Compact(hit.Snippet, AbstractChars),
        }).ToList();

        return Ok(new { ok = true, query, count = hits.Count, results = hits });
    }

    private static AiToolResult Ok(object payload) => new(true, JsonSerializer.Serialize(payload, PayloadOptions));

    private static AiToolResult Failure(string error, string message, object? extra = null)
    {
        var payload = new JsonObject
        {
            ["ok"] = false,
            ["error"] = error,
            ["message"] = message,
        };
        if (extra is not null)
        {
            payload["details"] = JsonSerializer.SerializeToNode(extra, PayloadOptions);
        }

        return new AiToolResult(false, payload.ToJsonString(PayloadOptions));
    }

    /// <summary>
    /// 网页搜索不可用时给模型的结构化错误：模型据此降级成「没联网查到」，而不是瞎编。
    /// </summary>
    private static AiToolResult WebSearchUnavailable(string detail, string? query)
    {
        var payload = new JsonObject
        {
            ["ok"] = false,
            ["error"] = "web_search_unavailable",
            ["hint"] = "网页搜索不可用：不要假装查过。请改用 ckan_* 工具查本地仓库数据，并在回答里说明这些结论没联网核实。",
            ["detail"] = detail,
        };
        if (!string.IsNullOrWhiteSpace(query))
        {
            payload["query"] = query;
        }

        return new AiToolResult(false, payload.ToJsonString(PayloadOptions));
    }

    private static bool TryParseArguments(string? json, out JsonElement args, out string error)
    {
        args = default;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(json))
        {
            // 无参数工具（ckan_context / ckan_list_pages）的 arguments 常是 "{}" 或空串。
            return true;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                error = "工具参数必须是 JSON 对象。";
                return false;
            }

            args = document.RootElement.Clone();
            return true;
        }
        catch (JsonException ex)
        {
            error = "工具参数不是合法 JSON：" + ex.Message;
            return false;
        }
    }

    private static string? GetString(JsonElement args, string name)
    {
        if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty(name, out var value))
        {
            return null;
        }

        var text = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.ToString(),
            _ => null,
        };

        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private static int GetInt(JsonElement args, string name, int fallback, int min, int max)
    {
        if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty(name, out var value))
        {
            return fallback;
        }

        var parsed = value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var number) => number,
            JsonValueKind.String when int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var text) => text,
            _ => fallback,
        };

        return Math.Clamp(parsed, min, max);
    }

    private static JsonArray BuildToolSchemaCore()
    {
        var tools = new JsonArray();
        foreach (var (name, description, parameters) in ToolSpecs)
        {
            tools.Add(new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = name,
                    ["description"] = description,
                    ["parameters"] = JsonNode.Parse(parameters),
                },
            });
        }

        return tools;
    }
}
