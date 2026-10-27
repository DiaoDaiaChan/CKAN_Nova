namespace CkanUI.Services.Abstractions.Dtos;

/// <summary>ZIP 包内容的安全元数据预览，不包含解压后的文件内容。</summary>
public sealed record ContentPreview(string Identifier, bool IsCached, string? CacheFilePath, long TotalBytes, IReadOnlyList<ContentEntry> Entries);

/// <summary>ZIP 中的文件或隐式目录。</summary>
public sealed record ContentEntry(string RelativePath, bool IsDirectory, long SizeBytes, int Depth);
