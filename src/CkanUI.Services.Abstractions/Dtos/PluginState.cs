namespace CkanUI.Services.Abstractions.Dtos;

/// <summary>
/// 插件在宿主里的状态（12 篇 F-17 / 06 篇 UI-33）。
/// </summary>
/// <remarks>
/// 三态对应原版 <c>PluginController</c> 的两个集合加上一个失败位：
/// 原版把「加载失败的 dll」只写进日志（<c>log.WarnFormat</c>），用户在对话框里看不到任何痕迹；
/// 新宿主把它显式建模成 <see cref="Failed"/>，让插件页能给出可读原因。
/// </remarks>
public enum PluginState
{
    /// <summary>已加载但未激活：程序集已进入内存，插件的 <c>Initialize</c> 还没跑过。</summary>
    Dormant = 0,

    /// <summary>已激活：<c>Initialize</c> 成功返回，插件可以向宿主提供页签/工具项。</summary>
    Active = 1,

    /// <summary>加载失败：程序集、类型或版本校验没通过，原因见 <see cref="PluginInfo.FailureReason"/>。</summary>
    Failed = 2,
}
