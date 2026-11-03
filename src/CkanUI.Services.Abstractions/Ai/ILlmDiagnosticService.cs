using CkanUI.Services.Abstractions.Logs;

namespace CkanUI.Services.Abstractions.Ai;

/// <summary>一次 AI 排障请求：配置 + 已经拼好的提示词（提示词由 <c>DiagnosticPromptBuilder</c> 生成，服务层不再二次加工）。</summary>
public sealed record LlmDiagnosisRequest
{
    /// <summary>OpenAI 兼容端点根地址（例如 <c>https://api.deepseek.com/v1</c>）。</summary>
    public string BaseUrl { get; init; } = string.Empty;

    /// <summary>模型名（例如 <c>deepseek-chat</c>）。</summary>
    public string Model { get; init; } = string.Empty;

    /// <summary>API Key 明文（只在内存里存活，落盘由 <c>DpapiSecret</c> 加密）。</summary>
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>完整提示词。</summary>
    public string Prompt { get; init; } = string.Empty;

    /// <summary>输出上限，防止一次诊断烧掉整月额度。</summary>
    public int MaxOutputTokens { get; init; } = 1200;
}

/// <summary>模型给出的一条假设。置信度低于 0.5 的在界面上折叠，避免玩家按噪声乱改配置。</summary>
public sealed record LlmHypothesis(
    string Cause,
    double Confidence,
    IReadOnlyList<int> EvidenceSeq,
    string Explain,
    string Fix);

/// <summary>结构化诊断结果：与 docs/plan/14-log-and-ai-debug.md §6 的 JSON schema 对齐。</summary>
public sealed record LlmDiagnosis(
    string Summary,
    string Severity,
    IReadOnlyList<LlmHypothesis> Hypotheses,
    IReadOnlyList<string> NeedMoreInfo);

/// <summary>AI 排障服务。实现只管「发出去、拿回来、按 schema 解析」，不碰 UI 状态，也不决定要不要发。</summary>
public interface ILlmDiagnosticService
{
    /// <summary>发送一次诊断请求。所有失败都以 <see cref="OperationResult{T}"/> 返回，不抛异常。</summary>
    Task<OperationResult<LlmDiagnosis>> DiagnoseAsync(
        LlmDiagnosisRequest request,
        CancellationToken cancellationToken = default);
}
