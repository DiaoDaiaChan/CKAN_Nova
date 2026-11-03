using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Plugin.Saves;

/// <summary>
/// 诊断列表的一行投影（03 篇 §14.3：级别 / 消息 / 行列）。
/// </summary>
public sealed record SaveDiagnosticRowViewModel(
    SaveDiagnosticSeverity Severity,
    string Code,
    int Line,
    int Column,
    string Message,
    string? Snippet)
{
    /// <summary>从服务层 DTO 投影一行。</summary>
    public static SaveDiagnosticRowViewModel From(SaveDiagnosticInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        return new SaveDiagnosticRowViewModel(
            info.Severity,
            info.Code,
            info.Line,
            info.Column,
            info.Message,
            info.Snippet);
    }

    /// <summary>级别文案（中文，与 13 篇诊断口径一致）。</summary>
    public string SeverityText => Severity switch
    {
        SaveDiagnosticSeverity.Error => "错误",
        SaveDiagnosticSeverity.Warning => "警告",
        _ => "提示",
    };

    /// <summary>行列文案：解析错误可能没有列号，给 <c>第 N 行</c> 即可。</summary>
    public string LocationText => Column > 0
        ? $"第 {Line} 行第 {Column} 列"
        : $"第 {Line} 行";

    /// <summary>是否错误（视图据此上红色）。</summary>
    public bool IsError => Severity == SaveDiagnosticSeverity.Error;

    /// <summary>是否警告（视图据此上黄色）。</summary>
    public bool IsWarning => Severity == SaveDiagnosticSeverity.Warning;

    /// <summary>是否提示级（视图据此上次要文字色）。</summary>
    public bool IsInfo => Severity == SaveDiagnosticSeverity.Info;

    /// <summary>是否有源码片段。</summary>
    public bool HasSnippet => !string.IsNullOrEmpty(Snippet);

    /// <summary>片段文案（无片段时为空串，视图据此折叠）。</summary>
    public string SnippetText => Snippet ?? string.Empty;

    /// <summary>单行文本形式（复制到剪贴板 / 状态栏用）。</summary>
    public string DisplayText => $"{Code} {SeverityText} {LocationText}：{Message}";
}
