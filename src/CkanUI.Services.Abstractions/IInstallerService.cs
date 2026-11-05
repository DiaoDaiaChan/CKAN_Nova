using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Services.Abstractions;

/// <summary>安装结果汇总（部分失败也要能说清哪几个失败）。</summary>
public sealed record InstallReport(
    IReadOnlyList<string> Installed,
    IReadOnlyList<string> Removed,
    IReadOnlyList<string> Failed,
    IReadOnlyList<string> Warnings)
{
    /// <summary>
    /// 全部落盘成功（B1）。警告不是失败：只装了一部分或某项装失败才算不完整，
    /// 仅出现警告时仍是一次完整成功。
    /// </summary>
    public bool IsFullSuccess => Failed.Count == 0;

    /// <summary>这次安装是否带出了警告（警告不等于失败，界面单独提示即可）。</summary>
    public bool HasWarnings => Warnings.Count > 0;

    /// <summary>
    /// 本次卸载/替换留下的残留目录（12 篇 F-43）：相对 <c>&lt;GameDir&gt;/GameData</c>、正斜杠。
    /// <para>
    /// 以 <c>init</c> 属性追加而不是新增构造参数：既有构造调用点（含测试）一个都不用改，
    /// 默认空集合等价于"没有残留"。界面把这份清单原样喂给残留清理对话框的"卸载结果"来源。
    /// </para>
    /// </summary>
    public IReadOnlyList<string> ResidualDirectories { get; init; } = Array.Empty<string>();
}

/// <summary>
/// 封装 NetAsyncModulesDownloader + ModuleInstaller.InstallList（02 篇 §4/§7）。
/// 实现内部必须传 ConfirmPrompt:false，确认由 UI 先行完成。
/// </summary>
public interface IInstallerService
{
    Task<OperationResult<InstallReport>> ApplyAsync(
        ChangeSetPreview changeSet,
        IProgress<OperationProgress>? progress = null,
        CancellationToken ct = default);
}
