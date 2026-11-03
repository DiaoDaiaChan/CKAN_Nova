using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Plugin.Rp1;

/// <summary>「RP-1 生涯日志」页签插件入口：读取 RP-1 存档并展示时间线。</summary>
public sealed class Rp1Plugin : ICkanUiPlugin
{
    /// <summary>插件唯一标识。</summary>
    public const string PluginId = "com.ckanui.rp1";

    /// <summary>页签导航键。</summary>
    public const string PageKey = "rp1-career";

    /// <summary>插件版本。</summary>
    public const string PluginVersion = "1.0.0";

    /// <summary>页面视图模型的完整类型名（宿主按名字跨 ALC 解析）。</summary>
    public const string ViewModelTypeName = "CkanUI.Plugin.Rp1.UI.Rp1CareerPageViewModel";

    private static readonly PluginPageDescriptor[] PageList =
    {
        new(
            PageKey,
            "RP-1 生涯日志",
            ViewModelTypeName,
            Glyph: "History",
            Section: "plugins",
            Order: 40),
    };

    /// <inheritdoc />
    public string Id => PluginId;

    /// <inheritdoc />
    public string Name => "RP-1 生涯日志";

    /// <inheritdoc />
    public string Version => PluginVersion;

    /// <inheritdoc />
    public string Author => "CKAN UI";

    /// <inheritdoc />
    public string Description => "读取 RP-1 存档，把合同、里程碑、项目、人员与预算周期整理成可按类别筛选的生涯时间线。";

    /// <inheritdoc />
    public IReadOnlyList<PluginPageDescriptor> Pages => PageList;

    /// <inheritdoc />
    public string MinimumHostVersion => string.Empty;

    /// <inheritdoc />
    /// <remarks>插件不持有进程内资源：存档在页面打开时读取。</remarks>
    public void Initialize()
    {
    }

    /// <inheritdoc />
    /// <remarks>与 <see cref="Initialize"/> 对称：停用再启用必须可重入。</remarks>
    public void Deinitialize()
    {
    }
}
