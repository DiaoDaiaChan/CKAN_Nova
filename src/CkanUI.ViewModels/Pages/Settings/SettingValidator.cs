using System.Globalization;

namespace CkanUI.ViewModels.Pages.Settings;

/// <summary>
/// 设置项的行内校验（06 篇 UI-07「校验」/ VM-03「单测覆盖校验分支」）。
/// 纯函数、无 IO：VM 在每次编辑后调用，视图只负责把返回的文案显示在行内。
/// 空值一律视为「回到默认」，因此不做必填校验——Core 的默认值语义就是 null/空。
/// </summary>
public static class SettingValidator
{
    /// <summary>文本与列表行条的字符上限，防止把超长粘贴写进配置文件。</summary>
    public const int MaxTextLength = 512;

    /// <summary>校验编辑值，返回行内错误文案；合法返回 null。</summary>
    /// <param name="kind">编辑形态。</param>
    /// <param name="value">编辑中的字符串值。</param>
    /// <param name="choices">单选候选（Choice 形态必传）。</param>
    /// <param name="min">整数下限（含）。</param>
    /// <param name="max">整数上限（含）。</param>
    public static string? Validate(
        SettingItemKind kind,
        string? value,
        IReadOnlyList<SettingChoice>? choices = null,
        long? min = null,
        long? max = null)
    {
        var text = value ?? string.Empty;
        return kind switch
        {
            SettingItemKind.Text => ValidateText(text),
            SettingItemKind.Path => ValidatePath(text),
            SettingItemKind.Integer => ValidateInteger(text, min, max),
            SettingItemKind.Boolean => ValidateBoolean(text),
            SettingItemKind.Choice => ValidateChoice(text, choices),
            SettingItemKind.LineList => ValidateLines(text),
            _ => null,
        };
    }

    /// <summary>普通文本：只限长度。</summary>
    public static string? ValidateText(string text) =>
        text.Length > MaxTextLength ? $"最多 {MaxTextLength} 个字符" : null;

    /// <summary>路径：空值合法（表示用 Core 默认值），非空必须是绝对路径且不含非法字符。</summary>
    public static string? ValidatePath(string text)
    {
        if (text.Length == 0)
        {
            return null;
        }

        if (text.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            return "路径包含非法字符";
        }

        return Path.IsPathRooted(text) ? null : "请输入绝对路径，例如 D:/CKAN/cache";
    }

    /// <summary>整数：空值合法；否则必须能解析且落在 [min, max] 内。</summary>
    public static string? ValidateInteger(string text, long? min, long? max)
    {
        if (text.Length == 0)
        {
            return null;
        }

        if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
        {
            return "请输入整数";
        }

        if (min is { } low && number < low)
        {
            return $"不能小于 {low.ToString(CultureInfo.InvariantCulture)}";
        }

        if (max is { } high && number > high)
        {
            return $"不能大于 {high.ToString(CultureInfo.InvariantCulture)}";
        }

        return null;
    }

    /// <summary>布尔：编辑空间只接受 "true"/"false"（空值表示跟随默认）。</summary>
    public static string? ValidateBoolean(string text) =>
        text.Length == 0 || string.Equals(text, "true", StringComparison.Ordinal) || string.Equals(text, "false", StringComparison.Ordinal)
            ? null
            : "取值只能是 true 或 false";

    /// <summary>单选：必须落在候选集合内（界面用下拉框，正常不会失败，防御性保留）。</summary>
    public static string? ValidateChoice(string text, IReadOnlyList<SettingChoice>? choices)
    {
        if (choices is null || choices.Count == 0)
        {
            return null;
        }

        foreach (var choice in choices)
        {
            if (string.Equals(choice.Value, text, StringComparison.Ordinal))
            {
                return null;
            }
        }

        return "请选择一个有效选项";
    }

    /// <summary>列表：逐行校验长度与空白（主机名/过滤器都不允许含空白）。</summary>
    public static string? ValidateLines(string text)
    {
        foreach (var line in SplitLines(text))
        {
            if (line.Length > MaxTextLength)
            {
                return $"每行最多 {MaxTextLength} 个字符";
            }

            foreach (var ch in line)
            {
                if (char.IsWhiteSpace(ch))
                {
                    return $"「{line}」不能包含空白字符";
                }
            }
        }

        return null;
    }

    /// <summary>
    /// 把多行文本切成条目：统一换行符、去掉行尾空行，中间的空行保留为空串
    /// （Core 用 null 表示 PreferredHosts 里「其余主机」的位置，02 篇 §3）。
    /// </summary>
    public static IReadOnlyList<string> SplitLines(string? text)
    {
        var normalized = (text ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n');
        if (normalized.Length == 0)
        {
            return Array.Empty<string>();
        }

        var raw = normalized.Split('\n');
        var result = new List<string>(raw.Length);
        foreach (var line in raw)
        {
            result.Add(line.Trim());
        }

        return result;
    }
}
