using System;
using System.Collections.Generic;
using System.Linq;

using CkanUI.Services.Abstractions.Logs;
using CkanUI.Services.Logs;

using NUnit.Framework;

namespace CkanUI.Services.Tests;

/// <summary>
/// 14 篇 §9.1 T-LOG-01：脱敏泄漏矩阵。
/// 每个样本都是真实日志里出现过的形态（中文用户名、带空格的用户名、Bearer、query 里的 api_key…），
/// 断言两件事：① 指定片段被抹掉 ② 抹完的文本再走一遍反向扫描必须零命中（这是出口硬门禁的前提）。
/// </summary>
[TestFixture]
public sealed class LogRedactorTests
{
    private static readonly LogRedactor Concrete = new();

    // ContainsLeak 是接口默认实现，用接口类型调用才能测到。
    private static readonly ILogRedactor Redactor = Concrete;

    /// <summary>20 个泄漏样本：输入 + 必须消失的片段。</summary>
    private static IEnumerable<TestCaseData> SensitiveSamples()
    {
        yield return Sample("中文用户名", @"打开日志 C:\Users\张伟\AppData\Local\CKAN\logs\ckanui.log 失败", "张伟");
        yield return Sample("带空格的用户名", @"C:\Users\John Smith\AppData\Local\CKAN\CKAN.log", "John Smith");
        yield return Sample("Linux home", "/home/alice/.config/ckan/registry.json", "alice");
        yield return Sample("macOS home", "/Users/bob/Library/Application Support/CKAN/registry.json", "bob");
        yield return Sample("裸 SteamID", "SteamID 76561198012345678 resolved", "76561198012345678");
        yield return Sample("SteamUser 字段", "SteamUser=43701 SteamAppId=220200", "43701");
        yield return Sample("Bearer 头", "Authorization: Bearer eyJhbGciOiJIUzI1NiJ9.abc123def456", "eyJhbGciOiJIUzI1NiJ9");
        yield return Sample("query 里的 api_key", "GET https://api.example.com/mods?api_key=abcdef123456&page=2 ", "abcdef123456");
        yield return Sample("邮箱", "Reporter 联系 someone@example.com 反馈", "someone@example.com");
        yield return Sample("IPv4", "Connect to 192.168.1.24:8080 timeout", "192.168.1.24");
        yield return Sample("IPv6", "inet6 addr 2001:db8::1 unreachable", "2001:db8::1");
        yield return Sample("带空格的游戏目录", @"加载 E:\Kerbal Space Program\GameData\RealChute\RealChute.dll", "Kerbal Space Program");
        yield return Sample("UNC 路径", @"\\fileserver\share\CKAN\CKAN.log", "fileserver");
        yield return Sample("Unix 绝对路径", "/var/log/ckan/ckan.log rotated", "var/log/ckan");
        yield return Sample("存档文件名", "Loading quicksave.sfs from slot 3", "quicksave.sfs");
        yield return Sample("COMPUTERNAME 占位符", "host=%COMPUTERNAME% os=windows", "%COMPUTERNAME%");
        yield return Sample("password 赋值", "password: hunter2 accepted", "hunter2");
        yield return Sample("JSON 形态的 api_key", @"""api_key"": ""eyJ-very-secret-value""", "eyJ-very-secret-value");
        yield return Sample("两段盘符路径", @"launched from C:\Program Files\CKAN\CKAN.exe", "Program Files");
        yield return Sample("tmp 下载缓存", "/tmp/ckan-download-8192.cache partial", "ckan-download-8192.cache");
    }

    private static TestCaseData Sample(string label, string input, params string[] forbidden) =>
        new TestCaseData(input, forbidden).SetName("Redact_抹掉泄漏_" + label);

    [TestCaseSource(nameof(SensitiveSamples))]
    public void Redact_RemovesSensitiveFragments(string input, string[] forbidden)
    {
        var redacted = Redactor.Redact(input);

        foreach (var fragment in forbidden)
        {
            Assert.That(
                redacted.Contains(fragment, StringComparison.OrdinalIgnoreCase),
                Is.False,
                $"未抹掉「{fragment}」：{redacted}");
        }
    }

    [TestCaseSource(nameof(SensitiveSamples))]
    public void Redact_LeavesNoLeakForExitScan(string input, string[] forbidden)
    {
        _ = forbidden;
        var redacted = Redactor.Redact(input);

        Assert.That(Redactor.FindLeaks(redacted), Is.Empty, $"出口反向扫描仍命中：{redacted}");
    }

