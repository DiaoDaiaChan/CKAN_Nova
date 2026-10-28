using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CkanUI.ModText;
using CkanUI.Services.ModText;
using CkanUI.TestSupport;
using NUnit.Framework;
using UI18n.Core.Hashing;
using UI18n.Core.Update;

namespace CkanUI.Services.Tests;

/// <summary>
/// 元数据译文服务：分层装载、目录定位、三档策略与包校验（17 篇 MTI-03 / MTI-06 / MTI-08）。
/// </summary>
[TestFixture]
public sealed class ModTextServiceLayeringTests
{
    private const string Identifier = "MechJeb2";
    private const string Field = "abstract";
    private const string English = "Automated pilot";

    [Test]
    public async Task Overrides_layer_wins_over_pack_layer_after_reload()
    {
        using var workspace = new TempWorkspace("modtext-layer");
        WriteLines(Path.Combine(workspace.Path, "packs", "overrides.jsonl"), Line(English, "人工覆盖", "human"));
        WriteLines(Path.Combine(workspace.Path, "packs", "zh-cn", "CKANMeta.jsonl"), Line(English, "语言包译文", "human"));

        using var service = new ModTextService(workspace.Path);
        await service.ReloadPacksAsync("zh-cn");

        // TotalEntries 仍是两层合计，GetAllEntries 仍是合并视图（旧测试依赖这两个可观察行为）。
        Assert.That(service.TotalEntries, Is.EqualTo(2));
        Assert.That(service.GetAllEntries(), Has.Count.EqualTo(2));
        Assert.That(service.Provider.Find(Identifier, Field, English)?.Text, Is.EqualTo("人工覆盖"));
    }

    [Test]
    public async Task Language_directory_is_located_case_insensitively()
    {
        using var workspace = new TempWorkspace("modtext-case");
        // 用户手工解包的目录常是 zh-CN，而 CurrentLanguage 全是小写。
        WriteLines(Path.Combine(workspace.Path, "packs", "zh-CN", "CKANMeta.jsonl"), Line(English, "语言包译文", "human"));

        using var service = new ModTextService(workspace.Path);
        await service.ReloadPacksAsync("zh-cn");

        Assert.That(service.TotalEntries, Is.EqualTo(1));
        Assert.That(service.Provider.Find(Identifier, Field, English)?.Text, Is.EqualTo("语言包译文"));
    }

    [Test]
    public async Task Missing_language_pack_degrades_to_english_without_throwing()
    {
        using var workspace = new TempWorkspace("modtext-missing");

        using var service = new ModTextService(workspace.Path);
        await service.ReloadPacksAsync("ja-jp");

        // 缺包不是错误：首帧必须能画，译文降级为英文原文（调用方拿 null 就用原文本）。
        Assert.That(service.TotalEntries, Is.Zero);
        Assert.That(service.Provider.Find(Identifier, Field, English), Is.Null);
    }

    [Test]
    public async Task Policy_defaults_to_existing_only_and_can_be_raised_hot()
    {
        using var workspace = new TempWorkspace("modtext-policy");
        WriteLines(Path.Combine(workspace.Path, "packs", "zh-cn", "CKANMeta.jsonl"), Line(English, "机翻译文", "machine"));

        using var service = new ModTextService(workspace.Path);
        await service.ReloadPacksAsync("zh-cn");

        Assert.That(service.Policy, Is.EqualTo(ModTextPolicy.ExistingOnly));
        Assert.That(service.Provider.Find(Identifier, Field, English), Is.Null, "默认档不允许机翻。");

        service.Policy = ModTextPolicy.AllowMachine;
        Assert.That(service.Provider.Find(Identifier, Field, English)?.Text, Is.EqualTo("机翻译文"), "改档立即生效，不需要重新载入。");

        service.Policy = ModTextPolicy.Off;
        Assert.That(service.Provider.Find(Identifier, Field, English), Is.Null);
    }

    [Test]
    public async Task Blocklist_file_is_loaded_and_enforced()
    {
        using var workspace = new TempWorkspace("modtext-blocklist");
        WriteLines(Path.Combine(workspace.Path, "packs", "zh-cn", "CKANMeta.jsonl"), Line(English, "人工译文", "human"));
        var blocklistJson = """{"blocked":["MechJeb2"]}""";
        File.WriteAllText(Path.Combine(workspace.Path, "blocklist.json"), blocklistJson, Encoding.UTF8);

        using var service = new ModTextService(workspace.Path);
        await service.ReloadPacksAsync("zh-cn");

        Assert.That(service.Blocklist.Count, Is.EqualTo(1));
        Assert.That(service.Provider.Find(Identifier, Field, English), Is.Null, "名单里的 mod 一律不译。");
    }

    [Test]
    public async Task VerifyInstalledPacks_reports_no_manifest_and_size_mismatch()
    {
        using var workspace = new TempWorkspace("modtext-verify");
        var langDir = Path.Combine(workspace.Path, "packs", "zh-cn");
        var filePath = Path.Combine(langDir, "CKANMeta.jsonl");
        WriteLines(filePath, Line(English, "人工译文", "human"));
        var bytes = File.ReadAllBytes(filePath);
        var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        using var service = new ModTextService(workspace.Path);

        var noManifest = service.VerifyInstalledPacks(null);
        Assert.That(noManifest.Ok, Is.False);
        Assert.That(noManifest.Reason, Is.EqualTo(UpdateVerify.ReasonNoManifest));

        var mismatch = service.VerifyInstalledPacks(Manifest("Packs/zh-cn/CKANMeta.jsonl", bytes.Length + 7, sha));
        Assert.That(mismatch.Ok, Is.False);
        Assert.That(mismatch.Reason, Does.StartWith(UpdateVerify.ReasonSizeMismatch));

        var ok = service.VerifyInstalledPacks(Manifest("Packs/zh-cn/CKANMeta.jsonl", bytes.Length, sha));
        Assert.That(ok.Ok, Is.True, ok.Reason);
    }

    private static void WriteLines(string path, params string[] lines)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, string.Join(Environment.NewLine, lines) + Environment.NewLine, Encoding.UTF8);
    }

    private static string Line(string text, string translation, string src)
    {
        var entry = new Dictionary<string, string?>
        {
            ["h"] = Fnv1a.Hash(text).ToString("x16"),
            ["n"] = "ckan:" + Identifier + ":" + Field,
            ["s"] = text,
            ["t"] = translation,
            ["mod"] = "CKANMeta",
            ["ctx"] = Identifier,
            ["src"] = src,
        };

        return JsonSerializer.Serialize(entry);
    }

    private static string Manifest(string relativePath, long byteCount, string sha256)
    {
        var zipSha = new string('A', 64);
        return $$"""
            {"version":"1.0.0","zip":{"name":"ui18n.zip","bytes":1,"sha256":"{{zipSha}}"},"files":[{"path":"{{relativePath}}","bytes":{{byteCount}},"sha256":"{{sha256}}"}]}
            """;
    }
}
