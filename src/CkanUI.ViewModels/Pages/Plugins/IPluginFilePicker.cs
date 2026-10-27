namespace CkanUI.ViewModels.Pages.Plugins;

/// <summary>
/// 选择插件 dll 的窄接口（12 篇 F-17 的「新增」按钮）。
/// </summary>
/// <remarks>
/// 与 <c>ICacheZipPicker</c> 同一个范式：ViewModels 层不许引用 Avalonia（工程约定 §5），
/// 文件选择器由视图侧实现（<c>AvaloniaPluginFilePicker</c>）并在视图的 <c>DataContextChanged</c> 里注入；
/// 测试直接塞一个返回固定路径的假实现，不需要真的弹对话框。
/// </remarks>
public interface IPluginFilePicker
{
    /// <summary>弹出文件选择框，返回用户选中的 dll 绝对路径；取消时返回 <c>null</c>。</summary>
    Task<string?> PickPluginFileAsync(CancellationToken ct = default);
}
