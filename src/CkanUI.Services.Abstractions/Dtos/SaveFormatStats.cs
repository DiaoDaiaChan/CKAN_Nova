namespace CkanUI.Services.Abstractions.Dtos;

/// <summary>格式化统计（对应 SFS 内核 SfsFormatStats 的可展示子集）。</summary>
/// <param name="ReindentedLines">重新缩进的行数。</param>
/// <param name="NormalizedSeparators">被规范成 <c> = </c> 的分隔符数。</param>
/// <param name="NormalizedLineEndings">被统一的行尾数。</param>
/// <param name="BlankLinesRemoved">被去掉的空行数（本实现保留空行，恒为 0）。</param>
public sealed record SaveFormatStats(
    int ReindentedLines,
    int NormalizedSeparators,
    int NormalizedLineEndings,
    int BlankLinesRemoved);
