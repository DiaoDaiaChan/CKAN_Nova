namespace CkanUI.Services.Abstractions.KspConfig;

/// <summary>
/// KSP 游戏配置项模型。
/// </summary>
public sealed record KspSettingItem
{
    /// <summary>设置键名（与 settings.cfg 中的键完全一致，如 MASTER_VOLUME）。</summary>
    public required string Key { get; init; }

    /// <summary>当前值。</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>读盘时的原始值（用于脏标记检测与重置）。</summary>
    public required string OriginalValue { get; init; }

    /// <summary>官方本地化词条键（如 #autoLOC_146154），可空。</summary>
    public string? LocKey { get; init; }

    /// <summary>显示名称（优先从 Squad 官方 dictionary.cfg 提取，缺失时采用优雅兜底）。</summary>
    public required string DisplayName { get; init; }

    /// <summary>描述或提示说明。</summary>
    public string? Description { get; init; }

    /// <summary>配置分组。</summary>
    public KspSettingCategory Category { get; init; } = KspSettingCategory.Other;

    /// <summary>配置值编辑类型。</summary>
    public KspSettingType SettingType { get; init; } = KspSettingType.Text;

    /// <summary>预设选项候选列表（供 ComboBox 下拉选择）。</summary>
    public IReadOnlyList<KspOptionEntry>? Options { get; init; }

    /// <summary>浮点/整数数值下限。</summary>
    public double? MinValue { get; init; }

    /// <summary>浮点/整数数值上限。</summary>
    public double? MaxValue { get; init; }

    /// <summary>滑块调节步长。</summary>
    public double? Step { get; init; }

    /// <summary>排序优先级权重（数值越大越排在前面，用于将常用核心设置置顶）。</summary>
    public int Priority { get; init; }

    /// <summary>是否已发生修改。</summary>
    public bool IsModified => !string.Equals(Value, OriginalValue, StringComparison.Ordinal);
}

/// <summary>
/// 预设选项定义（显示名与实际 cfg 值的配对）。
/// </summary>
public sealed record KspOptionEntry(string Value, string Label);
