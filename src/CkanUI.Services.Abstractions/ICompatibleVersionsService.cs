using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Services.Abstractions;

/// <summary>
/// 游戏兼容版本管理服务接口（F-14 / UI-11）。
/// 负责读取已知游戏版本、当前实例的兼容版本列表并持久化保存。
/// </summary>
public interface ICompatibleVersionsService
{
    /// <summary>
    /// 加载指定实例（或当前活动实例）的兼容版本快照。
    /// </summary>
    /// <param name="instanceName">可选的实例名称；若为空则针对当前活动实例。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>兼容版本快照，包含真实版本与所有可选版本项。</returns>
    Task<CompatibleVersionsSnapshot> LoadAsync(string? instanceName = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 保存所选的兼容版本列表至实例配置，并在需要时触发模组仓库兼容性刷新。
    /// </summary>
    /// <param name="selectedVersions">用户选定作为兼容版本的版本字符串集合。</param>
    /// <param name="instanceName">可选的实例名称；若为空则针对当前活动实例。</param>
    /// <param name="triggerRefresh">保存后是否触发仓库与注册表刷新（默认 true）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task SaveAsync(
        IReadOnlyCollection<string> selectedVersions,
        string? instanceName = null,
        bool triggerRefresh = true,
        CancellationToken cancellationToken = default);
}
