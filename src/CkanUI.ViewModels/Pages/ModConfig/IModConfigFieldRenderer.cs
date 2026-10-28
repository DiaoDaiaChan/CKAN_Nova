using CkanUI.Services.Abstractions.ModConfig;

namespace CkanUI.ViewModels.Pages.ModConfig;

/// <summary>
/// 字段渲染器（UI-CFG-01 的可扩展点，对应 CFG-04 的九种类型）。
/// </summary>
/// <remarks>
/// 页面骨架先实现 bool/int/float/enum/string 五种；path/color/keybinding/list 仍按
/// <see cref="ModConfigEditorKind.ReadOnlyText"/> 兜底展示，等各自取证到位后新增实现类注册即可，
/// 不需要改动页面或 XAML。
/// </remarks>
public interface IModConfigFieldRenderer
{
    /// <summary>负责的字段类型。</summary>
    ModConfigFieldType FieldType { get; }

    /// <summary>渲染形态。</summary>
    ModConfigEditorKind EditorKind { get; }

    /// <summary>把磁盘上的原值渲染成给人看的文本。</summary>
    /// <param name="field">字段快照。</param>
    string FormatValue(ModConfigFieldState field);
}
