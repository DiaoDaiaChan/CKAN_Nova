namespace CkanUI.Services.Abstractions;

/// <summary>
/// 插件页视图模型的标记接口（12 篇 F-17「往 UI 里加页签」）。
/// </summary>
/// <remarks>
/// <para>宿主外壳只认识两类页面：内置页（继承 <c>CkanUI.ViewModels.PageViewModel</c>）与插件页。
/// 插件不引用 <c>CkanUI.ViewModels</c>（契约程序集零依赖，工程约定 §5），因此不能继承内置基类；
/// 换成标记接口后，插件只引用 <c>CkanUI.Services.Abstractions</c> 就能声明页面，
/// 宿主按 ARC-03 的命名约定解析视图。</para>
/// <para>视图解析约定：视图模型与视图同程序集、同命名空间，只有类型名后缀不同
/// （<c>XxxPageViewModel</c> → <c>XxxPageView</c>）。视图必须有无参构造函数且是 Avalonia 控件，
/// 否则该页签回退到占位视图——导航不会因为插件缺视图而崩。</para>
/// </remarks>
public interface IPluginPage
{
}
