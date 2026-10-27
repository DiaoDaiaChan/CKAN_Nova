using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace CkanUI.Architecture.Tests;

/// <summary>01-architecture §2 分层表 + §11 依赖约束的可执行版本。</summary>
[TestFixture]
public class DependencyRulesTests
{
    private static ProjectFile Src(string name) => ProjectFile.Load($"src/{name}/{name}.csproj");

    [Test]
    public void Abstractions_has_no_project_or_package_dependencies()
    {
        var p = Src("CkanUI.Services.Abstractions");
        Assert.That(p.ProjectReferences, Is.Empty, "Services.Abstractions 必须零依赖，便于 headless 测试");
        Assert.That(p.PackageReferences, Is.Empty, "Services.Abstractions 必须零包依赖");
    }

    [Test]
    public void Core_domain_has_no_project_or_package_dependencies()
    {
        var p = Src("CkanUI.Core");
        Assert.That(p.ProjectReferences, Is.Empty);
        Assert.That(p.PackageReferences, Is.Empty, "CkanUI.Core 不得依赖 Avalonia / HTTP 客户端");
    }

    [Test]
    public void Only_Services_and_Localization_reference_CKAN_core()
    {
        var referrers = new[]
        {
            "CkanUI.Services", "CkanUI.Localization", "CkanUI.ViewModels", "CkanUI.App",
            "CkanUI.Controls", "CkanUI.Platform", "CkanUI.ModText", "CkanUI.Sfs", "CkanUI.Core",
        }.Select(Src).Where(p => p.ProjectReferences.Contains("CKAN-core")).Select(p => p.Name).OrderBy(x => x);

        Assert.That(referrers, Is.EqualTo(new[] { "CkanUI.Localization", "CkanUI.Services" }),
            "CKAN 命名空间只在 CkanUI.Services 与 CkanUI.Localization 出现（01 篇 §11 约束 2）");
    }

    [Test]
    public void ViewModels_depend_on_interfaces_not_implementations()
    {
        var p = Src("CkanUI.ViewModels");
        Assert.That(p.ProjectReferences, Does.Contain("CkanUI.Services.Abstractions"));
        Assert.That(p.ProjectReferences, Does.Not.Contain("CkanUI.Services"),
            "ViewModels 只许引用 Services.Abstractions（P6 依赖单向）");
    }

    [Test]
    public void Only_presentation_projects_reference_Avalonia()
    {
        var offenders = new[]
        {
            "CkanUI.Core", "CkanUI.Services", "CkanUI.Services.Abstractions", "CkanUI.ViewModels",
            "CkanUI.Localization", "CkanUI.ModText", "CkanUI.Sfs", "CkanUI.Sfs.Cli",
        }.Select(Src)
            .Where(p => p.PackageReferences.Any(r => r.StartsWith("Avalonia", System.StringComparison.Ordinal)))
            .Select(p => p.Name)
            .ToArray();

        Assert.That(offenders, Is.Empty, "Avalonia 只允许出现在 App / Controls / Platform（P5 可 headless 测试）");
    }

    [Test]
    public void Sfs_parser_stays_independent()
    {
        var p = Src("CkanUI.Sfs");
        Assert.That(p.ProjectReferences, Is.Empty, "Sfs 解析器必须独立可测（13 篇 §9）");
    }

    [Test]
    public void No_project_reference_cycles()
    {
        var names = new[]
        {
            "CkanUI.Core", "CkanUI.Services.Abstractions", "CkanUI.Services", "CkanUI.Localization",
            "CkanUI.ModText", "CkanUI.Sfs", "CkanUI.ViewModels", "CkanUI.Controls", "CkanUI.Platform",
        };
        var graph = names.ToDictionary(n => n, n => Src(n).ProjectReferences.ToArray());

        foreach (var start in names)
        {
            var visited = new HashSet<string>();
            var queue = new Queue<string>();
            foreach (var direct in graph[start])
            {
                queue.Enqueue(direct);
            }

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                Assert.That(current, Is.Not.EqualTo(start), $"存在项目引用环：{start} → … → {current}");
                if (!visited.Add(current) || !graph.TryGetValue(current, out var next))
                {
                    continue;
                }

                foreach (var r in next)
                {
                    queue.Enqueue(r);
                }
            }
        }
    }
}
