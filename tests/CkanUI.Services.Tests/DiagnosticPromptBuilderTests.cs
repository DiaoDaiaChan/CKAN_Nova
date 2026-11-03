using System;
using System.Collections.Generic;
using System.Linq;

using CkanUI.Services.Abstractions.Logs;
using CkanUI.Services.Abstractions.Ai;

using NUnit.Framework;

namespace CkanUI.Services.Tests;

/// <summary>
/// 提示词拼装（14 篇 §6）：序号必须真实存在、内容必须先脱敏、超预算先丢最旧整行。
/// 这三条任何一条破了，模型给的「证据」就是假的，玩家照着修反而更糟。
/// </summary>
[TestFixture]
public sealed class DiagnosticPromptBuilderTests
{
    private static LogEvent Event(int seq, LogLevel level, string message) =>
        new()
        {
            Seq = seq,
            TimeLocal = new DateTime(2024, 5, 6, 12, 34, 56, DateTimeKind.Local),
            Level = level,
            Kind = LogSourceKind.Game,
            Source = LogSources.Encode(LogSourceKind.Game, "测试实例"),
            Message = message,
        };

    [Test]
    public void Build_EmitsSeqTimeLevelAndMessagePerLine()
    {
        var prompt = DiagnosticPromptBuilder.Build(
            new[] { Event(7, LogLevel.Error, "NullReferenceException at PartLoader") },
            "测试实例",
            "1.12.5",
            "游戏日志");

        Assert.That(prompt, Does.Contain("#7 12:34:56.000 ERR"));
        Assert.That(prompt, Does.Contain("NullReferenceException at PartLoader"));
        Assert.That(prompt, Does.Contain("测试实例"));
        Assert.That(prompt, Does.Contain("1.12.5"));
        Assert.That(prompt, Does.Contain("游戏日志"));
    }

    [Test]
    public void Build_RedactsBeforeSending()
    {
        var prompt = DiagnosticPromptBuilder.Build(
            new[] { Event(1, LogLevel.Info, "C:/Users/43701/Documents/token=abc") },
            "i",
            null,
            "界面日志",
            new ReplacementRedactor());

        Assert.That(prompt, Does.Not.Contain("43701"));
        Assert.That(prompt, Does.Contain("[已脱敏]"));
    }

    [Test]
    public void Build_DropsOldestWholeLinesWhenOverBudget()
    {
        var events = Enumerable.Range(1, 200)
            .Select(i => Event(i, LogLevel.Info, new string('x', 80)))
            .ToList();

        var prompt = DiagnosticPromptBuilder.Build(events, "i", null, "游戏日志", budget: 2000);

        Assert.That(prompt, Does.Contain("行之前的日志已按预算省略"));
        Assert.That(prompt, Does.Contain("#200 "), "最新的行必须留下");
        Assert.That(prompt, Does.Not.Contain("#1 12:34:56"), "最旧的行应该先被丢掉");
        Assert.That(prompt.Length, Is.LessThan(2200));
    }

    [Test]
    public void Build_UsesPlaceholdersWhenInstanceIsUnknown()
    {
        var prompt = DiagnosticPromptBuilder.Build(Array.Empty<LogEvent>(), null, null, string.Empty);

        Assert.That(prompt, Does.Contain("（未选中）"));
        Assert.That(prompt, Does.Contain("未知"));
        Assert.That(prompt, Does.Contain("界面日志"));
    }

    private sealed class ReplacementRedactor : ILogRedactor
    {
        public string Redact(string? text) =>
            text?.Replace("C:/Users/43701/Documents/token=abc", "[已脱敏]") ?? string.Empty;

        public IReadOnlyDictionary<string, string> RedactConfig(IReadOnlyDictionary<string, string?> values) =>
            new Dictionary<string, string>();

        public IReadOnlyList<RedactionLeak> FindLeaks(string? text) => Array.Empty<RedactionLeak>();
    }
}
