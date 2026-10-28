using System.Globalization;
using CkanUI.Services.Abstractions.ModConfig;

namespace CkanUI.ViewModels.Pages.ModConfig;

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
