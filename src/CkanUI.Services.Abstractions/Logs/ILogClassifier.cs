namespace CkanUI.Services.Abstractions.Logs;

/// <summary>
/// 族判定引擎（14 篇 §8 LOG-03）。
/// 采集侧在入队前调用它，所以时间轴/过滤/着色看到的事件一定带族——UI 不做分类，避免「同一行在列表与报告里族不一致」。
/// </summary>
public interface ILogClassifier
{
    /// <summary>当前生效的规则表（快照测试与诊断包用）。</summary>
    LogRuleSet RuleSet { get; }

    /// <summary>判定族。无命中返回 <see cref="LogFamily.Other"/>。</summary>
    LogFamily Classify(LogSourceKind kind, LogLevel level, string? source, string? message, string? detail);

    /// <summary>重新加载规则表（热更新）。失败保留旧表并返回失败原因。</summary>
    OperationResult Reload();
}
