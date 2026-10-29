using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Services.Abstractions;

/// <summary>读取并更新 UI 自有的实例游戏时长。</summary>
public interface IPlayTimeService
{
    /// <summary>读取持久化的累计时长；文件缺失或损坏返回空列表。</summary>
    IReadOnlyList<PlayTimeEntry> Load();

    /// <summary>覆盖指定实例的累计时长（小时），写入成功后返回真。</summary>
    bool Save(string instanceName, double hours);

    /// <summary>
    /// 把指定实例的累计时长重置为 0：手工基线与自动累计（会话账本）一起清零，两个文件各自原子替换。
    /// </summary>
    /// <param name="instanceName">实例名（与 <see cref="Load"/> 返回的 <c>InstanceName</c> 同一口径）。</param>
    /// <param name="ct">取消令牌：已取消时直接返回失败结果，不写盘。</param>
    /// <returns>成功或失败的 <see cref="OperationResult"/>；实现不得向调用方抛异常。</returns>
    /// <remarks>
    /// 默认实现返回「不支持重置」，好让既有替身（只实现 Load/Save）零改动即可编译：
    /// 重置是本次新增的**可选**能力，没实现的替身按「不支持」处理比抛出 <see cref="NotSupportedException"/> 更安全。
    /// </remarks>
    Task<OperationResult> ResetAsync(string instanceName, CancellationToken ct = default) =>
        Task.FromResult(OperationResult.Fail("当前游戏时长服务不支持重置。"));
}
