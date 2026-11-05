using System.Globalization;
using CkanUI.Services.Abstractions.ModConfig;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CkanUI.Plugin.ModConfig;

/// <summary>字段行（右侧列表的一行：键名 + 编辑控件 + 灰字说明 + 风险/重启标记）。</summary>
/// <remarks>
/// CFG-04 起这一行可编辑：bool/int/float/enum/string/path/list 用 <see cref="EditValue"/> 承载草稿值，
/// 页面 VM 收集"草稿与磁盘值不同"的行后统一交给 IAdapterWriter 写盘（写前快照、写后自校验都在服务层）。
/// color/keybinding 也有各自的编辑控件（取色文本 + 色块预览、点击后按组合键捕获），
/// readonly 字段保持只读且必须用 <see cref="ReadOnlyNote"/> 写明原因，不做静默禁用。
/// 编辑期间不要重建行对象（会丢输入焦点）：页面只在保存/重新读盘之后重建。
/// </remarks>
public sealed partial class ModConfigFieldRowViewModel : ObservableObject
{
    private readonly string? _readonlyReason;

    /// <summary>最近一次成功写盘的 wire 值（保存后"放弃改动"要回到它，而不是回到读盘时的旧值）。</summary>
    private string? _savedText;

    /// <summary>构造一行。</summary>
    /// <param name="field">字段快照。</param>
    /// <param name="renderer">该类型的渲染器。</param>
    /// <param name="isFileWritable">所在文件当前是否可写（决定这一行能不能编辑）。</param>
    /// <param name="filePath">字段所属配置文件的绝对路径（写盘时按文件分组）。</param>
    /// <param name="readonlyReason">文件不可写时的原因（来自 adapter 的 readonlyReason）。</param>
    public ModConfigFieldRowViewModel(
        ModConfigFieldState field,
        IModConfigFieldRenderer renderer,
        bool isFileWritable,
        string? filePath = null,
        string? readonlyReason = null)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(renderer);

