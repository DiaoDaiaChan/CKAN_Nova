using System.Collections.ObjectModel;
using System.Globalization;

using CommunityToolkit.Mvvm.ComponentModel;

using CkanUI.Services.Abstractions;

namespace CkanUI.ViewModels.Dialogs;

/// <summary>首次启动指引的步骤种类（顺序即流程顺序）。</summary>
public enum FirstRunStepKind
{
    /// <summary>欢迎与说明。</summary>
    Welcome,

    /// <summary>自动扫描 / 手动指定 KSP 根目录并登记为实例。</summary>
    LocateGame,

    /// <summary>配置下载代理（指数与模组都从 GitHub 拉）。</summary>
    Proxy,

    /// <summary>选择实例的兼容游戏版本。</summary>
    GameVersion,

    /// <summary>拉取模组元数据索引。</summary>
    Metadata,

    /// <summary>可选：勾选社区基础模组。</summary>
    RecommendedMods,

    /// <summary>汇总与收尾。</summary>
    Done,
}

/// <summary>左侧步骤轨里的一行：序号/对勾 + 标题 + 当前态。</summary>
public sealed partial class FirstRunStepItemViewModel : ObservableObject
{
    /// <param name="kind">步骤种类。</param>
    /// <param name="index">从 0 开始的序号。</param>
    /// <param name="title">标题（本地化后）。</param>
    public FirstRunStepItemViewModel(FirstRunStepKind kind, int index, string title)
    {
        Kind = kind;
        Index = index;
        _title = title;
    }

    /// <summary>步骤种类。</summary>
    public FirstRunStepKind Kind { get; }

    /// <summary>序号（从 0 开始）。</summary>
    public int Index { get; }

    /// <summary>标题。</summary>
    [ObservableProperty]
    private string _title;

    /// <summary>是否是当前步骤。</summary>
    [ObservableProperty]
    private bool _isCurrent;

    /// <summary>是否已完成（走过去了）。</summary>
    [ObservableProperty]
    private bool _isCompleted;

    /// <summary>圆点里显示的字：完成了打勾，没完成显示序号。</summary>
    public string Marker => IsCompleted ? "✓" : (Index + 1).ToString(CultureInfo.InvariantCulture);

    /// <summary>尚未走到（既不是当前也没完成）。</summary>
    public bool IsPending => !IsCurrent && !IsCompleted;

    partial void OnIsCompletedChanged(bool value)
    {
        OnPropertyChanged(nameof(Marker));
        OnPropertyChanged(nameof(IsPending));
    }

    partial void OnIsCurrentChanged(bool value) => OnPropertyChanged(nameof(IsPending));
}

/// <summary>扫描到的一个 KSP 安装目录。</summary>
public sealed partial class KspCandidateItemViewModel : ObservableObject
{
    private readonly string _unknownVersionLabel;

    /// <param name="candidate">扫描结果。</param>
    /// <param name="sourceLabel">来源标签（本地化后）。</param>
    /// <param name="unknownVersionLabel">版本未知时的占位文案。</param>
    public KspCandidateItemViewModel(KspCandidate candidate, string sourceLabel, string unknownVersionLabel)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        GameDir = candidate.GameDir;
        GameVersion = candidate.GameVersion;
        SourceLabel = sourceLabel;
        _unknownVersionLabel = unknownVersionLabel;
    }

    /// <summary>游戏根目录。</summary>
    public string GameDir { get; }

    /// <summary>读到的游戏版本（可能为空）。</summary>
    public string? GameVersion { get; }

    /// <summary>目录名，作为简短标题。</summary>
    public string FolderName
    {
        get
        {
            var trimmed = GameDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var name = Path.GetFileName(trimmed);
            return string.IsNullOrWhiteSpace(name) ? GameDir : name;
        }
    }

    /// <summary>来源标签（Steam 库 / 注册表 / 常见路径 / 手动选择）。</summary>
    public string SourceLabel { get; }

    /// <summary>版本展示文案。</summary>
    public string VersionText => string.IsNullOrWhiteSpace(GameVersion) ? _unknownVersionLabel : GameVersion!;

    /// <summary>是否被选中。</summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>被选中时的回调：由 VM 负责取消其余候选（单选语义跨容器只能这么实现）。</summary>
    public Action<KspCandidateItemViewModel>? Selected { get; set; }

    partial void OnIsSelectedChanged(bool value)
    {
        if (value)
        {
            Selected?.Invoke(this);
        }
    }
}

