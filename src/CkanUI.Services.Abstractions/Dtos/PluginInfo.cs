namespace CkanUI.Services.Abstractions.Dtos;

/// <summary>
/// 插件的一行快照（12 篇 F-17 / 06 篇 UI-33）。
/// </summary>
/// <remarks>
/// <para>只读投影：插件页每次整体替换两栏列表，不做增量更新（与 04 篇 §2.1 的列表口径一致）。</para>
/// <para>元数据是插件自己给的（<c>ICkanUiPlugin</c> 的 getter），因此可能是空串甚至是坏值：
/// 服务层在读取时就做了兜底，这里出现的字段已经是「读出来且非空」的版本，读不到时给空串而不是 null。</para>
/// </remarks>
/// <param name="Id">插件唯一标识（宿主用它做激活/重载/卸载的键）。</param>
/// <param name="Name">显示名。</param>
/// <param name="Version">插件版本（插件自述字符串，不强制 semver）。</param>
/// <param name="Author">作者。</param>
/// <param name="Description">一句话描述。</param>
/// <param name="MinimumHostVersion">要求的最小宿主版本；空串表示不限制。</param>
/// <param name="AssemblyPath">来源 dll 的绝对路径（原版对话框只显示 <c>ToString()</c>，这里保留路径便于排障）。</param>
/// <param name="State">当前状态。</param>
/// <param name="FailureReason">失败原因（仅 <see cref="PluginState.Failed"/> 时有意义）。</param>
public sealed record PluginInfo(
    string Id,
    string Name,
    string Version,
    string Author,
    string Description,
    string MinimumHostVersion,
    string AssemblyPath,
    PluginState State,
    string? FailureReason = null)
{
    /// <summary>插件声明的页签/工具项；默认空列表（不声明页签的插件是合法的）。</summary>
    public IReadOnlyList<PluginPageDescriptor> Pages { get; init; } = Array.Empty<PluginPageDescriptor>();

    /// <summary>是否已激活。</summary>
    public bool IsActive => State == PluginState.Active;

    /// <summary>「名字 - 版本」摘要（对齐原版 <c>IGUIPlugin.ToString()</c> 的显示形态）。</summary>
    public string DisplayName => string.IsNullOrEmpty(Version) ? Name : Name + " - " + Version;

    /// <summary>空列表（服务层初始状态共用）。</summary>
    public static IReadOnlyList<PluginInfo> Empty { get; } = Array.Empty<PluginInfo>();
}
