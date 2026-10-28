namespace CkanUI.Services.Abstractions.Dtos;

/// <summary>
/// 插件声明的一个工具项（12 篇 F-17 的「往现有工具条插按钮」）。
/// </summary>
/// <remarks>
/// <para>页签是「加一页」，工具项是「加一个按钮」：前者换导航目标，后者挂在宿主既有页面的工具条上。
/// 契约里依旧只放字符串，插件不引用 <c>CkanUI.ViewModels</c>（工程约定 §5）。</para>
/// <para>与页签的区别：页签由宿主反射构造视图模型，工具项不构造任何类型——按钮点下去时宿主回调
/// 插件自己的 <see cref="ICkanUiPlugin.ExecuteToolItemAsync(string, System.Threading.CancellationToken)"/>。
/// 这样插件不必为了一个按钮去写一个空视图模型，也不会因为反射失败而让按钮静默消失。</para>
/// <para><see cref="TargetPageKey"/> 必须命中宿主某页的工具条（当前支持 <c>mods</c>）。键不认识时按钮不出现，
/// 但插件本身照常工作——这是「装错了页面」而不是「插件坏了」。</para>
/// </remarks>
/// <param name="TargetPageKey">目标页键（宿主导航键，如 <c>mods</c>；忽略大小写）。</param>
/// <param name="Key">工具项键（插件内唯一，回调时原样传回；忽略大小写）。</param>
/// <param name="Title">按钮文字（插件自述，宿主直接显示，不做本地化）。</param>
/// <param name="ToolTip">悬停提示；<c>null</c> 时宿主显示「插件名 · 工具项键」。</param>
public sealed record PluginToolItemDescriptor(string TargetPageKey, string Key, string Title, string? ToolTip = null);
