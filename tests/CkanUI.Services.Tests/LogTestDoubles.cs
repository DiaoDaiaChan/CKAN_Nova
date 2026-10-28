using CKAN;

using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Logs;

namespace CkanUI.Services.Tests;

/// <summary>固定返回同一族的假分类器：用来验证「存储层如何对待分类结果」而不是规则表本身。</summary>
internal sealed class StubClassifier(LogFamily family) : ILogClassifier
{
    public LogFamily Result { get; set; } = family;

    public int ClassifyCalls { get; private set; }

    public LogRuleSet RuleSet { get; } = LogRuleSet.Empty;

    public int ReloadCount { get; private set; }

    public LogFamily Classify(LogSourceKind kind, LogLevel level, string? source, string? message, string? detail)
    {
        ClassifyCalls++;
        return Result;
    }

    public OperationResult Reload()
    {
        ReloadCount++;
        return OperationResult.Ok();
    }
}

/// <summary>记录型 IUser 假实现：验证 LoggingUser 装饰器有没有转发、有没有多写。</summary>
internal sealed class RecordingUser : IUser
{
    public List<string> Messages { get; } = [];

    public List<string> Errors { get; } = [];

    public List<(string Message, int Percent)> Progress { get; } = [];

    public int ProgressRateCalls { get; private set; }

    public int YesNoCalls { get; private set; }

    public int SelectionCalls { get; private set; }

    public bool Headless => false;

    public bool RaiseYesNoDialog(string question)
    {
        YesNoCalls++;
        return true;
    }

    public int RaiseSelectionDialog(string message, params object[] args)
    {
        SelectionCalls++;
        return 2;
    }

    public void RaiseError(string message, params object[] args) => Errors.Add(string.Format(message, args));

    public void RaiseProgress(string message, int percent) => Progress.Add((message, percent));

    public void RaiseProgress(ByteRateCounter rateCounter) => ProgressRateCalls++;

    public void RaiseMessage(string message, params object[] args) => Messages.Add(string.Format(message, args));
}

/// <summary>假事实提供者：诊断包测试用它注入可预测的 summary/config。</summary>
internal sealed class StubFactsProvider(DiagnosticFacts? facts) : IDiagnosticFactsProvider
{
    public int Calls { get; private set; }

    public Task<DiagnosticFacts?> CollectAsync(CancellationToken cancellationToken = default)
    {
        Calls++;
        return Task.FromResult(facts);
    }
}
