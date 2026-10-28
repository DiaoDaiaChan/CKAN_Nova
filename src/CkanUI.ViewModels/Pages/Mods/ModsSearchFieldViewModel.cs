using CommunityToolkit.Mvvm.ComponentModel;

namespace CkanUI.ViewModels.Pages.Mods;

/// <summary>
/// 筛选面板里一个字段的控件形态（12 篇 F-21）。
/// </summary>
public enum ModsSearchFieldKind
{
    /// <summary>文本框：值原样进查询串；字段自带前缀时按 <c>前缀:值</c> 拼装。</summary>
    Text = 0,

    /// <summary>三态下拉（不限 / 是 / 否）：落成 <c>is:x</c> 或 <c>not:x</c>。</summary>
    State = 1,
}

/// <summary>
/// 图形化筛选面板里的一个字段（12 篇 F-20/F-21）。
/// 只持有"用户填了什么"，把"怎么拼成查询语法"完全交给服务层的
/// <c>IModSearchFormatter</c>，视图模型不复制前缀规则。
/// </summary>
public sealed partial class ModsSearchFieldViewModel : ObservableObject
{
    /// <summary>创建一个文本字段。</summary>
    public static ModsSearchFieldViewModel CreateText(string key, string label, string hint, string prefix = "") =>
        new(key, label, ModsSearchFieldKind.Text, hint, prefix);

    /// <summary>创建一个三态字段（不限 / 是 / 否）。</summary>
    public static ModsSearchFieldViewModel CreateState(string key, string label) =>
        new(key, label, ModsSearchFieldKind.State, "is:" + key, "is:");

    private ModsSearchFieldViewModel(
        string key,
        string label,
        ModsSearchFieldKind kind,
        string hint,
        string prefix)
    {
        Key = key;
        Label = label;
        Kind = kind;
        Hint = hint;
        Prefix = prefix;
    }

    /// <summary>字段标识：文本字段用语法前缀名，三态字段用 <c>is:</c> 后缀。</summary>
    public string Key { get; }

    /// <summary>面板上的中文名（与上游 <c>EditModSearchDetails</c> 的字段顺序一致）。</summary>
    public string Label { get; }

    /// <summary>占位提示，直接复用搜索框的语法写法。</summary>
    public string Hint { get; }

    /// <summary>该字段在查询串里的前缀（空串表示裸词）。</summary>
    public string Prefix { get; }

    /// <summary>控件形态。</summary>
    public ModsSearchFieldKind Kind { get; }

    /// <summary>文本字段的取值。</summary>
    [ObservableProperty]
    private string _text = string.Empty;

    /// <summary>三态字段的取值下标：0 不限、1 是、2 否。</summary>
    [ObservableProperty]
    private int _stateIndex;

    /// <summary>是否文本字段（视图按它二选一渲染控件）。</summary>
    public bool IsText => Kind == ModsSearchFieldKind.Text;

    /// <summary>是否三态字段。</summary>
    public bool IsState => Kind == ModsSearchFieldKind.State;

    /// <summary>三态字段的语义值，null 表示不限。</summary>
    public bool? StateValue => StateIndex switch
    {
        1 => true,
        2 => false,
        _ => null,
    };

    /// <summary>该字段是否已参与筛选。</summary>
    public bool HasValue => Kind == ModsSearchFieldKind.Text
        ? !string.IsNullOrWhiteSpace(Text)
        : StateValue.HasValue;

    /// <summary>清空本字段。</summary>
    public void Clear()
    {
        if (Kind == ModsSearchFieldKind.Text)
        {
            Text = string.Empty;
        }
        else
        {
            StateIndex = 0;
        }
    }

    partial void OnStateIndexChanged(int value)
    {
        OnPropertyChanged(nameof(StateValue));
        OnPropertyChanged(nameof(HasValue));
    }

    partial void OnTextChanged(string value) => OnPropertyChanged(nameof(HasValue));
}
