using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace CkanUI.Architecture.Tests;

/// <summary>
/// 页面 ViewModel 文案层的源码级约束（缺口 ⑧ 收口后的守卫）。
/// 页面里的 <c>Text("中文原文", LocalizationKeys.X)</c> 有两个契约：
/// ① 第一个参数必须与 <c>i18n/packs/zh-CN.json</c> 里那条键的译文逐字相同——否则切到中文时看到的是 A，
///    回退时看到的是 B，同一句话两种写法；② 常量名必须真的对应一个存在的键。
/// 这两条过去靠一次性脚本核，脚本一扔就没人守，所以固化成架构测试。
/// </summary>
[TestFixture]
public sealed class PageTextLocalizationTests
{
    /// <summary>已迁移的 <c>Text(...)</c> 站点下限：只许变多，不许被改回硬编码。</summary>
    private const int BaselineSites = 800;

    /// <summary>带站点的页面文件数下限（Pages 下跨子目录合计）。</summary>
    private const int BaselineFiles = 17;

    private static readonly Regex ConstantPattern = new(
        "public const string (\\w+)\\s*=\\s*\"(Ui_[^\"]+)\";",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex CallPattern = new(
        "\\bText\\(\\s*\"((?:[^\"\\\\]|\\\\.)*)\"\\s*,\\s*LocalizationKeys\\.(\\w+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [Test]
    public void EveryPageTextFallbackMatchesTheChinesePack()
    {
        var packs = ReadChinesePack();
        var constants = ReadKeyConstants();
        var failures = new List<string>();
        var sites = 0;

        foreach (var (file, source) in ReadPageSources())
        {
            foreach (Match match in CallPattern.Matches(source))
            {
                sites++;
                var fallback = Unescape(match.Groups[1].Value);
                var constant = match.Groups[2].Value;

                if (!constants.TryGetValue(constant, out var key))
                {
                    failures.Add($"{file}: LocalizationKeys.{constant} 在 LocalizationKeys.cs 里不存在");
                    continue;
                }

                if (!packs.TryGetValue(key, out var chinese))
                {
                    failures.Add($"{file}: 键 {key} 在 i18n/packs/zh-CN.json 里没有译文");
                    continue;
                }

                if (!string.Equals(fallback, chinese, StringComparison.Ordinal))
                {
                    failures.Add($"{file}: {key} 的 fallback 与 zh-CN 译文不一致\n  fallback: {fallback}\n  zh-CN   : {chinese}");
                }
            }
        }

        Assert.That(sites, Is.GreaterThan(0), "页面里一处 Text(...) 都没扫到，正则或目录结构变了。");
        Assert.That(failures, Is.Empty, "页面文案 fallback 与中文语言包不一致：\n" + string.Join("\n", failures));
    }

    [Test]
    public void PageTextCoverageStaysAboveTheBaseline()
    {
        var files = 0;
        var sites = 0;

        foreach (var (_, source) in ReadPageSources())
        {
            var count = CallPattern.Matches(source).Count;
            if (count == 0)
            {
                continue;
            }

            files++;
            sites += count;
        }

        Assert.That(sites, Is.GreaterThanOrEqualTo(BaselineSites), $"Text(...) 站点只剩 {sites} 处，疑似被改回硬编码中文。");
        Assert.That(files, Is.GreaterThanOrEqualTo(BaselineFiles), $"只剩 {files} 个页面走文案层，疑似有页面整体回退。");
    }

    [Test]
    public void NoPageUsesStringInterpolatedTextCalls()
    {
        var offenders = new List<string>();

        foreach (var (file, source) in ReadPageSources())
        {
            // 插值串当 fallback：译文没法逐字核对，等于把文案层架空。
            if (source.Contains("$Text(", StringComparison.Ordinal) ||
                source.Contains("Text($\"", StringComparison.Ordinal))
            {
                offenders.Add(file);
            }
        }

        Assert.That(offenders, Is.Empty, "以下页面把插值字符串当 fallback 传给 Text(...)：\n" + string.Join("\n", offenders));
    }

    private static Dictionary<string, string> ReadChinesePack()
    {
        var path = Path.Combine(RepoRoot.Path, "i18n", "packs", "zh-CN.json");
        using var stream = File.OpenRead(path);
        using var document = JsonDocument.Parse(stream);
        var strings = document.RootElement.GetProperty("strings");
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in strings.EnumerateObject())
        {
            result[property.Name] = property.Value.GetString() ?? string.Empty;
        }

        return result;
    }

    private static Dictionary<string, string> ReadKeyConstants()
    {
        var path = Path.Combine(RepoRoot.Path, "src", "CkanUI.Localization", "LocalizationKeys.cs");
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match match in ConstantPattern.Matches(File.ReadAllText(path)))
        {
            result[match.Groups[1].Value] = match.Groups[2].Value;
        }

        return result;
    }

    private static IEnumerable<(string File, string Source)> ReadPageSources()
    {
        var pages = Path.Combine(RepoRoot.Path, "src", "CkanUI.ViewModels", "Pages");
        foreach (var path in Directory.EnumerateFiles(pages, "*.cs", SearchOption.AllDirectories))
        {
            var normalized = path.Replace('\\', '/');
            if (normalized.Contains("/obj/") || normalized.Contains("/bin/"))
            {
                continue;
            }

            yield return (Path.GetFileName(path), File.ReadAllText(path));
        }
    }

    /// <summary>把 C# 普通字符串字面量里的转义还原成真实字符（只处理引号与反斜杠，文案里不会有别的转义）。</summary>
    private static string Unescape(string literal)
    {
        var builder = new StringBuilder(literal.Length);
        for (var i = 0; i < literal.Length; i++)
        {
            if (literal[i] == '\\' && i + 1 < literal.Length)
            {
                i++;
                builder.Append(literal[i]);
                continue;
            }

            builder.Append(literal[i]);
        }

        return builder.ToString();
    }
}
