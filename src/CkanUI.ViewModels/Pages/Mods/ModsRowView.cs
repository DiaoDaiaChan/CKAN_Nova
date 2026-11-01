using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Pages.Settings;

namespace CkanUI.ViewModels.Pages.Mods;

/// <summary>
/// 列表行的展示投影（03 篇 §3.2/§3.3）：包住不可变的 <see cref="ModRowData"/>，把格式化与徽章可见性
/// 收在 VM 侧，XAML 里不写格式串、不放转换器（App 项目不需要新增任何文件）。
/// </summary>
/// <remarks>
/// 每行一个轻量 record：3,000 行在过滤/排序时整体重建，一次性替换 ItemsSource（04 篇 §2.1），
/// 不做增量 CollectionChanged，也不产生 3,000 个 ObservableObject。
/// </remarks>
public sealed record ModsRowView(ModRowData Data)
{
    /// <summary>行标识符（CKAN 内部唯一名）。</summary>
    public string Identifier => Data.Identifier;

    /// <summary>显示名。</summary>
    public string Name => Data.Name;

    /// <summary>作者（缺失显示"—"）。</summary>
    public string AuthorText => string.IsNullOrWhiteSpace(Data.Author) ? "—" : Data.Author;

    /// <summary>版本号是否隐藏 epoch 前缀（F-54，来自 ckan-ui.json）。</summary>
    public bool HideVersionEpoch { get; init; }

    /// <summary>版本号是否隐藏 v 前缀（F-54，来自 ckan-ui.json）。</summary>
    public bool HideVersionVPrefix { get; init; }

    /// <summary>已装版本（未装显示"—"）。</summary>
    public string InstalledVersionText => FormatVersion(Data.InstalledVersion);

    /// <summary>最新版本（缺失显示"—"）。</summary>
    public string LatestVersionText => FormatVersion(Data.LatestVersion);

    /// <summary>高级搜索与元数据事实（包含主页、下载链接等，F-31）。</summary>
    public ModSearchFacts Facts => Data.Facts;

    /// <summary>下载量（千分位）。</summary>
    public string DownloadText => Data.DownloadCount > 0 ? ModsFormatter.Count(Data.DownloadCount) : "—";

    /// <summary>安装体积。</summary>
    public string SizeText => ModsFormatter.Bytes(Data.SizeBytes);

    /// <summary>模组核心分类定义（8 大分类系统）。</summary>
    public CkanUI.ModText.ModCategoryDefinition Category =>
        CkanUI.ModText.ModTaxonomyEngine.Classify(Data.Identifier, Data.Name, Data.Tags, Data.Facts.Abstract, Data.Facts.Description);

    /// <summary>核心分类徽章展示文案：🎨 视觉与音效美化</summary>
    public string CategoryBadgeText => Category.DisplayTitle;

    /// <summary>标签摘要：最多 4 个，其余折叠成 "+n"（03 篇 §3.2）。</summary>
    public string TagsText
    {
        get
        {
            if (Data.Tags is not { Count: > 0 } tags)
            {
                return string.Empty;
            }

            const int Max = 4;
            var shown = tags.Count <= Max ? tags : tags.Take(Max).ToList();
            var text = string.Join("、", shown);
            return tags.Count > Max ? $"{text} +{tags.Count - Max}" : text;
        }
    }

    /// <summary>是否已安装（行首绿色竖条）。</summary>
    public bool IsInstalled => Data.IsInstalled;

    /// <summary>是否有可用更新（加粗 + ↑）。</summary>
    public bool HasUpdate => Data.HasUpdate;

    /// <summary>是否兼容当前游戏版本。</summary>
    public bool IsCompatible => Data.IsCompatible;

    /// <summary>是否自动安装（"自动"徽章）。</summary>
    public bool IsAutoInstalled => Data.IsAutoInstalled;

    /// <summary>是否隐式依赖（"依赖"徽章 + 减号）。</summary>
    public bool IsImplicit => Data.IsImplicit;

    /// <summary>是否已勾选进变更集。</summary>
    public bool IsSelected => Data.IsSelected;

    /// <summary>是否显示"有更新"徽章。</summary>
    public bool ShowUpdateBadge => Data.HasUpdate;

    /// <summary>是否显示"不兼容"徽章。</summary>
    public bool ShowIncompatibleBadge => !Data.IsCompatible;

    /// <summary>是否显示"自动"徽章。</summary>
    public bool ShowAutoBadge => Data.IsAutoInstalled;

    /// <summary>是否显示"依赖"徽章。</summary>
    public bool ShowDependencyBadge => Data.IsImplicit;

    /// <summary>行状态文本（供详情面板与无障碍读屏，不只靠颜色表达状态）。</summary>
    public string StatusText
    {
        get
        {
            var parts = new List<string>(4);
            parts.Add(Data.IsInstalled ? "已安装" : "未安装");
            if (Data.HasUpdate)
            {
                parts.Add("有更新");
            }

            if (Data.IsAutoInstalled)
            {
                parts.Add("自动安装");
            }

            if (Data.IsImplicit)
            {
                parts.Add("隐式依赖");
            }

            if (!Data.IsCompatible)
            {
                parts.Add("不兼容");
            }

            return string.Join(" · ", parts);
        }
    }

    /// <summary>把 <see cref="ModRowData"/> 标记为勾选/取消勾选后重新包装。</summary>
    public ModsRowView WithSelected(bool selected) => this with { Data = Data with { IsSelected = selected } };

    /// <summary>把行数据包装成展示行；两个版本格式开关由页面按 ckan-ui.json 传入（F-54）。</summary>
    public static ModsRowView From(ModRowData data, bool hideVersionEpoch = false, bool hideVersionVPrefix = false) =>
        new(data) { HideVersionEpoch = hideVersionEpoch, HideVersionVPrefix = hideVersionVPrefix };

    private string FormatVersion(string? version) =>
        string.IsNullOrWhiteSpace(version)
            ? "—"
            : VersionDisplayFormat.Format(version, HideVersionEpoch, HideVersionVPrefix);
}
