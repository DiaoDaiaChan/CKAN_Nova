using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Services.Abstractions;

/// <summary>
/// 第三方 UI 插件契约（12 篇 F-17 / 06 篇 UI-33）。
/// </summary>
/// <remarks>
/// <para>纯 BCL 类型：本程序集零依赖（工程约定 §5），因此插件只引用 <c>CkanUI.Services.Abstractions</c>
/// 就能编译，不需要 Avalonia、不需要 CKAN。视图通过 <see cref="PluginPageDescriptor.ViewModelTypeName"/>
/// 间接引用，由宿主解析。</para>
/// <para>与原版 <c>CKAN.GUI.IGUIPlugin</c>（<c>abstract class</c>，<c>GetName/GetVersion/Initialize/Deinitialize</c>）
/// 的差异：改成接口 + 元数据属性，理由是宿主需要在**不构造实例**的前提下就能显示插件自述——
/// 原版只能先构造再问名字，构造抛异常就什么都不剩。</para>
/// <para><b>宿主不承诺沙箱</b>：插件代码与宿主同进程、同权限运行，<c>Initialize</c> 能做的任何事
/// （读写文件、联网、P/Invoke）宿主都拦不住。默认不启用、必须用户显式激活，见 <c>docs/design/plugins.md</c>。</para>
/// </remarks>
public interface ICkanUiPlugin
{
    /// <summary>插件唯一标识（建议反向域名，如 <c>com.example.hello</c>）。空串视为无效插件。</summary>
    string Id { get; }

    /// <summary>显示名。</summary>
    string Name { get; }

    /// <summary>插件版本（自由字符串，宿主要求最小宿主版本时才需要解析）。</summary>
    string Version { get; }

    /// <summary>作者。</summary>
    string Author { get; }

    /// <summary>一句话描述（插件页直接显示）。</summary>
    string Description { get; }

    /// <summary>要求的最小宿主版本（如 <c>1.2.0</c>）；空串或无法解析都表示不限制。</summary>
    string MinimumHostVersion { get; }

    /// <summary>
    /// 插件声明的页签/工具项。默认空列表：只做后台工作（例如订阅日志）的插件不声明页签是合法的。
    /// </summary>
    IReadOnlyList<PluginPageDescriptor> Pages => Array.Empty<PluginPageDescriptor>();

    /// <summary>
    /// 激活插件（进入 Active）。宿主在 try/catch 里调用：抛异常只会让插件留在 Dormant 并显示原因。
    /// </summary>
    void Initialize();

    /// <summary>
    /// 停用插件（回到 Dormant）。必须能反复调用：<c>Initialize → Deinitialize → Initialize</c> 是重载的路径。
    /// </summary>
    void Deinitialize();
}
