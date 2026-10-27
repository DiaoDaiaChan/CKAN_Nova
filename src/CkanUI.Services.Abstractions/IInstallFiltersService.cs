using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CkanUI.Services;

/// <summary>
/// 安装文件过滤器服务契约（F-16 / UI-31）。
/// 管理全局或实例级安装文件忽略规则（如 *.pdb, .git 等）。
/// </summary>
public interface IInstallFiltersService
{
    /// <summary>
    /// 异步加载当前配置的过滤器规则列表。
    /// 若尚未配置或为空，则返回默认规则列表。
    /// </summary>
    Task<IReadOnlyList<string>> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 异步持久化过滤器规则列表至配置层。
    /// </summary>
    Task SaveAsync(IReadOnlyCollection<string> filters, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取系统内置的默认过滤器规则列表。
    /// </summary>
    IReadOnlyList<string> GetDefaultFilters();
}
