using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Services.Abstractions;

/// <summary>查看模块 ZIP 内目录结构，并按需下载到 CKAN 缓存。</summary>
public interface IContentService
{
    /// <summary>读取已缓存 ZIP 的文件目录，不执行解压。</summary>
    Task<OperationResult<ContentPreview>> PreviewAsync(string identifier, CancellationToken ct = default);

    /// <summary>将模块 ZIP 下载到缓存并返回目标路径。</summary>
    Task<OperationResult<string>> EnsureCachedAsync(string identifier, IProgress<OperationProgress>? progress = null, CancellationToken ct = default);
}
