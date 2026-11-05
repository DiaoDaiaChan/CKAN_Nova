using System;

namespace CkanUI.ViewModels.Pages.Mods;

/// <summary>
/// 「联动模组」面板里的一行：清单来自 <see cref="ModsCompanionCatalog"/>，
/// 状态由当前仓库快照与已装集合现算（不再依赖"这一行有没有被过滤出来"）。
/// </summary>
/// <remarks>
/// 之所以要有这层投影：模组页的主表格吃的是仓库快照，索引为空时整张表是空的，
/// 用户打开「联动模组」只会看到"没有匹配的模组"——联动清单本身却被藏起来了。
/// 面板独立于快照渲染，索引没加载也能说清"这个模组是什么、点亮哪个插件、当前装没装"。
/// </remarks>
public sealed class ModsCompanionItemViewModel
{
    /// <summary>构造一条联动项。</summary>
    /// <param name="link">清单声明。</param>
    /// <param name="statusText">状态文案（已安装 / 可安装 / 不在仓库 / 索引未加载）。</param>
    /// <param name="isInstalled">当前实例是否已装。</param>
    /// <param name="isMissing">仓库里没有这个模组（CKAN 之外的渠道分发）。</param>
    /// <param name="canAddToChanges">能否一键加入变更集（仓库里有且未安装）。</param>
    public ModsCompanionItemViewModel(
        ModsCompanionCatalog.Link link,
        string statusText,
        bool isInstalled,
        bool isMissing,
        bool canAddToChanges)
    {
        Link = link ?? throw new ArgumentNullException(nameof(link));
        StatusText = statusText;
        IsInstalled = isInstalled;
        IsMissing = isMissing;
        CanAddToChanges = canAddToChanges;
    }

    /// <summary>清单声明（标识符、插件名、收益说明）。</summary>
    public ModsCompanionCatalog.Link Link { get; }

    /// <summary>CKAN 标识符。</summary>
    public string ModIdentifier => Link.ModIdentifier;

    /// <summary>插件显示名。</summary>
    public string PluginName => Link.PluginName;

    /// <summary>装上之后插件能做什么。</summary>
    public string Benefit => Link.Benefit;

    /// <summary>状态文案。</summary>
    public string StatusText { get; }

    /// <summary>是否已安装。</summary>
    public bool IsInstalled { get; }

    /// <summary>仓库里没有这个模组（不在 CKAN 分发）。</summary>
    public bool IsMissing { get; }

    /// <summary>能否一键加入变更集。</summary>
    public bool CanAddToChanges { get; }
}
