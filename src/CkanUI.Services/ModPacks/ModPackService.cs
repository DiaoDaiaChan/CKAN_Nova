using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Services.ModPacks;

/// <summary>
/// <see cref="IModPackService"/> 的实现：只做三件事——把同步的 Core 调用搬离 UI 线程、
/// 把预览结果翻译成变更集请求、把安装交给既有的 <see cref="IInstallerService"/>。
/// </summary>
/// <remarks>
/// 刻意不自己下载、不自己解压、不自己改注册表：导入执行必须复用
/// <c>IInstallerService.ApplyAsync</c>（下载/校验/回滚/进度全在那条链路上），
/// 否则会出现两套并行的安装路径（12 篇 F-39 的验收要求）。
/// 所有异常在这里收口成中文 <see cref="OperationResult{T}"/>，UI 侧只负责显示。
/// </remarks>
public sealed class ModPackService : IModPackService
{
    private const string ExportTaskId = "modpack-export";
    private const string ImportTaskId = "modpack-import";

    private readonly ModPackCatalog _catalog;
    private readonly IChangeSetService _changeSets;
    private readonly IInstallerService _installer;

    /// <summary>构造导入导出服务。</summary>
    public ModPackService(ModPackCatalog catalog, IChangeSetService changeSets, IInstallerService installer)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _changeSets = changeSets ?? throw new ArgumentNullException(nameof(changeSets));
        _installer = installer ?? throw new ArgumentNullException(nameof(installer));
    }

    /// <inheritdoc />
    public bool HasActiveInstance => _catalog.HasActiveInstance;

    /// <inheritdoc />
    public string? InstanceName => _catalog.InstanceName;

    /// <inheritdoc />
    public Task<OperationResult<ModPackExportResult>> ExportAsync(
        ModPackExportRequest request,
        IProgress<OperationProgress>? progress = null,
        CancellationToken ct = default)
    {
        progress?.Report(new OperationProgress(
            ExportTaskId, "export", "正在导出模组列表", null, 0d, false, OperationState.Running));

        return RunAsync(() =>
        {
            var result = _catalog.Export(request);
            progress?.Report(new OperationProgress(
                ExportTaskId,
                "export",
                result.IsSuccess ? "导出完成" : "导出失败",
                result.IsSuccess ? result.Value?.Path : result.Message,
                result.IsSuccess ? 100d : 0d,
                false,
                result.IsSuccess ? OperationState.Succeeded : OperationState.Failed));
            return result;
        }, ct);
    }

    /// <inheritdoc />
    public Task<OperationResult<ModPackPreview>> PreviewImportAsync(
        IReadOnlyCollection<string> sourcePaths,
        CancellationToken ct = default)
        => RunAsync(() => _catalog.Preview(sourcePaths), ct);

    /// <inheritdoc />
    public async Task<OperationResult<ModPackImportReport>> ImportAsync(
        ModPackPreview preview,
        IReadOnlyCollection<string>? selectedIdentifiers = null,
        IProgress<OperationProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (preview is null)
        {
            return OperationResult<ModPackImportReport>.Fail("没有可导入的预览结果，请先选择 .ckan 文件。");
        }

        HashSet<string>? selected = null;
        if (selectedIdentifiers is not null)
        {
            selected = new HashSet<string>(selectedIdentifiers, StringComparer.OrdinalIgnoreCase);
        }

        var entries = preview.Entries
            .Where(entry => entry.IsSelectable && (selected is null || selected.Contains(entry.Identifier)))
            .ToList();
        if (entries.Count == 0)
        {
            return OperationResult<ModPackImportReport>.Fail("没有勾选任何可安装的条目。");
        }

        var requests = entries
            .Select(entry => new ChangeRequest(entry.Identifier, ToAction(entry.State), entry.TargetVersion))
            .ToList();

        try
        {
            ct.ThrowIfCancellationRequested();

            // 依赖求解仍交给既有的变更集服务：它会补齐依赖、算出下载体积、报告 resolver 冲突。
            var built = await _changeSets.BuildAsync(requests, ct).ConfigureAwait(false);
            if (!built.IsSuccess || built.Value is null)
            {
                return OperationResult<ModPackImportReport>.Fail(
                    built.Message ?? "依赖求解失败。", built.Detail, built.Retryable);
            }

            if (built.Value.HasConflicts)
            {
                return OperationResult<ModPackImportReport>.Fail(
                    "依赖求解发现冲突，已取消导入。",
                    string.Join("；", built.Value.Conflicts),
                    retryable: true);
            }

            var applied = await _installer.ApplyAsync(built.Value, progress, ct).ConfigureAwait(false);
            if (!applied.IsSuccess || applied.Value is null)
            {
                return OperationResult<ModPackImportReport>.Fail(
                    applied.Message ?? "安装失败。", applied.Detail, applied.Retryable);
            }

            var report = applied.Value;
            var warnings = new List<string>(preview.Warnings);
            warnings.AddRange(built.Value.Warnings);
            warnings.AddRange(report.Warnings);

            return OperationResult<ModPackImportReport>.Ok(
                new ModPackImportReport(report.Installed, report.Failed, warnings));
        }
        catch (OperationCanceledException)
        {
            return OperationResult<ModPackImportReport>.Fail("操作已取消。", retryable: true);
        }
        catch (Exception exception)
        {
            return OperationResult<ModPackImportReport>.Fail("导入失败。", exception.Message, retryable: true);
        }
    }

    /// <inheritdoc />
    public Task<OperationResult<InstanceComparison>> CompareAsync(
        string leftInstance,
        string rightInstance,
        CancellationToken ct = default)
        => RunAsync(() => _catalog.Compare(leftInstance, rightInstance), ct);

    private static ChangeAction ToAction(ModPackEntryState state) => state switch
    {
        ModPackEntryState.Upgrade => ChangeAction.Upgrade,
        ModPackEntryState.Downgrade => ChangeAction.Downgrade,
        _ => ChangeAction.Install,
    };

    private static async Task<OperationResult<T>> RunAsync<T>(Func<OperationResult<T>> work, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(work);

        try
        {
            ct.ThrowIfCancellationRequested();
            // Core 的注册表访问是同步阻塞的，统一搬到线程池，UI 线程不参与 IO。
            return await Task.Run(work, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return OperationResult<T>.Fail("操作已取消。", retryable: true);
        }
        catch (Exception exception)
        {
            return OperationResult<T>.Fail("导入导出操作失败。", exception.Message, retryable: true);
        }
    }
}
