using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Plugin.Performance;

/// <summary>
/// 「性能」页签插件入口（第三方式插件的试点实现，契约 docs/design/plugins.md §6.1）。
/// </summary>
/// <remarks>
/// <para>本类只做三件事：报元数据、声明一个页签、把生命周期回调做成空实现。
/// 真正的遥测轮询跟着页面实例走（<see cref="PerformancePageViewModel"/>），
/// 因为"没人看的时候不该每秒读盘"——插件页是按导航按需构造的。</para>
/// <para><see cref="Initialize"/> / <see cref="Deinitialize"/> 必须能反复成对调用（宿主重载路径），
/// 这里没有要停的后台资源，因此两个方法都是幂等的空实现，而不是抛 NotSupported。</para>
/// </remarks>
public sealed class PerformancePlugin : ICkanUiPlugin
{
    /// <summary>插件唯一标识（反向域名）。</summary>
    public const string PluginId = "com.ckanui.performance";

    /// <summary>页签导航键（不得与宿主内置页签重名）。</summary>
    public const string PageKey = "performance";

    /// <summary>插件版本。</summary>
    public const string PluginVersion = "1.0.0";

    private static readonly PluginPageDescriptor[] PageList =
    {
        // Glyph 取宿主的「速度表」字形：插件页签得跟「插件」入口的七巧板图标区分开。
        new(PageKey, "性能", typeof(PerformancePageViewModel).FullName ?? nameof(PerformancePageViewModel), Glyph: "Performance"),
    };

    /// <inheritdoc />
    public string Id => PluginId;

    /// <inheritdoc />
    public string Name => "性能";

    /// <inheritdoc />
    public string Version => PluginVersion;

    /// <inheritdoc />
    public string Author => "ckan-ui";

    /// <inheritdoc />
    public string Description => "KSP 性能遥测（KPP 文件桥）：帧指标、耗时分解、GPU、热点榜、诊断卡与内存垫高。";

    /// <inheritdoc />
    /// <remarks>空串 = 不限制宿主版本：本页只用到 <see cref="ICkanUiPlugin"/> 与 <see cref="IPluginPage"/>，
    /// 不依赖宿主的任何具体实现，硬写一个下限反而会在版本号变动时误拦。</remarks>
    public string MinimumHostVersion => string.Empty;

    /// <inheritdoc />
    public IReadOnlyList<PluginPageDescriptor> Pages => PageList;

    /// <inheritdoc />
    public void Initialize()
    {
        // 没有后台工作要启动：轮询由页面实例自己管理生命周期。
    }

    /// <inheritdoc />
    public void Deinitialize()
    {
        // 与 Initialize 对称的空实现，保证 初始化→停用→初始化 的路径可重入。
    }
}