    [TestCaseSource(nameof(SensitiveSamples))]
    public void Redact_IsIdempotent(string input, string[] forbidden)
    {
        _ = forbidden;
        var once = Redactor.Redact(input);

        Assert.That(Redactor.Redact(once), Is.EqualTo(once), "二次脱敏必须不变，否则出口扫描会误报");
    }

    [Test]
    public void Redact_KeepsOrdinaryTextAndClock()
    {
        // 时间戳里的冒号不能被 IPv6 规则吃掉——这是最容易踩的误伤。
        const string line = "12:34:56 heartbeat ok, 3 mods loaded";

        Assert.That(Redactor.Redact(line), Is.EqualTo(line));
    }

    [Test]
    public void Redact_OverRedactsVersionNumbersOnPurpose()
    {
        // 设计取舍：形如 x.y.z.w 的版本号会被 IP 规则吃掉，宁过度勿泄漏。
        var redacted = Redactor.Redact("CKAN 1.2.3.4 ready");

        Assert.That(redacted, Does.Contain("<IP>"));
    }

    [Test]
    public void Redact_KeepsEnvironmentPlaceholders()
    {
        const string line = @"%LOCALAPPDATA%\CKAN\logs\ckanui.log";

        Assert.That(Redactor.Redact(line), Does.Contain("%LOCALAPPDATA%"));
    }

    [TestCase(null)]
    [TestCase("")]
    public void Redact_EmptyInputReturnsEmpty(string? input) =>
        Assert.That(Redactor.Redact(input), Is.Empty);

    [Test]
    public void FindLeaks_ReportsKindsAndPositions()
    {
        const string text = "mail someone@example.com from 192.168.1.24 steamid 76561198012345678";

        var leaks = Redactor.FindLeaks(text);

        var kinds = leaks.Select(leak => leak.Kind).ToList();
        Assert.That(kinds, Does.Contain(RedactionLeakKind.Email));
        Assert.That(kinds, Does.Contain(RedactionLeakKind.IpAddress));
        Assert.That(kinds, Does.Contain(RedactionLeakKind.SteamId));
        Assert.That(leaks.Select(leak => leak.Index), Is.Ordered);
        Assert.That(Redactor.ContainsLeak(text), Is.True);
    }

    [Test]
    public void FindLeaks_CleanTextIsEmpty()
    {
        const string text = "Installing RealChute 1.4.2 for KSP 1.12";

        Assert.That(Redactor.FindLeaks(text), Is.Empty);
        Assert.That(Redactor.ContainsLeak(text), Is.False);
    }

    [Test]
    public void FindLeaks_DoesNotFlagColonCandidateThatIsNotAddress()
    {
        Assert.That(Redactor.FindLeaks("elapsed 12:34:56 total"), Is.Empty);
    }

    [Test]
    public void FindLeaks_NullInputIsEmpty() =>
        Assert.That(Redactor.FindLeaks(null), Is.Empty);

    [Test]
    public void RedactConfig_DropsSensitiveKeysEntirely()
    {
        var config = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Repository.AuthTokens"] = "abc",
            ["api_key"] = "def",
            ["Password"] = "hunter2",
            ["GameDir"] = @"C:\Kerbal Space Program",
            ["PreferredVersion"] = "1.12.5",
        };

        var redacted = Redactor.RedactConfig(config);

        Assert.That(redacted.ContainsKey("Repository.AuthTokens"), Is.False, "AuthTokens 必须整键删除");
        Assert.That(redacted.ContainsKey("api_key"), Is.False);
        Assert.That(redacted.ContainsKey("Password"), Is.False);
        Assert.That(redacted["PreferredVersion"], Is.EqualTo("1.12.5"));
        Assert.That(redacted["GameDir"], Does.Not.Contain("Kerbal"));
    }

    [Test]
    public void RedactConfig_RequiresConfig()
    {
        Assert.Throws<ArgumentNullException>(() => Redactor.RedactConfig(null!));
    }

    [TestCase("AuthTokens", true)]
    [TestCase("Repository.AuthTokens", true)]
    [TestCase("repository/api_key", true)]
    [TestCase("client_secret", true)]
    [TestCase("GameDir", false)]
    [TestCase("PreferredVersion", false)]
    [TestCase("", false)]
    [TestCase(null, false)]
    public void IsSensitiveKey_RecognisesSensitiveNames(string? key, bool expected) =>
        Assert.That(LogRedactor.IsSensitiveKey(key), Is.EqualTo(expected));

    [Test]
    public void SensitiveKeyNames_ExposeFragmentList() =>
        Assert.That(LogRedactor.SensitiveKeyNames.Count, Is.GreaterThan(5));
}
