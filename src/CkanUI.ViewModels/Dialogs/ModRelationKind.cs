namespace CkanUI.ViewModels.Dialogs;

/// <summary>详情面板里一条关系（依赖/推荐/建议/冲突/替代）的种类（03 篇 §3.1 依赖树着色）。</summary>
public enum ModRelationKind
{
    /// <summary>硬依赖（不满足则装不上）。</summary>
    Dependency = 0,

    /// <summary>推荐项（默认勾选）。</summary>
    Recommendation = 1,

    /// <summary>建议项（默认不勾选）。</summary>
    Suggestion = 2,

    /// <summary>冲突项（不能共存）。</summary>
    Conflict = 3,

    /// <summary>替代项（provides/replaces）。</summary>
    Replacement = 4,
}
