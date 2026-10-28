namespace CkanUI.ViewModels.Pages.ModConfig;

/// <summary>
/// 字段编辑器形态（CFG-04 的九种类型 → 控件族）。
/// 页面只按这个枚举选模板，不按字段类型写 switch，新增类型时不用改 XAML。
/// </summary>
public enum ModConfigEditorKind
{
    /// <summary>开关（bool）。</summary>
    Toggle,

    /// <summary>数字框（int/float，带 min/max/unit）。</summary>
    NumberField,

    /// <summary>下拉（enum，取自 adapter 的 values 白名单）。</summary>
    Dropdown,

    /// <summary>单行文本（string，可带正则校验）。</summary>
    TextField,

    /// <summary>路径选择（path，v1 只读展示）。</summary>
    PathPicker,

    /// <summary>颜色选择（color，v1 只读展示）。</summary>
    ColorPicker,

    /// <summary>按键捕获（keybinding，v1 只读展示）。</summary>
    KeyCapture,

    /// <summary>列表编辑（list，v1 只读展示）。</summary>
    ListEditor,

    /// <summary>只读展示（readonly 或暂未支持的类型）。</summary>
    ReadOnlyText,
}
