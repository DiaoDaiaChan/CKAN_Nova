namespace CkanUI.Services.Abstractions.Logs;

/// <summary>
/// 脱敏器（14 篇 §5.2 / LOG-05）。
/// 两件事必须成对存在：<see cref="Redact"/> 把敏感内容替换成占位符，
/// <see cref="FindLeaks"/> 在打包后用同一套模式反向扫描——出口硬门禁靠它，命中即拒绝生成。
/// </summary>
public interface ILogRedactor
{
    /// <summary>脱敏文本（null 安全，返回空串）。</summary>
    string Redact(string? text);

    /// <summary>脱敏配置字典：AuthTokens 整键删除，其余值按文本规则抹除，路径类占位化。</summary>
    IReadOnlyDictionary<string, string> RedactConfig(IReadOnlyDictionary<string, string?> values);

    /// <summary>反向扫描，返回所有漏检位置（空列表 = 干净）。</summary>
    IReadOnlyList<RedactionLeak> FindLeaks(string? text);

    /// <summary>是否含漏检（<see cref="FindLeaks"/> 的布尔快捷方式）。</summary>
    bool ContainsLeak(string? text) => FindLeaks(text).Count > 0;
}
