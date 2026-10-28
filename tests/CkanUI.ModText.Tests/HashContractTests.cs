using System.Text.Json;
using NUnit.Framework;
using UI18n.Core.Hashing;
using UI18n.Core.Text;

namespace CkanUI.ModText.Tests;

/// <summary>
/// 跨语言哈希契约（17 篇 §2.2 / T-MTI-01）：向量由 i18n/tools/extract_ckan_meta.py 产出，
/// C# 侧必须逐条相等——两边规则一旦漂移，运行时永远命不中抽取脚本写出的译文。
/// </summary>
[TestFixture]
public sealed class HashContractTests
{
    private static readonly string[] OfficialVectors = ["", "a", "foobar"];

    [Test]
    public void CSharp_hash_matches_python_extraction_script()
    {
        var vectorsPath = Path.Combine(RepositoryRoot(), "i18n", "tools", "contract-vectors.jsonl");
        Assert.That(File.Exists(vectorsPath), Is.True, "缺少契约向量文件: " + vectorsPath);

        var rows = File.ReadAllLines(vectorsPath)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => JsonSerializer.Deserialize<Dictionary<string, string>>(line)!)
            .ToList();

        Assert.That(rows, Has.Count.GreaterThanOrEqualTo(3));
        foreach (var row in rows)
        {
            var normalized = TextNormalizer.Normalize(row["text"]);
            var actual = Fnv1a.Hash(normalized).ToString("x16");
            Assert.That(actual, Is.EqualTo(row["h"]), "向量不一致：" + row["text"]);
        }
    }

    [Test]
    public void Official_vectors_are_stable()
    {
        Assert.That(Fnv1a.Hash(string.Empty).ToString("x16"), Is.EqualTo("cbf29ce484222325"));
        Assert.That(Fnv1a.Hash("a").ToString("x16"), Is.EqualTo("af63dc4c8601ec8c"));
        Assert.That(Fnv1a.Hash("foobar").ToString("x16"), Is.EqualTo("85944171f73967e8"));
        Assert.That(OfficialVectors, Has.Length.EqualTo(3));
    }

    [Test]
    public void Normalization_collapses_whitespace_and_applies_nfkc()
    {
        Assert.That(TextNormalizer.Normalize("  a  b\n\tc  "), Is.EqualTo("a b c"));
        Assert.That(TextNormalizer.Normalize("全角空格　与不间断 空格"), Is.EqualTo("全角空格 与不间断 空格"));
        Assert.That(TextNormalizer.Normalize("NFKC: ＡＢＣ"), Is.EqualTo("NFKC: ABC"));
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ckan-ui.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return Directory.GetCurrentDirectory();
    }
}
