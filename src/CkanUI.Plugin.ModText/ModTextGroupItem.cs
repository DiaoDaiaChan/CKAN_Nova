namespace CkanUI.Plugin.ModText;

/// <summary>
/// 左栏「按模组查看」的一条分组。词条来自 CKAN 元数据时，Key 是 normalized 里解析出的
/// <c>ckan:&lt;identifier&gt;:&lt;field&gt;</c> 的 identifier；老数据没有 identifier 时退回语言包 mod 名；
/// Key 为空表示「全部」。
/// </summary>
public sealed class ModTextGroupItem
{
    public ModTextGroupItem(string key, string displayName, int count, string countText, bool isAll)
    {
        Key = key;
        DisplayName = displayName;
        Count = count;
        CountText = countText;
        IsAll = isAll;
    }

    /// <summary>分组键；空字符串表示「全部」。</summary>
    public string Key { get; }

    /// <summary>界面上显示的名字。</summary>
    public string DisplayName { get; }

    /// <summary>该分组下的词条数。</summary>
    public int Count { get; }

    /// <summary>「N 条」的本地化文本。</summary>
    public string CountText { get; }

    /// <summary>是否为「全部」首项。</summary>
    public bool IsAll { get; }

    /// <inheritdoc />
    public override string ToString() => DisplayName;
}
