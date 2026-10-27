namespace CkanUI.Services.Abstractions.Dtos;

/// <summary>
/// 字节级下载进度（03 篇「操作状态区」/ 05 篇 §4）。
/// </summary>
/// <remarks>
/// 为什么不复用 <see cref="OperationProgress"/>：后者是通用任务进度（7 个字段、无字节量），
/// 而下载既要显示「已收多少字节 / 共多少字节」，也要显示「当前速率」。
/// 按 05 篇 §4 与 01 篇 §8 的约束，通用契约不因单个场景膨胀，所以另立一个 DTO。
/// 本类型只在 UI 线程被读取（由 <see cref="IDispatcher.Post"/> 封送后才交付给调用方）。
/// </remarks>
/// <param name="BytesReceived">已接收字节数，单调不减。</param>
/// <param name="TotalBytes">总字节数；0 表示长度未知（chunked，或服务器未给 Content-Length）。</param>
/// <param name="BytesPerSecond">平滑后的速率（字节/秒）；样本不足时为 0。</param>
/// <param name="Percent">0–100 的整数百分比；<paramref name="TotalBytes"/> 为 0 时恒为 0。</param>
/// <param name="Message">状态栏文案（已完成本地化，UI 直接显示）。</param>
public sealed record DownloadProgressInfo(
    long BytesReceived,
    long TotalBytes,
    double BytesPerSecond,
    int Percent,
    string Message)
{
    /// <summary>长度未知时为 false，UI 应显示不确定进度而不是 0%。</summary>
    public bool IsLengthKnown => TotalBytes > 0;

    /// <summary>剩余时间估算；速率或长度未知时为 null。</summary>
    public TimeSpan? Remaining
        => TotalBytes > 0 && BytesPerSecond > 0
            ? TimeSpan.FromSeconds(Math.Max(0, TotalBytes - BytesReceived) / BytesPerSecond)
            : null;

    /// <summary>起始帧：0 字节、0 速率、0%。</summary>
    public static DownloadProgressInfo Start(string message) => new(0, 0, 0, 0, message);
}
