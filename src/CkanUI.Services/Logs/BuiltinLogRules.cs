using CkanUI.Services.Abstractions.Logs;

namespace CkanUI.Services.Logs;

/// <summary>
/// 内置规则表（14 篇 §3.3）：外置 <c>log-rules.json</c> 缺失或损坏时的兜底，也是快照测试的基线。
/// 权重含义：同一行命中多条规则时取权重最高者。经验值——越「专指」的场景权重越高，
/// 例如「SHA256 mismatch」明确是下载校验问题，不该被通用的网络关键词抢走；
/// 「401」必须先于网络判定，否则凭据失效会被误报成网络故障。
/// </summary>
public static class BuiltinLogRules
{
    /// <summary>内置规则的来源标识（诊断包与 UI 显示规则来自哪里）。</summary>
    public const string Origin = "builtin";

    /// <summary>内置规则集。</summary>
    public static LogRuleSet Create() => new(
        [
            // ---- 下载/校验（权重 520）：先于网络判定，因为下载失败的根因常在校验 ----
            Rule(LogFamily.Download, 520, @"sha-?256|checksum|hash\s+mismatch|校验(和|失败)", "sha256", "sha", "hash", "checksum", "mismatch", "校验"),
            Rule(LogFamily.Download, 515, @"unzip|解压|archive.*(corrupt|invalid)|zip.*(invalid|corrupt)", "unzip", "解压", "corrupt", "invalid", "zip", "archive"),
            Rule(LogFamily.Download, 510, @"download(ing|ed)?\b|fail(ed)?\s+to\s+download|下载失败|partial\s+download|resume.*failed", "download", "下载", "partial", "resume"),
            Rule(LogFamily.Download, 505, @"size\s+mismatch|文件大小|expected\s+\d+\s+bytes|content-length.*mismatch", "mismatch", "size", "文件大小", "content-length", "expected"),

            // ---- 安全/凭据（权重 560）：401/403 必须先于通用网络判定 ----
            Rule(LogFamily.Sec, 560, @"401|403|unauthori[sz]ed|forbidden|api[\s_-]?key|auth\s*token|bearer\s", "401", "403", "unauthori", "forbidden", "api key", "apikey", "token", "bearer", "auth"),
            Rule(LogFamily.Sec, 555, @"token.*(expired|invalid)|credential|凭据|认证失败", "credential", "expired", "凭据", "token", "认证"),

            // ---- 网络（权重 500）----
            Rule(LogFamily.Net, 500, @"webexception|http\s*\d{3}|timeout|timed?\s*out|超时", "timeout", "超时", "http", "webexception", "timed"),
            Rule(LogFamily.Net, 495, @"ssl|tls|certificate|证书", "ssl", "tls", "certificate", "证书"),
            Rule(LogFamily.Net, 490, @"connection.*(reset|refused|closed|aborted|fail|error)|socket|dns|proxy", "connection", "socket", "dns", "proxy"),

            // ---- 依赖求解（权重 480）----
            Rule(LogFamily.Solve, 480, @"unresolved|not\s?satisfiable|relationshipresolver|依赖冲突|无法解析", "unresolved", "notsatisfiable", "satisfiable", "resolver", "依赖", "无法解析"),
            Rule(LogFamily.Solve, 475, @"conflict|incompatible|requires\s+.*which|冲突|不兼容", "conflict", "incompatible", "冲突", "不兼容", "requires"),

            // ---- 安装/卸载（权重 460）----
            Rule(LogFamily.Install, 460, @"installing|installed|removing|removed|uninstalling|transaction", "installing", "installed", "removing", "removed", "uninstall", "transaction", "安装", "卸载"),
            Rule(LogFamily.Install, 455, @"overwrit(e|ing|ten)|replace.*file|backup.*restore|回滚|rollback", "overwrit", "replace", "backup", "restore", "rollback", "回滚"),

            // ---- 文件系统（权重 440）----
            // 权重 570 的单列一组：.NET 的异常类型名本身就是最硬的证据。
            // UnauthorizedAccessException 会被通用的 «unauthorized»（Sec）抢先匹配，
            // 但它的真实含义是「这个目录没权限写」，属于文件系统而不是凭据问题。
            Rule(LogFamily.Fs, 570, @"ioexception|unauthorizedaccessexception|access\s+to\s+the\s+path|ebusy|directorynotfoundexception|filenotfoundexception|path\s+too\s+long", "ioexception", "unauthorizedaccess", "access to the path", "ebusy", "notfoundexception", "path too long"),
            Rule(LogFamily.Fs, 440, @"ioexception|unauthorizedaccess|access\s+to\s+the\s+path|ebusy|file\s+in\s+use|被占用", "ioexception", "unauthorizedaccess", "access to the path", "ebusy", "占用", "file in use"),
            Rule(LogFamily.Fs, 435, @"directorynotfound|filenotfound|path\s+too\s+long|找不到文件|路径", "directorynotfound", "filenotfound", "path", "路径", "找不到文件"),

            // ---- 注册表/实例（权重 420）----
            // 权重 470：跨实例锁与注册表版本是安装失败的根因，必须高于「installing」（460），
            // 否则一行 "RegistryVersionNotSupported while installing" 会被归成安装族而看不到真凶。
            Rule(LogFamily.Reg, 470, @"registry\.locked|registryversionnotsupported", "registry.locked", "registryversionnotsupported"),
            Rule(LogFamily.Reg, 420, @"注册表|registry", "registry", "注册表"),
            Rule(LogFamily.Reg, 415, @"instance.*(locked|missing|invalid)|实例.*(锁|丢失|无效)|instancename", "instance", "实例"),

            // ---- 存档（权重 400）----
            Rule(LogFamily.Save, 400, @"\.sfs\b|loadmeta|\.ckan\b|存档", ".sfs", "loadmeta", ".ckan", "存档"),

            // ---- UI（权重 300）：我们自己产生的事件，命中规则少但要看得出是 UI 侧 ----
            Rule(LogFamily.Ui, 300, @"navigation.*fail|binding.*error|theme.*(fallback|fail)|导航|绑定|主题", "navigation", "binding", "theme", "导航", "绑定", "主题"),
        ],
        Origin);

    private static LogRule Rule(LogFamily family, int weight, string pattern, params string[] keywords) => new()
    {
        Family = family,
        Weight = weight,
        Pattern = pattern,
        Keywords = keywords,
        Description = null,
    };
}
