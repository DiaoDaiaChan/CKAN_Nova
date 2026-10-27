namespace CkanUI.Services.Abstractions;

/// <summary>
/// L0 视图层资源参与方（18 篇 §3 L0）：清列表投影、丢位图、停掉后台刷新计时器。
/// 实现在 UI 侧（Avalonia 的位图只能在那里释放），服务只负责按顺序调用并汇总释放条目数。
/// </summary>
/// <remarks>一个参与方抛异常只记 Warn 并继续调用其余参与方，本层最后按「有没有异常」判成败。</remarks>
public interface IViewResourceParticipant
{
    /// <summary>参与方名字，只用于日志与报告。</summary>
    string Name { get; }

    /// <summary>清掉可再生的视图资源，返回释放的条目数（列表项 + 位图个数）。</summary>
    int TrimViewResources();
}
