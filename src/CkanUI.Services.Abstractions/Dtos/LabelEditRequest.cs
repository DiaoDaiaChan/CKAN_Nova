namespace CkanUI.Services.Abstractions.Dtos;

/// <summary>
/// 新建 / 编辑标签组的请求（12 篇 F-24）。名称是键，编辑时按名称定位，不支持改名。
/// </summary>
public sealed record LabelEditRequest(string Name)
{
    /// <summary>背景色（<c>#RRGGBB</c>）；null 表示不着色。</summary>
    public string? ColorHex { get; init; }

    /// <summary>是否全局标签（作用于所有实例）。false 表示只作用于当前实例。</summary>
    public bool IsGlobal { get; init; } = true;

    /// <summary>隐藏于其它过滤器（上游 <c>ModuleLabel.Hide</c>）。</summary>
    public bool HideFromOtherFilters { get; init; }

    /// <summary>成员变更时通知（上游 <c>ModuleLabel.NotifyOnChange</c>）。</summary>
    public bool NotifyOnChanges { get; init; }

    /// <summary>成员变更时移出标签组（上游 <c>ModuleLabel.RemoveOnChange</c>）。</summary>
    public bool RemoveOnChanges { get; init; }

    /// <summary>安装时提醒（上游 <c>ModuleLabel.AlertOnInstall</c>）。</summary>
    public bool AlertOnInstall { get; init; }

    /// <summary>安装时移出标签组（上游 <c>ModuleLabel.RemoveOnInstall</c>）。</summary>
    public bool RemoveOnInstall { get; init; }

    /// <summary>锁定版本：组成员不参与自动升级（上游 <c>ModuleLabel.HoldVersion</c>）。</summary>
    public bool HoldVersion { get; init; }

    /// <summary>忽略缺失文件：组成员缺文件时报错降级（上游 <c>ModuleLabel.IgnoreMissingFiles</c>）。</summary>
    public bool IgnoreMissingFiles { get; init; }
}
