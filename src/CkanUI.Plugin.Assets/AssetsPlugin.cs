using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Plugin.Assets;

/// <summary>
/// 「资产工作台」页签插件入口（首方内置插件，契约 docs/design/plugins.md §11.11）。
/// </summary>
/// <remarks>
/// <para>本插件只搬页面：解析、诊断、写入等业务仍在宿主服务里，页面靠构造函数注入拿服务。
/// 于是停用插件只丢页签，业务能力本身不受影响。</para>
/// <para>页签 <c>Section = "instances"</c>：这几页是「实例」分节内部的页签，不是侧栏顶格入口。
/// 宿主把它排进该分节的页签条（顺序由宿主 SectionSpecs 的槽位决定，<c>Order</c> 只用于槽位之外的补充页）。</para>
/// </remarks>
public sealed class AssetsPlugin : ICkanUiPlugin
{
    /// <summary>插件唯一标识（反向域名）。</summary>
    public const string PluginId = "com.ckanui.assets";

    /// <summary>插件版本。</summary>
    public const string PluginVersion = "1.0.0";

    private static readonly PluginPageDescriptor[] PageList =
    {
        new(
            "ships",
            "飞船",
            typeof(ShipsPageViewModel).FullName ?? nameof(ShipsPageViewModel),
            TitleKey: "Ui_NavRail_Ships",
            Glyph: "Rocket",
            Section: "instances",
            Order: 20),
        new(
            "flags",
            "旗帜",
            typeof(FlagsPageViewModel).FullName ?? nameof(FlagsPageViewModel),
            TitleKey: "Ui_NavRail_Flags",
            Glyph: "Flag",
            Section: "instances",
            Order: 30),
        new(
            "loadingimages",
            "加载图",
            typeof(LoadingImagesPageViewModel).FullName ?? nameof(LoadingImagesPageViewModel),
            TitleKey: "Ui_NavRail_LoadingScreens",
            Glyph: "Image",
            Section: "instances",
            Order: 40),
    };

    /// <inheritdoc />
    public string Id => PluginId;

    /// <inheritdoc />
    public string Name => "资产工作台";

    /// <inheritdoc />
    public string Version => PluginVersion;

    /// <inheritdoc />
    public string Author => "ckan-ui";

    /// <inheritdoc />
    public string Description => "实例目录里的可换资产：飞船（craft）、旗帜（flag）与加载图（loading image）。";

    /// <inheritdoc />
    /// <remarks>空串 = 不限制宿主版本：页面只用 PageViewModel / IActivatable / IPageProjectionTrim 三个稳定契约。</remarks>
    public string MinimumHostVersion => string.Empty;

    /// <inheritdoc />
    public IReadOnlyList<PluginPageDescriptor> Pages => PageList;

    /// <inheritdoc />
    public void Initialize()
    {
        // 没有后台工作：页面在 OnActivated 里按需加载。
    }

    /// <inheritdoc />
    public void Deinitialize()
    {
        // 与 Initialize 对称的空实现，保证 初始化→停用→初始化 的路径可重入。
    }
}
