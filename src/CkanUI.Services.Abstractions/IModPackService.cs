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

    /// <summary>把当前实例的已安装模组导出成 <c>.ckan</c> 元数据包或非 ckan 文本/表格格式（可选连同缓存里的下载包打包成 zip）。</summary>
    Task<OperationResult<ModPackExportResult>> ExportAsync(
        ModPackExportRequest request,
        IProgress<OperationProgress>? progress = null,
        CancellationToken ct = default)
        => Task.FromResult(OperationResult<ModPackExportResult>.Fail("当前导入导出服务不可用。"));

    /// <summary>
    /// 将当前实例的已安装模组导出为非 ckan 的纯文本/标记/表格格式（F-45 / UI-24）。
    /// 支持 PlainText, Markdown, BbCode, CSV, TSV 格式。
    /// 默认实现（DIM）将请求转发给通用的 ExportAsync。
    /// </summary>
    /// <param name="request">导出请求参数（目标路径、导出格式）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>操作结果，包含成功写入的文件绝对路径。</returns>
    Task<OperationResult<string>> ExportInstalledListAsync(
        ExportInstalledListRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var exportRequest = new ModPackExportRequest(
            TargetPath: request.TargetPath,
            WithVersions: true,
            BundleDownloads: false,
            UseRecommends: false,
            Format: request.Format);

        return ExportAsync(exportRequest, progress: null, ct)
            .ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    var ex = t.Exception?.GetBaseException() ?? new Exception("导出失败");
                    return OperationResult<string>.Fail(ex.Message, ex.GetType().FullName);
                }

                var res = t.Result;
                if (!res.IsSuccess)
                {
                    return OperationResult<string>.Fail(res.Message ?? "导出失败", res.Detail, res.Retryable);
                }

                return OperationResult<string>.Ok(res.Value?.Path ?? request.TargetPath);
            }, TaskScheduler.Default);
    }

    /// <summary>
    /// 将当前已安装的模组列表渲染为指定格式的文本（供一键复制到剪贴板，UI-21 / F-45）。
    /// </summary>
    /// <param name="format">导出格式（纯文本、Markdown、BBCode、CSV、TSV）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>操作结果，包含格式化后的文本内容。</returns>
    Task<OperationResult<string>> RenderInstalledListTextAsync(
        InstalledListExportFormat format,
        CancellationToken ct = default)
        => Task.FromResult(OperationResult<string>.Fail("当前导入导出服务不可用。"));

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

    /// <summary>读取一个 <c>.ckan</c> 文件并投影成编辑器文档（UI-24 编辑器表单）。</summary>
    /// <param name="path">目标 <c>.ckan</c> 文件路径。</param>
    /// <param name="ct">取消令牌。</param>
    Task<OperationResult<ModPackEditorDocument>> LoadEditorAsync(
        string path,
        CancellationToken ct = default)
        => Task.FromResult(OperationResult<ModPackEditorDocument>.Fail("当前导入导出服务不可用。"));

    /// <summary>校验并写回一个 <c>.ckan</c> 文件；存在 Error 级问题时不写盘。</summary>
    /// <param name="document">编辑器文档。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>成功时给出实际写盘路径。</returns>
    Task<OperationResult<string>> SaveEditorAsync(
        ModPackEditorDocument document,
        CancellationToken ct = default)
        => Task.FromResult(OperationResult<string>.Fail("当前导入导出服务不可用。"));

    /// <summary>纯函数校验：不读盘、不写盘、不抛异常，返回全部问题（含提醒）。</summary>
    /// <param name="document">编辑器文档。</param>
    IReadOnlyList<ModPackEditorIssue> ValidateEditor(ModPackEditorDocument document)
        => Array.Empty<ModPackEditorIssue>();
}
