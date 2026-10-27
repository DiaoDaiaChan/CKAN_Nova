namespace CkanUI.Services.Abstractions.Dtos;

/// <summary>
/// 一次残留目录清理的计数汇总（12 篇 F-43）：删掉几个、跳过几个（受管/链接/已不存在）、失败几个。
/// <para>
/// 计数与明细同时给：UI 只展示计数，诊断与日志需要明细才能对上用户说的"那个文件夹"。
/// </para>
/// </summary>
/// <param name="DeletedCount">已删除的目录数。</param>
/// <param name="SkippedCount">被安全策略跳过的目录数（受管内容、符号链接、越权路径、已不存在）。</param>
/// <param name="FailedCount">删除时报错（占用/权限）的目录数。</param>
public sealed record ResidualCleanupReport(int DeletedCount, int SkippedCount, int FailedCount)
{
    /// <summary>已删除的目录（相对 GameData）。</summary>
    public IReadOnlyList<string> Deleted { get; init; } = Array.Empty<string>();

    /// <summary>被跳过的目录（相对 GameData）。</summary>
    public IReadOnlyList<string> Skipped { get; init; } = Array.Empty<string>();

    /// <summary>删除失败的目录（相对 GameData）。</summary>
    public IReadOnlyList<string> Failed { get; init; } = Array.Empty<string>();

    /// <summary>本次释放的字节数。</summary>
    public long FreedBytes { get; init; }

    /// <summary>什么都没发生时的空报告。</summary>
    public static ResidualCleanupReport Empty { get; } = new(0, 0, 0);
}
