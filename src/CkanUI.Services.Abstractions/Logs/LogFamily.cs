namespace CkanUI.Services.Abstractions.Logs;

/// <summary>
/// 语义族（14 篇 §3.3）：决定颜色、决定过滤。
/// 9 个真实族 + <see cref="Other"/> 兜底。族由规则表判定（不靠调用方自报），
/// 所以 Core 的日志不用改一行也能被正确染色。
/// </summary>
public enum LogFamily
{
    /// <summary>无匹配规则的兜底族。</summary>
    Other = 0,

    /// <summary>HTTP/网络/证书。</summary>
    Net = 1,

    /// <summary>下载/校验/解包。</summary>
    Download = 2,

    /// <summary>依赖求解/冲突。</summary>
    Solve = 3,

    /// <summary>安装/卸载/替换。</summary>
    Install = 4,

    /// <summary>文件系统/权限/占用。</summary>
    Fs = 5,

    /// <summary>注册表/实例/锁。</summary>
    Reg = 6,

    /// <summary>存档（E1）。</summary>
    Save = 7,

    /// <summary>安全/凭据。</summary>
    Sec = 8,

    /// <summary>我们自己的 UI。</summary>
    Ui = 9,
}
