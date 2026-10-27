namespace CkanUI.Services.Abstractions.Dtos;

/// <summary>
/// 首选下载主机条目 DTO（F-15 / UI-30）。
/// </summary>
/// <param name="Host">主机名，null 或特殊标记表示「其余所有主机」(All Other Hosts)。</param>
/// <param name="Priority">优先级序号（1-based 顺序）。</param>
/// <param name="IsPlaceholder">是否为「其余所有主机」占位条目。</param>
public sealed record HostPriorityItemDto(string? Host, int Priority, bool IsPlaceholder = false);
