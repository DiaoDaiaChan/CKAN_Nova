namespace CkanUI.ViewModels.Dialogs;

/// <summary>
/// 详情面板关系树的一行（03 篇 §3.1 "依赖树（已满足/缺失着色）"）。
/// <see cref="IsSatisfied"/> 由服务层按注册表快照判定，VM 只负责渲染，颜色留给视图。
/// </summary>
public sealed record ModRelationItem(
    string Identifier,
    string? Name = null,
    ModRelationKind Kind = ModRelationKind.Dependency,
    bool IsSatisfied = false)
{
    /// <summary>展示文本："显示名 (标识)"，没有显示名时只给标识。</summary>
    public string DisplayText => string.IsNullOrWhiteSpace(Name) ? Identifier : Name + " (" + Identifier + ")";

    /// <summary>满足状态文案。03 篇 §9：状态不能只靠颜色传达，所以同时给文字。</summary>
    public string StatusText => IsSatisfied ? "已满足" : "缺失";
}
