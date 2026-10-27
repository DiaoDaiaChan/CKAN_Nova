namespace CkanUI.Services.Abstractions.Dtos;

/// <summary>诊断级别（与 SFS 内核 SfsSeverity 同序，契约层不引用内核类型）。</summary>
public enum SaveDiagnosticSeverity
{
    /// <summary>提示：不影响加载，但值得知道。</summary>
    Info = 0,

    /// <summary>警告：KSP 通常能容忍，建议修正。</summary>
    Warning = 1,

    /// <summary>错误：结构性问题，KSP 可能加载失败。</summary>
    Error = 2,
}
