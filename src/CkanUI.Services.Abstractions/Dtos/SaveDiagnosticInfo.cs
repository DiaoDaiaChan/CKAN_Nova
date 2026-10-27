namespace CkanUI.Services.Abstractions.Dtos;

/// <summary>
/// 单条存档诊断（13 篇 §6 的 V-01..V-09）。
/// </summary>
/// <param name="Severity">级别。</param>
/// <param name="Code">诊断码，例如 <c>V-01</c>。</param>
/// <param name="Line">1 基行号；0 表示不指向具体行。</param>
/// <param name="Column">1 基列号；0 表示不指向具体列。</param>
/// <param name="Message">中文说明。</param>
/// <param name="Snippet">相关片段（可能为 null）。</param>
public sealed record SaveDiagnosticInfo(
    SaveDiagnosticSeverity Severity,
    string Code,
    int Line,
    int Column,
    string Message,
    string? Snippet = null);
