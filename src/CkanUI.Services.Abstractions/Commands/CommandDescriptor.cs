namespace CkanUI.Services.Abstractions.Commands;

/// <summary>
/// 命令面板里的一条命令（UI-40 / 06 篇「命令面板」）。
/// </summary>
/// <remarks>
/// <para>纯数据契约：id、文案与可见性判定所需的标志都由注册方（App 外壳或插件）填好，服务层只做匹配与排序。</para>
/// <para><see cref="Id"/> 是全目录唯一键，执行时宿主也是拿它去映射到导航或既有命令，因此**不要**放本地化键以外的可变量；
/// 标题与分类可以是已翻译的文本（注册时翻译），<see cref="ShortcutText"/> 只放真实生效的手势，
/// 免得快捷键目录里出现按了没反应的行。</para>
/// <para>Id/Title 为空属于编程错误（注册方拼错了数据），构造时直接抛出；
/// 运行期才可能出现的冲突（同一 Id 注册两次）由 <see cref="ICommandCatalog"/> 返回 <see cref="OperationResult"/> 表达。</para>
/// </remarks>
public sealed record CommandDescriptor
{
    /// <summary>创建一条命令；<paramref name="id"/> 与 <paramref name="title"/> 为必填。</summary>
    /// <param name="id">全目录唯一键（约定 <c>nav:&lt;页面键&gt;</c> / <c>action:&lt;动词&gt;</c>）。</param>
    /// <param name="title">主标题：命令面板里高亮显示的一行。</param>
    public CommandDescriptor(string id, string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        Id = id;
        Title = title;
    }

    /// <summary>全目录唯一键。</summary>
    public string Id { get; }

    /// <summary>主标题（匹配与高亮都以它为主）。</summary>
    public string Title { get; }

    /// <summary>分组名，空查询时按它排序，面板右侧也用它做徽标。</summary>
    public string Category { get; init; } = "常规";

    /// <summary>副标题（更长的说明，参与匹配但权重最低）；空串表示不显示。</summary>
    public string Subtitle { get; init; } = "";

    /// <summary>同义词/别名（中英文都能命中，比如「模组 mods」）；空项会被丢弃。</summary>
    public IReadOnlyList<string> Keywords
    {
        get => _keywords;
        init => _keywords = NormalizeKeywords(value);
    }

    /// <summary>真实生效的快捷键手势文本（如 <c>Ctrl+K</c>）；空串表示没有快捷键。</summary>
    public string ShortcutText { get; init; } = "";

    /// <summary>是否可用；宿主在注册前就该按可见性预过滤，这里是兜底（不可用的命令不会被执行）。</summary>
    public bool IsEnabled { get; init; } = true;

    /// <summary>是否要求当前已选中游戏实例（宿主用它预过滤，面板本身不判断实例状态）。</summary>
    public bool RequiresSelectedInstance { get; init; }

    /// <summary>是否带了快捷键（快捷键目录据此筛选）。</summary>
    public bool HasShortcut => !string.IsNullOrWhiteSpace(ShortcutText);

    /// <summary>是否带了副标题。</summary>
    public bool HasSubtitle => !string.IsNullOrWhiteSpace(Subtitle);

    private readonly IReadOnlyList<string> _keywords = Array.Empty<string>();

    /// <summary>去掉空白项与重复项（忽略大小写），保证匹配时不会把空串当成一个能命中的关键词。</summary>
    private static IReadOnlyList<string> NormalizeKeywords(IReadOnlyList<string>? keywords)
    {
        if (keywords is null || keywords.Count == 0)
        {
            return Array.Empty<string>();
        }

        var result = new List<string>(keywords.Count);
        foreach (var keyword in keywords)
        {
            if (string.IsNullOrWhiteSpace(keyword))
            {
                continue;
            }

            var trimmed = keyword.Trim();
            if (!result.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
            {
                result.Add(trimmed);
            }
        }

        return result.Count == 0 ? Array.Empty<string>() : result;
    }
}
