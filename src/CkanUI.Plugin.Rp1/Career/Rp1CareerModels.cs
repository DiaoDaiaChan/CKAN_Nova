using System.Globalization;
using CkanUI.Services.Abstractions;

namespace CkanUI.Plugin.Rp1.Career;

/// <summary>生涯日志里一条事件的类别。类别只影响筛选与徽标，不改变排序。</summary>
public enum Rp1EventKind
{
    Milestone = 0,
    Contract = 1,
    ProgramStart = 2,
    ProgramCompleted = 3,
    CrewRetired = 4,
    CrewLost = 5,
    Finance = 6,
}

/// <summary>一条生涯事件。时间未知时 <see cref="Ut"/> 为 null，排序放到最后而不是假装是第 0 秒。</summary>
public sealed record Rp1CareerEvent(
    double? Ut,
    string DateText,
    Rp1EventKind Kind,
    string Title,
    string? Subtitle,
    string? Detail,
    string InternalName,
    string? Meta)
{
    /// <summary>时间线徽标文案。</summary>
    public string BadgeText => Kind switch
    {
        Rp1EventKind.Milestone => "里程碑",
        Rp1EventKind.Contract => "合同",
        Rp1EventKind.ProgramStart => "项目启动",
        Rp1EventKind.ProgramCompleted => "项目完成",
        Rp1EventKind.CrewRetired => "人员退役",
        Rp1EventKind.CrewLost => "乘员损失",
        Rp1EventKind.Finance => "财务周期",
        _ => "事件",
    };

    /// <summary>筛选用的小写键。</summary>
    public string KindKey => Kind switch
    {
        Rp1EventKind.Milestone => "milestone",
        Rp1EventKind.Contract => "contract",
        Rp1EventKind.ProgramStart or Rp1EventKind.ProgramCompleted => "program",
        Rp1EventKind.CrewRetired or Rp1EventKind.CrewLost => "crew",
        Rp1EventKind.Finance => "finance",
        _ => "other",
    };

    /// <summary>对应类别图标名（GlyphCatalog 中的合法字形）。</summary>
    public string GlyphName => Kind switch
    {
        Rp1EventKind.Milestone => "Milestone",
        Rp1EventKind.Contract => "Contract",
        Rp1EventKind.ProgramStart or Rp1EventKind.ProgramCompleted => "Program",
        Rp1EventKind.CrewRetired or Rp1EventKind.CrewLost => "Crew",
        Rp1EventKind.Finance => "Money",
        _ => "Milestone",
    };

    /// <summary>时间线节点样式名。</summary>
    public string NodeClass => Kind switch
    {
        Rp1EventKind.Milestone => "milestone",
        Rp1EventKind.Contract => "contract",
        Rp1EventKind.ProgramStart or Rp1EventKind.ProgramCompleted => "program",
        Rp1EventKind.CrewLost => "crew-lost",
        Rp1EventKind.CrewRetired => "crew-retired",
        _ => "default",
    };

    /// <summary>节点徽标类别样式名（Warning / Accent / Info / Danger / Success）。</summary>
    public string BadgeClass => Kind switch
    {
        Rp1EventKind.Milestone => "Warning",
        Rp1EventKind.Contract => "Accent",
        Rp1EventKind.ProgramStart or Rp1EventKind.ProgramCompleted => "Info",
        Rp1EventKind.CrewLost => "Danger",
        Rp1EventKind.CrewRetired => "Success",
        _ => string.Empty,
    };

    public bool IsMilestone => Kind == Rp1EventKind.Milestone;
}

/// <summary>CareerLog 里的一个预算周期（约 31 天）。时间线默认不展开，切到「财务周期」才看。</summary>
public sealed record Rp1CareerPeriod(
    int Index,
    double StartUt,
    double EndUt,
    string DateText,
    double? Funds,
    double? Science,
    double? ScienceEarned,
    double? Confidence,
    double? Reputation,
    int CrewLost,
    double? SubsidyPaidOut);

/// <summary>一次读取得到的完整生涯时间线。统计值取最后一个预算周期，缺失就是 null。</summary>
public sealed record Rp1CareerTimeline(
    string SaveName,
    string SavePath,
    string GameDir,
    string? Rp1Version,
    string? GameVersion,
    double? CurrentUt,
    string CurrentDateText,
    bool Rp1Installed,
    IReadOnlyList<Rp1CareerEvent> Events,
    IReadOnlyList<Rp1CareerPeriod> Periods,
    int ContractCount,
    int MilestoneCount,
    int ProgramCount,
    int CrewRetiredCount,
    int CrewLostCount,
    double? CurrentFunds,
    double? CurrentScience,
    double? CurrentConfidence,
    double? CurrentReputation,
    IReadOnlyList<string> Diagnostics)
{
    /// <summary>有事件才展示时间线；只有统计、没有事件时页面要明确说清楚。</summary>
    public bool HasEvents => Events.Count > 0;
}

/// <summary>存档目录里的一份 persistent.sfs。</summary>
public sealed record Rp1SaveSummary(
    string Name,
    string DirectoryPath,
    string PersistentPath,
    DateTime LastWriteUtc,
    long SizeBytes)
{
    /// <summary>下拉框文案：名字 + 修改时间 + 体积。</summary>
    public string DisplayName =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{Name} · {LastWriteUtc.ToLocalTime():yyyy-MM-dd HH:mm} · {FormatSize(SizeBytes)}");

    private static string FormatSize(long bytes)
    {
        if (bytes >= 1024L * 1024L)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{bytes / (1024d * 1024d):F1} MB");
        }

        if (bytes >= 1024L)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024d:F0} KB");
        }

        return string.Create(CultureInfo.InvariantCulture, $"{bytes} B");
    }
}

/// <summary>插件对宿主只暴露两个动作：列存档、读一份存档。</summary>
public interface IRp1CareerService
{
    Task<OperationResult<IReadOnlyList<Rp1SaveSummary>>> ListSavesAsync(
        string gameDir,
        CancellationToken cancellationToken = default);

    Task<OperationResult<Rp1CareerTimeline>> LoadAsync(
        string gameDir,
        string saveName,
        string? instanceName = null,
        string? gameVersion = null,
        CancellationToken cancellationToken = default);
}
