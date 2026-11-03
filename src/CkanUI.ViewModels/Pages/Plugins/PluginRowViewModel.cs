using System.Linq;
using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.ViewModels.Pages.Plugins;

/// <summary>
/// 插件页的一行（12 篇 F-17 / 06 篇 UI-33）。
/// </summary>
/// <remarks>
/// 纯只读投影，没有 <c>INotifyPropertyChanged</c>：列表每次整体替换（与 04 篇 §2.1 的列表口径一致），
/// 行内没有任何会被就地改写的字段，选中状态交给两个 <c>ListBox.SelectedItem</c>。
/// </remarks>
public sealed class PluginRowViewModel
{
    /// <summary>用服务层快照构造一行。</summary>
    /// <param name="info">插件快照。</param>
    public PluginRowViewModel(PluginInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        Info = info;
    }

    /// <summary>原始快照。</summary>
    public PluginInfo Info { get; }

    /// <summary>插件标识（激活/停用/重载/卸载的键）。</summary>
    public string Id => Info.Id;

    /// <summary>「名字 - 版本」。</summary>
    public string DisplayName => Info.DisplayName;

    /// <summary>作者；为空时显示占位。</summary>
    public string AuthorText => string.IsNullOrEmpty(Info.Author) ? "未署名" : Info.Author;

    /// <summary>描述；为空时显示占位。</summary>
    public string DescriptionText => string.IsNullOrEmpty(Info.Description) ? "（插件没有提供描述）" : Info.Description;

    /// <summary>来源 dll 路径。</summary>
    public string AssemblyPath => Info.AssemblyPath;

    /// <summary>状态文案。</summary>
    public string StateText => Info.State switch
    {
        PluginState.Active => "已激活",
        PluginState.Dormant => "未激活",
        _ => "加载失败",
    };

    /// <summary>是否已激活（视图用来切换状态点颜色）。</summary>
    public bool IsActive => Info.IsActive;

    /// <summary>
    /// 页签摘要：不止报数量，还点名。
    /// </summary>
    /// <remarks>
    /// <para>停用一个插件之后，它的页签会从侧栏与分节页签条里一起消失；用户要能在这里看清
    /// 「我刚才关掉的这个插件原本提供哪些入口」，否则只能靠记忆猜是不是自己弄丢了一页。</para>
    /// <para>因此除了点名，还要说清入口落在哪儿（「实例 › 存档、飞船」而不是「4 个页签」）：
    /// 内嵌在分节里的页签消失时，用户翻侧栏是找不到的，只有这里能告诉他原来在「实例」下面。</para>
    /// </remarks>
    public string PagesText
    {
        get
        {
            if (Info.Pages.Count == 0)
            {
                return "不提供页签";
            }

            var groups = Info.Pages
                .GroupBy(page => SectionTitle(page.Section))
                // OrderBy 是稳定排序：Order 相同的页保持插件自己声明的顺序，别按标题重排。
                .Select(group => group.Key + "：" + string.Join('、', group
                    .OrderBy(page => page.Order)
                    .Select(PageName)));

            var text = "提供 " + Info.Pages.Count + " 个页签 · " + string.Join(" · ", groups);
            return Info.State == PluginState.Dormant
                ? text + " · 已停用，重新激活后这些入口才会出现"
                : text;
        }
    }

    private static string PageName(PluginPageDescriptor page) =>
        string.IsNullOrWhiteSpace(page.Title) ? page.Key : page.Title.Trim();

    /// <summary>页签所在分节的显示名；没有声明分节的页签落在侧栏「扩展」分组下。</summary>
    private static string SectionTitle(string? section) => section?.Trim().ToLowerInvariant() switch
    {
        "home" => "主页",
        "mods" => "模组",
        "instances" => "实例",
        "modpacks" => "模组包",
        "cache" => "数据",
        "plugins" => "工具",
        _ => "扩展",
    };

    /// <summary>失败原因（仅失败行有值）。</summary>
    public string? FailureReason => Info.FailureReason;

    /// <summary>最小宿主版本文案。</summary>
    public string MinimumHostVersionText =>
        string.IsNullOrEmpty(Info.MinimumHostVersion) ? "不限宿主版本" : "要求宿主 " + Info.MinimumHostVersion + "+";
}
