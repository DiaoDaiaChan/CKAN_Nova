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
        return Line(text, translation, src, ctx, mod, key: null, normalized: null);
    }

    /// <summary>
    /// 构造一行 JSONL。<paramref name="key"/> 写 v1.4 的可选稳定键 <c>k</c>（null = 不写该字段，模拟旧包）；
    /// <paramref name="normalized"/> 覆盖 <c>n</c>，默认是本仓早期人工覆盖的 <c>ckan:&lt;identifier&gt;:&lt;field&gt;</c>，
    /// 传文本则模拟上游管线落盘的「n = 归一化原文」形状。
    /// </summary>
    private static string Line(
        string text,
        string translation,
        string? src,
        string? ctx,
        string? mod,
        string? key,
        string? normalized)
    {
        var entry = new Dictionary<string, string?>
        {
            ["h"] = Fnv1a.Hash(text).ToString("x16"),
            ["n"] = normalized ?? ("ckan:" + Identifier + ":" + Field),
            ["s"] = text,
            ["t"] = translation,
            ["ctx"] = ctx,
            ["mod"] = mod,
            ["src"] = src,
        };

        if (key is not null)
        {
            entry["k"] = key;
        }

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
    public void Find_reports_stale_via_stable_key_when_upstream_n_is_plain_text()
    {
        const string oldText = "Automated pilot (v2)";
        const string newText = "Automated pilot (v3)";

        // 上游管线落盘的形状：n 是归一化原文，键只在 k 里。
        // 旧实现只认 n 前缀，这种条目压根进不了索引，过期判定结构性失效（设计文档关键决策 9 的缺口）。
        string packLine = Line(oldText, "旧译文", "human", Identifier, "CKANMeta", "ckan:" + Identifier + ":" + Field, oldText);
        var provider = ModTextProvider.FromJsonLines([], [packLine]);

        var found = provider.Find(Identifier, Field, newText);

        Assert.That(found, Is.Not.Null, "k 是本字段的稳定键，文本变了也该认出这是同一个字段的旧译文。");
        Assert.That(found!.Text, Is.EqualTo("旧译文"));
        Assert.That(found.Stale, Is.True);
        Assert.That(found.Source, Is.EqualTo(ModTextSource.Human));
    }

    [Test]
    public void StableKey_wins_over_normalized_text_when_both_present()
    {
        // 两条条目 n 完全相同（同一句原文同时出现在 abstract 与 description），只有 k 能区分它们。
        const string shared = "Automated pilot for launch";
        string abstractLine = Line("Automated pilot for launch (old)", "摘要旧译文", "human", Identifier, "CKANMeta",
            "ckan:" + Identifier + ":abstract", shared);
        string descriptionLine = Line("Automated pilot for launch (old)", "描述旧译文", "human", Identifier, "CKANMeta",
            "ckan:" + Identifier + ":description", shared);

        var provider = ModTextProvider.FromJsonLines([], [abstractLine, descriptionLine]);

        // 原文都改过了，精确查找必然落空，此时只能靠 k 找到"自己字段"的旧条目。
        var byAbstract = provider.Find(Identifier, "abstract", "Automated pilot for launch (new)");
        var byDescription = provider.Find(Identifier, "description", "Automated pilot for launch (new)");

        Assert.That(byAbstract?.Text, Is.EqualTo("摘要旧译文"), "k=...:abstract 的条目必须命中 abstract，不能串到 description。");
        Assert.That(byAbstract!.Stale, Is.True);
        Assert.That(byDescription?.Text, Is.EqualTo("描述旧译文"), "k=...:description 的条目必须命中 description。");
        Assert.That(byDescription!.Stale, Is.True);
    }

    [Test]
    public void Legacy_pack_without_stable_key_still_reports_stale()
    {
        // 旧包兼容回归：没有 k、n 就是 ckan:<identifier>:<field> 的人工覆盖，行为必须与之前一致。
        const string oldText = "Telemetry readout (v1)";
        const string newText = "Telemetry readout (v2)";
        var provider = ModTextProvider.FromJsonLines([], [Line(oldText, "旧译文", "human")]);

        var found = provider.Find(Identifier, Field, newText);

        Assert.That(found, Is.Not.Null);
        Assert.That(found!.Text, Is.EqualTo("旧译文"));
        Assert.That(found.Stale, Is.True);
    }

    [Test]
    public void Stable_key_does_not_leak_across_fields_when_absent()
    {
        // 回归：n 是普通原文、又没有 k 的条目仍然不建索引——不能因为这次改动把它们当成键。
        const string oldText = "Plain upstream text (v1)";
        string plainLine = Line(oldText, "旧译文", "human", Identifier, "CKANMeta", key: null, normalized: oldText);

        var provider = ModTextProvider.FromJsonLines([], [plainLine]);

        Assert.That(provider.Find(Identifier, Field, "Plain upstream text (v2)"), Is.Null,
            "n 是原文且没有 k 时无法定位字段，宁可漏报过期也不能错报。");
    }

    [Test]
    public void Duplicate_stable_key_keeps_human_over_machine()
    {
        // 上游原文改版：同一个 (identifier, field) 在包里留下多条历史译文。
        // CKAN repo.json 的 module_version 各带一份 abstract，实测 72 行 / 61 个键就是这样来的，
        // 所以「同 k 多行」是合法数据，读取侧必须自己定取舍规则。
        const string machineOld = "Automated pilot (v1)";
        const string humanOld = "Automated pilot (v1, hotfix)";
        const string current = "Automated pilot (v2)";
        string key = "ckan:" + Identifier + ":" + Field;

        string machineLine = Line(machineOld, "机翻旧译文", "machine", Identifier, "CKANMeta", key, machineOld);
        string humanLine = Line(humanOld, "人工旧译文", "human", Identifier, "CKANMeta", key, humanOld);

        // 两种载入顺序都要压过机翻：人工 > 机翻与「谁后写」无关。
        foreach (var lines in new[] { new[] { machineLine, humanLine }, new[] { humanLine, machineLine } })
        {
            var provider = ModTextProvider.FromJsonLines([], lines);

            var found = provider.Find(Identifier, Field, current);

            Assert.That(found, Is.Not.Null);
            Assert.That(found!.Text, Is.EqualTo("人工旧译文"));
            Assert.That(found.Source, Is.EqualTo(ModTextSource.Human));
            Assert.That(found.Stale, Is.True);
        }
    }

    [Test]
    public void Duplicate_stable_key_later_same_class_wins()
    {
        // 同类相撞（都是人工）时后载入的胜：文件按追加顺序载入，后写的是作者最近的意图。
        const string earlier = "Automated pilot (v1)";
        const string later = "Automated pilot (v1.1)";
        string key = "ckan:" + Identifier + ":" + Field;

        string earlierLine = Line(earlier, "较早的人工译文", "human", Identifier, "CKANMeta", key, earlier);
        string laterLine = Line(later, "较晚的人工译文", "human", Identifier, "CKANMeta", key, later);

        var provider = ModTextProvider.FromJsonLines([], [earlierLine, laterLine]);

        var found = provider.Find(Identifier, Field, "Automated pilot (v2)");

        Assert.That(found, Is.Not.Null);
        Assert.That(found!.Text, Is.EqualTo("较晚的人工译文"));
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
