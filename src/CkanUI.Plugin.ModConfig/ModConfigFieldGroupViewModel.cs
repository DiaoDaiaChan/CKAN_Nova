namespace CkanUI.Plugin.ModConfig;

/// <summary>字段分组（标题来自 // #### 分节注释；没有分组的字段落在空标题组里，视图不画表头）。</summary>
/// <param name="title">分组标题（可为空串）。</param>
/// <param name="rows">该组字段行。</param>
public sealed class ModConfigFieldGroupViewModel(string title, IReadOnlyList<ModConfigFieldRowViewModel> rows)
{
    /// <summary>分组标题。</summary>
    public string Title { get; } = title ?? string.Empty;

    /// <summary>该组字段行。</summary>
    public IReadOnlyList<ModConfigFieldRowViewModel> Rows { get; } = rows ?? [];

    /// <summary>是否要画分组表头。</summary>
    public bool HasTitle => Title.Length > 0;

    /// <summary>条数文案。</summary>
    public string CountText => "（" + Rows.Count + "）";
}
