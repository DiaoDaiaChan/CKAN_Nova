namespace CkanUI.Services.Abstractions.Dtos;

/// <summary>
/// 供已安装列表多格式导出使用的数据项 (F-45 / UI-21)。
/// </summary>
public sealed record InstalledModExportItem(
    string Identifier,
    string Name,
    string InstalledVersion,
    string? Author = null,
    string? Homepage = null,
    string? DownloadUrl = null,
    string? Description = null,
    string? License = null,
    long? DownloadSizeBytes = null
);
