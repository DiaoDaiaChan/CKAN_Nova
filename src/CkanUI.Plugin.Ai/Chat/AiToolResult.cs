namespace CkanUI.Plugin.Ai.Chat;

/// <summary>一次工具调用的结果：既有给模型看的 JSON，也有给界面看的成败。</summary>
/// <param name="Ok">工具是否按预期完成。</param>
/// <param name="Json">回灌给模型的紧凑 JSON（永远是合法 JSON 字符串）。</param>
public sealed record AiToolResult(bool Ok, string Json);
