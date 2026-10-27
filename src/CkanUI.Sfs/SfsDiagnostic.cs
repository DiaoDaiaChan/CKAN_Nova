using System.Globalization;

namespace CkanUI.Sfs;

/// <summary>诊断严重级别（13 篇 §6.4 的"级别"列）。</summary>
public enum SfsSeverity
{
    /// <summary>提示：不影响读写。</summary>
    Info = 0,

    /// <summary>警告：可疑但可以继续（真实存档里常见）。</summary>
    Warning = 1,

    /// <summary>错误：结构已损坏，禁止格式化/落盘。</summary>
    Error = 2,
}

/// <summary>13 篇 §6.4 与 §11 的校验项编号。原样使用规格里的 V-xx，便于对文档。</summary>
public static class SfsDiagnosticCodes
{
    /// <summary>V-01 括号平衡。</summary>
    public const string UnbalancedBraces = "V-01";

    /// <summary>V-02 根节点唯一且为 GAME。</summary>
    public const string RootNode = "V-02";

    /// <summary>V-03 每行可分词（无 OpaqueLine）。</summary>
    public const string OpaqueLine = "V-03";

    /// <summary>V-04 同节点内重复键（L5：只警告，不合并）。</summary>
    public const string DuplicateKey = "V-04";

    /// <summary>V-05 行尾混合 / BOM / UTF-8 非法字节。</summary>
    public const string EncodingOrLineEnding = "V-05";

    /// <summary>V-06 缩进与括号深度不一致。</summary>
    public const string Indentation = "V-06";

    /// <summary>V-07 嵌套深度 &gt; 16 / 单行 &gt; 64KB。</summary>
    public const string DepthOrLineLength = "V-07";

    /// <summary>V-08 SCENARIO 缺 name 键。</summary>
    public const string ScenarioName = "V-08";

    /// <summary>V-09 .loadmeta 与 .sfs 不一致（需要存档包，见 13 篇 §7.3，尚未实现）。</summary>
    public const string LoadMeta = "V-09";
}

/// <summary>
/// 一条诊断：编号 + 严重级别 + 行号 + 列号 + 可读原因 + 出错片段。
/// 解析器与校验器只产出本类型，不向上层抛裸异常（任务书要求"解析错误必须带行号 + 列号 + 出错片段"）。
/// </summary>
public sealed class SfsDiagnostic
{
    private const int SnippetLimit = 80;

    public SfsDiagnostic(SfsSeverity severity, string code, int line, int column, string message, string? snippet = null)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(message);

        Severity = severity;
        Code = code;
        Line = line;
        Column = column;
        Message = message;
        Snippet = snippet is null ? null : Truncate(snippet);
    }

    /// <summary>严重级别。</summary>
    public SfsSeverity Severity { get; }

    /// <summary>校验项编号（V-01..V-09）。</summary>
    public string Code { get; }

    /// <summary>1 起算的行号；0 表示与具体行无关（例如整文件级问题）。</summary>
    public int Line { get; }

    /// <summary>1 起算的列号；0 表示未知。</summary>
    public int Column { get; }

    /// <summary>可读原因（面向用户，不是异常消息）。</summary>
    public string Message { get; }

    /// <summary>出错片段（截断到 80 字符），没有则为 null。</summary>
    public string? Snippet { get; }

    /// <inheritdoc />
    public override string ToString()
    {
        var location = Line > 0
            ? string.Create(CultureInfo.InvariantCulture, $"第 {Line} 行第 {Column} 列")
            : "整文件";

        var snippet = Snippet is null ? string.Empty : string.Concat("  <", Snippet, ">");
        return string.Create(CultureInfo.InvariantCulture, $"{Code} {SeverityText(Severity)} {location}: {Message}{snippet}");
    }

    private static string SeverityText(SfsSeverity severity) => severity switch
    {
        SfsSeverity.Error => "错误",
        SfsSeverity.Warning => "警告",
        _ => "提示",
    };

    private static string Truncate(string snippet)
    {
        var single = snippet.Replace("\r", string.Empty, StringComparison.Ordinal)
                            .Replace("\n", string.Empty, StringComparison.Ordinal);
        return single.Length <= SnippetLimit ? single : string.Concat(single[..SnippetLimit], "…");
    }
}
