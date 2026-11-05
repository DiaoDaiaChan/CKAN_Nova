using CkanUI.Plugin.Ai.Settings;

namespace CkanUI.Plugin.Ai.Chat;

/// <summary>一次问答要用的端点参数（明文 Key 只在这里用一次，不落盘）。</summary>
/// <param name="BaseUrl">OpenAI 兼容端点前缀，如 <c>http://host:8317/v1</c>。</param>
/// <param name="Model">模型名。</param>
/// <param name="ApiKey">明文 API Key；为空时不发 Authorization 头。</param>
/// <param name="Temperature">采样温度。</param>
/// <param name="MaxToolRounds">工具调用轮次上限。</param>
/// <param name="SystemPrompt">自定义系统提示词；为空时用内置中文提示词。</param>
public sealed record AiChatOptions(
    string BaseUrl,
    string Model,
    string? ApiKey,
    double Temperature = AiSettings.DefaultTemperature,
    int MaxToolRounds = AiSettings.DefaultMaxToolRounds,
    string? SystemPrompt = null);

/// <summary>一轮问答停下来的原因。</summary>
public enum AiStopReason
{
    /// <summary>模型给出了最终回答。</summary>
    Completed,

    /// <summary>工具轮次用尽，按上限收口。</summary>
    ToolRoundsExhausted,

    /// <summary>模型回了空正文也没要求工具。</summary>
    EmptyAnswer,
}

/// <summary>一轮问答的结果。</summary>
/// <param name="Text">给用户看的正文。</param>
/// <param name="StopReason">为什么停下来。</param>
/// <param name="ToolRounds">实际用掉的工具轮次。</param>
/// <param name="Traces">工具轨迹（按发生顺序）。</param>
public sealed record AiChatOutcome(string Text, AiStopReason StopReason, int ToolRounds, IReadOnlyList<AiToolTrace> Traces)
{
    /// <summary>工具轮次用尽时的固定收口文案（界面与测试都认这一句）。</summary>
    public const string ToolRoundsExhaustedText = "工具调用轮次用尽";
}

/// <summary>AI 端点的协议级失败（HTTP 非 2xx、返回不是预期 JSON、超时）。</summary>
public sealed class AiChatException : Exception
{
    /// <summary>创建异常。</summary>
    /// <param name="message">用户可读原因。</param>
    public AiChatException(string message)
        : base(message)
    {
    }

    /// <summary>创建异常（带内部原因）。</summary>
    /// <param name="message">用户可读原因。</param>
    /// <param name="innerException">内部异常。</param>
    public AiChatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>系统提示词：约束模型的核实纪律，以及工具与降级路径。</summary>
public static class AiSystemPrompt
{
    /// <summary>内置中文系统提示词。</summary>
    public const string Text = """
        你是 CKAN UI 的助手，帮 KSP 玩家理解并管理模组。

        行为纪律：
        1. 先用工具核实再回答；不确定就直说不知道，不要凭印象编造版本号、依赖或下载量。
        2. 提到模组必须带标识符（identifier），方便用户搜索。
        3. 需要联网信息时用 web_search；它不可用时要明确说明「没联网查到」，再用已有数据回答。
        4. 操作界面不要靠猜：ckan_list_pages 列页面键（含插件页），ckan_navigate 打开页面，ckan_ui_state 读当前页状态与可用动作，ckan_page_actions 看指定页动作。点按钮用 ckan_run_page_action（需要参数就先看动作的 parameter_type）；往输入框/下拉框里填值用 ckan_set_ui_field（字段名来自 ckan_ui_state，只支持文本/布尔/数字/枚举/时间）。改设置先用 ckan_settings 查键名与可选值，再 ckan_set_setting。有副作用的动作由宿主弹确认框；用户拒绝就如实说没有执行。
        5. 只有用户明确说「装 / 安装 / 升级某个模组」时才调用 ckan_install_mod；装完把 applied / summary / failed 如实转述，applied=false 时不要声称已经装好。用户只是问「有什么好用的」时，先搜索、看详情、给候选，等用户点名再装。
        6. 回答用中文，简洁，先说结论再给依据。
        """;
}
