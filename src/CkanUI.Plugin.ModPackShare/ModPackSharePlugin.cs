using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Plugin.ModPackShare;

/// <summary>「整合包配置」页签插件入口：导入 .ckan 整合包并按配置档切换。</summary>
/// <remarks>
/// 插件本身不持有任何进程内资源：配置档列表、游戏是否在跑、.ckan 解析都发生在宿主的服务里，
/// 页面打开时才去问。因此 <see cref="Initialize"/> / <see cref="Deinitialize"/> 只做空实现，
/// 停用再启用（Initialize → Deinitialize → Initialize）天然可重入。
/// </remarks>
public sealed class ModPackSharePlugin : ICkanUiPlugin
{
    /// <summary>插件唯一标识。</summary>
    public const string PluginId = "com.ckanui.modpackshare";

    /// <summary>页签导航键。</summary>
    public const string PageKey = "modpack-profiles";

    /// <summary>插件版本。</summary>
    public const string PluginVersion = "1.0.0";

    /// <summary>页面视图模型的完整类型名（宿主按名字跨 ALC 解析）。</summary>
    public const string ViewModelTypeName = "CkanUI.Plugin.ModPackShare.UI.ModPackSharePageViewModel";

    private static readonly PluginPageDescriptor[] PageList =
    {
        // Glyph 用宿主已有的 Archive 字形（NavGlyphToGeometryConverter 里已有），
        // 不新增图标资源：整合包 = 归档包，语义也对得上。
        new(
            PageKey,
            "整合包配置",
            ViewModelTypeName,
            Glyph: "Archive",
            Section: "modpacks",
            Order: 10),
    };

    /// <inheritdoc />
    public string Id => PluginId;

    /// <inheritdoc />
    public string Name => "整合包分享";

    /// <inheritdoc />
    public string Version => PluginVersion;

    /// <inheritdoc />
    public string Author => "CKAN UI";

    /// <inheritdoc />
    public string Description => "把 .ckan 整合包导入成独立配置档，并在原版 / 整合包之间随时切换，不必复制游戏实例。";

    /// <inheritdoc />
    public IReadOnlyList<PluginPageDescriptor> Pages => PageList;

    /// <inheritdoc />
    public string MinimumHostVersion => string.Empty;

    /// <inheritdoc />
    /// <remarks>页面每次挂到可视树时自己刷新；插件不订阅全局事件，所以这里不需要做什么。</remarks>
    public void Initialize()
    {
    }

    /// <inheritdoc />
    /// <remarks>与 <see cref="Initialize"/> 对称：没有需要释放的订阅或句柄。</remarks>
    public void Deinitialize()
    {
    }
}
