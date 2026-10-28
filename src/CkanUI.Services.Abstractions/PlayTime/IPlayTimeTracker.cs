namespace CkanUI.Services.Abstractions;

/// <summary>
/// 游戏会话起止的自动计时（12 篇 F-48）。
/// </summary>
/// <remarks>
/// <para>
/// 与 <see cref="IPlayTimeService"/> 拆成两个接口，是因为使用者完全不同：
/// 读/改累计时长是「游戏时长页」的能力，起止计时是「实例页启动/退出流程」的旁路能力；
/// <see cref="IPlayTimeService"/> 属共享契约不可改，因此以窄接口补上计时一半。
/// </para>
/// <para>
/// 所有成员都带默认实现（DIM）：测试替身与旧实现不必改动即可继续工作，
/// 拿不到计时能力时启动/退出流程也只会退化成「不计时」而不是 <see cref="NullReferenceException"/>。
/// </para>
/// <para>
/// 实现必须自己保证「同一个落盘文件只有一个写者」：读累计时长与写会话账本应落在同一个实例上，
/// 否则两次原子替换会互相覆盖（<c>PlayTimeService</c> 同时实现两个接口就是为此）。
/// </para>
/// </remarks>
public interface IPlayTimeTracker
{
    /// <summary>当前正在计时的实例名；没有未闭合会话时为 null。</summary>
    string? ActiveInstanceName => null;

    /// <summary>
    /// 开始一次游戏会话计时；实例名为空或账本写不进去时返回 false。
    /// </summary>
    /// <remarks>已有未闭合会话（上次崩溃遗留）会被直接覆盖：拿不到真实退出时刻，宁可少记也不虚增。</remarks>
    bool BeginSession(string instanceName) => false;

    /// <summary>
    /// 结束当前会话，把时长按本地自然日切分后累计进账本；返回本次记入的小时数（没有会话为 0）。
    /// </summary>
    double EndSession() => 0d;

    /// <summary>丢弃未闭合会话（崩溃遗留）；返回是否真的丢掉了一条。</summary>
    bool DiscardOpenSession() => false;
}
