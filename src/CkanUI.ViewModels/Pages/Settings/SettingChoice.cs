namespace CkanUI.ViewModels.Pages.Settings;

/// <summary>单选设置的一个候选。</summary>
/// <param name="Value">写入配置的字符串值（Core 的键值或 ckan-ui.json 的字段值）。</param>
/// <param name="Label">界面显示文案（中文）。</param>
public sealed record SettingChoice(string Value, string Label);
