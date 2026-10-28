using System.Threading;
using System.Threading.Tasks;

namespace CkanUI.Services.Abstractions.Logs;

/// <summary>
/// 诊断包服务（14 篇 §5 / LOG-05）。
/// 返回 <see cref="OperationResult{T}"/> 而不是抛异常：磁盘满、路径不可写都是可预期失败，
/// UI 要拿到中文可读消息直接展示。
/// </summary>
public interface ILogBundleService
{
    /// <summary>生成诊断包 zip。</summary>
    Task<OperationResult<LogBundleResult>> CreateAsync(LogBundleRequest request, CancellationToken cancellationToken = default);
}