        Field = field;
        Key = field.Key;
        Label = string.IsNullOrWhiteSpace(field.Label) ? field.Key : field.Label;
        EditorKind = renderer.EditorKind;
        ValueText = renderer.FormatValue(field);
        Choices = BuildChoices(field);
        IsFileWritable = isFileWritable;
        FilePath = filePath ?? string.Empty;
        _readonlyReason = readonlyReason;
        HintText = BuildHint(field);
        IsModified = field.DefaultValue is not null
            && !ModConfigValueComparer.AreEqual(field.Type, field.Value, field.DefaultValue);
        _editValue = ToEditorText(field.Value);
    }

    /// <summary>原始字段快照（写盘时用它找回 key/type/risk）。</summary>
    public ModConfigFieldState Field { get; }

    /// <summary>键名（配置文件里的字面量，搜索匹配用）。</summary>
    public string Key { get; }

    /// <summary>显示名（adapter 的 label，缺省回落到键名）。</summary>
    public string Label { get; }

    /// <summary>值的展示文本（只读行直接用；编辑行作为提示）。</summary>
    public string ValueText { get; }

    /// <summary>字段所属配置文件路径（没有上下文时为空串）。</summary>
    public string FilePath { get; }

    /// <summary>渲染形态（视图据此选模板）。</summary>
    public ModConfigEditorKind EditorKind { get; }

    /// <summary>enum 的白名单取值（当前值不在白名单时会补到首位；其它类型为空）。</summary>
    public IReadOnlyList<string> Choices { get; }

    /// <summary>编辑草稿的文本（bool 写 true/false，list 每行一项）。</summary>
    [ObservableProperty]
    private string _editValue;

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

    /// <summary>当前值是否与 adapter 声明的默认值不同（读盘时的状态，不随草稿变）。</summary>
    public bool IsModified { get; }

    /// <summary>所在文件是否可写。</summary>
    public bool IsFileWritable { get; }

    /// <summary>这一行当前是否允许编辑（文件可写且字段本身不是 readonly）。</summary>
    public bool CanEdit => IsFileWritable && Field.Type != ModConfigFieldType.ReadOnly;

    /// <summary>要写进配置文件的值：list 拆项后拼成单行，其它类型原样。</summary>
    public string WireValue => EditorKind == ModConfigEditorKind.ListEditor ? ToWireText(EditValue) : EditValue;

    /// <summary>草稿是否与磁盘值不同（决定是否进待保存集合、是否显示"待保存"徽标）。</summary>
    public bool HasPendingChange => CanEdit
        && !string.Equals(WireValue.Trim(), BaselineValue.Trim(), StringComparison.Ordinal);

    /// <summary>bool 字段的开关状态（CheckBox 双向绑定）。</summary>
    public bool IsToggleOn
    {
        get
        {
            var raw = EditValue.Trim();
            return string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase) || raw == "1";
        }

        set
        {
            var text = value ? "true" : "false";
            if (string.Equals(EditValue, text, StringComparison.Ordinal))
            {
                return;
            }

            EditValue = text;
            OnPropertyChanged();
        }
    }

    /// <summary>是否显示开关控件。</summary>
    public bool ShowToggle => CanEdit && EditorKind == ModConfigEditorKind.Toggle;

    /// <summary>路径字段的「浏览…」通道（由页面注入：视图层把它接到系统的目录选择器）。</summary>
    /// <remarks>用委托而不是接口，是为了让 ViewModels 层不依赖任何平台类型；为空时不显示按钮。</remarks>
    public Func<ModConfigFieldRowViewModel, Task<string?>>? PathPicker { get; set; }

    /// <summary>是否可以点「浏览…」挑路径。</summary>
    public bool CanBrowse => CanEdit
        && EditorKind == ModConfigEditorKind.PathPicker
        && PathPicker is not null;

    /// <summary>是否显示下拉控件。</summary>
    public bool ShowDropdown => CanEdit && EditorKind == ModConfigEditorKind.Dropdown;

    /// <summary>是否显示文本框（含数字框、路径框、列表多行框）。</summary>
    public bool ShowTextEditor => CanEdit && EditorKind is ModConfigEditorKind.TextField
        or ModConfigEditorKind.NumberField
        or ModConfigEditorKind.PathPicker
        or ModConfigEditorKind.ListEditor;

    /// <summary>文本框是否多行（list 逐行一项）。</summary>
    public bool IsMultilineEditor => EditorKind == ModConfigEditorKind.ListEditor;

    /// <summary>是否显示取色编辑（文本 + 色块预览）。</summary>
    public bool ShowColorEditor => CanEdit && EditorKind == ModConfigEditorKind.ColorPicker;

    /// <summary>是否显示按键捕获（点击后按组合键，Esc 清空）。</summary>
    public bool ShowKeyCapture => CanEdit && EditorKind == ModConfigEditorKind.KeyCapture;

    /// <summary>按键捕获的行内提示：说清"既可以直接输入键名，也可以按住修饰键捕获"。</summary>
    public string KeyCaptureHint => ShowKeyCapture
        ? "可直接输入键名，或按住 Ctrl/Alt 再按键捕获；Esc 清空。"
        : string.Empty;

    /// <summary>是否显示按键捕获提示。</summary>
    public bool HasKeyCaptureHint => KeyCaptureHint.Length > 0;

    /// <summary>色块预览用的十六进制颜色（无法识别时为空串，视图据此隐藏色块）。</summary>
    public string ColorPreviewHex => ModConfigColorText.TryNormalize(EditValue, out var hex) ? hex : string.Empty;

    /// <summary>是否显示色块预览。</summary>
    public bool HasColorPreview => ColorPreviewHex.Length > 0;

    /// <summary>颜色写法提示：空值/识别不了时给一句人话，识别成功时不占版面。</summary>
    public string ColorNote => ModConfigColorText.Describe(EditValue);

    /// <summary>是否显示颜色写法提示。</summary>
    public bool HasColorNote => ColorNote.Length > 0;

    /// <summary>是否只读展示（此时 ReadOnlyNote 一定有内容，不会静默禁用）。</summary>
    public bool ShowReadOnlyValue => !ShowToggle && !ShowDropdown && !ShowTextEditor
        && !ShowColorEditor && !ShowKeyCapture;

    /// <summary>只读/暂不支持时的原因说明。</summary>
    public string ReadOnlyNote => ShowReadOnlyValue ? BuildReadOnlyNote() : string.Empty;

    /// <summary>是否要显示只读原因。</summary>
    public bool HasReadOnlyNote => ReadOnlyNote.Length > 0;

    /// <summary>写盘成功后记为新的基线（"放弃改动"回到它，而不是回到读盘前的旧值）。</summary>
    public void MarkSaved()
    {
        _savedText = WireValue;
        OnPropertyChanged(nameof(HasPendingChange));
    }

    /// <summary>丢弃草稿，回到基线值。</summary>
    public void ResetEdit() => EditValue = ToEditorText(_savedText ?? Field.Value);

    /// <summary>把 wire 值装回编辑器（页面重读后重新铺未保存的草稿时用）。</summary>
    /// <param name="wireValue">配置文件里的单行写法。</param>
    public void ApplyDraft(string? wireValue) => EditValue = ToEditorText(wireValue);

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

    partial void OnEditValueChanged(string value)
    {
        OnPropertyChanged(nameof(WireValue));
        OnPropertyChanged(nameof(HasPendingChange));
        OnPropertyChanged(nameof(IsToggleOn));
        OnPropertyChanged(nameof(ColorPreviewHex));
        OnPropertyChanged(nameof(HasColorPreview));
        OnPropertyChanged(nameof(ColorNote));
        OnPropertyChanged(nameof(HasColorNote));
    }

    /// <summary>命令：打开目录选择器挑一个路径，挑到就填进草稿（取消则原样保留）。</summary>
    [RelayCommand]
    private async Task BrowseAsync()
    {
        if (PathPicker is not { } picker || !CanBrowse)
        {
            return;
        }

        var picked = await picker(this).ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(picked))
        {
            EditValue = picked;
        }
    }

    private string BaselineValue => _savedText ?? Field.Value ?? string.Empty;

    private string BuildReadOnlyNote()
    {
        if (!IsFileWritable)
        {
            return string.IsNullOrWhiteSpace(_readonlyReason)
                ? "只读：所在配置文件当前不可写。"
                : "只读：" + _readonlyReason;
        }

        if (Field.Type == ModConfigFieldType.ReadOnly)
        {
            return "只读字段：值由 mod 自己维护，页面不提供修改。";
        }

        return "本版只读：这类字段还没有编辑控件。";
    }

    private static IReadOnlyList<string> BuildChoices(ModConfigFieldState field)
    {
        var declared = field.Values ?? [];
        var current = field.Value?.Trim();
        if (declared.Count == 0 || string.IsNullOrEmpty(current))
        {
            return declared;
        }

        // 磁盘上的值不在白名单里时补一项，免得下拉框一片空白（写盘时服务层还会按白名单校验）。
        return declared.Any(value => string.Equals(value, current, StringComparison.OrdinalIgnoreCase))
            ? declared
            : [current, .. declared];
    }

    /// <summary>wire 值 → 编辑器文本（list 拆成每行一项）。</summary>
    private string ToEditorText(string? value)
        => EditorKind == ModConfigEditorKind.ListEditor ? string.Join("\n", SplitListItems(value)) : value ?? string.Empty;

    /// <summary>编辑器文本 → wire 值（list 拼回单行：配置文件一行一键，值里不能带换行）。</summary>
    private static string ToWireText(string? text) => string.Join(", ", SplitListItems(text));

    private static IReadOnlyList<string> SplitListItems(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        return text.Split(['\n', '\r', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
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
