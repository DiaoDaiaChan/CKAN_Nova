using System.IO.Compression;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Services.Content;

/// <summary>读取 ZIP 中央目录并合并显式/隐式目录；不提取归档内容。</summary>
public sealed class ContentService(IContentZipStore store) : IContentService
{
    private const int MaxEntries = 20000;
    private const long MaxDisplayedFileSize = 1L << 50;
    private readonly IContentZipStore _store = store ?? throw new ArgumentNullException(nameof(store));

    public async Task<OperationResult<ContentPreview>> PreviewAsync(string identifier, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(identifier)) return OperationResult<ContentPreview>.Fail("未指定模组标识符。");
        var entries = new Dictionary<string, ContentEntry>(StringComparer.Ordinal);
        try
        {
            ct.ThrowIfCancellationRequested();
            var path = _store.GetCachedPath(identifier);
            if (path is null || !_store.IsCached(identifier) || !File.Exists(path)) return OperationResult<ContentPreview>.Fail("该模组的 ZIP 尚未下载到缓存。");
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
            long total = 0;
            foreach (var item in archive.Entries)
            {
                ct.ThrowIfCancellationRequested();
                var normalized = item.FullName.Replace('\\', '/').TrimStart('/');
                if (normalized.Length == 0) continue;
                var isDirectory = normalized.EndsWith("/", StringComparison.Ordinal);
                normalized = normalized.TrimEnd('/');
                if (normalized.Length == 0 || normalized.Split('/').Any(part => part is "." or "..")) return OperationResult<ContentPreview>.Fail("ZIP 包含无效的路径，无法安全显示。");
                var parts = normalized.Split('/');
                for (var i = 1; i < parts.Length; i++) AddDirectory(string.Join('/', parts, 0, i));
                if (isDirectory) AddDirectory(normalized);
                else { var size = Math.Clamp(item.Length, 0, MaxDisplayedFileSize); entries[normalized] = new ContentEntry(normalized, false, size, parts.Length - 1); total = checked(total + item.Length); }
                if (entries.Count > MaxEntries) return OperationResult<ContentPreview>.Fail("ZIP 条目过多，超过 20000 项，已停止预览。");
            }
            return OperationResult<ContentPreview>.Ok(new ContentPreview(identifier, true, path, total, entries.Values.OrderBy(e => e.RelativePath, StringComparer.Ordinal).ToArray()));
        }
        catch (OperationCanceledException ex) { return OperationResult<ContentPreview>.Fail("操作已取消。", ex.Message); }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or OverflowException) { return OperationResult<ContentPreview>.Fail("ZIP 文件损坏或无法读取，不能预览内容。", ex.Message); }
        void AddDirectory(string directory) => entries.TryAdd(directory, new ContentEntry(directory, true, 0, directory.Count(c => c == '/') + 1));
    }
    public Task<OperationResult<string>> EnsureCachedAsync(string identifier, IProgress<OperationProgress>? progress = null, CancellationToken ct = default) => _store.EnsureCachedAsync(identifier, progress, ct);
}
