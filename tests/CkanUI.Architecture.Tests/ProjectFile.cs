using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace CkanUI.Architecture.Tests;

/// <summary>极简 csproj 读取器：只关心 ProjectReference / PackageReference 的名字。</summary>
internal sealed class ProjectFile
{
    private ProjectFile(string path, IReadOnlyList<string> projectRefs, IReadOnlyList<string> packageRefs)
    {
        Path = path;
        ProjectReferences = projectRefs;
        PackageReferences = packageRefs;
    }

    /// <summary>仓库相对路径，例如 src/CkanUI.Services/CkanUI.Services.csproj。</summary>
    public string Path { get; }

    public string Name => System.IO.Path.GetFileNameWithoutExtension(Path);

    public IReadOnlyList<string> ProjectReferences { get; }

    public IReadOnlyList<string> PackageReferences { get; }

    public static ProjectFile Load(string relativePath)
    {
        var absolute = System.IO.Path.Combine(RepoRoot.Path, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
        var doc = XDocument.Load(absolute);

        static IReadOnlyList<string> ReadRefs(XDocument document, string element)
            => document.Descendants()
                .Where(e => e.Name.LocalName == element)
                .Select(e => (string?)e.Attribute("Include"))
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => Normalize(v!))
                .ToList();

        return new ProjectFile(relativePath, ReadRefs(doc, "ProjectReference"), ReadRefs(doc, "PackageReference"));

        // $(CkanCoreProject) 归一成 CKAN-core，$(UI18nCoreProject) 归一成 UI18n.Core，便于断言。
        static string Normalize(string include)
        {
            if (include.Contains("CkanCoreProject", StringComparison.Ordinal))
            {
                return "CKAN-core";
            }
            if (include.Contains("UI18nCoreProject", StringComparison.Ordinal))
            {
                return "UI18n.Core";
            }
            return System.IO.Path.GetFileNameWithoutExtension(include);
        }
    }
}
