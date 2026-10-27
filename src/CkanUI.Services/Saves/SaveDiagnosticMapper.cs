using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Sfs;

namespace CkanUI.Services.Saves;

/// <summary>
/// SFS 内核诊断 → 契约 DTO 的映射（契约层零依赖，不能直接暴露 SfsDiagnostic）。
/// </summary>
internal static class SaveDiagnosticMapper
{
    /// <summary>映射单条诊断；级别与内核枚举同序。</summary>
    public static SaveDiagnosticInfo Map(SfsDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);

        var severity = diagnostic.Severity switch
        {
            SfsSeverity.Error => SaveDiagnosticSeverity.Error,
            SfsSeverity.Warning => SaveDiagnosticSeverity.Warning,
            _ => SaveDiagnosticSeverity.Info,
        };

        return new SaveDiagnosticInfo(
            severity,
            diagnostic.Code,
            diagnostic.Line,
            diagnostic.Column,
            diagnostic.Message,
            diagnostic.Snippet);
    }

    /// <summary>批量映射（保持内核给出的顺序）。</summary>
    public static IReadOnlyList<SaveDiagnosticInfo> MapAll(IReadOnlyList<SfsDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        if (diagnostics.Count == 0)
        {
            return Array.Empty<SaveDiagnosticInfo>();
        }

        var mapped = new SaveDiagnosticInfo[diagnostics.Count];
        for (var i = 0; i < diagnostics.Count; i++)
        {
            mapped[i] = Map(diagnostics[i]);
        }

        return mapped;
    }
}
