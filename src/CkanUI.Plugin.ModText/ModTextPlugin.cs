using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Plugin.ModText;

/// <summary>
/// 「模组翻译」页签插件入口（首方内置插件，契约 docs/design/plugins.md §11.9）。
/// </summary>
/// <remarks>
/// <para>本插件只搬「页面」：词库、策略、校验、服务实现（<c>CkanUI.ModText</c> 与
/// <c>CkanUI.Services.ModText</c>）仍在宿主里，页面用构造函数注入 <see cref="IModTextService"/>。
/// 于是关掉插件只丢一个页签，翻译管线本身不受影响。</para>
/// <para>标题走本地化键 <c>Ui_NavRail_ModText</c>：页面文案有 800+ 条键，导航标题不能因为搬进插件就退回硬编码中文。</para>
/// </remarks>
public sealed class ModTextPlugin : ICkanUiPlugin
{
    /// <summary>插件唯一标识（反向域名）。</summary>
    public const string PluginId = "com.ckanui.modtext";

    /// <summary>页签导航键（与宿主原内置键一致，搬迁对用户透明）。</summary>
    public const string PageKey = "modtext";

    /// <summary>插件版本。</summary>
    public const string PluginVersion = "1.0.0";

    private static readonly PluginPageDescriptor[] PageList =
    {
        new(
            PageKey,
            "模组翻译",
            typeof(ModTextPageViewModel).FullName ?? nameof(ModTextPageViewModel),
            TitleKey: "Ui_NavRail_ModText",
            // 图标沿用宿主原本给「模组翻译」的内置字形（Material translate），搬迁后视觉不变。
            Glyph: "Translate"),
    };

    /// <inheritdoc />
    public string Id => PluginId;

    /// <inheritdoc />
    public string Name => "模组翻译";

    /// <inheritdoc />
    public string Version => PluginVersion;

    /// <inheritdoc />
    public string Author => "ckan-ui";

    /// <inheritdoc />
    public string Description => "CKAN 元数据与模组文本的翻译词库管理：词条检索、人工覆盖、UniversalI18n 同步。";

    /// <inheritdoc />
    /// <remarks>空串 = 不限制宿主版本：页面只用 PageViewModel / IActivatable / IPageProjectionTrim 三个稳定契约。</remarks>
    public string MinimumHostVersion => string.Empty;

    /// <inheritdoc />
    public IReadOnlyList<PluginPageDescriptor> Pages => PageList;

    /// <inheritdoc />
    public void Initialize()
    {
        // 没有后台工作：词库由页面在 OnActivated 里按需加载。
    }

    /// <inheritdoc />
    public void Deinitialize()
    {
        // 与 Initialize 对称的空实现，保证 初始化→停用→初始化 的路径可重入。
    }
}
