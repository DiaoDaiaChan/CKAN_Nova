using System.Globalization;

namespace CkanUI.Services.Abstractions.Logs;

/// <summary>
/// 搜索语法解析（14 篇 §4.2 ②）：子串 + <c>族:net</c>、<c>级别:error</c>、<c>源:user</c>、<c>mod:xxx</c>。
/// 放在抽象层是因为「语法」属于查询契约的一部分：日志页要把同一份解析结果同时用于过滤与命中高亮，
/// 两边各写一份解析器迟早会不一致。
/// </summary>
public static class LogSearchSyntax
{
    private static readonly char[] Separators = [' ', '\t'];

    /// <summary>解析用户输入为查询条件（未识别的 <c>键:值</c> 当作标签过滤）。</summary>
    public static LogQuery Parse(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return LogQuery.All;
        }

        var textParts = new List<string>();
        var levels = new List<LogLevel>();
        var families = new List<LogFamily>();
        var kinds = new List<LogSourceKind>();
        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var token in input.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var colon = token.IndexOf(':');
            if (colon <= 0 || colon == token.Length - 1)
            {
                textParts.Add(token);
                continue;
            }

            var field = token[..colon];
            var value = token[(colon + 1)..];
            if (TryParseFamilyField(field, value, out var family))
            {
                families.Add(family);
            }
            else if (TryParseLevelField(field, value, out var level))
            {
                levels.Add(level);
            }
            else if (TryParseSourceField(field, value, out var kind))
            {
                kinds.Add(kind);
            }
            else if (IsTagField(field))
            {
                // 四个别名统一收敛到事件里的标签名「mod」，否则 标签:xxx / 模块:xxx 永远匹配不到事件。
                tags["mod"] = value;
            }
            else
            {
                // 不认识的 键:值 一律按「原文子串」处理：用户搜 "http://..." 时不该被当成字段。
                textParts.Add(token);
            }
        }

        return new LogQuery
        {
            Text = textParts.Count == 0 ? null : string.Join(' ', textParts),
            Levels = levels.Count == 0 ? null : levels,
            Families = families.Count == 0 ? null : families,
            Kinds = kinds.Count == 0 ? null : kinds,
            Tags = tags.Count == 0 ? null : tags,
        };
    }

    private static bool IsTagField(string field) =>
        field.Equals("mod", StringComparison.OrdinalIgnoreCase)
        || field.Equals("tag", StringComparison.OrdinalIgnoreCase)
        || field.Equals("标签", StringComparison.OrdinalIgnoreCase)
        || field.Equals("模块", StringComparison.OrdinalIgnoreCase);

    private static bool TryParseFamilyField(string field, string value, out LogFamily family)
    {
        family = LogFamily.Other;
        if (!field.Equals("族", StringComparison.OrdinalIgnoreCase)
            && !field.Equals("family", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (TryParseEnumName(value, out family))
        {
            return true;
        }

        // 用户输入不认识时不要变成「过滤掉一切」：返回 false 让它降级成自由文本。
        return false;
    }

    private static bool TryParseLevelField(string field, string value, out LogLevel level)
    {
        level = LogLevel.Info;
        if (!field.Equals("级别", StringComparison.OrdinalIgnoreCase)
            && !field.Equals("level", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        switch (value.Trim().ToLowerInvariant())
        {
            case "trace": level = LogLevel.Trace; return true;
            case "debug": level = LogLevel.Debug; return true;
            case "info": level = LogLevel.Info; return true;
            case "warn" or "warning": level = LogLevel.Warn; return true;
            case "error" or "err": level = LogLevel.Error; return true;
            case "fatal": level = LogLevel.Fatal; return true;
            case "user": level = LogLevel.User; return true;
            default: return false;
        }
    }

    private static bool TryParseSourceField(string field, string value, out LogSourceKind kind)
    {
        kind = LogSourceKind.Ui;
        if (!field.Equals("源", StringComparison.OrdinalIgnoreCase)
            && !field.Equals("source", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        switch (value.Trim().ToLowerInvariant())
        {
            case "log4net" or "log": kind = LogSourceKind.Log4Net; return true;
            case "user" or "iuser": kind = LogSourceKind.User; return true;
            case "exception" or "ex": kind = LogSourceKind.Exception; return true;
            case "child" or "process": kind = LogSourceKind.Child; return true;
            case "ui": kind = LogSourceKind.Ui; return true;
            case "legacy" or "import": kind = LogSourceKind.Legacy; return true;
            default: return false;
        }
    }

    private static bool TryParseEnumName(string value, out LogFamily family)
    {
        foreach (var candidate in Enum.GetValues<LogFamily>())
        {
            if (candidate.ToString().Equals(value, StringComparison.OrdinalIgnoreCase))
            {
                family = candidate;
                return true;
            }
        }

        family = LogFamily.Other;
        return false;
    }

    /// <summary>把查询条件渲染回可读文本（日志页搜索框的占位提示、诊断包 summary.json 的过滤快照）。</summary>
    public static string Describe(LogQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(query.Text))
        {
            parts.Add(query.Text.Trim());
        }

        if (query.Levels is { Count: > 0 } levels)
        {
            parts.AddRange(levels.Select(level => string.Create(CultureInfo.InvariantCulture, $"级别:{LogLevels.Label(level).ToLowerInvariant()}")));
        }

        if (query.Families is { Count: > 0 } families)
        {
            parts.AddRange(families.Select(family => string.Create(CultureInfo.InvariantCulture, $"族:{family.ToString().ToLowerInvariant()}")));
        }

        if (query.Kinds is { Count: > 0 } kinds)
        {
            parts.AddRange(kinds.Select(kind => string.Create(CultureInfo.InvariantCulture, $"源:{LogSources.Prefix(kind)}")));
        }

        if (query.Tags is { Count: > 0 } tags)
        {
            parts.AddRange(tags.Select(pair => string.Create(CultureInfo.InvariantCulture, $"{pair.Key}:{pair.Value}")));
        }

        return parts.Count == 0 ? "全部" : string.Join(' ', parts);
    }
}
