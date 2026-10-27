using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Services.Abstractions;

/// <summary>GameData 中未托管文件（M4，INT-09）。</summary>
/// <remarks>
/// <para><see cref="RelativePath"/> 一律是相对 <c>&lt;GameDir&gt;/GameData</c> 的正斜杠路径；目录项自身也出现在列表里，
/// 由消费方（<c>Pages/Unmanaged</c> 的树构建器）拼成树。</para>
/// <para>目录项的 <see cref="SizeBytes"/> 是其下未托管文件的递归合计；<see cref="ManagedChildCount"/> 是其下已受管文件的递归计数
/// （&gt; 0 表示这个目录里混着受管内容，不允许整目录删除）。</para>
/// </remarks>
public sealed record UnmanagedFileInfo(
    string RelativePath,
    long SizeBytes,
    bool IsDirectory,
    string? GuessIdentifier)
{
    /// <summary>该目录下（递归）已受管文件数；文件项恒为 0。</summary>
    public int ManagedChildCount { get; init; }

    /// <summary>该目录下（递归）未受管文件数；文件项恒为 1。</summary>
    public int UnmanagedChildCount { get; init; }

    /// <summary>最后修改时间（UTC）；读取失败时为 <c>null</c>。</summary>
    public DateTime? LastWriteTimeUtc { get; init; }
}

/// <summary>GameData 中未托管内容的扫描、预览删除与删除（12 篇 F-42 / UI-10）。</summary>
public interface IUnmanagedService
{
    /// <summary>扫描实例 GameData 下所有未受注册表登记的文件与目录（树以扁平列表给出）。</summary>
    Task<OperationResult<IReadOnlyList<UnmanagedFileInfo>>> ScanAsync(
        InstanceInfo instance,
        CancellationToken ct = default);

    /// <summary>把选中的文件接管为托管安装（写进注册表）。</summary>
    Task<OperationResult<IReadOnlyList<string>>> AdoptAsync(
        InstanceInfo instance,
        IReadOnlyCollection<string> relativePaths,
        CancellationToken ct = default);

    /// <summary>
    /// 删除 GameData 中的未托管文件/目录。破坏性操作：默认 <paramref name="dryRun"/> 为 <c>true</c>，
    /// 即只做校验并返回「将被删除」的相对路径，绝不落盘；界面必须先预演再二次确认后才传 <c>false</c>。
    /// 只要选中项里含受管路径，整批拒绝、不删任何内容。
    /// </summary>
    Task<OperationResult<IReadOnlyList<string>>> DeleteAsync(
        InstanceInfo instance,
        IReadOnlyCollection<string> relativePaths,
        bool dryRun = true,
        CancellationToken ct = default)
        => Task.FromResult(OperationResult<IReadOnlyList<string>>.Fail(
            "当前实现未提供未管理文件删除能力。",
            "IUnmanagedService.DeleteAsync 的默认实现（契约决策 §12 #9）。",
            retryable: false));
}
