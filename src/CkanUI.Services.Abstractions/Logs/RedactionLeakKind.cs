namespace CkanUI.Services.Abstractions.Logs;

/// <summary>
/// 脱敏漏检的种类（14 篇 §5.2 的每一类敏感信息对应一项）。
/// 出口扫描只报「哪一类、在哪个偏移」，绝不回显命中文本——否则报错日志本身会变成新的泄漏点。
/// </summary>
public enum RedactionLeakKind
{
    /// <summary>用户名（<c>[A-Za-z]:\Users\&lt;name&gt;\</c>、<c>/home/&lt;name&gt;/</c>、<c>/Users/&lt;name&gt;/</c>）。</summary>
    UserName = 0,

    /// <summary>令牌/API key/password。</summary>
    Token = 1,

    /// <summary>邮箱。</summary>
    Email = 2,

    /// <summary>IP 地址（IPv4/IPv6）。</summary>
    IpAddress = 3,

    /// <summary>SteamID / SteamUser / SteamAppId。</summary>
    SteamId = 4,

    /// <summary>主机名。</summary>
    HostName = 5,

    /// <summary>存档文件名（<c>*.sfs</c>）。</summary>
    SaveFile = 6,

    /// <summary>未占位化的绝对路径。</summary>
    AbsolutePath = 7,
}
