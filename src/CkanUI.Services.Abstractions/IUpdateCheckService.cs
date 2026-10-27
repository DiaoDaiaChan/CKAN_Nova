using System.Threading;
using System.Threading.Tasks;
using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Services.Abstractions;

/// <summary>
/// 自动更新与新版本检查器契约（F-52 / UI-27）。
/// </summary>
public interface IUpdateCheckService
{
    /// <summary>
    /// 检查新版本。
    /// </summary>
    /// <param name="includeDevBuilds">是否包含开发预览版 (dev builds)。若未指定或配置开启，则检查 dev builds。</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>操作结果，失败时返回 Fail，不向外抛出未捕获异常。</returns>
    Task<OperationResult<UpdateCheckResult>> CheckForUpdatesAsync(bool includeDevBuilds = false, CancellationToken ct = default);
}
