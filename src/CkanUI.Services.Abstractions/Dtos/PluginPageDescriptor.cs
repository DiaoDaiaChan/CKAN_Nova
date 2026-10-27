namespace CkanUI.Services.Abstractions.Dtos;

/// <summary>
/// 插件声明的一个页签/工具项（12 篇 F-17 的「往 UI 里加页签」）。
/// </summary>
/// <remarks>
/// <para>契约里只放字符串，不放视图类型：<c>CkanUI.Services.Abstractions</c> 零依赖（工程约定 §5），
/// 而视图类型在 <c>CkanUI.App</c>。宿主拿到 <see cref="ViewModelTypeName"/> 后按 ARC-03 的
/// <c>ViewLocator</c> 约定解析视图，插件因此可以只用契约程序集编写。</para>
/// <para><see cref="Key"/> 是导航键，必须与宿主既有页签不重名；重名由宿主在接线时拒绝（本层不做校验，
/// 因为「哪些键已被占用」是 Shell 的事实，不属于插件服务）。</para>
/// </remarks>
/// <param name="Key">导航键（小写短横线，如 <c>example-plugin</c>）。</param>
/// <param name="Title">页签标题（插件自述，宿主直接显示，不做本地化）。</param>
/// <param name="ViewModelTypeName">视图模型的程序集限定名；宿主用它反射出 VM 再交给 ViewLocator。</param>
public sealed record PluginPageDescriptor(string Key, string Title, string ViewModelTypeName);
