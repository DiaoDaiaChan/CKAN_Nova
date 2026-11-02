namespace CkanUI.ViewModels;

/// <summary>
/// 空闲预热用的只读旁路：把导航键解析成页面视图模型，但不激活、不改当前页。
/// 单独拆成 partial 文件，避免和外壳主干（并行改动热点）互相踩。
/// </summary>
public sealed partial class ShellViewModel
{
    /// <summary>
    /// 解析页面视图模型，找不到返回 null；不激活、不改当前页。
    /// 仅供外壳空闲预热使用（首帧后把页面视图提前建出来）。
    /// </summary>
    public object? PeekPage(string key)
        => _navigation is Navigation.NavigationService service ? service.Peek(key) : null;
}
