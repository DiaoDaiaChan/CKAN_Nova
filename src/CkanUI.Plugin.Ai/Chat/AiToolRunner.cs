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
/// <para><b>只读 + 四个动作</b>：会改变状态的只有 <c>ckan_navigate</c>、<c>ckan_install_mod</c>、
/// <c>ckan_run_page_action</c>（等价于点按钮，有副作用的命令由宿主弹确认框）与
/// <c>ckan_set_ui_field</c>（等价于往输入框里打字）；
/// 其余都读快照。失败也照实回给模型（导航失败不是异常，是「这个页面键不存在」这条信息；
/// 用户拒绝安装也不是错误，是 <c>applied=false</c> 这条信息）。</para>
/// <para><b>页面面不是清单</b>：<c>ckan_ui_state</c> / <c>ckan_page_actions</c> / <c>ckan_run_page_action</c>
/// 走宿主反射，新增页面或按钮不需要改这个文件——手写清单漏一项，表现是「AI 说它做不到」，
/// 而没有任何编译错误会提醒你。</para>
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
            "ckan_ui_state",
            "读取当前界面状态：在哪一页、这一页的可读字段（搜索词、选中项、计数、开关等）以及可执行命令。改完界面后用这个自证变化，不要凭 ckan_navigate 的 ok 就宣布成功。无参数。",
            """{"type":"object","properties":{},"additionalProperties":false}"""),
        (
            "ckan_page_actions",
            "枚举某一页上全部可执行命令（等价于界面上能点的按钮），含当前是否可点与参数类型。默认当前页，也可指定 page_key（不会导航）。",
            """{"type":"object","properties":{"page_key":{"type":"string","description":"页面键；留空表示当前页"}},"additionalProperties":false}"""),
        (
            "ckan_run_page_action",
            "执行某一页上的一个命令，等价于替用户点那个按钮。命令名来自 ckan_page_actions；有副作用的命令会先弹确认框。",
            """{"type":"object","properties":{"action":{"type":"string","description":"命令属性名，如 ApplyCommand；Command 后缀可省"},"argument":{"type":"string","description":"命令参数；无参命令留空"},"page_key":{"type":"string","description":"页面键；留空表示当前页，指定别的页会先导航过去"}},"required":["action"],"additionalProperties":false}"""),
        (
            "ckan_set_ui_field",
            "给当前页面上的一个可写字段赋值，等价于替用户在输入框/下拉框里改值（搜索词、过滤词、开关等）。字段名来自 ckan_ui_state；只支持文本/布尔/数字/枚举/时间。",
            """{"type":"object","properties":{"field":{"type":"string","description":"字段名，来自 ckan_ui_state"},"value":{"type":"string","description":"新值；布尔用 true/false，枚举用名字"},"page_key":{"type":"string","description":"页面键；留空表示当前页，指定别的页会先导航过去"}},"required":["field","value"],"additionalProperties":false}"""),
        (
            "ckan_settings",
            "列出全部可写界面设置项及当前值（含枚举取值）。ckan_set_setting 的 key 必须来自这里。无参数。",
            """{"type":"object","properties":{},"additionalProperties":false}"""),
        (
            "ckan_navigate",
            "在 CKAN UI 里打开一个页面。只能用在 ckan_list_pages 里出现过的页面键。",
            """{"type":"object","properties":{"page_key":{"type":"string","description":"页面键，如 mods / instances / settings"}},"required":["page_key"],"additionalProperties":false}"""),
        (
            "ckan_install_mod",
            "安装或升级一个模组。会先算出依赖与冲突，再弹确认框让用户点头；用户拒绝时不会动磁盘。只在用户明确要求安装某个模组时调用，不要用搜索结果自行决定安装。",
            """{"type":"object","properties":{"identifier":{"type":"string","description":"模组标识符，如 ModuleManager"}},"required":["identifier"],"additionalProperties":false}"""),
        (
            "ckan_set_theme",
            "切换界面浅色/深色主题或皮肤风格。例如主题设为 'dark' 或 'light'，皮肤可选 'default', 'coronal-gold', 'moon-graphite', 'celadon', 'bronze-gold', 'nebula-purple', 'monochrome-silver'。",
            """{"type":"object","properties":{"theme":{"type":"string","description":"主题模式：dark / light / system"},"skin":{"type":"string","description":"深色皮肤风格，如 coronal-gold / moon-graphite / celadon / bronze-gold / nebula-purple / monochrome-silver"}},"additionalProperties":false}"""),
        (
            "ckan_filter_mods",
            "联动模组页面的搜索和过滤。可自动跳转到模组页，并填入搜索词、更新筛选分类（如 compatible / installed / upgradeable / all 等）。",
            """{"type":"object","properties":{"query":{"type":"string","description":"搜索关键字"},"filter":{"type":"string","description":"过滤类别：compatible / installed / upgradeable / not_installed / cached / all"}},"additionalProperties":false}"""),
        (
            "ckan_switch_instance",
            "切换当前活动的 KSP 游戏实例。需提供目标实例名称。",
            """{"type":"object","properties":{"name":{"type":"string","description":"目标实例名称"}},"required":["name"],"additionalProperties":false}"""),
        (
            "ckan_launch_game",
            "拉起并启动当前选中的 KSP 实例游戏进程。无参数。",
            """{"type":"object","properties":{},"additionalProperties":false}"""),
        (
            "ckan_set_setting",
            "更新界面设置项。key 用 ckan_settings 返回的属性名（如 Theme / Density / Locale / SoundEffectsEnabled），value 传文本，布尔用 true/false。",
            """{"type":"object","properties":{"key":{"type":"string","description":"设置项名，来自 ckan_settings"},"value":{"type":"string","description":"新值"}},"required":["key","value"],"additionalProperties":false}"""),
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
                "ckan_ui_state" => Ok(UiStatePayload()),
                "ckan_page_actions" => Ok(PageActionsPayload(args)),
                "ckan_run_page_action" => await RunPageActionAsync(args, cancellationToken).ConfigureAwait(false),
                "ckan_set_ui_field" => await SetUiFieldAsync(args, cancellationToken).ConfigureAwait(false),
                "ckan_settings" => Ok(SettingsPayload()),
                "ckan_navigate" => await NavigateAsync(args, cancellationToken).ConfigureAwait(false),
                "ckan_install_mod" => await InstallAsync(args, cancellationToken).ConfigureAwait(false),
                "ckan_set_theme" => await SetThemeAsync(args, cancellationToken).ConfigureAwait(false),
                "ckan_filter_mods" => await FilterModsAsync(args, cancellationToken).ConfigureAwait(false),
                "ckan_switch_instance" => await SwitchInstanceAsync(args, cancellationToken).ConfigureAwait(false),
                "ckan_launch_game" => await LaunchGameAsync(cancellationToken).ConfigureAwait(false),
                "ckan_set_setting" => await SetSettingAsync(args, cancellationToken).ConfigureAwait(false),
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

    private async Task<AiToolResult> InstallAsync(JsonElement args, CancellationToken cancellationToken)
    {
        var identifier = GetString(args, "identifier");
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return Failure("bad_arguments", "缺少 identifier。");
        }

        try
        {
            var result = await _bridge!.InstallAsync(identifier, cancellationToken).ConfigureAwait(false);
            if (!result.IsSuccess || result.Value is null)
            {
                return Failure("install_failed", result.Message ?? "安装失败。");
            }

            var outcome = result.Value;
            if (!outcome.Applied)
            {
                // 用户拒绝不是错误：模型要能如实说「没装」，而不是把拒绝包装成成功或异常。
                return Ok(new
                {
                    ok = true,
                    applied = false,
                    identifier = outcome.Identifier,
                    summary = outcome.Summary,
                });
            }

            return Ok(new
            {
                ok = true,
                applied = true,
                identifier = outcome.Identifier,
                summary = outcome.Summary,
                installed = outcome.Installed,
                removed = outcome.Removed,
                failed = outcome.Failed,
                warnings = outcome.Warnings,
            });
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Failure("install_failed", ex.Message);
        }
    }

    private async Task<AiToolResult> SetThemeAsync(JsonElement args, CancellationToken cancellationToken)
    {
        var theme = GetString(args, "theme");
        var skin = GetString(args, "skin");
        if (string.IsNullOrWhiteSpace(theme) && string.IsNullOrWhiteSpace(skin))
        {
            return Failure("bad_arguments", "需提供 theme（如 dark/light）或 skin（深色皮肤风格，如 coronal-gold）参数。");
        }

        var result = await _bridge!.SetThemeAsync(theme, skin, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess
            ? Ok(new { ok = true, theme, skin, message = result.Message ?? "已应用主题设置。" })
            : Failure("set_theme_failed", result.Message ?? "主题切换失败。");
    }

    private async Task<AiToolResult> FilterModsAsync(JsonElement args, CancellationToken cancellationToken)
    {
        var query = GetString(args, "query");
        var filter = GetString(args, "filter");
        if (query is null && filter is null)
        {
            return Failure("bad_arguments", "需提供搜索词 query 或筛选类别 filter（如 compatible/installed 等）。");
        }

        var result = await _bridge!.FilterModsAsync(query, filter, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess
            ? Ok(new { ok = true, query, filter, message = result.Message ?? "已应用模组筛选。" })
            : Failure("filter_mods_failed", result.Message ?? "模组筛选失败。");
    }

    private async Task<AiToolResult> SwitchInstanceAsync(JsonElement args, CancellationToken cancellationToken)
    {
        var name = GetString(args, "name") ?? GetString(args, "instance_name") ?? GetString(args, "instance");
        if (string.IsNullOrWhiteSpace(name))
        {
            return Failure("bad_arguments", "缺少实例名称 name。");
        }

        var result = await _bridge!.SwitchInstanceAsync(name, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess
            ? Ok(new { ok = true, instance = name, message = result.Message ?? $"已切换至实例 {name}。" })
            : Failure("switch_instance_failed", result.Message ?? $"切换至实例 {name} 失败。");
    }

    private async Task<AiToolResult> LaunchGameAsync(CancellationToken cancellationToken)
    {
        var result = await _bridge!.LaunchGameAsync(cancellationToken).ConfigureAwait(false);
        return result.IsSuccess
            ? Ok(new { ok = true, message = result.Message ?? "游戏已启动。" })
            : Failure("launch_game_failed", result.Message ?? "游戏启动失败。");
    }

    private async Task<AiToolResult> SetSettingAsync(JsonElement args, CancellationToken cancellationToken)
    {
        var key = GetString(args, "key");
        var value = GetString(args, "value");
        if (string.IsNullOrWhiteSpace(key) || value is null)
        {
            return Failure("bad_arguments", "需提供设置项名称 key 与设置值 value。");
        }

        var result = await _bridge!.SetSettingAsync(key, value, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess
            ? Ok(new { ok = true, key, value, message = result.Message ?? $"设置项 {key} 已更新。" })
            : Failure("set_setting_failed", result.Message ?? $"设置项 {key} 更新失败。");
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

    private object UiStatePayload()
    {
        var state = _bridge!.GetUiState();
        return new
        {
            ok = true,
            page = state.PageKey,
            title = state.PageTitle,
            view_model = state.ViewModel,
            fields = state.Fields.Select(field => new
            {
                name = field.Name,
                kind = field.Kind,
                value = field.Value,
            }),
            actions = state.Actions.Select(action => new
            {
                name = action.Name,
                label = action.Label,
                can_execute = action.CanExecute,
                parameter = action.ParameterType,
            }),
        };
    }

    private object PageActionsPayload(JsonElement args)
    {
        var pageKey = GetString(args, "page_key");
        var actions = _bridge!.ListPageActions(pageKey) ?? Array.Empty<AiPageAction>();
        return new
        {
            ok = true,
            page_key = string.IsNullOrWhiteSpace(pageKey) ? null : pageKey,
            count = actions.Count,
            actions = actions.Select(action => new
            {
                name = action.Name,
                label = action.Label,
                can_execute = action.CanExecute,
                parameter = action.ParameterType,
            }),
        };
    }

    private async Task<AiToolResult> RunPageActionAsync(JsonElement args, CancellationToken cancellationToken)
    {
        var action = GetString(args, "action");
        if (string.IsNullOrWhiteSpace(action))
        {
            return Failure("missing_action", "缺少 action；先调 ckan_page_actions 拿到这一页的命令名。");
        }

        var argument = GetString(args, "argument");
        var pageKey = GetString(args, "page_key");

        var result = await _bridge!
            .RunPageActionAsync(action, argument, pageKey, cancellationToken)
            .ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return Failure(
                "run_page_action_failed",
                result.Message ?? $"命令「{action}」执行失败。",
                new { action, page_key = pageKey, detail = result.Detail });
        }

        return Ok(new
        {
            ok = true,
            action,
            argument,
            page_key = pageKey,
            message = result.Value ?? result.Message ?? "已执行。",
        });
    }

    private async Task<AiToolResult> SetUiFieldAsync(JsonElement args, CancellationToken cancellationToken)
    {
        var field = GetString(args, "field");
        if (string.IsNullOrWhiteSpace(field))
        {
            return Failure("missing_field", "缺少 field；先调 ckan_ui_state 拿到这一页的字段名。");
        }

        var value = GetString(args, "value");
        var pageKey = GetString(args, "page_key");

        var result = await _bridge!
            .SetUiFieldAsync(field, value, pageKey, cancellationToken)
            .ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return Failure(
                "set_ui_field_failed",
                result.Message ?? $"字段「{field}」写入失败。",
                new { field, value, page_key = pageKey, detail = result.Detail });
        }

        return Ok(new
        {
            ok = true,
            field,
            value,
            page_key = pageKey,
            message = result.Value ?? result.Message ?? "已写入。",
        });
    }

    private object SettingsPayload()
    {
        var settings = _bridge!.ListSettings() ?? Array.Empty<AiSettingEntry>();
        return new
        {
            ok = true,
            count = settings.Count,
            settings = settings.Select(setting => new
            {
                key = setting.Key,
                kind = setting.Kind,
                value = setting.Value,
                choices = setting.Choices.Count == 0 ? null : setting.Choices,
            }),
        };
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
