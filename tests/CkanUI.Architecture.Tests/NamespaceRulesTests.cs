using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace CkanUI.Architecture.Tests;

/// <summary>源码级约束：Avalonia 命名空间不得出现在非表现层项目里（01 篇 §11 约束 1）。</summary>
[TestFixture]
public class NamespaceRulesTests
{
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
            .Where(f => f.Text.Contains("Avalonia", StringComparison.Ordinal))
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
