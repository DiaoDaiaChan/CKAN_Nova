namespace CkanUI.Services.Abstractions.Logs;

/// <summary>
/// Source 字符串的编解码。<see cref="LogEvent.Source"/> 是人可读的稳定串（写进日志文件与诊断包），
/// 约定形如 <c>log4net:CKAN.Repo</c>、<c>user</c>、<c>exception:appdomain</c>、<c>child:1234</c>、<c>legacy:CKAN.log</c>。
/// 前缀只是给人看的，机器判断一律走 <see cref="LogEvent.Kind"/>。
/// </summary>
public static class LogSources
{
    /// <summary>拼接 Source 串（名称为空时只保留前缀）。</summary>
    public static string Encode(LogSourceKind kind, string? name)
    {
        var prefix = Prefix(kind);
        return string.IsNullOrWhiteSpace(name) ? prefix : prefix + ":" + name.Trim();
    }

    /// <summary>族对应的稳定前缀（小写，日志文件与搜索语法用）。</summary>
    public static string Prefix(LogSourceKind kind) => kind switch
    {
        LogSourceKind.Log4Net => "log4net",
        LogSourceKind.User => "user",
        LogSourceKind.Exception => "exception",
        LogSourceKind.Child => "child",
        LogSourceKind.Ui => "ui",
        LogSourceKind.Legacy => "legacy",
        _ => "ui",
    };

    /// <summary>把 Source 串解析回来源类别；未知前缀一律归 <see cref="LogSourceKind.Ui"/>。</summary>
    public static LogSourceKind Decode(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return LogSourceKind.Ui;
        }

        var span = source.AsSpan().Trim();
        var colon = span.IndexOf(':');
        var prefix = colon < 0 ? span : span[..colon];
        return prefix switch
        {
            var p when p.Equals("log4net", StringComparison.OrdinalIgnoreCase) => LogSourceKind.Log4Net,
            var p when p.Equals("user", StringComparison.OrdinalIgnoreCase) => LogSourceKind.User,
            var p when p.Equals("exception", StringComparison.OrdinalIgnoreCase) => LogSourceKind.Exception,
            var p when p.Equals("child", StringComparison.OrdinalIgnoreCase) => LogSourceKind.Child,
            var p when p.Equals("ui", StringComparison.OrdinalIgnoreCase) => LogSourceKind.Ui,
            var p when p.Equals("legacy", StringComparison.OrdinalIgnoreCase) => LogSourceKind.Legacy,
            _ => LogSourceKind.Ui,
        };
    }
}
