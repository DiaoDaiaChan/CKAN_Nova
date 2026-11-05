using System;
using System.Collections.Generic;

namespace CkanUI.ViewModels.Pages.Mods;

/// <summary>
/// 内置 GUI 插件与其依赖模组的联动清单（13 篇 F-17 配套）。
/// </summary>
/// <remarks>
/// <para>插件契约 <c>ICkanUiPlugin</c> 里没有「关联 CKAN 标识」字段，所以这层对应关系只能由页面显式声明。
/// 清单里的每一项都来自插件源码里的硬编码目录名，不猜：</para>
/// <list type="bullet">
/// <item>kOS 脚本 → <c>GameData/kOS</c>（src/CkanUI.Plugin.Kos/Scripts/KosScriptService.cs:36 KosModDirectoryName）</item>
/// <item>RP-1 生涯日志 → <c>GameData/RP-1</c>（src/CkanUI.Plugin.Rp1/Services/Rp1CareerService.cs:124）</item>
/// <item>性能 → <c>GameData/KSPPerformanceProfiler</c>（src/CkanUI.Plugin.Performance/Telemetry/KppPaths.cs:13）</item>
/// <item>音乐 → <c>GameData/{Soundtrack,SoundtrackEditor,SoundtrackEditorForked}</c>（src/CkanUI.Plugin.Soundtrack/Playlists/SoundtrackService.cs:31）</item>
/// </list>
/// <para>纯静态表，不碰任何服务，可在 headless 测试里直接断言。</para>
/// </remarks>
public static class ModsCompanionCatalog
{
    /// <summary>一条「联动模组」声明。</summary>
    /// <param name="ModIdentifier">CKAN 标识符（与仓库快照的 Identifier 逐字比较，忽略大小写）。</param>
    /// <param name="PluginId">内置插件标识（<c>ICkanUiPlugin.Id</c>）。</param>
    /// <param name="PluginName">插件显示名（与插件源码里的 <c>Name</c> 逐字一致）。</param>
    /// <param name="Benefit">装上这个模组之后插件能做什么（面向用户的一句话，不吹）。</param>
    public sealed record Link(string ModIdentifier, string PluginId, string PluginName, string Benefit);

    /// <summary>全部联动声明。顺序即详情面板与徽标提示里的展示顺序。</summary>
    public static IReadOnlyList<Link> All { get; } =
    [
        new("kOS", "com.ckanui.kos", "kOS 脚本", "在「kOS 脚本」页里新建、重命名、删除与备份 Archive 脚本（Ships/Script/*.ks）。"),
        new("RP-1", "com.ckanui.rp1", "RP-1 生涯日志", "在「RP-1 生涯日志」页里把合同、里程碑、项目与预算周期整理成生涯时间线。"),
        new("KSPPerformanceProfiler", "com.ckanui.performance", "性能", "在「性能」页里读 KPP 遥测文件：帧指标、耗时分解、热点榜与内存占用。"),
        new("Soundtrack", "com.ckanui.soundtrack", "音乐", "在「音乐」页里增删曲目、排序与开关 Soundtrack 播放列表。"),
        new("SoundtrackEditor", "com.ckanui.soundtrack", "音乐", "在「音乐」页里管理 Soundtrack Editor 的播放列表与曲目。"),
        new("SoundtrackEditorForked", "com.ckanui.soundtrack", "音乐", "在「音乐」页里管理 Soundtrack Editor Forked 的播放列表与曲目。"),
    ];

    private static readonly Dictionary<string, Link> ByIdentifier = BuildIndex();

    /// <summary>该标识符是否有内置插件联动。</summary>
    public static bool IsCompanion(string? identifier) => TryGet(identifier, out _);

    /// <summary>按标识符取联动声明。</summary>
    public static bool TryGet(string? identifier, out Link link)
    {
        link = null!;
        return !string.IsNullOrWhiteSpace(identifier) && ByIdentifier.TryGetValue(identifier, out link!);
    }

    /// <summary>该标识符的全部联动声明（同一模组可能被多个插件支持，例如 Soundtrack 的三种分支互斥，各占一条）。</summary>
    public static IReadOnlyList<Link> GetLinks(string? identifier) =>
        TryGet(identifier, out var link) ? [link] : Array.Empty<Link>();

    private static Dictionary<string, Link> BuildIndex()
    {
        var map = new Dictionary<string, Link>(All.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var link in All)
        {
            map[link.ModIdentifier] = link;
        }

        return map;
    }
}
