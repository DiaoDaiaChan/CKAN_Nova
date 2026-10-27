using System.Collections.Generic;

namespace CkanUI.Services.Abstractions.Dtos;

/// <summary>
/// 单个游戏版本的兼容性选择项 DTO。
/// </summary>
/// <param name="VersionString">版本字符串（如 "1.12.5" 或通配符 "1.12"）。</param>
/// <param name="IsSelected">当前是否被勾选为兼容版本。</param>
/// <param name="IsRealVersion">是否为游戏实例的真实版本（真实版本始终受支持且通常不可取消）。</param>
/// <param name="IsWildcard">是否为通配符版本（如 "1.12.*" 或 "1.12"）。</param>
public sealed record CompatibleVersionItemDto(
    string VersionString,
    bool IsSelected,
    bool IsRealVersion,
    bool IsWildcard = false);

/// <summary>
/// 实例兼容游戏版本状态快照。
/// </summary>
/// <param name="InstanceName">游戏实例名称。</param>
/// <param name="RealVersion">游戏实例的真实原生版本。</param>
/// <param name="Items">所有已知及当前配置的游戏版本项列表。</param>
public sealed record CompatibleVersionsSnapshot(
    string InstanceName,
    string RealVersion,
    IReadOnlyList<CompatibleVersionItemDto> Items);
