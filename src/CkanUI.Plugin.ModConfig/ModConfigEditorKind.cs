namespace CkanUI.Plugin.ModConfig;

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

    /// <summary>路径编辑（path）：文本框直接改，旁边另有「浏览…」按钮调系统目录选择器。</summary>
    PathPicker,

    /// <summary>颜色选择（color）：十六进制文本 + 色块预览；写法不受限，认不出来仍按原文写入。</summary>
    ColorPicker,

    /// <summary>按键捕获（keybinding）：点击后按住 Ctrl/Alt 再按主键写入组合键，也可直接打字。</summary>
    KeyCapture,

    /// <summary>列表编辑（list）：逐行文本，写盘时拼成单行（配置文件一行一键，值里不能带换行）。</summary>
    ListEditor,

    /// <summary>只读展示（readonly 或暂未支持的类型）。</summary>
    ReadOnlyText,
}
