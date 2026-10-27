namespace CkanUI.Services.Abstractions.Dtos;

/// <summary>格式化请求。</summary>
/// <param name="FilePath">要格式化的存档路径。</param>
/// <param name="Mode">落盘方式，默认只写新文件（安全默认）。</param>
public sealed record SaveFormatRequest(
    string FilePath,
    SaveFormatMode Mode = SaveFormatMode.NewFile);
