namespace CkanUI.ViewModels.Pages.Instances;

/// <summary>
/// 目录选择器（03 篇 §8 D-06「路径校验」）：VM 禁止碰 <c>File.*</c>/<c>Directory.*</c> 与平台 API（04 篇 §1），
/// 因此把"弹系统目录选择框"这件事交给宿主实现（App 层用 Avalonia <c>StorageProvider</c>）。
/// </summary>
/// <remarks>
/// 之所以是可选属性而不是构造参数：<see cref="InstancesPageViewModel"/> 必须能由 DI 容器按类型构造（简报 §9），
/// 而本接口的实现在 App 层、注册在 Lead 的 <c>ServiceRegistration</c> 里；用属性注入可以让二者解耦，
/// headless 测试直接塞假实现。
/// </remarks>
public interface IInstanceFolderPicker
{
    /// <summary>弹出目录选择框；用户取消返回 null。</summary>
    Task<string?> PickFolderAsync(string title, string? startPath = null);
}
