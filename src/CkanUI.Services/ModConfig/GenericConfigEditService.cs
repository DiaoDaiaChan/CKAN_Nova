using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.ModConfig;
using log4net;

namespace CkanUI.Services.ModConfig;

/// <summary>
/// <see cref="IGenericConfigEditService"/> 的实现：把 <see cref="GenericConfigService"/> 的 ConfigNode 文档
/// 折成插件能用的行快照，写回时按行号把值贴回一份"重新解析出来的"文档再交给它落盘。
/// </summary>
/// <remarks>
/// 为什么要重新解析而不是让调用方交回文档：插件只拿到行号与值，文档对象是宿主内部类型；
/// 重新解析 + 按行贴值既保持了"只改值片段"的无损口径，也顺手挡掉了跨进程的过期文档。
/// 代价是拍下快照后文件被别人改过时，行号可能已挪位——这种场景由内核的 ParseError/值类型校验兜住，
/// 页面在保存失败时把服务层的原话显示给用户（宁可报错也不猜着写）。
/// </remarks>
public sealed class GenericConfigEditService : IGenericConfigEditService
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(GenericConfigEditService));

    private readonly IGenericConfigService _inner;

    public GenericConfigEditService(IGenericConfigService inner)
        => _inner = inner ?? throw new ArgumentNullException(nameof(inner));

    /// <inheritdoc />
    public Task<OperationResult<GenericConfigCatalog>> DiscoverAsync(
        string gameDir,
        string instanceName,
        CancellationToken cancellationToken = default)
        => _inner.DiscoverAsync(gameDir, instanceName, cancellationToken);

    /// <inheritdoc />
    public Task<OperationResult<GenericConfigCatalog>> DiscoverAsync(
        string gameDir,
        string instanceName,
        GenericConfigScanOptions options,
        CancellationToken cancellationToken = default)
        => _inner.DiscoverAsync(gameDir, instanceName, options, cancellationToken);

    /// <inheritdoc />
    public async Task<OperationResult<GenericConfigDocumentView>> LoadAsync(
        string gameDir,
        string filePath,
        CancellationToken cancellationToken = default)
    {
        var loaded = await _inner.LoadAsync(gameDir, filePath, cancellationToken).ConfigureAwait(false);
        if (!loaded.IsSuccess || loaded.Value is null)
        {
            return OperationResult<GenericConfigDocumentView>.Fail(
                loaded.Message ?? "读取配置失败。",
                loaded.Detail,
                loaded.Retryable);
        }

        var document = loaded.Value;
        var rows = document.Document.Pairs
            .Select(pair => new GenericConfigRow(
                pair.LineNumber,
                pair.Depth,
                pair.NodePath,
                pair.Key,
                pair.Value,
                pair.Comment))
            .ToArray();

        return OperationResult<GenericConfigDocumentView>.Ok(new GenericConfigDocumentView(
            document.FilePath,
            document.RelativePath,
            document.CanWrite,
            document.ReadonlyReason,
            rows,
            document.Document.ParseErrorCount));
    }

    /// <inheritdoc />
    public async Task<OperationResult<GenericConfigSaveResult>> SaveAsync(
        string gameDir,
        string instanceName,
        string filePath,
        IReadOnlyList<GenericConfigChange> changes,
        bool confirmedGameRunning,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(gameDir))
        {
            return OperationResult<GenericConfigSaveResult>.Fail("未选定有效游戏实例目录。");
        }

        if (changes is null || changes.Count == 0)
        {
            return OperationResult<GenericConfigSaveResult>.Fail("没有要写入的改动。");
        }

        ConfigNodeDocument document;
        try
        {
            var absolute = Path.IsPathRooted(filePath)
                ? filePath
                : Path.Combine(gameDir, filePath);
            document = ConfigNodeDocument.ParseFile(absolute);
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException
            or System.Security.SecurityException)
        {
            Log.Warn($"写回前重新读取失败（{filePath}）：{ex.Message}", ex);
            return OperationResult<GenericConfigSaveResult>.Fail(
                $"写回前重新读取失败：{ex.Message}",
                detail: ex.GetType().Name);
        }

        foreach (var change in changes)
        {
            if (change.NewValue is null)
            {
                return OperationResult<GenericConfigSaveResult>.Fail(
                    string.Create(CultureInfo.InvariantCulture, $"第 {change.LineNumber} 行的新值为空——ConfigNode 的键必须有值。"));
            }

            if (!document.TrySetValue(change.LineNumber, change.NewValue, out var error))
            {
                return OperationResult<GenericConfigSaveResult>.Fail(
                    string.Create(CultureInfo.InvariantCulture, $"第 {change.LineNumber} 行写不进去：{error}"),
                    detail: "invalid-value");
            }
        }

        return await _inner
            .SaveAsync(gameDir, instanceName, filePath, document, confirmedGameRunning, cancellationToken)
            .ConfigureAwait(false);
    }
}
