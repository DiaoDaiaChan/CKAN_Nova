namespace CkanUI.Services.Abstractions.Dtos;

/// <summary>
/// 存档校验报告：解析统计 + 诊断汇总（13 篇 §6/§7，03 篇 §14.3）。
/// 诊断已按「级别 → 行 → 列」排序，可直接渲染。
/// </summary>
/// <param name="FilePath">被校验的文件。</param>
/// <param name="SizeBytes">文件字节数。</param>
/// <param name="LastWriteTime">文件修改时间（UTC）。</param>
/// <param name="HasBom">是否带 UTF-8 BOM（V-05）。</param>
/// <param name="DominantNewLine">主导行尾（<c>\r\n</c>/<c>\n</c>），空串表示无法判定。</param>
/// <param name="LineCount">行数。</param>
/// <param name="NodeCount">节点数（<c>{ }</c> 块）。</param>
/// <param name="PairCount">键值对数。</param>
/// <param name="MaxDepth">最大嵌套深度（超过 16 会出 V-07）。</param>
/// <param name="IsTextLossy">解码时是否有非法 UTF-8 被替换。</param>
/// <param name="Diagnostics">诊断列表。</param>
public sealed record SaveValidationReport(
    string FilePath,
    long SizeBytes,
    DateTimeOffset LastWriteTime,
    bool HasBom,
    string DominantNewLine,
    int LineCount,
    int NodeCount,
    int PairCount,
    int MaxDepth,
    bool IsTextLossy,
    IReadOnlyList<SaveDiagnosticInfo> Diagnostics)
{
    /// <summary>错误条数。</summary>
    public int ErrorCount => Diagnostics.Count(d => d.Severity == SaveDiagnosticSeverity.Error);

    /// <summary>警告条数。</summary>
    public int WarningCount => Diagnostics.Count(d => d.Severity == SaveDiagnosticSeverity.Warning);

    /// <summary>是否有错误级诊断（KSP 可能加载失败）。</summary>
    public bool HasErrors => ErrorCount > 0;

    /// <summary>是否有警告级诊断。</summary>
    public bool HasWarnings => WarningCount > 0;

    /// <summary>是否完全干净。</summary>
    public bool IsClean => Diagnostics.Count == 0;
}
