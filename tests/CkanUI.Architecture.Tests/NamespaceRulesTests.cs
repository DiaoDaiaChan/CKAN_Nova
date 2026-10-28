using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace CkanUI.Architecture.Tests;

/// <summary>源码级约束：Avalonia 命名空间不得出现在非表现层项目里（01 篇 §11 约束 1）。</summary>
[TestFixture]
public class NamespaceRulesTests
{
    private static readonly System.Text.RegularExpressions.Regex AvaloniaNamespace =
        new(@"\bAvalonia\.[A-Za-z]", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly string[] NonUiProjects =
    {
        "CkanUI.Core", "CkanUI.Services", "CkanUI.Services.Abstractions",
        "CkanUI.ViewModels", "CkanUI.Localization", "CkanUI.ModText", "CkanUI.Sfs", "CkanUI.Sfs.Cli",
    };

    [Test]
    public void Non_ui_projects_do_not_use_Avalonia_namespaces()
    {
        var offenders = NonUiProjects
            .SelectMany(project => EnumerateSources(project).Select(file => new
            {
                Project = project,
                File = Path.GetRelativePath(RepoRoot.Path, file),
                Text = File.ReadAllText(file),
            }))
            // 只认命名空间引用（Avalonia.Controls / Avalonia.Threading …）。
            // 类名里出现 "Avalonia" 不算违规：CkanUI.Services 里的 AvaloniaUser 是 UI 侧 IUser 实现，
            // 它只依赖 IDispatcher 抽象，并没有引用任何 Avalonia 类型。
            .Where(f => AvaloniaNamespace.IsMatch(f.Text))
            .Select(f => f.File)
            .ToArray();

        Assert.That(offenders, Is.Empty, "Avalonia.* 只允许出现在 App / Controls / Platform / Services.Threading");
    }

    [Test]
    public void Non_services_projects_do_not_use_CKAN_namespaces()
    {
        var allowed = new[] { "CkanUI.Services", "CkanUI.Localization" };
        var offenders = NonUiProjects
            .Where(p => !allowed.Contains(p))
            .SelectMany(project => EnumerateSources(project).Select(file => new
            {
                Project = project,
                File = Path.GetRelativePath(RepoRoot.Path, file),
                Text = File.ReadAllText(file),
            }))
            .Where(f => f.Text.Contains("using CKAN", StringComparison.Ordinal)
                        || f.Text.Contains("namespace CKAN", StringComparison.Ordinal))
            .Select(f => f.File)
            .ToArray();

        Assert.That(offenders, Is.Empty, "CKAN 命名空间只允许在 Services / Localization 出现（P1 Core 唯一真相源）");
    }

    [Test]
    public void Registry_access_lives_only_in_services_platform()
    {
        // 01 篇 §16 + 21 篇：注册表读写只允许在 Services/Platform（只读探测）与 Services/Platform/Windows（写入）。
        // 跨平台 TFM 下这段代码要自己守 CA1416，分散到别处就会在别人的项目上炸。
        var projects = Directory.EnumerateDirectories(Path.Combine(RepoRoot.Path, "src"))
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrEmpty(name))
            .ToArray();

        var offenders = projects
            .SelectMany(project => EnumerateSources(project!).Select(file => new
            {
                File = Path.GetRelativePath(RepoRoot.Path, file),
                Text = File.ReadAllText(file),
            }))
            .Where(f => f.Text.Contains("Microsoft.Win32.Registry", StringComparison.Ordinal)
                        || f.Text.Contains("using Microsoft.Win32;", StringComparison.Ordinal))
            .Where(f => !f.File.StartsWith(
                Path.Combine("src", "CkanUI.Services", "Platform"),
                StringComparison.OrdinalIgnoreCase))
            .Select(f => f.File)
            .ToArray();

        Assert.That(offenders, Is.Empty, "注册表只允许在 src/CkanUI.Services/Platform/ 下访问");
    }

    private static System.Collections.Generic.IEnumerable<string> EnumerateSources(string project)
    {
        var dir = Path.Combine(RepoRoot.Path, "src", project);
        return Directory.Exists(dir)
            ? Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                            && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            : Array.Empty<string>();
    }
}
