namespace CkanUI.Plugin.Ai.Settings;

/// <summary>
/// AI 助手的持久化设置（落在 <c>%LocalAppData%/CKAN/ai-assistant.json</c>）。
/// </summary>
/// <remarks>
/// <para>只有 <see cref="ApiKeyProtected"/> 会落盘，而且必须是加密后的字符串：明文 Key 只活在内存里
/// （见 <see cref="AiSettingsStore.LoadApiKey"/> 与 <see cref="AiSettingsStore.SaveWithApiKey"/>）。</para>
/// <para>端点与模型给默认值是为了「装上就能用」，但同样是可配置项：默认值不写死在客户端里。</para>
/// </remarks>
public sealed record AiSettings
{
    /// <summary>默认端点（OpenAI 兼容 <c>/chat/completions</c> 前缀）。</summary>
    public const string DefaultBaseUrl = "http://100.111.24.121:8317/v1";

    /// <summary>默认模型。</summary>
    public const string DefaultModel = "gemini-3.8-flash-high";

    /// <summary>默认 MCP 服务器命令：留空（未配置）。</summary>
    /// <remarks>
    /// 这里刻意不给 <c>node</c> 之类的默认值。命令猜对了是运气，猜错了是每次 <c>web_search</c> 都白等一次
    /// 进程启动直到握手超时——用户看到的只是「联网查不到」，却不知道是自己机器上根本没有那个命令。
    /// 留空时工具直接回 <c>web_search_unavailable</c>，模型据此降级，界面能明确说「没配搜索服务」。
    /// </remarks>
    public const string DefaultMcpCommand = "";

    /// <summary>默认采样温度。</summary>
    public const double DefaultTemperature = 0.7;

    /// <summary>默认工具调用轮次上限。</summary>
    public const int DefaultMaxToolRounds = 6;

    /// <summary>工具轮次的硬上限：再高就不是「多轮核实」而是死循环了。</summary>
    public const int MaxAllowedToolRounds = 12;

    /// <summary>OpenAI 兼容端点前缀（如 <c>http://host:8317/v1</c>）。</summary>
    public string BaseUrl { get; init; } = DefaultBaseUrl;

    /// <summary>模型名。</summary>
    public string Model { get; init; } = DefaultModel;

    /// <summary>加密后的 API Key；<c>null</c> 表示没配。</summary>
    public string? ApiKeyProtected { get; init; }

    /// <summary>MCP 搜索服务器命令。</summary>
    public string McpCommand { get; init; } = DefaultMcpCommand;

    /// <summary>MCP 搜索服务器参数。</summary>
    public string[] McpArgs { get; init; } = Array.Empty<string>();

    /// <summary>采样温度。</summary>
    public double Temperature { get; init; } = DefaultTemperature;

    /// <summary>工具调用轮次上限。</summary>
    public int MaxToolRounds { get; init; } = DefaultMaxToolRounds;

    /// <summary>可选的自定义系统提示词；为空时用内置中文提示词。</summary>
    public string? SystemPrompt { get; init; }

    /// <summary>把从磁盘读回来的（可能是手工编辑过的）值收敛到合法区间。</summary>
    /// <returns>规范化后的设置；缺失字段回落到默认值。</returns>
    public AiSettings Normalized()
    {
        var baseUrl = string.IsNullOrWhiteSpace(BaseUrl) ? DefaultBaseUrl : BaseUrl.Trim();
        var model = string.IsNullOrWhiteSpace(Model) ? DefaultModel : Model.Trim();
        var command = string.IsNullOrWhiteSpace(McpCommand) ? string.Empty : McpCommand.Trim();
        var temperature = double.IsFinite(Temperature) ? Math.Clamp(Temperature, 0d, 2d) : DefaultTemperature;
        var rounds = MaxToolRounds <= 0 ? DefaultMaxToolRounds : Math.Min(MaxToolRounds, MaxAllowedToolRounds);
        return this with
        {
            BaseUrl = baseUrl,
            Model = model,
            McpCommand = command,
            McpArgs = McpArgs ?? Array.Empty<string>(),
            Temperature = temperature,
            MaxToolRounds = rounds,
        };
    }
}
