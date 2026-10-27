namespace CkanUI.Services.Abstractions.Dtos;

/// <summary>
/// 一个 CKAN 模组标签组的只读投影（12 篇 F-24，上游 <c>CKAN.Types.Labels.ModuleLabel</c>）。
/// </summary>
/// <remarks>
/// <para>十项可编辑属性与上游 <c>EditLabelsDialog</c> 一一对应：名称、背景色、隐藏于其它过滤器、
/// 变更时通知、变更时移除、安装时提醒、安装时移除、锁定版本、忽略缺失文件，另外附加"是否全局标签"
/// （<c>InstanceName == null</c>）与当前实例的成员数。</para>
/// <para>铁律：DTO 不出现任何 <c>CKAN.*</c> 类型，颜色以 <c>#RRGGBB</c> 字符串跨界，
/// 视图模型不持有活的 <c>ModuleLabel</c>。</para>
/// </remarks>
public sealed record LabelInfo(
    string Name = "",
    string? ColorHex = null,
    bool IsGlobal = true,
    bool HideFromOtherFilters = false,
    bool NotifyOnChanges = false,
    bool RemoveOnChanges = false,
    bool AlertOnInstall = false,
    bool RemoveOnInstall = false,
    bool HoldVersion = false,
    bool IgnoreMissingFiles = false,
    int ModuleCount = 0)
{
    /// <summary>只有名字、没有任何属性的标签（用于"该标签组当前实例不可用"的占位）。</summary>
    public static LabelInfo Named(string name) =>
        new(name, null, true, false, false, false, false, false, false, false, 0);

    /// <summary>是否带背景色（视图据此决定是否上色，而不是只看 null）。</summary>
    public bool HasColor => !string.IsNullOrWhiteSpace(ColorHex);
}
