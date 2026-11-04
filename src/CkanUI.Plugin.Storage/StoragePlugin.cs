using CkanUI.Plugin.Storage.UI;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Plugin.Storage;

/// <summary>「存储管理」页签插件入口：多实例磁盘占用总览与配置档去重。</summary>
public sealed class StoragePlugin : ICkanUiPlugin
{
    /// <summary>插件唯一标识。</summary>
    public const string PluginId = "com.ckanui.storage";

    /// <summary>页签导航键。</summary>
    public const string PageKey = "storage";

    /// <summary>插件版本。</summary>
    public const string PluginVersion = "1.0.0";

    /// <summary>页面视图模型的完整类型名（宿主按名字跨 ALC 解析）。</summary>
    public const string ViewModelTypeName = "CkanUI.Plugin.Storage.UI.StoragePageViewModel";

    private static readonly PluginPageDescriptor[] PageList =
    {
        new(
            PageKey,
            "存储",
            ViewModelTypeName,
            Glyph: "Storage",
            Section: "cache",
            Order: 20),
    };

    /// <inheritdoc />
    public string Id => PluginId;

    /// <inheritdoc />
    public string Name => "存储管理";

    /// <inheritdoc />
    public string Version => PluginVersion;

    /// <inheritdoc />
    public string Author => "CKAN UI";

    /// <inheritdoc />
    public string Description => "管理多个 KSP 实例的磁盘占用：GameData、CKAN 与各配置档体积，并把重复的配置档文件合并成硬链接。";

    /// <inheritdoc />
    public IReadOnlyList<PluginPageDescriptor> Pages => PageList;

    /// <inheritdoc />
    public string MinimumHostVersion => string.Empty;

    /// <inheritdoc />
    /// <remarks>插件不持进程内资源：扫描在页面打开时按需执行，页面关闭即停止。</remarks>
    public void Initialize()
    {
    }

    /// <inheritdoc />
    /// <remarks>与 <see cref="Initialize"/> 对称：停用再启用必须可重入。</remarks>
    public void Deinitialize()
    {
    }
}
