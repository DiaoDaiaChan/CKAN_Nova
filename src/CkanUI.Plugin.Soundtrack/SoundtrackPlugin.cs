using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Plugin.Soundtrack;

/// <summary>「音乐」页签插件入口：管理 Soundtrack 模组的播放列表（Playlists/playlists.cfg）。</summary>
public sealed class SoundtrackPlugin : ICkanUiPlugin
{
    /// <summary>插件唯一标识。</summary>
    public const string PluginId = "com.ckanui.soundtrack";

    /// <summary>页签导航键。</summary>
    public const string PageKey = "music";

    /// <summary>插件版本。</summary>
    public const string PluginVersion = "1.0.0";

    private static readonly PluginPageDescriptor[] PageList =
    {
        new(
            PageKey,
            "音乐",
            typeof(CkanUI.Plugin.Soundtrack.UI.SoundtrackPageViewModel).FullName
                ?? nameof(CkanUI.Plugin.Soundtrack.UI.SoundtrackPageViewModel),
            Glyph: "Music",
            Section: "plugins",
            Order: 20),
    };

    /// <inheritdoc />
    public string Id => PluginId;

    /// <inheritdoc />
    public string Name => "音乐";

    /// <inheritdoc />
    public string Version => PluginVersion;

    /// <inheritdoc />
    public string Author => "CKAN UI";

    /// <inheritdoc />
    public string Description => "管理 Soundtrack 播放列表：增删曲目、排序、开关播放列表、备份后保存。";

    /// <inheritdoc />
    public IReadOnlyList<PluginPageDescriptor> Pages => PageList;

    /// <inheritdoc />
    public string MinimumHostVersion => string.Empty;

    /// <inheritdoc />
    /// <remarks>本插件不持有进程内资源（读盘都在页面里按需发生），初始化保持空实现。</remarks>
    public void Initialize()
    {
    }

    /// <inheritdoc />
    /// <remarks>与 <see cref="Initialize"/> 对称的空实现：停用再启用是宿主的常规路径，必须可重入。</remarks>
    public void Deinitialize()
    {
    }
}
