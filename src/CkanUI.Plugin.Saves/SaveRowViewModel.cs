using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Plugin.Saves;

/// <summary>
/// 存档列表的一行投影（03 篇 §14.2：槽位名 / 标题 / 模式 / KSP 版本 / UT / 飞船数 / 资金 / 修改时间 / 体积 / 快照数）。
/// </summary>
/// <remarks>
/// 不可变记录：整表替换、不做增量更新（04 篇 §2.1），显示文案在这里拼好，headless 单测可直接断言。
/// </remarks>
public sealed record SaveRowViewModel(
    string SlotName,
    string FilePath,
    long SizeBytes,
    DateTimeOffset LastWriteTime,
    string? Title,
    string? Mode,
    string? GameVersion,
    string? Scene,
    string? UniversalTime,
    int? VesselCount,
    string? Funds,
    int SnapshotCount,
    bool IsBackup,
    bool HasLoadMeta)
{
    /// <summary>从服务层 DTO 投影一行。</summary>
    public static SaveRowViewModel From(SaveGameInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        return new SaveRowViewModel(
            info.SlotName,
            info.FilePath,
            info.SizeBytes,
            info.LastWriteTime,
            info.Title,
            info.Mode,
            info.GameVersion,
            info.Scene,
            info.UniversalTime,
            info.VesselCount,
            info.Funds,
            info.SnapshotCount,
            info.IsBackup,
            info.HasLoadMeta);
    }

    /// <summary>槽位名（列表主列）。</summary>
    public string SlotText => SlotName;

    /// <summary>存档标题（<c>GAME.Title</c>）；老存档可能没有，给可读占位。</summary>
    /// <remarks>
    /// KSP 自带存档的标题常是未解析的本地化键（<c>#autoLOC_1200093</c>），直接显示等于把键名当标题，
    /// 列表里只剩一串截断的 <c>#autoLOC_…</c>。这类值按"没有标题"处理，槽位列已经能区分存档。
    /// </remarks>
    public string TitleText => string.IsNullOrWhiteSpace(Title) || Title!.StartsWith("#autoLOC", StringComparison.OrdinalIgnoreCase)
        ? "—"
        : Title!;

    /// <summary>游戏模式：存档里是 <c>SANDBOX</c> / <c>CAREER</c> / <c>SCIENCE</c>，翻成中文。</summary>
    public string ModeText => Mode?.Trim().ToUpperInvariant() switch
    {
        "SANDBOX" => "沙盒",
        "CAREER" => "生涯",
        "SCIENCE" => "科学",
        "SCENARIO" => "场景",
        null or "" => "—",
        _ => Mode!.Trim(),
    };

    /// <summary>KSP 版本列。</summary>
    public string VersionText => string.IsNullOrWhiteSpace(GameVersion) ? "版本未知" : GameVersion!;

    /// <summary>游戏内时间：原始 UT + 可读的"第 N 年第 M 天"。</summary>
    public string UniversalTimeText => SaveTimeText.UniversalTime(UniversalTime);

    /// <summary>原始 UT 字符串（详情面板展示，保字不保义，13 篇 L1）。</summary>
    public string UniversalTimeRaw => string.IsNullOrWhiteSpace(UniversalTime) ? "—" : UniversalTime!;

    /// <summary>飞船数列。</summary>
    public string VesselCountText => VesselCount is { } count ? $"{count} 艘" : "—";

    /// <summary>资金列。</summary>
    public string FundsText => SaveTimeText.Funds(Funds);

    /// <summary>体积列。</summary>
    public string SizeText => SaveTimeText.FileSize(SizeBytes);

    /// <summary>修改时间列。</summary>
    public string TimeText => SaveTimeText.LocalTimestamp(LastWriteTime);

    /// <summary>快照数（写前快照 + KSP 自带 Backup/ 目录）。</summary>
    public string SnapshotCountText => $"{SnapshotCount} 份";

    /// <summary>存档种类：主文件还是 <c>.sfs.bak</c>。</summary>
    public string KindText => IsBackup ? "备份" : "存档";

    /// <summary>场景（<c>GAME.scene</c>）：<c>space</c> 表示在飞行中，<c>KSC</c> 表示在航天中心。</summary>
    public string SceneText => string.IsNullOrWhiteSpace(Scene) ? "—" : Scene!;

    /// <summary>摘要来源：读到了 <c>.loadmeta</c> 还是只解析了文件头。</summary>
    public string SourceText => HasLoadMeta ? ".loadmeta 摘要" : "仅文件头";

    /// <summary>是否允许格式化（备份文件不参与，避免误伤）；服务层同样会拒绝结构损坏的存档。</summary>
    public bool CanFormat => !IsBackup;

    /// <summary>是否 KSP 自带存档（<c>saves/training</c> 与 <c>saves/scenarios</c> 下的训练与场景存档）。</summary>
    public bool IsStock => IsStockSlot(SlotName);

    /// <summary>按槽位名判断是否自带存档：只看首段目录名，大小写不敏感。</summary>
    /// <remarks>
    /// 服务层拼出的 <see cref="SlotName"/> 形如 <c>training / tutorial_basic / persistent</c>，
    /// 首段就是 <c>saves/</c> 下的存档目录名；<c>training</c> 与 <c>scenarios</c> 是 KSP 安装自带的，
    /// 玩家自己的存档永远落在别的目录里，所以这两段是可靠且不需要改服务层 DTO 的判定依据。
    /// </remarks>
    public static bool IsStockSlot(string? slotName)
    {
        if (string.IsNullOrEmpty(slotName))
        {
            return false;
        }

        var head = slotName.AsSpan();
        var separator = head.IndexOf('/');
        if (separator >= 0)
        {
            head = head[..separator];
        }

        // 服务层用 " / " 分隔，切出来的首段带一个尾空格（"scenarios / xxx" → "scenarios "），必须先去空白。
        head = head.Trim();

        return head.Equals("training", StringComparison.OrdinalIgnoreCase)
            || head.Equals("scenarios", StringComparison.OrdinalIgnoreCase);
    }
}
