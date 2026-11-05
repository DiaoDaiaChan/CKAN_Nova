using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using CkanUI.Plugin.Ai.Search;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Ai;

namespace CkanUI.Plugin.Ai.Tests.Support;

/// <summary>假宿主桥：数据全可控，导航结果可配成失败，用来验「工具层怎么翻译这些结果」。</summary>
internal sealed class FakeCkanBridge : IAiCkanBridge
{
    /// <summary>上下文返回值。</summary>
    public AiBridgeContext Context { get; set; } = new(
        "测试实例",
        "D:/KSP",
        "1.12.5",
        3,
        42,
        new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc),
        false);

    /// <summary>搜索结果返回值。</summary>
    public IReadOnlyList<AiModSummary> Mods { get; set; } = new[]
    {
        new AiModSummary(
            "TestMod",
            "测试模组",
            "某人",
            "一句话摘要",
            null,
            "1.2.0",
            1234,
            false,
            true,
            false,
            new[] { "标签A" }),
    };

    /// <summary>详情返回值；<c>null</c> 表示仓库里没有。</summary>
    public AiModDetail? Detail { get; set; } = new(
        "TestMod",
        "测试模组",
        "某人",
        "一句话摘要",
        "长描述",
        new[] { "MIT" },
        "https://example.invalid",
        null,
        "1.2.0",
        1234,
        new[] { "标签A" },
        false,
        true,
        new[] { "ModuleManager" },
        Array.Empty<string>(),
        Array.Empty<string>());

    /// <summary>已安装列表返回值。</summary>
    public IReadOnlyList<AiInstalledMod> Installed { get; set; } = new[]
    {
        new AiInstalledMod("TestMod", "1.1.0", false),
    };

    /// <summary>页面列表返回值。</summary>
    public IReadOnlyList<AiPageRef> Pages { get; set; } = new[]
    {
        new AiPageRef("mods", "模组"),
    };

    /// <summary>导航结果。</summary>
    public OperationResult<bool> NavigateResult { get; set; } = OperationResult<bool>.Ok(true);

    /// <summary>导航被调用的次数。</summary>
    public int NavigateCalls { get; private set; }

    /// <summary>最后一次导航的目标键。</summary>
    public string? LastNavigateKey { get; private set; }

    /// <summary>安装计划结果；默认给出一步安装。</summary>
    public OperationResult<AiInstallPlan> PlanInstallResult { get; set; } = OperationResult<AiInstallPlan>.Ok(
        new AiInstallPlan(
            "TestMod",
            new[] { new AiInstallStep("TestMod", "测试模组", "Install", null, "1.2.0") },
            Array.Empty<string>(),
            Array.Empty<string>(),
            1024));

    /// <summary>安装结果；默认成功装上。</summary>
    public OperationResult<AiInstallOutcome> InstallResult { get; set; } = OperationResult<AiInstallOutcome>.Ok(
        new AiInstallOutcome(
            "TestMod",
            true,
            "已安装 TestMod。",
            new[] { "TestMod" },
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>()));

    /// <summary>PlanInstallAsync 被调用的次数。</summary>
    public int PlanInstallCalls { get; private set; }

    /// <summary>最后一次计划请求的标识符。</summary>
    public string? LastPlanIdentifier { get; private set; }

    /// <summary>InstallAsync 被调用的次数。</summary>
    public int InstallCalls { get; private set; }

    /// <summary>最后一次安装请求的标识符。</summary>
    public string? LastInstallIdentifier { get; private set; }

    /// <inheritdoc />
    public AiBridgeContext GetContext() => Context;

    /// <inheritdoc />
    public IReadOnlyList<AiModSummary> SearchMods(string? query, int limit = 10) => Mods;

    /// <inheritdoc />
    public AiModDetail? GetModDetail(string identifier) => Detail;

    /// <inheritdoc />
    public IReadOnlyList<AiInstalledMod> ListInstalled(int limit = 50) => Installed;

    /// <inheritdoc />
    public IReadOnlyList<AiPageRef> ListPages() => Pages;

    /// <inheritdoc />
    public Task<OperationResult<bool>> NavigateAsync(string pageKey, CancellationToken cancellationToken = default)
    {
        NavigateCalls++;
        LastNavigateKey = pageKey;
        return Task.FromResult(NavigateResult);
    }

    /// <inheritdoc />
    public Task<OperationResult<AiInstallPlan>> PlanInstallAsync(string identifier, CancellationToken cancellationToken = default)
    {
        PlanInstallCalls++;
        LastPlanIdentifier = identifier;
        return Task.FromResult(PlanInstallResult);
    }

    /// <inheritdoc />
    public Task<OperationResult<AiInstallOutcome>> InstallAsync(string identifier, CancellationToken cancellationToken = default)
    {
        InstallCalls++;
        LastInstallIdentifier = identifier;
        return Task.FromResult(InstallResult);
    }

    public string? LastSetTheme { get; private set; }
    public string? LastSetSkin { get; private set; }
    public OperationResult<bool> SetThemeResult { get; set; } = OperationResult<bool>.Ok(true);

    public Task<OperationResult<bool>> SetThemeAsync(string? theme, string? skin = null, CancellationToken cancellationToken = default)
    {
        LastSetTheme = theme;
        LastSetSkin = skin;
        return Task.FromResult(SetThemeResult);
    }

    public string? LastFilterQuery { get; private set; }
    public string? LastFilterKind { get; private set; }
    public OperationResult<bool> FilterModsResult { get; set; } = OperationResult<bool>.Ok(true);

    public Task<OperationResult<bool>> FilterModsAsync(string? query, string? filter = null, CancellationToken cancellationToken = default)
    {
        LastFilterQuery = query;
        LastFilterKind = filter;
        return Task.FromResult(FilterModsResult);
    }

    public string? LastSwitchedInstance { get; private set; }
    public OperationResult<bool> SwitchInstanceResult { get; set; } = OperationResult<bool>.Ok(true);

    public Task<OperationResult<bool>> SwitchInstanceAsync(string instanceName, CancellationToken cancellationToken = default)
    {
        LastSwitchedInstance = instanceName;
        return Task.FromResult(SwitchInstanceResult);
    }

    public int LaunchGameCalls { get; private set; }
    public OperationResult<bool> LaunchGameResult { get; set; } = OperationResult<bool>.Ok(true);

    public Task<OperationResult<bool>> LaunchGameAsync(CancellationToken cancellationToken = default)
    {
        LaunchGameCalls++;
        return Task.FromResult(LaunchGameResult);
    }

    public string? LastSettingKey { get; private set; }
    public string? LastSettingValue { get; private set; }
    public OperationResult<bool> SetSettingResult { get; set; } = OperationResult<bool>.Ok(true);

    public Task<OperationResult<bool>> SetSettingAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        LastSettingKey = key;
        LastSettingValue = value;
        return Task.FromResult(SetSettingResult);
    }

    /// <summary>界面状态返回值。</summary>
    public AiUiState UiState { get; set; } = new(
        "mods",
        "模组",
        "ModsPageViewModel",
        new[] { new AiUiField("SearchText", "text", "abc") },
        new[] { new AiPageAction("ApplyCommand", "Apply", true, null) });

    /// <summary>页面动作列表返回值。</summary>
    public IReadOnlyList<AiPageAction> PageActions { get; set; } = new[]
    {
        new AiPageAction("ApplyCommand", "Apply", true, null),
        new AiPageAction("SelectCommand", "Select", false, "Int32"),
    };

    /// <summary>RunPageActionAsync 返回值。</summary>
    public OperationResult<string> RunPageActionResult { get; set; } =
        OperationResult<string>.Ok("已执行「ApplyCommand」。");

    /// <summary>SetUiFieldAsync 返回值。</summary>
    public OperationResult<string> SetUiFieldResult { get; set; } =
        OperationResult<string>.Ok("「SearchText」：\"\" → RealChute。");

    /// <summary>SetUiFieldAsync 调用次数。</summary>
    public int SetUiFieldCalls { get; private set; }

    /// <summary>最近一次写入的字段名。</summary>
    public string? LastUiField { get; private set; }

    /// <summary>最近一次写入的值。</summary>
    public string? LastUiFieldValue { get; private set; }

    /// <summary>最近一次写入指定过的页面键。</summary>
    public string? LastUiFieldPageKey { get; private set; }

    /// <summary>可写设置项返回值。</summary>
    public IReadOnlyList<AiSettingEntry> Settings { get; set; } = new[]
    {
        new AiSettingEntry("Theme", "text", "dark", Array.Empty<string>()),
        new AiSettingEntry("Density", "enum", "compact", new[] { "standard", "compact" }),
    };

    /// <summary>RunPageActionAsync 被调用的次数。</summary>
    public int RunPageActionCalls { get; private set; }

    /// <summary>最后一次执行的动作名。</summary>
    public string? LastPageAction { get; private set; }

    /// <summary>最后一次执行的动作参数。</summary>
    public string? LastPageActionArgument { get; private set; }

    /// <summary>最后一次执行动作时指定或推导出的页面键。</summary>
    public string? LastPageActionPageKey { get; private set; }

    /// <summary>最后一次 ListPageActions 传入的页面键。</summary>
    public string? LastPageActionsQuery { get; private set; }

    /// <inheritdoc />
    public AiUiState GetUiState() => UiState;

    /// <inheritdoc />
    public IReadOnlyList<AiPageAction> ListPageActions(string? pageKey = null)
    {
        LastPageActionsQuery = pageKey;
        return PageActions;
    }

    /// <inheritdoc />
    public Task<OperationResult<string>> RunPageActionAsync(
        string action,
        string? argument = null,
        string? pageKey = null,
        CancellationToken cancellationToken = default)
    {
        RunPageActionCalls++;
        LastPageAction = action;
        LastPageActionArgument = argument;
        LastPageActionPageKey = pageKey;
        return Task.FromResult(RunPageActionResult);
    }

    /// <inheritdoc />
    public Task<OperationResult<string>> SetUiFieldAsync(
        string field,
        string? value,
        string? pageKey = null,
        CancellationToken cancellationToken = default)
    {
        SetUiFieldCalls++;
        LastUiField = field;
        LastUiFieldValue = value;
        LastUiFieldPageKey = pageKey;
        return Task.FromResult(SetUiFieldResult);
    }

    /// <inheritdoc />
    public IReadOnlyList<AiSettingEntry> ListSettings() => Settings;
}

/// <summary>假网页搜索：用来验「MCP 可用时结果怎么压缩」这条正常路径。</summary>
internal sealed class FakeWebSearch : IWebSearchTool
{
    private readonly WebSearchResult _result;

    /// <summary>创建实例。</summary>
    /// <param name="result">固定返回的结果。</param>
    public FakeWebSearch(WebSearchResult result) => _result = result;

    /// <inheritdoc />
    public Task<WebSearchResult> SearchAsync(string query, CancellationToken cancellationToken = default)
        => Task.FromResult(_result);
}
