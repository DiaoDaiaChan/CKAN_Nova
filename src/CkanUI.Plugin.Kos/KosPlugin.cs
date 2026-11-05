using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Plugin.Kos;

/// <summary>「kOS 脚本」页签插件入口：管理 Archive 卷（Ships/Script/*.ks）。</summary>
public sealed class KosPlugin : ICkanUiPlugin
{
    /// <summary>插件唯一标识。</summary>
    public const string PluginId = "com.ckanui.kos";

    /// <summary>页签导航键。</summary>
    public const string PageKey = "kos";

    /// <summary>插件版本。</summary>
    public const string PluginVersion = "1.0.0";

    /// <summary>页面视图模型的完整类型名（宿主按名字跨 ALC 解析）。</summary>
    public const string ViewModelTypeName = "CkanUI.Plugin.Kos.UI.KosScriptPageViewModel";

    private static readonly PluginPageDescriptor[] PageList =
    {
        new(
            PageKey,
            "kOS 脚本",
            ViewModelTypeName,
            Glyph: "Code",
            Order: 30),
    };

    /// <inheritdoc />
    public string Id => PluginId;

    /// <inheritdoc />
    public string Name => "kOS 脚本";

    /// <inheritdoc />
    public string Version => PluginVersion;

    /// <inheritdoc />
    public string Author => "CKAN UI";

    /// <inheritdoc />
    public string Description => "管理和编辑 kOS 的 Archive 脚本（Ships/Script/*.ks）：新建、重命名、删除、备份后保存。";

    /// <inheritdoc />
    public IReadOnlyList<PluginPageDescriptor> Pages => PageList;

    /// <inheritdoc />
    public string MinimumHostVersion => string.Empty;

    /// <inheritdoc />
    /// <remarks>插件不持有进程内资源：目录在页面打开时扫描，编辑器按需读文件。</remarks>
    public void Initialize()
    {
    }

    /// <inheritdoc />
    /// <remarks>与 <see cref="Initialize"/> 对称：停用再启用必须可重入。</remarks>
    public void Deinitialize()
    {
    }
}
