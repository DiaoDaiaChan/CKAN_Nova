using System.Globalization;
using CkanUI.Services.Abstractions.ModConfig;

namespace CkanUI.ViewModels.Pages.ModConfig;

/// <summary>字段行（右侧列表的一行：键名 + 值 + 灰字说明 + 风险/重启标记）。</summary>
/// <remarks>
/// 只读渲染：本页骨架（UI-CFG-01）不提供写盘入口，写盘一律走 IAdapterWriter（CFG-05）与后续 CFG-04 的编辑控件。
/// </remarks>
public sealed class ModConfigFieldRowViewModel
{
    /// <summary>构造一行。</summary>
    /// <param name="field">字段快照。</param>
    /// <param name="renderer">该类型的渲染器。</param>
    /// <param name="isFileWritable">所在文件当前是否可写（决定这一行能不能编辑）。</param>
    public ModConfigFieldRowViewModel(ModConfigFieldState field, IModConfigFieldRenderer renderer, bool isFileWritable)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(renderer);

        Field = field;
        Key = field.Key;
        Label = string.IsNullOrWhiteSpace(field.Label) ? field.Key : field.Label;
        EditorKind = renderer.EditorKind;
        ValueText = renderer.FormatValue(field);
        Choices = field.Values ?? [];
        IsFileWritable = isFileWritable;
        HintText = BuildHint(field);
        IsModified = field.DefaultValue is not null
            && !string.Equals(field.Value?.Trim(), field.DefaultValue.Trim(), StringComparison.Ordinal);
    }

    /// <summary>原始字段快照（写盘时用它找回 key/type）。</summary>
    public ModConfigFieldState Field { get; }

    /// <summary>键名（配置文件里的字面量，搜索匹配用）。</summary>
    public string Key { get; }

    /// <summary>显示名（adapter 的 label，缺省回落到键名）。</summary>
    public string Label { get; }

    /// <summary>值的展示文本。</summary>
    public string ValueText { get; }

    /// <summary>渲染形态（视图据此选模板）。</summary>
    public ModConfigEditorKind EditorKind { get; }

    /// <summary>enum 的白名单取值（其它类型为空）。</summary>
    public IReadOnlyList<string> Choices { get; }

    /// <summary>键上方的注释块（adapter 的 descriptionFromComment 语义）。</summary>
    public string CommentText => Field.Comment ?? string.Empty;

    /// <summary>是否有注释可显示。</summary>
    public bool HasComment => CommentText.Length > 0;

    /// <summary>分组标题（来自 // #### 分节注释，没有则为空串）。</summary>
    public string GroupTitle => Field.Group ?? string.Empty;

    /// <summary>范围/单位/正则的提示文案。</summary>
    public string HintText { get; }

    /// <summary>是否有提示文案。</summary>
    public bool HasHint => HintText.Length > 0;

    /// <summary>风险等级文案（low 为空串）。</summary>
    public string RiskText => Field.Risk switch
    {
        ModConfigFieldRisk.High => "高风险",
        ModConfigFieldRisk.Medium => "注意",
        _ => string.Empty,
    };

    /// <summary>是否要显示风险标记。</summary>
    public bool HasRisk => Field.IsRisky;

    /// <summary>改完需要重启 KSP 的标记。</summary>
    public string RestartText => Field.RequiresRestart ? "需重启" : string.Empty;

    /// <summary>是否显示重启标记。</summary>
    public bool HasRestartMark => Field.RequiresRestart;

    /// <summary>当前值是否与 adapter 声明的默认值不同。</summary>
    public bool IsModified { get; }

    /// <summary>所在文件是否可写。</summary>
    public bool IsFileWritable { get; }

    /// <summary>这一行当前是否允许编辑（文件可写且字段本身不是 readonly）。</summary>
    public bool CanEdit => IsFileWritable && Field.Type != ModConfigFieldType.ReadOnly;

    /// <summary>搜索匹配：键名、显示名、注释三路都可命中（04 篇 §6.2 的"搜索按键名与说明双路"）。</summary>
    /// <param name="keyword">关键字（空=不筛）。</param>
    public bool Matches(string? keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return true;
        }

        var text = keyword.Trim();
        return Key.Contains(text, StringComparison.OrdinalIgnoreCase)
            || Label.Contains(text, StringComparison.OrdinalIgnoreCase)
            || CommentText.Contains(text, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildHint(ModConfigFieldState field)
    {
        var parts = new List<string>();
        if (field.Type is ModConfigFieldType.Int or ModConfigFieldType.Float)
        {
            if (field.Min is { } min && field.Max is { } max)
            {
                parts.Add("范围 " + Format(min) + " ~ " + Format(max) + UnitSuffix(field.Unit));
            }
            else if (field.Min is { } lower)
            {
                parts.Add("不小于 " + Format(lower) + UnitSuffix(field.Unit));
            }
            else if (field.Max is { } upper)
            {
                parts.Add("不大于 " + Format(upper) + UnitSuffix(field.Unit));
            }
        }
        else if (field.Unit is { Length: > 0 } unit)
        {
            parts.Add("单位 " + unit);
        }

        return string.Join("；", parts);
    }

    private static string Format(double value)
        => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string UnitSuffix(string? unit) => string.IsNullOrWhiteSpace(unit) ? string.Empty : " " + unit;
}
