using CkanUI.Services.Abstractions.ModConfig;

namespace CkanUI.Plugin.ModConfig;

/// <summary>
/// 字段渲染器（UI-CFG-01 的可扩展点，对应 CFG-04 的九种类型）。
/// </summary>
/// <remarks>
/// 九种类型各有一个实现类（bool/int/float/enum/string/path/list/color/keybinding）：
/// 前七种给出可编辑的 <see cref="ModConfigEditorKind"/>，color/keybinding 给只读展示形态，
/// 由页面在行内写明"本版只读"的原因。新增类型只需加实现类并 <see cref="ModConfigFieldRendererRegistry.Register"/>，
/// 不用改页面或 XAML。
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
