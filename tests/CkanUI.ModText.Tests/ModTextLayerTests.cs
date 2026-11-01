using System.Text;
using System.Text.Json;
using CkanUI.ModText;
using NUnit.Framework;
using UI18n.Core.Hashing;
using UI18n.Core.Update;

namespace CkanUI.ModText.Tests;

/// <summary>
/// 分层查找链、策略、黑名单与包校验（17 篇 MTI-03 / MTI-06 / MTI-07 / MTI-08）。
/// </summary>
[TestFixture]
public sealed class ModTextLayerTests
{
    private const string Identifier = "MechJeb2";
    private const string Field = "abstract";
    private const string English = "Automated pilot";

    private static string Line(string text, string translation, string? src = null, string? ctx = Identifier, string? mod = "CKANMeta")
    {
        var entry = new Dictionary<string, string?>
        {
            ["h"] = Fnv1a.Hash(text).ToString("x16"),
            ["n"] = "ckan:" + Identifier + ":" + Field,
            ["s"] = text,
            ["t"] = translation,
            ["ctx"] = ctx,
            ["mod"] = mod,
            ["src"] = src,
        };

        return JsonSerializer.Serialize(entry);
    }

    [Test]
    public void Overrides_win_over_pack_layer()
    {
        var provider = ModTextProvider.FromJsonLines(
            [Line(English, "人工译文", "human")],
            [Line(English, "语言包译文", "human")]);

        var found = provider.Find(Identifier, Field, English);

        Assert.That(found, Is.Not.Null);
        Assert.That(found!.Text, Is.EqualTo("人工译文"));
        Assert.That(found.Source, Is.EqualTo(ModTextSource.Human));
    }

    [Test]
    public void ExistingOnly_skips_machine_override_and_falls_back_to_pack()
    {
        var provider = ModTextProvider.FromJsonLines(
            [Line(English, "机翻译文", "machine")],
            [Line(English, "语言包译文", "human")],
            ModTextPolicy.ExistingOnly);

        var found = provider.Find(Identifier, Field, English);

        // 覆盖层里的机翻条目不能挡住语言包里的人工条目。
        Assert.That(found?.Text, Is.EqualTo("语言包译文"));
    }

    [Test]
    public void AllowMachine_returns_machine_translation()
    {
        var provider = ModTextProvider.FromJsonLines(
            [],
            [Line(English, "机翻译文", "machine")],
            ModTextPolicy.AllowMachine);

        Assert.That(provider.Find(Identifier, Field, English)?.Source, Is.EqualTo(ModTextSource.Machine));
    }

    [Test]
    public void Off_policy_returns_null()
    {
        var provider = ModTextProvider.FromJsonLines([], [Line(English, "人工译文", "human")], ModTextPolicy.Off);

        Assert.That(provider.Find(Identifier, Field, English), Is.Null);
    }

    [Test]
    public void Blocklisted_identifier_is_not_translated()
    {
        var json = """{"blocked":["MechJeb2"]}""";
        Assert.That(ModTextBlocklist.TryParse(json, out var blocklist, out var error), Is.True, error);

        var provider = ModTextProvider.FromJsonLines([], [Line(English, "人工译文", "human")], ModTextPolicy.AllowMachine, blocklist);

        // 黑名单是作者意愿，连人工覆盖也不该绕开。
        Assert.That(provider.Find(Identifier, Field, English), Is.Null);
        Assert.That(provider.Find("OtherMod", Field, English), Is.Null, "没有条目的 mod 本来就该是 null。");
    }

    [Test]
    public void Field_does_not_affect_lookup()
    {
        var provider = ModTextProvider.FromJsonLines([], [Line(English, "人工译文", "human")]);

        // 哈希只由文本决定，同一句话换字段仍应命中（避免同一句在 abstract/description 里各存一份）。
        Assert.That(provider.Find(Identifier, "description", English)?.Text, Is.EqualTo("人工译文"));
    }

    [Test]
    public void Find_reports_stale_when_entry_hash_differs_from_current_text()
    {
        const string oldText = "Automated pilot (v1)";
        const string newText = "Automated pilot (v2)";
        var provider = ModTextProvider.FromJsonLines([], [Line(oldText, "旧译文", "human")]);

        // 文本变了，h 桶必然匹配不到；但 (identifier, field) 索引能认出"还是这个字段的旧条目"，据此报过期。
        var found = provider.Find(Identifier, Field, newText);

        Assert.That(found, Is.Not.Null);
        Assert.That(found!.Text, Is.EqualTo("旧译文"));
        Assert.That(found.Stale, Is.True);
        Assert.That(found.Source, Is.EqualTo(ModTextSource.Human));
    }

    [Test]
    public void Find_does_not_report_stale_when_hash_matches()
    {
        var provider = ModTextProvider.FromJsonLines([], [Line(English, "人工译文", "human")]);

        var found = provider.Find(Identifier, Field, English);

        Assert.That(found, Is.Not.Null);
        Assert.That(found!.Text, Is.EqualTo("人工译文"));
        Assert.That(found.Stale, Is.False, "命中的是当前文本的同哈希条目，不算过期。");
    }

