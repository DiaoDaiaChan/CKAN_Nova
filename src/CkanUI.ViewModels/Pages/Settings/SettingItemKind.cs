namespace CkanUI.ViewModels.Pages.Settings;

/// <summary>
/// 设置项的编辑形态（决定视图用哪个控件、走哪条校验）。
/// 与「持久化位置」正交：同一个 kind 既可能是 Core 的键，也可能是 UI 自有字段。
/// </summary>
public enum SettingItemKind
{
    /// <summary>单行文本。</summary>
    Text,

    /// <summary>绝对路径（空值表示使用默认目录）。</summary>
    Path,

    /// <summary>整数（可带上下限；空值表示使用默认值）。</summary>
    Integer,

    /// <summary>布尔开关（编辑空间里存 "true"/"false"）。</summary>
    Boolean,

    /// <summary>单选（取值必须落在 <see cref="SettingItemViewModel.Choices"/> 之内）。</summary>
    Choice,

    /// <summary>多行列表：每行一个条目，写入配置时序列化成 JSON 字符串数组（02 篇 §3）。</summary>
    LineList,
}
