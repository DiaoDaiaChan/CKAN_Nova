using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Plugin.SpaceDock;

/// <summary>
/// 「SpaceDock」页签插件入口（首方内置插件，契约 docs/design/plugins.md §11.11）。
/// </summary>
/// <remarks>
/// 页面挂在模组分节下，与 CKAN 模组页并列；插件本身只声明描述符，
/// 浏览、下载、解压与服务调用全部由注入的宿主服务完成，停用插件不丢任何数据。
/// </remarks>
public sealed class SpaceDockPlugin : ICkanUiPlugin
{
    /// <summary>插件唯一标识（反向域名）。</summary>
    public const string PluginId = "com.ckanui.spacedock";

    /// <summary>插件版本。</summary>
    public const string PluginVersion = "1.0.0";

    private static readonly PluginPageDescriptor[] PageList =
    {
        new(
            "spacedock",
            "SpaceDock",
            typeof(SpaceDockPageViewModel).FullName ?? nameof(SpaceDockPageViewModel),
            TitleKey: "Ui_NavRail_SpaceDock",
            Glyph: "Globe",
            Section: "mods",
            Order: 60),
    };

    /// <inheritdoc />
    public string Id => PluginId;

    /// <inheritdoc />
    public string Name => "SpaceDock";

    /// <inheritdoc />
    public string Version => PluginVersion;

    /// <inheritdoc />
    public string Author => "ckan-ui";

    /// <inheritdoc />
    public string Description => "浏览并直接安装 SpaceDock 模组；与 CKAN 重复的条目默认隐藏。";

    /// <inheritdoc />
    public string MinimumHostVersion => string.Empty;

    /// <inheritdoc />
    public IReadOnlyList<PluginPageDescriptor> Pages => PageList;

    /// <inheritdoc />
    public void Initialize()
    {
        // 页面在 OnActivated 里按需加载，不占用启动时间。
    }

    /// <inheritdoc />
    public void Deinitialize()
    {
        // 与 Initialize 对称的空实现。
    }
}
