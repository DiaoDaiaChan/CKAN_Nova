using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Services.Abstractions;

/// <summary>
/// 模组列表的导入 / 导出 / 实例比对（03 篇 §7 导入导出对话框，06 篇 UI-20 / UI-24，01 篇 F-39 / F-40 / F-41 / F-45）。
/// </summary>
/// <remarks>
/// 四组能力：导出（已装模组 → <c>.ckan</c> 元数据包，可选打包下载 zip）、导入预览（先算清楚会装/会升/会冲突/找不到）、
/// 导入执行（复用 <see cref="IInstallerService.ApplyAsync"/> 的安装流水线，不另写下载逻辑）、实例比对（两个实例的模组差异）。
/// 所有成员都给了默认实现（DIM），因此任何已有替身不必为新增成员改代码；默认实现明确返回"不可用"而不是假装成功。
/// </remarks>
public interface IModPackService
{
    /// <summary>当前是否有可用的游戏实例（没有实例时导出与导入预览都无从谈起）。</summary>
    bool HasActiveInstance => false;

    /// <summary>当前实例名；没有实例时为 null。</summary>
    string? InstanceName => null;

    /// <summary>把当前实例的已安装模组导出成 <c>.ckan</c> 元数据包（可选连同缓存里的下载包打包成 zip）。</summary>
    Task<OperationResult<ModPackExportResult>> ExportAsync(
        ModPackExportRequest request,
        IProgress<OperationProgress>? progress = null,
        CancellationToken ct = default)
        => Task.FromResult(OperationResult<ModPackExportResult>.Fail("当前导入导出服务不可用。"));

    /// <summary>预览一组 <c>.ckan</c> 文件会带来什么变化；绝不落盘、绝不静默安装。</summary>
    Task<OperationResult<ModPackPreview>> PreviewImportAsync(
        IReadOnlyCollection<string> sourcePaths,
        CancellationToken ct = default)
        => Task.FromResult(OperationResult<ModPackPreview>.Fail("当前导入导出服务不可用。"));

    /// <summary>按预览结果执行导入：只用勾选的条目，走 <see cref="IChangeSetService"/> + <see cref="IInstallerService"/>。</summary>
    /// <param name="preview">先由 <see cref="PreviewImportAsync"/> 得到的预览。</param>
    /// <param name="selectedIdentifiers">勾选的模组标识；null 表示预览里所有可执行条目。</param>
    /// <param name="progress">进度回调（安装流水线自己的进度）。</param>
    /// <param name="ct">取消令牌。</param>
    Task<OperationResult<ModPackImportReport>> ImportAsync(
        ModPackPreview preview,
        IReadOnlyCollection<string>? selectedIdentifiers = null,
        IProgress<OperationProgress>? progress = null,
        CancellationToken ct = default)
        => Task.FromResult(OperationResult<ModPackImportReport>.Fail("当前导入导出服务不可用。"));

    /// <summary>比对两个实例的模组差异（仅 A 有 / 仅 B 有 / 版本不同），纯读取、不改任何实例。</summary>
    Task<OperationResult<InstanceComparison>> CompareAsync(
        string leftInstance,
        string rightInstance,
        CancellationToken ct = default)
        => Task.FromResult(OperationResult<InstanceComparison>.Fail("当前导入导出服务不可用。"));
}
