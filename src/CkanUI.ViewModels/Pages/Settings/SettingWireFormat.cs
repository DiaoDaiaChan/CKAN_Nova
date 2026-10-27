using System.Text.Json;

namespace CkanUI.ViewModels.Pages.Settings;

/// <summary>
/// 设置项「编辑空间」与「配置线格式」的互转（02 篇 §3：字典型配置一律字符串化成 JSON 数组）。
/// 编辑空间对用户友好（多行文本），线格式对 Core 友好（JSON）；转换只发生在这一个地方。
/// </summary>
public static class SettingWireFormat
{
    /// <summary>线格式 → 编辑空间。非列表形态原样返回；不是合法 JSON 数组时按原文显示，交给用户修正。</summary>
    public static string FromWire(SettingItemKind kind, string? wireValue)
    {
        if (kind != SettingItemKind.LineList)
        {
            return wireValue ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(wireValue))
        {
            return string.Empty;
        }

        try
        {
            var items = JsonSerializer.Deserialize<string?[]>(wireValue);
            return items is null
                ? string.Empty
                : string.Join("\n", items.Select(item => item ?? string.Empty));
        }
        catch (JsonException)
        {
            return wireValue!;
        }
    }

    /// <summary>编辑空间 → 线格式。整数去掉首尾空白；列表序列化成 JSON 字符串数组，空行写成 null。</summary>
    public static string ToWire(SettingItemKind kind, string? editValue)
    {
        var text = editValue ?? string.Empty;
        switch (kind)
        {
            case SettingItemKind.LineList:
                var lines = SettingValidator.SplitLines(text);
                return lines.Count == 0
                    ? string.Empty
                    : JsonSerializer.Serialize(lines.Select(line => line.Length == 0 ? null : line).ToArray());
            case SettingItemKind.Integer:
            case SettingItemKind.Choice:
            case SettingItemKind.Boolean:
                return text.Trim();
            default:
                return text;
        }
    }
}