    [Test]
    public void ExistingOnly_does_not_substitute_machine_translation_for_changed_text()
    {
        const string oldText = "Telemetry readout (v1)";
        const string newText = "Telemetry readout (v2)";
        string[] pack = [Line(oldText, "机翻旧译文", "machine")];

        // ExistingOnly：文本变了就不拿机翻旧译文顶替，宁可回退英文。
        var strict = ModTextProvider.FromJsonLines([], pack, ModTextPolicy.ExistingOnly);
        Assert.That(strict.Find(Identifier, Field, newText), Is.Null);

        // AllowMachine：允许用机翻，但必须标出这是过期译文。
        var loose = ModTextProvider.FromJsonLines([], pack, ModTextPolicy.AllowMachine);
        var found = loose.Find(Identifier, Field, newText);
        Assert.That(found, Is.Not.Null);
        Assert.That(found!.Text, Is.EqualTo("机翻旧译文"));
        Assert.That(found.Stale, Is.True);
    }

    [Test]
    public void Blocklist_parses_both_shapes_and_degrades_on_bad_json()
    {
        Assert.That(ModTextBlocklist.TryParse("""["A"]""", out var flat, out _), Is.True);
        Assert.That(flat.IsBlocked("a"), Is.True, "名单匹配忽略大小写。");

        var nestedJson = """{"identifiers":["B"]}""";
        Assert.That(ModTextBlocklist.TryParse(nestedJson, out var nested, out _), Is.True);
        Assert.That(nested.IsBlocked("B"), Is.True);

        var brokenJson = """{ not json""";
        Assert.That(ModTextBlocklist.TryParse(brokenJson, out var broken, out var error), Is.False);
        Assert.That(broken.Count, Is.Zero, "坏 JSON 得到空名单，不能抛。");
        Assert.That(error, Is.Not.Null);
    }

    [Test]
    public void Manifest_is_required_for_verification()
    {
        Assert.That(ModTextPackVerifier.TryReadManifest(null, out _, out var error), Is.False);
        Assert.That(error, Is.EqualTo(UpdateVerify.ReasonNoManifest));

        var result = ModTextPackVerifier.VerifyPacksDirectory(Path.GetTempPath(), null);
        Assert.That(result.Ok, Is.False);
        Assert.That(result.Reason, Is.EqualTo(UpdateVerify.ReasonNoManifest));
    }

    [Test]
    public void VerifyPacksDirectory_accepts_matching_file_and_reports_mismatch()
    {
        using var workspace = new TempDirectory();
        var packsRoot = Path.Combine(workspace.Path, "packs");
        var langDir = Path.Combine(packsRoot, "zh-cn");
        Directory.CreateDirectory(langDir);
        var filePath = Path.Combine(langDir, "CKANMeta.jsonl");
        var content = Encoding.UTF8.GetBytes(Line(English, "人工译文", "human") + Environment.NewLine);
        File.WriteAllBytes(filePath, content);

        var sha = Sha256Hex.OfBytes(content);
        var ok = ModTextPackVerifier.VerifyPacksDirectory(packsRoot, ParseManifest("Packs/zh-cn/CKANMeta.jsonl", content.Length, sha));
        Assert.That(ok.Ok, Is.True, ok.Reason);

        var bad = ModTextPackVerifier.VerifyPacksDirectory(packsRoot, ParseManifest("Packs/zh-cn/CKANMeta.jsonl", content.Length + 1, sha));
        Assert.That(bad.Ok, Is.False);
        Assert.That(bad.Reason, Does.StartWith(UpdateVerify.ReasonSizeMismatch));
        Assert.That(bad.Files, Has.Count.EqualTo(1));

        var missing = ModTextPackVerifier.VerifyPacksDirectory(packsRoot, ParseManifest("Packs/zh-cn/Other.jsonl", content.Length, sha));
        Assert.That(missing.Ok, Is.False);
        Assert.That(missing.Reason, Does.StartWith(UpdateVerify.ReasonUnreadable));
    }

    [Test]
    public void TryMapToLocal_rejects_parent_dir_entries()
    {
        var root = Path.Combine(Path.GetTempPath(), "packs");
        Assert.That(ModTextPackVerifier.TryMapToLocal(root, "Packs/zh-cn/a.jsonl", out var mapped), Is.True);
        Assert.That(mapped, Is.EqualTo(Path.Combine(root, "zh-cn", "a.jsonl")));

        Assert.That(ModTextPackVerifier.TryMapToLocal(root, "Packs/../evil.jsonl", out _), Is.False);
        Assert.That(ModTextPackVerifier.TryMapToLocal(root, " ", out _), Is.False);
    }

    private static UpdateManifest ParseManifest(string path, long bytes, string sha256)
    {
        var zipSha = new string('A', 64);
        var json = $$"""
            {"version":"1.0.0","zip":{"name":"ui18n.zip","bytes":1,"sha256":"{{zipSha}}"},"files":[{"path":"{{path}}","bytes":{{bytes}},"sha256":"{{sha256}}"}]}
            """;

        Assert.That(UpdateManifest.TryParse(json, out var manifest, out var error), Is.True, error);
        return manifest!;
    }

    /// <summary>临时目录；测试自己的小工具，避免给测试项目添 TestSupport 依赖。</summary>
    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(AppContext.BaseDirectory, "ckan-ui-modtext-" + Guid.NewGuid().ToString("n"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
                // 测试清理失败不影响结论。
            }
        }
    }
}
