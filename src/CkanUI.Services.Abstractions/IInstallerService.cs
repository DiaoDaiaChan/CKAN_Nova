using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Services.Abstractions;

/// <summary>安装结果汇总（部分失败也要能说清哪几个失败）。</summary>
public sealed record InstallReport(
    IReadOnlyList<string> Installed,
    IReadOnlyList<string> Removed,
    IReadOnlyList<string> Failed,
    IReadOnlyList<string> Warnings)
{
    public bool IsFullSuccess => Failed.Count == 0 && Warnings.Count == 0;
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
