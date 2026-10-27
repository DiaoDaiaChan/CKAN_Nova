using System.Collections.Generic;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Services.ModPacks;

/// <summary>
/// <see cref="IModPackService"/> 的编辑器部分（UI-24）：读写单个 <c>.ckan</c> 文件、纯函数校验。
/// </summary>
/// <remarks>
/// 单独放一个 partial 文件，是为了让"导入导出"（<c>ModPackService.cs</c>）与"编辑器"互不干扰地演进。
/// 复用同一个 <c>RunAsync</c> 包装器：编辑器读写同样是同步阻塞的文件 IO，统一搬到线程池，
/// 并且取消 / 异常都收敛成 <see cref="OperationResult{T}"/> 而不是抛给界面。
/// </remarks>
public sealed partial class ModPackService
{
    private readonly ModPackEditorStore _editorStore = new();

    /// <inheritdoc />
    public Task<OperationResult<ModPackEditorDocument>> LoadEditorAsync(string path, CancellationToken ct = default)
        => RunAsync(() => _editorStore.Load(path), ct);

    /// <inheritdoc />
    public Task<OperationResult<string>> SaveEditorAsync(ModPackEditorDocument document, CancellationToken ct = default)
        => RunAsync(() => _editorStore.Save(document), ct);

    /// <inheritdoc />
    public IReadOnlyList<ModPackEditorIssue> ValidateEditor(ModPackEditorDocument document)
        => ModPackEditorValidator.Validate(document);
}
