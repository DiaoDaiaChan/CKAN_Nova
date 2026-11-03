using CkanUI.Services.Abstractions.Logs;

namespace CkanUI.ViewModels.Pages.Logs;

/// <summary>
/// 日志行的字形与中文标签映射（14 篇 §3.4）。
/// 为什么要有字形：规格要求「颜色永不单独承载语义」，警告/错误/用户消息必须各有字形前缀，
/// 高对比度或色盲用户只看形状也能分辨级别。字形与主题色解耦，后端只给字符与样式键。
/// </summary>
public static class LogGlyphs
{
    /// <summary>级别字形前缀：!(Warn) ×(Error) »(User)，其余用中性符号。</summary>
    public static string Glyph(LogLevel level) => level switch
    {
        LogLevel.Trace => "·",
        LogLevel.Debug => "·",
        LogLevel.Info => "i",
        LogLevel.Warn => "!",
        LogLevel.Error => "×",
        LogLevel.Fatal => "!!",
        LogLevel.User => "»",
        _ => "·",
    };

    /// <summary>级别中文名（过滤条与详情面板用）。</summary>
    public static string LevelLabel(LogLevel level) => level switch
    {
        LogLevel.Trace => "详细",
        LogLevel.Debug => "调试",
        LogLevel.Info => "信息",
        LogLevel.Warn => "警告",
        LogLevel.Error => "错误",
        LogLevel.Fatal => "致命",
        LogLevel.User => "用户",
        _ => "信息",
    };

    /// <summary>样式键：视图据此选 <c>Log.Error</c> / <c>Log.Warn</c> 等主题资源，避免在 XAML 里写颜色。</summary>
    public static string LevelClass(LogLevel level) => level switch
    {
        LogLevel.Trace => "trace",
        LogLevel.Debug => "debug",
        LogLevel.Info => "info",
        LogLevel.Warn => "warn",
        LogLevel.Error => "error",
        LogLevel.Fatal => "fatal",
        LogLevel.User => "user",
        _ => "info",
    };

    /// <summary>日志族中文名（14 篇 §3.3 的 9 族 + 兜底）。</summary>
    public static string FamilyLabel(LogFamily family) => family switch
    {
        LogFamily.Net => "网络",
        LogFamily.Download => "下载",
        LogFamily.Solve => "求解",
        LogFamily.Install => "安装",
        LogFamily.Fs => "文件",
        LogFamily.Reg => "注册表",
        LogFamily.Save => "存档",
        LogFamily.Sec => "安全",
        LogFamily.Ui => "界面",
        _ => "其他",
    };

    /// <summary>采集来源中文名（14 篇 §3.2 的四路采集 + 界面事件 + 旧日志导入）。</summary>
    public static string SourceLabel(LogSourceKind kind) => kind switch
    {
        LogSourceKind.Log4Net => "内核日志",
        LogSourceKind.User => "用户消息",
        LogSourceKind.Exception => "异常",
        LogSourceKind.Child => "子进程",
        LogSourceKind.Legacy => "导入日志",
        LogSourceKind.Game => "游戏日志",
        _ => "界面事件",
    };
}
