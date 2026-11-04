namespace CkanUI.Services.Abstractions.Home;

/// <summary>插件在宿主主页上声明的一块部件（卡片）。</summary>
/// <remarks>
/// <para>部件与页签是两条独立的通道：页签是「用户点进去看全貌」，部件是「不进页面就能看到一眼」。
/// 插件可以只声明其中一条，也可以两条都声明。宿主不会因为插件声明了部件就自动把它放到主页上，
/// 用户必须在主页的「添加部件」里显式选中——主页是用户的空间，不该被插件单方面占位。</para>
/// <para>描述符只放静态元数据；真正的数据在 <see cref="PluginHomeWidgetSnapshot"/> 里，由宿主按实例上下文拉取。</para>
/// </remarks>
/// <param name="Key">部件键（插件内唯一，忽略大小写）。</param>
/// <param name="Title">显示名（添加菜单与部件标题）。</param>
/// <param name="Description">添加菜单里的一句话说明。</param>
/// <param name="Glyph">宿主字形名；认不出时宿主退通用图标。</param>
/// <param name="PageKey">点「打开」时跳转的页签键；空表示不提供跳转。</param>
public sealed record PluginHomeWidgetDescriptor(
    string Key,
    string Title,
    string? Description = null,
    string? Glyph = null,
    string? PageKey = null);

/// <summary>宿主拉取部件数据时提供的当前实例上下文。</summary>
/// <param name="InstanceName">实例显示名；没有实例时为空。</param>
/// <param name="GameDir">游戏目录；没有实例或目录缺失时为空。</param>
/// <param name="GameVersion">游戏版本；未知时为空。</param>
public sealed record PluginHomeWidgetContext(
    string? InstanceName = null,
    string? GameDir = null,
    string? GameVersion = null);

/// <summary>部件里的一个统计值（例如「资金 4,575,691」）。</summary>
/// <param name="Label">指标名。</param>
/// <param name="Value">已经格式化好的值（宿主不再二次换算）。</param>
/// <param name="Tone">可选语气：<c>accent</c> / <c>warn</c> / <c>muted</c>；空为默认。</param>
public sealed record PluginHomeWidgetMetric(string Label, string Value, string? Tone = null);

/// <summary>部件里的一行内容（例如一条最近事件）。</summary>
/// <param name="Text">主文案。</param>
/// <param name="Detail">次要说明（时间、地点等）。</param>
/// <param name="Badge">小徽标（例如「里程碑」）。</param>
public sealed record PluginHomeWidgetLine(string Text, string? Detail = null, string? Badge = null);

/// <summary>一次部件数据快照。插件只返回已经算好的文本，宿主只负责排版。</summary>
/// <param name="Title">部件标题（可与描述符不同，例如带上存档名）。</param>
/// <param name="Subtitle">副标题。</param>
/// <param name="Metrics">统计值。</param>
/// <param name="Lines">内容行。</param>
/// <param name="Footer">页脚（更新时间、数据来源等）。</param>
/// <param name="Error">有值时宿主显示为错误提示；正常数据可以为空。</param>
public sealed record PluginHomeWidgetSnapshot(
    string Title,
    string? Subtitle = null,
    IReadOnlyList<PluginHomeWidgetMetric>? Metrics = null,
    IReadOnlyList<PluginHomeWidgetLine>? Lines = null,
    string? Footer = null,
    string? Error = null)
{
    /// <summary>空快照（插件没有数据时的合法返回）。</summary>
    public static PluginHomeWidgetSnapshot Empty { get; } = new(string.Empty);
}

/// <summary>一个可用部件的来源绑定：哪个插件提供了它。</summary>
/// <param name="PluginId">插件标识。</param>
/// <param name="PluginName">插件显示名。</param>
/// <param name="Widget">插件声明的描述符。</param>
public sealed record PluginHomeWidgetBinding(
    string PluginId,
    string PluginName,
    PluginHomeWidgetDescriptor Widget);
