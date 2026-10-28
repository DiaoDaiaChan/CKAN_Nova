using CkanUI.Services.Abstractions;

namespace CkanUI.ViewModels.Pages;

/// <summary>
/// 接收「待计算的变更请求」的落点（04 篇 §3 / UI-04 交接）。
/// 模组页把勾选行翻译成请求后灌给变更集页，页面之间不互相持有具体类型。
/// </summary>
public interface IChangeRequestSink
{
    /// <summary>用一批请求替换当前待计算集合；<c>null</c> 或空集合等价于清空。</summary>
    void SetRequests(IReadOnlyCollection<ChangeRequest>? requests);

    /// <summary>当变更集已成功全部应用到游戏实例时触发，通知来源页面（如模组页）清空勾选与待决状态。</summary>
    event Action? RequestsApplied;
}
