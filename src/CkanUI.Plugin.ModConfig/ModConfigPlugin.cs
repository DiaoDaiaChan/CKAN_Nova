using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Plugin.ModConfig;

/// <summary>
/// 「Mod 配置」页签插件入口（首方内置插件，契约 docs/design/plugins.md §11.11）。
/// </summary>
/// <remarks>
/// <para>本插件只搬页面：读盘合成、写盘、快照恢复、适配器导入等业务仍在宿主服务里
/// （<c>CkanUI.Services/ModConfig/*</c>），页面靠构造函数注入拿服务。
/// 于是停用插件只丢页签，业务能力本身不受影响。</para>
/// <para>页签 <c>Section = "mods"</c>：这一页是「模组」分节内部的页签，不是侧栏顶格入口。
/// 宿主把它排进该分节的页签条（顺序由宿主 SectionSpecs 的槽位决定，<c>Order</c> 只用于槽位之外的补充页）。</para>
/// </remarks>
public sealed class ModConfigPlugin : ICkanUiPlugin
{
    /// <summary>插件唯一标识（反向域名）。</summary>
    public const string PluginId = "com.ckanui.modconfig";

    /// <summary>页签导航键（与宿主原内置键一致，搬迁对用户透明）。</summary>
    public const string PageKey = "modconfig";

    /// <summary>插件版本。</summary>
    public const string PluginVersion = "1.0.0";

    private static readonly PluginPageDescriptor[] PageList =
    {
        new(
            PageKey,
            "Mod 配置",
            typeof(ModConfigPageViewModel).FullName ?? nameof(ModConfigPageViewModel),
            TitleKey: "Ui_NavRail_ModConfig",
            Glyph: "Sliders",
            Section: "mods",
            Order: 50),
    };

    /// <inheritdoc />
    public string Id => PluginId;

    /// <inheritdoc />
    public string Name => "Mod 配置";

    /// <inheritdoc />
    public string Version => PluginVersion;

    /// <inheritdoc />
    public string Author => "ckan-ui";

    /// <inheritdoc />
    public string Description => "读取并编辑 mod 的配置文件：适配器字段编辑、写盘前快照、.bak 恢复与 adapter 包导入。";

    /// <inheritdoc />
    /// <remarks>空串 = 不限制宿主版本：页面只用 PageViewModel / IActivatable / IUnsavedChangesAware 三个稳定契约。</remarks>
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
