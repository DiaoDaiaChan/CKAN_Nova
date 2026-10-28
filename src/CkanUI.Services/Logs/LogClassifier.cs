using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Logs;

namespace CkanUI.Services.Logs;

/// <summary>
/// 族判定引擎（14 篇 §3.3 / LOG-03）：有序规则表 + 权重，外置 JSON 可热更新，内置规则兜底。
/// 为什么「取最高权重」而不是「第一个命中」：规则表要能随手追加而不重排顺序，
/// 权重把优先级写在数据里，热更新换文件时不会因为顺序变化导致族漂移。
/// </summary>
public sealed class LogClassifier : ILogClassifier
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string? _rulesPath;
    private CompiledRule[] _rules;
    private LogRuleSet _ruleSet;

    /// <summary>构造分类器。<paramref name="rulesPath"/> 为 null 时直接用内置规则。</summary>
    public LogClassifier(string? rulesPath = null)
    {
        _rulesPath = string.IsNullOrWhiteSpace(rulesPath) ? null : rulesPath;
        _ruleSet = LogRuleSet.Empty;
        _rules = [];
        Load();
    }

    /// <inheritdoc />
    public LogRuleSet RuleSet => Volatile.Read(ref _ruleSet);

    /// <summary>当前生效规则条数（诊断包/UI 显示）。</summary>
    public int RuleCount => Volatile.Read(ref _rules).Length;

    /// <summary>最近一次加载失败的说明（成功时 null）。</summary>
    public string? LastLoadError { get; private set; }

    /// <inheritdoc />
    public LogFamily Classify(LogSourceKind kind, LogLevel level, string? source, string? message, string? detail)
    {
        var rules = Volatile.Read(ref _rules);
        if (rules.Length == 0)
        {
            return kind == LogSourceKind.Ui ? LogFamily.Ui : LogFamily.Other;
        }

        var text = ComposeText(source, message, detail);
        if (text.Length == 0)
        {
            return kind == LogSourceKind.Ui ? LogFamily.Ui : LogFamily.Other;
        }

        var bestFamily = LogFamily.Other;
        var bestWeight = int.MinValue;
        foreach (var rule in rules)
        {
            if (!rule.PrefilterPasses(text))
            {
                continue;
            }

            if (!rule.Regex.IsMatch(text))
            {
                continue;
            }

            if (rule.Rule.Weight > bestWeight)
            {
                bestWeight = rule.Rule.Weight;
                bestFamily = rule.Rule.Family;
            }
        }

        if (bestWeight != int.MinValue)
        {
            return bestFamily;
        }

        // 没命中任何规则：UI 自己产生的事件至少归 Ui，便于「只看我们自己的问题」。
        return kind == LogSourceKind.Ui ? LogFamily.Ui : LogFamily.Other;
    }

    /// <inheritdoc />
    public OperationResult Reload()
    {
        Load();
        return LastLoadError is null
            ? OperationResult.Ok()
            : OperationResult.Fail("规则表加载失败，已保留内置规则", LastLoadError);
    }

    /// <summary>拼接参与匹配的文本（来源 + 原文 + Detail）：Core 的 logger 名本身带语义，忽略它会漏判。</summary>
    public static string ComposeText(string? source, string? message, string? detail) =>
        string.Concat(source, "\n", message, "\n", detail);

    private void Load()
    {
        LastLoadError = null;
        if (_rulesPath is not null && File.Exists(_rulesPath))
        {
            try
            {
                var json = File.ReadAllText(_rulesPath);
                var file = JsonSerializer.Deserialize<RulesFile>(json, JsonOptions);
                if (file?.Rules is { Count: > 0 } loaded)
                {
                    Apply(new LogRuleSet(loaded, _rulesPath));
                    return;
                }

                LastLoadError = "规则表为空";
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
                LastLoadError = exception.Message;
            }
        }

        Apply(BuiltinLogRules.Create());
    }

    private void Apply(LogRuleSet ruleSet)
    {
        var compiled = new List<CompiledRule>(ruleSet.Count);
        foreach (var rule in ruleSet.Rules)
        {
            if (string.IsNullOrWhiteSpace(rule.Pattern))
            {
                continue;
            }

            try
            {
                compiled.Add(new CompiledRule(
                    rule,
                    new Regex(rule.Pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)));
            }
            catch (ArgumentException)
            {
                // 单条规则写坏不能拖垮整套表：跳过并继续（热更新时用户手改 JSON 很容易写错正则）。
            }
        }

        Volatile.Write(ref _ruleSet, ruleSet);
        Volatile.Write(ref _rules, [.. compiled]);
    }

    private sealed record RulesFile(List<LogRule>? Rules);

    /// <summary>编译后的规则：正则 + 字面量预筛（预筛不过就不跑正则）。</summary>
    private sealed class CompiledRule
    {
        private readonly string[] _keywords;

        public CompiledRule(LogRule rule, Regex regex)
        {
            Rule = rule;
            Regex = regex;
            _keywords = rule.Keywords is { Count: > 0 } keywords ? [.. keywords] : [];
        }

        public LogRule Rule { get; }

        public Regex Regex { get; }

        public bool PrefilterPasses(string text)
        {
            if (_keywords.Length == 0)
            {
                return true;
            }

            foreach (var keyword in _keywords)
            {
                if (text.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
