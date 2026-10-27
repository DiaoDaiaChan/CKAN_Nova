using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Services.Abstractions;

/// <summary>
/// 首选下载主机管理服务契约（F-15 / UI-30）。
/// 管理下载模组时的主机优先级排序与已知主机发现。
/// </summary>
public interface IPreferredHostsService
{
    /// <summary>
    /// 获取当前有序的首选主机列表。列表中可包含一个 null 项代表「其余所有主机」占位符。
    /// </summary>
    Task<IReadOnlyList<HostPriorityItemDto>> GetPreferredHostsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取所有已知的主机列表（从已加载的仓库/注册表/常用公共源中提取）。
    /// </summary>
    Task<IReadOnlyList<string>> GetKnownHostsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 保存并持久化有序的首选主机列表。
    /// </summary>
    /// <param name="hosts">有序的主机名列表（允许包含 null 代表其余主机）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<OperationResult<bool>> SavePreferredHostsAsync(IEnumerable<string?> hosts, CancellationToken cancellationToken = default);
}
