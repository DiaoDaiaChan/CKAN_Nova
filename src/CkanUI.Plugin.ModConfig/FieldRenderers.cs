using System.Globalization;
using CkanUI.Services.Abstractions.ModConfig;

namespace CkanUI.Plugin.ModConfig;

/// <summary>bool 字段：true/false（含 1/0）渲染成开/关。</summary>
public sealed class BoolFieldRenderer : IModConfigFieldRenderer
{
    /// <inheritdoc />
    public ModConfigFieldType FieldType => ModConfigFieldType.Bool;

    /// <inheritdoc />
    public ModConfigEditorKind EditorKind => ModConfigEditorKind.Toggle;

    /// <inheritdoc />
    public string FormatValue(ModConfigFieldState field)
    {
        ArgumentNullException.ThrowIfNull(field);
        var raw = field.Value?.Trim();
        if (string.IsNullOrEmpty(raw))
        {
            return "未设置";
        }

        return raw.ToLowerInvariant() switch
        {
            "true" or "1" => "开",
            "false" or "0" => "关",
            _ => raw,
        };
    }
}

/// <summary>int 字段：整数 + 可选单位。</summary>
public sealed class IntFieldRenderer : IModConfigFieldRenderer
{
    /// <inheritdoc />
    public ModConfigFieldType FieldType => ModConfigFieldType.Int;

    /// <inheritdoc />
    public ModConfigEditorKind EditorKind => ModConfigEditorKind.NumberField;

    /// <inheritdoc />
    public string FormatValue(ModConfigFieldState field)
    {
        ArgumentNullException.ThrowIfNull(field);
        return NumberText.Format(field);
    }
}

/// <summary>float 字段：数字 + 可选单位。</summary>
public sealed class FloatFieldRenderer : IModConfigFieldRenderer
{
    /// <inheritdoc />
    public ModConfigFieldType FieldType => ModConfigFieldType.Float;

    /// <inheritdoc />
    public ModConfigEditorKind EditorKind => ModConfigEditorKind.NumberField;

    /// <inheritdoc />
    public string FormatValue(ModConfigFieldState field)
    {
        ArgumentNullException.ThrowIfNull(field);
        return NumberText.Format(field);
    }
}

/// <summary>enum 字段：原样展示取值（白名单来自 adapter）。</summary>
public sealed class EnumFieldRenderer : IModConfigFieldRenderer
{
    /// <inheritdoc />
    public ModConfigFieldType FieldType => ModConfigFieldType.Enum;

    /// <inheritdoc />
    public ModConfigEditorKind EditorKind => ModConfigEditorKind.Dropdown;

    /// <inheritdoc />
    public string FormatValue(ModConfigFieldState field)
    {
        ArgumentNullException.ThrowIfNull(field);
        return string.IsNullOrWhiteSpace(field.Value) ? "未设置" : field.Value!;
    }
}

/// <summary>string 字段：原样展示。</summary>
public sealed class StringFieldRenderer : IModConfigFieldRenderer
{
    /// <inheritdoc />
    public ModConfigFieldType FieldType => ModConfigFieldType.String;

    /// <inheritdoc />
    public ModConfigEditorKind EditorKind => ModConfigEditorKind.TextField;

    /// <inheritdoc />
    public string FormatValue(ModConfigFieldState field)
    {
        ArgumentNullException.ThrowIfNull(field);
        return field.Value ?? string.Empty;
    }
}

/// <summary>path 字段：用文本框直接编辑路径（CFG-04）。</summary>
/// <remarks>
/// 服务层对 path 没有额外约束（只有"不能为空/不能带换行"，见 ModConfigDetector.ValidateFieldValue），
/// 所以这里给单行文本框而不是只读展示；将来加"浏览…"按钮时只需换 EditorKind 到
/// <see cref="ModConfigEditorKind.PathPicker"/> 的模板，字段类型到渲染器的映射不用改。
/// </remarks>
public sealed class PathFieldRenderer : IModConfigFieldRenderer
{
    /// <inheritdoc />
    public ModConfigFieldType FieldType => ModConfigFieldType.Path;

    /// <inheritdoc />
    public ModConfigEditorKind EditorKind => ModConfigEditorKind.PathPicker;

    /// <inheritdoc />
    public string FormatValue(ModConfigFieldState field)
    {
        ArgumentNullException.ThrowIfNull(field);
        return string.IsNullOrWhiteSpace(field.Value) ? "未设置" : field.Value!;
    }
}

/// <summary>list 字段：逐行文本编辑（CFG-04）。</summary>
/// <remarks>
/// 配置文件是"一行一键"，值里带换行会被服务层直接拒绝（ModConfigDetector 的换行拦截），
/// 所以编辑器给多行文本框，写盘时由行 VM 拆项后用 ", " 拼成单行；展示沿用磁盘原文。
/// </remarks>
public sealed class ListFieldRenderer : IModConfigFieldRenderer
{
    /// <inheritdoc />
    public ModConfigFieldType FieldType => ModConfigFieldType.List;

    /// <inheritdoc />
    public ModConfigEditorKind EditorKind => ModConfigEditorKind.ListEditor;

    /// <inheritdoc />
    public string FormatValue(ModConfigFieldState field)
    {
        ArgumentNullException.ThrowIfNull(field);
        return string.IsNullOrWhiteSpace(field.Value) ? "未设置" : field.Value!;
    }
}

/// <summary>color 字段：本版只读展示（取色器留待后续，行内会写明原因）。</summary>
public sealed class ColorFieldRenderer : IModConfigFieldRenderer
{
    /// <inheritdoc />
    public ModConfigFieldType FieldType => ModConfigFieldType.Color;

    /// <inheritdoc />
    public ModConfigEditorKind EditorKind => ModConfigEditorKind.ColorPicker;

    /// <inheritdoc />
    public string FormatValue(ModConfigFieldState field)
    {
        ArgumentNullException.ThrowIfNull(field);
        return string.IsNullOrWhiteSpace(field.Value) ? "未设置" : field.Value!;
    }
}

/// <summary>keybinding 字段：本版只读展示（按键捕获留待后续，行内会写明原因）。</summary>
public sealed class KeybindingFieldRenderer : IModConfigFieldRenderer
{
    /// <inheritdoc />
    public ModConfigFieldType FieldType => ModConfigFieldType.Keybinding;

    /// <inheritdoc />
    public ModConfigEditorKind EditorKind => ModConfigEditorKind.KeyCapture;

    /// <inheritdoc />
    public string FormatValue(ModConfigFieldState field)
    {
        ArgumentNullException.ThrowIfNull(field);
        return string.IsNullOrWhiteSpace(field.Value) ? "未设置" : field.Value!;
    }
}

/// <summary>数字与单位的公用格式化（int/float 共用，避免两份不完全一致的实现）。</summary>
internal static class NumberText
{
    /// <summary>数值 + 单位；值本身不是合法数字时老实展示原文（配置里可能有 mod 的私有写法）。</summary>
    public static string Format(ModConfigFieldState field)
    {
        var raw = field.Value?.Trim();
        if (string.IsNullOrEmpty(raw))
        {
            return "未设置";
        }

        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            return raw;
        }

        var text = field.Type == ModConfigFieldType.Int
            ? Math.Round(parsed).ToString("0", CultureInfo.InvariantCulture)
            : parsed.ToString("0.###", CultureInfo.InvariantCulture);
        return string.IsNullOrWhiteSpace(field.Unit) ? text : text + " " + field.Unit;
    }
}
