using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.ViewModels.Pages.Instances;

/// <summary>
/// 实例列表的一行投影（03 篇 §5「实例卡片列表」：名称 / 游戏版本 / 路径 / 模组数 / 是否当前 / 是否锁定）。
/// </summary>
/// <remarks>
/// 不可变记录：投影整体替换（04 篇 §2.1「不做增量 CollectionChanged」），视图绑定到的永远是稳定快照，
/// 因此切换实例后旧行不会残留旧状态。显示文案在这里拼装，保证 headless 单测可断言（03 篇 §11 文案集中）。
/// </remarks>
public sealed record InstanceRowViewModel(
    string Name,
    string GameDir,
    string? GameVersion,
    bool IsCurrent,
    bool IsLocked,
    bool IsDefault,
    int? ModuleCount)
{
    /// <summary>游戏目录是否不存在（03 篇 §5「异常态：路径不存在 → 灰显」）；由路径探测器回填，默认未知即不灰显。</summary>
    public bool IsPathMissing { get; init; }

    /// <summary>从服务层 DTO 投影一行；<paramref name="currentName"/> 为当前实例名（大小写敏感，Core 的实例字典同此口径）。</summary>
    public static InstanceRowViewModel From(InstanceInfo info, string? currentName)
    {
        ArgumentNullException.ThrowIfNull(info);
        return new InstanceRowViewModel(
            info.Name,
            info.GameDir,
            info.GameVersion,
            string.Equals(currentName, info.Name, StringComparison.Ordinal),
            info.IsLocked,
            info.IsDefault,
            info.ModuleCount);
    }

    /// <summary>主标题：名称（括号里带版本，与 <see cref="InstanceInfo.DisplayName"/> 口径一致）。</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(GameVersion) ? Name : $"{Name} ({GameVersion})";

    /// <summary>游戏版本列；未知时给可读占位而不是空白。</summary>
    public string VersionText => string.IsNullOrWhiteSpace(GameVersion) ? "版本未知" : GameVersion!;

    /// <summary>模组数列。</summary>
    public string ModuleCountText => ModuleCount is { } count ? $"{count} 个模组" : "—";

    /// <summary>锁定状态（03 篇 §5：⚠ 被其他进程占用）。</summary>
    public string LockText => IsLocked ? "⚠ 被其他进程占用" : "未锁定";

    /// <summary>是否当前实例的文案。</summary>
    public string CurrentText => IsCurrent ? "当前实例" : "未选中";

    /// <summary>是否默认实例的文案（空串表示不显示徽标）。</summary>
    public string DefaultText => IsDefault ? "默认实例" : string.Empty;

    /// <summary>路径状态文案：目录不存在时提示重新定位。</summary>
    public string PathStatusText => IsPathMissing ? "⚠ 目录不存在，请重新定位" : GameDir;

    /// <summary>路径正常（供视图做灰显的反向可见性绑定，Avalonia 支持 <c>!</c> 但显式属性更好读）。</summary>
    public bool IsPathOk => !IsPathMissing;

    /// <summary>能否切换为该实例（已是当前或被锁定都不行）。</summary>
    public bool CanSwitch => !IsCurrent && !IsLocked;

    /// <summary>能否遗忘（当前实例不可遗忘，服务层也会拒绝）。</summary>
    public bool CanForget => !IsCurrent;
}
