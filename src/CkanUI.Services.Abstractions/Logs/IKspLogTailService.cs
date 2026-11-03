namespace CkanUI.Services.Abstractions.Logs;

/// <summary>
/// 当前实例 KSP 游戏日志（<c>&lt;GameDir&gt;/KSP.log</c>）的增量跟随器（14 篇 §3.2 的第五路）。
/// 为什么放在服务层而不是页面里：切换实例、页面离开/回来都不该断开文件跟随，
/// 否则用户为了看安装后的游戏报错，得先停在日志页才「开始记录」。
/// </summary>
public interface IKspLogTailService : IDisposable
{
    /// <summary>当前跟随的实例名（未跟随为 null）。</summary>
    string? InstanceName { get; }

    /// <summary>当前跟随的文件绝对路径；实例目录里没有 KSP.log 时为 null。</summary>
    string? GameLogPath { get; }

    /// <summary>是否正在轮询。</summary>
    bool IsWatching { get; }

    /// <summary>本次跟随以来导入的行数。</summary>
    long ImportedLineCount { get; }

    /// <summary>最近一次读取失败的原因（权限/占用/文件消失）；正常时为 null。</summary>
    string? LastError { get; }

    /// <summary>跟随状态/导入行数/错误变化（可能在后台线程触发，订阅方自行切 UI 线程）。</summary>
    event EventHandler? StatusChanged;

    /// <summary>切换到某实例：定位 <c>KSP.log</c>，从末尾有限窗口开始跟随；目录为空则停止。</summary>
    void Attach(string? gameDirectory, string? instanceName);

    /// <summary>停止跟随。</summary>
    void Detach();

    /// <summary>手动导入整份日志（用户点「导入已有日志」时用；返回导入行数）。</summary>
    Task<int> ImportFromStartAsync(CancellationToken cancellationToken = default);

    /// <summary>读一次增量（测试与「立即刷新」按钮用；正常由内部定时器驱动）。</summary>
    Task<int> PumpAsync(CancellationToken cancellationToken = default);
}
