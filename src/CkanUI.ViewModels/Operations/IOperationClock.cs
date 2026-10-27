namespace CkanUI.ViewModels.Operations;

/// <summary>
/// 单调时钟抽象（05 篇 §8：速率与剩余时间必须可测）。
/// 队列快照本身不带字节数与时间戳（<c>OperationProgress</c> 契约已冻结），
/// 速率/剩余时间只能在视图模型侧用"快照差分 + 单调时间"推算，因此时间源必须可替换，
/// 否则单测只能靠 sleep 等待真实时钟，既慢又不稳定。
/// </summary>
public interface IOperationClock
{
    /// <summary>单调递增的毫秒时间戳（语义同 <see cref="System.Environment.TickCount64"/>）。</summary>
    long TimestampMilliseconds { get; }
}
