using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
namespace CkanUI.Services.Content;

/// <summary>隔离 CKAN ZIP 缓存存取，供内容目录服务及离线测试使用。</summary>
public interface IContentZipStore
{
    bool IsCached(string identifier);
    string? GetCachedPath(string identifier);
    Task<OperationResult<string>> EnsureCachedAsync(string identifier, IProgress<OperationProgress>? progress, CancellationToken ct);
}
