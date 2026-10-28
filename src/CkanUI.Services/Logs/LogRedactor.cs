using System.Net;
using System.Text.RegularExpressions;

using CkanUI.Services.Abstractions.Logs;

namespace CkanUI.Services.Logs;

/// <summary>
/// 脱敏器（14 篇 §5.2 / LOG-05）。诊断包要能发给别人，所以「出口前抹干净」是硬需求。
/// 设计要点：
/// 1) 每条规则 = 正则 + 替换值 + 泄漏类别，脱敏与反向扫描共用同一套规则——两套规则一定会漂移，
///    漂移的后果是「反向扫描零命中」这个验收指标变成假绿。
/// 2) 替换全部幂等（把已脱敏文本再脱一次不变），因此出口反向扫描可以直接复用 FindLeaks。
/// 3) 宁可过度脱敏：版本号形如 1.2.3.4 也会被 IP 规则吃掉，这比泄漏一个内网地址便宜。
/// </summary>
public sealed partial class LogRedactor : ILogRedactor
{
    private static readonly string[] SensitiveKeyFragments =
    [
        "token", "secret", "password", "passwd", "pwd", "apikey", "api_key", "api-key",
        "credential", "cookie", "session", "auth", "private",
    ];

    private readonly Detector[] _detectors;

    /// <summary>构造脱敏器（机器名在构造时快照，避免运行中被改）。</summary>
    public LogRedactor()
    {
        var detectors = new List<Detector>
        {
            new(WindowsUserPathRegex(), RedactionLeakKind.UserName, static _ => @"%USERPROFILE%\"),
            new(UnixHomePathRegex(), RedactionLeakKind.UserName, static _ => "%USERPROFILE%/"),
            new(TokenAssignmentRegex(), RedactionLeakKind.Token, static m => m.Groups["key"].Value + "=<REDACTED>"),
            new(BearerTokenRegex(), RedactionLeakKind.Token, static _ => "Bearer <REDACTED>"),
            new(SteamIdRegex(), RedactionLeakKind.SteamId, static _ => "<STEAMID>"),
            new(SteamIdFieldRegex(), RedactionLeakKind.SteamId, static m => m.Groups["key"].Value + "=<ID>"),
            new(EmailRegex(), RedactionLeakKind.Email, static _ => "<EMAIL>"),
            new(Ipv4Regex(), RedactionLeakKind.IpAddress, static _ => "<IP>"),
            new(Ipv6CandidateRegex(), RedactionLeakKind.IpAddress, static m => IsIpv6(m.Value) ? "<IP>" : m.Value),
            new(SaveFileRegex(), RedactionLeakKind.SaveFile, static _ => "<SAVE>"),
            new(WindowsPathRegex(), RedactionLeakKind.AbsolutePath, static _ => "<PATH>"),
            new(UnixPathRegex(), RedactionLeakKind.AbsolutePath, static _ => "<PATH>"),
            new(ComputerNamePlaceholderRegex(), RedactionLeakKind.HostName, static _ => "<HOST>"),
        };

        var machine = Environment.MachineName;
        if (machine.Length >= 4)
        {
            detectors.Add(new(
                new Regex(Regex.Escape(machine), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
                RedactionLeakKind.HostName,
                static _ => "<HOST>"));
        }

        _detectors = [.. detectors];
    }

    /// <summary>被判定为敏感、需要在配置里整键删除的键名片段。</summary>
    public static IReadOnlyList<string> SensitiveKeyNames => SensitiveKeyFragments;

    /// <inheritdoc />
    public string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var current = text;
        foreach (var detector in _detectors)
        {
            current = detector.Pattern.Replace(current, detector.Replace);
        }

        return current;
    }

    /// <inheritdoc />
    public IReadOnlyList<RedactionLeak> FindLeaks(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        var leaks = new List<RedactionLeak>();
        foreach (var detector in _detectors)
        {
            foreach (Match match in detector.Pattern.Matches(text))
            {
                if (!match.Success || match.Length == 0)
                {
                    continue;
                }

                // 替换值等于原文说明这条规则对它不生效（例如 IPv6 候选串其实不是地址），不算泄漏。
                if (string.Equals(detector.Replace(match), match.Value, StringComparison.Ordinal))
                {
                    continue;
                }

                leaks.Add(new RedactionLeak(detector.Kind, match.Index, match.Length));
            }
        }

        leaks.Sort(static (left, right) => left.Index.CompareTo(right.Index));
        return leaks;
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string> RedactConfig(IReadOnlyDictionary<string, string?> config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in config)
        {
            if (IsSensitiveKey(pair.Key))
            {
                // AuthTokens 之类整键删除：留着空串也会暗示存在这个键。
                continue;
            }

            result[pair.Key] = Redact(pair.Value);
        }

        return result;
    }

    /// <summary>键名是否敏感（取最后一段比较，兼容 "Repository.AuthTokens" 这类点号键）。</summary>
    public static bool IsSensitiveKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        var segment = key;
        var separator = segment.LastIndexOfAny(['.', '/', ':', '\\']);
        if (separator >= 0 && separator < segment.Length - 1)
        {
            segment = segment[(separator + 1)..];
        }

        foreach (var fragment in SensitiveKeyFragments)
        {
            if (segment.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsIpv6(string value)
    {
        if (!value.Contains(':', StringComparison.Ordinal) || LooksLikeClock(value))
        {
            return false;
        }

        return IPAddress.TryParse(value, out var address) && address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6;
    }

    private static bool LooksLikeClock(string value) => ClockRegex().IsMatch(value);

    private sealed record Detector(Regex Pattern, RedactionLeakKind Kind, MatchEvaluator Replace);

    // 形如 <盘符>:\Users\<name>\ 或 <盘符>:/Users/<name>/ 的 Windows 用户目录，外加 Documents and Settings（XP 遗留）。
    // 注释里不写具体盘符：H-10 门禁会把它当成本机绝对路径（21 篇 §12）。
    // 用户名段允许空格：Windows 用户名可以是 "John Smith"，按 [^\s] 切只会抹掉 "John" 而漏掉 "Smith"。
    // 安全性由「必须紧跟反斜杠」保证——段内不含反斜杠，所以最多吃到下一个目录分隔符为止。
    [GeneratedRegex(
        @"[A-Za-z]:[\\/](?:Users|Documents and Settings)[\\/](?:[^\\/""'<>]+[\\/])?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WindowsUserPathRegex();

    // /home/<name>/ 或 /Users/<name>/
    [GeneratedRegex(
        @"(?<![\w.])/(?:home|Users)/[^\s/""']+/?",
        RegexOptions.CultureInvariant)]
    private static partial Regex UnixHomePathRegex();

    // token=xxx / "api_key": "xxx" / password: xxx
    [GeneratedRegex(
        @"\b(?<key>auth[_-]?token|access[_-]?token|refresh[_-]?token|api[_-]?key|apikey|private[_-]?key|token|password|passwd|pwd|secret|client[_-]?secret|credential|session[_-]?id|cookie)\b""?\s*[=:]\s*(?:""[^""]*""|'[^']*'|[^\s,;&""'{}]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TokenAssignmentRegex();

    // Authorization: Bearer xxx —— 手工 token 常常没有 key= 前缀，只能按协议头识别
    [GeneratedRegex(
        @"\bBearer\s+[A-Za-z0-9\-._~+/]{8,}=*",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BearerTokenRegex();

    [GeneratedRegex(@"\b7656119\d{10}\b", RegexOptions.CultureInvariant)]
    private static partial Regex SteamIdRegex();

    // SteamUser=43701 / steamid: 7656119...
    [GeneratedRegex(
        @"\b(?<key>SteamUser|SteamAppId|steamid)\b\s*[=:]\s*\d+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SteamIdFieldRegex();

    [GeneratedRegex(
        @"(?<![\w.+-])[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}(?![\w.-])",
        RegexOptions.CultureInvariant)]
    private static partial Regex EmailRegex();

    [GeneratedRegex(
        @"(?<![\w.])(?:(?:25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)\.){3}(?:25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)(?![\w.])",
        RegexOptions.CultureInvariant)]
    private static partial Regex Ipv4Regex();

    // 候选串（十六进制 + 冒号），再用 IPAddress.TryParse 兜住误判
    [GeneratedRegex(@"(?<![\w:])[0-9A-Fa-f:]{4,39}(?![\w:])", RegexOptions.CultureInvariant)]
    private static partial Regex Ipv6CandidateRegex();

    [GeneratedRegex(@"^\d{1,2}:\d{2}(?::\d{2})?$", RegexOptions.CultureInvariant)]
    private static partial Regex ClockRegex();

    // save1.sfs → <SAVE>
    [GeneratedRegex(@"[^\s""'\\/<>]+\.sfs\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SaveFileRegex();

    // 盘符路径与 UNC 路径 → <PATH>（%LOCALAPPDATA% 这类占位符无盘符，天然保留）
    // 为什么分段而不是一个 [^\s]*：游戏目录常带空格（"Kerbal Space Program"），
    // 只有「后面还有反斜杠」的段才允许含空格，末段不含空格，
    // 这样 "E:\Kerbal Space Program\GameData" 整体被抹掉，而 "E:\KSP loaded OK" 不会吃掉后半句。
    [GeneratedRegex(
        @"(?<![\w%<>""])(?:[A-Za-z]:[\\/]|\\\\[^\\/""'<>;|\s]+[\\/])(?:[^\\/""'<>;|]+[\\/])*[^\s\\/""'<>;|]*",
        RegexOptions.CultureInvariant)]
    private static partial Regex WindowsPathRegex();

    // 常见 Unix 绝对路径（限制顶级目录，避免把 URL 的路径段吃掉）
    [GeneratedRegex(
        @"(?<![\w:/])/(?:usr|opt|var|etc|tmp|mnt|media|srv|root|bin|lib|lib64|proc|sys|dev)/[^\s""']*",
        RegexOptions.CultureInvariant)]
    private static partial Regex UnixPathRegex();

    [GeneratedRegex(@"%COMPUTERNAME%", RegexOptions.CultureInvariant)]
    private static partial Regex ComputerNamePlaceholderRegex();
}
