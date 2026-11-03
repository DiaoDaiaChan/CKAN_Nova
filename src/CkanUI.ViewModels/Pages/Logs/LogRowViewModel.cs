using System.Globalization;
using CkanUI.Services.Abstractions.Logs;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CkanUI.ViewModels.Pages.Logs;

/// <summary>
/// 日志列表的一行（14 篇 §4.2③）。只做展示映射，不持有服务；
/// 高亮与「当前命中」是可变状态，所以继承 <see cref="ObservableObject"/> 让虚拟化列表能增量刷新。
/// </summary>
public sealed partial class LogRowViewModel : ObservableObject
{
    /// <summary>用一条日志事件构造行。</summary>
    public LogRowViewModel(LogEvent entry)
    {
        Entry = entry ?? throw new ArgumentNullException(nameof(entry));
    }

    /// <summary>原始事件（详情面板与导出要用原文，不做裁剪）。</summary>
    public LogEvent Entry { get; }

    /// <summary>全局单调序号：排障时用它引用某一行日志。</summary>
    public long Seq => Entry.Seq;

    /// <summary>行首时间（本地时区，毫秒精度足够定位）。</summary>
    public string TimeText => Entry.TimeLocal.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);

    /// <summary>级别。</summary>
    public LogLevel Level => Entry.Level;

    /// <summary>日志族。</summary>
    public LogFamily Family => Entry.Family;

    /// <summary>级别字形前缀（颜色之外的第二语义通道）。</summary>
    public string Glyph => LogGlyphs.Glyph(Entry.Level);

    /// <summary>级别中文名。</summary>
    public string LevelLabel => LogGlyphs.LevelLabel(Entry.Level);

    /// <summary>级别样式键（trace/debug/info/warn/error/fatal/user）。</summary>
    public string LevelClass => LogGlyphs.LevelClass(Entry.Level);

    /// <summary>日志族中文名。</summary>
    public string FamilyLabel => LogGlyphs.FamilyLabel(Entry.Family);

    /// <summary>来源（含通道前缀，如 log4net:CKAN.RegistryManager）。</summary>
    public string SourceText => Entry.Source;

    /// <summary>消息正文（不裁剪）。</summary>
    public string Message => Entry.Message;

    /// <summary>异常栈等细节；无则为 null。</summary>
    public string? Detail => Entry.Detail;

    /// <summary>是否有细节可展开（详情面板里的「异常栈」段）。</summary>
    public bool HasDetail => !string.IsNullOrWhiteSpace(Entry.Detail);

    /// <summary>是否有结构化标签（mod/instance/url/task/correlationId）。</summary>
    public bool HasTags => Entry.Tags is { Count: > 0 };

    /// <summary>复制/导出用的单行文本。</summary>
    public string PlainText => LogSearchKeys.FormatLine(Entry);

    /// <summary>是否错误（Error/Fatal）。</summary>
    public bool IsError => LogLevels.IsError(Entry.Level);

    /// <summary>是否警告。</summary>
    public bool IsWarning => Entry.Level == LogLevel.Warn;

    /// <summary>是否用户消息（IUser 通道）。</summary>
    public bool IsUser => Entry.Level == LogLevel.User;

    /// <summary>是否来自 KSP 游戏日志（<c>Kind == Game</c>）。</summary>
    public bool IsGame => Entry.Kind == LogSourceKind.Game;

    /// <summary>是否堆栈/Unity 附注这类「接着上一行」的续行（游戏日志里成片出现，缩进压暗处理）。</summary>
    public bool IsContinuation => Entry.Tag("game.cont") == "1";

    /// <summary>游戏日志的模块标签（KSP 的 logger 名）；不是游戏日志时为 null。</summary>
    public string? Module => IsGame ? Entry.Tag("module") : null;

    /// <summary>行首那枚标签的文字：游戏日志显示模块名（插件名一眼可见），其余仍显示日志族。</summary>
    public string TagLabel => IsGame ? Module ?? "KSP" : FamilyLabel;

    /// <summary>是否当前命中（F3 跳转高亮）。</summary>
    [ObservableProperty]
    private bool _isCurrentMatch;

    /// <summary>是否命中搜索关键字（背景高亮用）。</summary>
    [ObservableProperty]
    private bool _isHighlighted;

    /// <summary>关键字是否命中本行（大小写不敏感，含 Detail 与标签）。</summary>
    public bool Matches(string? keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return false;
        }

        return LogSearchKeys.Contains(LogSearchKeys.Build(Entry), LogSearchKeys.NormalizeKeyword(keyword));
    }
}
