using System.Text.RegularExpressions;

namespace CkanUI.ViewModels.Pages.Logs;

/// <summary>日志行里可跳转的对象种类（14 篇 §4.3 对象联动）。</summary>
public enum LogLinkKind
{
    /// <summary>mod=&lt;identifier&gt; → 查看模组详情。</summary>
    Mod = 0,

    /// <summary>URL → 用系统浏览器打开。</summary>
    Url = 1,

    /// <summary>instance=&lt;name&gt; → 切换到该实例。</summary>
    Instance = 2,

    /// <summary>task=&lt;id&gt; → 打开安装任务（可重试）。</summary>
    InstallTask = 3,
}

/// <summary>一个可跳转对象。<see cref="Text"/> 是给按钮用的中文动作名。</summary>
public sealed record LogObjectLink(LogLinkKind Kind, string Value, string Text);

/// <summary>
/// 从日志正文里识别可联动对象（14 篇 §4.3）。
/// 为什么用正则而不是结构化标签：真实日志里这些信息大多来自上游拼好的字符串（Core 一行不改），
/// 少部分来自我们自己的 Tags；两条路都要能认出来，所以按正文扫一遍最省事。
/// </summary>
public static partial class LogObjectLinks
{
    /// <summary>识别优先级：模组 &gt; 实例 &gt; URL &gt; 安装任务。</summary>
    public static LogObjectLink? Detect(string? message)
    {
        var all = DetectAll(message);
        return all.Count == 0 ? null : all[0];
    }

    /// <summary>识别所有可联动对象（同一行可能既有 URL 又有 mod）。</summary>
    public static IReadOnlyList<LogObjectLink> DetectAll(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return Array.Empty<LogObjectLink>();
        }

        var links = new List<LogObjectLink>(4);

        var mod = ModPattern().Match(message);
        if (mod.Success)
        {
            links.Add(new LogObjectLink(LogLinkKind.Mod, mod.Groups["v"].Value, "查看模组"));
        }

        var instance = InstancePattern().Match(message);
        if (instance.Success)
        {
            links.Add(new LogObjectLink(LogLinkKind.Instance, instance.Groups["v"].Value, "切换实例"));
        }

        var url = UrlPattern().Match(message);
        if (url.Success)
        {
            links.Add(new LogObjectLink(LogLinkKind.Url, url.Value.TrimEnd('.', ',', ';', ')'), "打开链接"));
        }

        var task = TaskPattern().Match(message);
        if (task.Success)
        {
            links.Add(new LogObjectLink(LogLinkKind.InstallTask, task.Groups["v"].Value, "重试安装"));
        }

        return links;
    }

    // 前置断言排除 mod 作为单词内部片段（如 "commit=..." / "node_modules=..."）的误判。
    [GeneratedRegex(@"(?<![\w./])mod[:=](?<v>[\w.\-]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ModPattern();

    [GeneratedRegex(@"(?<![\w./])instance[:=](?<v>[^\s,;，；]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InstancePattern();

    [GeneratedRegex(@"https?://[^\s""'<>\]()]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UrlPattern();

    [GeneratedRegex(@"(?<![\w./])task[:=](?<v>[\w.\-]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TaskPattern();
}