/// <summary>兼容版本列表里的一项。</summary>
public sealed partial class FirstRunVersionItemViewModel : ObservableObject
{
    /// <param name="versionString">版本号文本。</param>
    /// <param name="isSelected">是否已勾选。</param>
    /// <param name="isRealVersion">是否是游戏真实版本（真实版本不可取消）。</param>
    /// <param name="isWildcard">是否是通配项。</param>
    /// <param name="realLabel">真实版本的角标文案。</param>
    public FirstRunVersionItemViewModel(
        string versionString,
        bool isSelected,
        bool isRealVersion,
        bool isWildcard,
        string realLabel)
    {
        VersionString = versionString;
        IsRealVersion = isRealVersion;
        IsWildcard = isWildcard;
        RealLabel = realLabel;

        // 真实版本永远是兼容的，界面不给取消的机会，模型里也直接钉住。
        _isSelected = isSelected || isRealVersion;
    }

    /// <summary>版本号文本。</summary>
    public string VersionString { get; }

    /// <summary>是否是游戏真实版本。</summary>
    public bool IsRealVersion { get; }

    /// <summary>是否是通配项。</summary>
    public bool IsWildcard { get; }

    /// <summary>真实版本角标文案。</summary>
    public string RealLabel { get; }

    /// <summary>是否可编辑（真实版本不可编辑）。</summary>
    public bool IsEditable => !IsRealVersion;

    /// <summary>是否勾选。</summary>
    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>装机必备清单里的一个模组（默认不勾）。</summary>
public sealed partial class RecommendedModItemViewModel : ObservableObject
{
    /// <param name="identifier">模组标识。</param>
    /// <param name="name">显示名。</param>
    /// <param name="summary">一句话说明。</param>
    public RecommendedModItemViewModel(string identifier, string name, string summary)
    {
        Identifier = identifier;
        Name = name;
        Summary = summary;
    }

    /// <summary>模组标识。</summary>
    public string Identifier { get; }

    /// <summary>显示名。</summary>
    public string Name { get; }

    /// <summary>一句话说明。</summary>
    public string Summary { get; }

    /// <summary>是否勾选：默认 false，一勾一装，不勾就是不装。</summary>
    [ObservableProperty]
    private bool _isChecked;
}

/// <summary>装机必备清单的数据源。</summary>
/// <remarks>
/// 只收录「几乎人人装、装了不会打架、卸了也能跑」的那一层基础件：依赖注入（ModuleManager）、
/// 数据面板（KER / MechJeb2 / KAC）、可视化辅助（Trajectories / Waterfall）。
/// 这里刻意不放任何重型内容包 —— 推荐的意义是「省掉第一次装基础件的时间」，不是替用户做玩法选择。
/// </remarks>
public static class EssentialModCatalog
{
    /// <summary>一条推荐项的定义。</summary>
    /// <param name="Identifier">模组标识。</param>
    /// <param name="Name">显示名。</param>
    /// <param name="Summary">一句话说明。</param>
    public sealed record Entry(string Identifier, string Name, string Summary);

    /// <summary>推荐清单。</summary>
    public static IReadOnlyList<Entry> Entries { get; } = new[]
    {
        new Entry("ModuleManager", "Module Manager", "几乎所有模组的前置：没有它，别的模组连启动都做不到。"),
        new Entry("KerbalEngineerRedux", "Kerbal Engineer Redux", "飞行与装配时的工程读数：Δv、TWR、分级余量。"),
        new Entry("MechJeb2", "MechJeb 2", "自动驾驶与轨道规划，新手靠它把「怎么入轨」跑通。"),
        new Entry("KerbalAlarmClock", "Kerbal Alarm Clock", "按轨道事件提醒，长时间任务不再错过下一次点火。"),
        new Entry("Trajectories", "Trajectories", "按大气阻力预测落点，再入和回收不再靠猜。"),
        new Entry("Waterfall", "Waterfall", "把引擎尾焰换成可配置的粒子效果，视觉提升明显。"),
    };

    /// <summary>把清单转成界面条目（全部默认不勾选）。</summary>
    public static ObservableCollection<RecommendedModItemViewModel> CreateItems() =>
        new(Entries.Select(entry => new RecommendedModItemViewModel(entry.Identifier, entry.Name, entry.Summary)));
}
