using CkanUI.Services.Abstractions.Logs;
using CkanUI.Services.Logs;
using CkanUI.TestSupport;
using NUnit.Framework;

namespace CkanUI.Services.Tests;

/// <summary>
/// LOG-03 分类引擎（14 篇 §3.3 / §9.1 T-LOG-03 染色规则快照）：
/// 用真实 CKAN 日志片段锁定族归属，规则表改动导致漂移时这里会红。
/// </summary>
[TestFixture]
sealed class LogClassifierTests
{
    private LogClassifier _classifier = null!;

    [SetUp]
    public void SetUp() => _classifier = new LogClassifier();

    private LogFamily Classify(string message, string? detail = null, LogSourceKind kind = LogSourceKind.Log4Net) =>
        _classifier.Classify(kind, LogLevel.Info, "log4net:CKAN.Module", message, detail);

    public static IEnumerable<TestCaseData> ClassificationSnippets()
    {
        // ---- 下载/校验 ----
        yield return new TestCaseData("SHA256 mismatch for RealChute-1.4.2.zip", LogFamily.Download);
        yield return new TestCaseData("Checksum verification failed", LogFamily.Download);
        yield return new TestCaseData("校验和失败：RealChute-1.4.2.zip", LogFamily.Download);
        yield return new TestCaseData("Failed to unzip archive", LogFamily.Download);
        yield return new TestCaseData("The archive is corrupt", LogFamily.Download);
        yield return new TestCaseData("Downloading https://example.com/RealChute.zip", LogFamily.Download);
        yield return new TestCaseData("Failed to download https://example.com/RealChute.zip", LogFamily.Download);
        yield return new TestCaseData("File size mismatch: expected 1024 bytes, got 0", LogFamily.Download);
        yield return new TestCaseData("Content-Length mismatch while fetching metadata", LogFamily.Download);
        yield return new TestCaseData("partial download resume failed", LogFamily.Download);

        // ---- 网络 ----
        yield return new TestCaseData("System.Net.WebException: The request was aborted", LogFamily.Net);
        yield return new TestCaseData("The connection was closed unexpectedly", LogFamily.Net);
        yield return new TestCaseData("SSL handshake failed with repository host", LogFamily.Net);
        yield return new TestCaseData("The operation has timed out", LogFamily.Net);
        yield return new TestCaseData("请求超时，正在重试", LogFamily.Net);
        yield return new TestCaseData("Connection refused (10061)", LogFamily.Net);
        yield return new TestCaseData("DNS resolution failed for ckan.example.com", LogFamily.Net);
        yield return new TestCaseData("Proxy authentication required", LogFamily.Net);
        yield return new TestCaseData("The remote certificate has expired", LogFamily.Net);
        yield return new TestCaseData("Server returned HTTP 503", LogFamily.Net);
        yield return new TestCaseData("socket hang up", LogFamily.Net);

        // ---- 安全/凭据 ----
        yield return new TestCaseData("401 Unauthorized from api.github.com", LogFamily.Sec);
        yield return new TestCaseData("403 Forbidden for repository SpaceDock", LogFamily.Sec);
        yield return new TestCaseData("API key invalid", LogFamily.Sec);
        yield return new TestCaseData("AuthToken expired, please re-login", LogFamily.Sec);
        yield return new TestCaseData("凭据无效，请重新登录", LogFamily.Sec);
        yield return new TestCaseData("Authorization: Bearer eyJhbGciOiJIUzI1NiJ9", LogFamily.Sec);
        yield return new TestCaseData("access token invalid", LogFamily.Sec);

        // ---- 依赖求解 ----
        yield return new TestCaseData("Unresolved dependencies: RealChute", LogFamily.Solve);
        yield return new TestCaseData("NotSatisfiable: no version of X satisfies the constraints", LogFamily.Solve);
        yield return new TestCaseData("RelationshipResolver found a cycle", LogFamily.Solve);
        yield return new TestCaseData("依赖冲突：ModuleManager 版本过低", LogFamily.Solve);
        yield return new TestCaseData("Module RealChute is incompatible with KSP 1.11", LogFamily.Solve);
        yield return new TestCaseData("Module A requires B which is not installed", LogFamily.Solve);
        yield return new TestCaseData("无法解析依赖关系", LogFamily.Solve);
        yield return new TestCaseData("version conflict detected between A and B", LogFamily.Solve);

        // ---- 安装/卸载 ----
        yield return new TestCaseData("Installing module RealChute", LogFamily.Install);
        yield return new TestCaseData("Removing module RealChute", LogFamily.Install);
        yield return new TestCaseData("Transaction started", LogFamily.Install);
        yield return new TestCaseData("Uninstalling RealChute 1.4.2", LogFamily.Install);
        yield return new TestCaseData("Overwriting file GameData/RealChute/x.dll", LogFamily.Install);
        yield return new TestCaseData("回滚已开始", LogFamily.Install);
        yield return new TestCaseData("Backup restore failed", LogFamily.Install);
        yield return new TestCaseData("Replace existing file GameData/RealChute/x.dll", LogFamily.Install);

        // ---- 文件系统 ----
        yield return new TestCaseData("System.IO.IOException: The process cannot access the file", LogFamily.Fs);
        yield return new TestCaseData("UnauthorizedAccessException: Access to the path is denied.", LogFamily.Fs);
        yield return new TestCaseData("Access to the path 'GameData' is denied.", LogFamily.Fs);
        yield return new TestCaseData("EBUSY: resource busy or locked", LogFamily.Fs);
        yield return new TestCaseData("DirectoryNotFoundException: Could not find a part of the path", LogFamily.Fs);
        yield return new TestCaseData("路径太长，无法写入", LogFamily.Fs);
        yield return new TestCaseData("FileNotFoundException: RealChute.dll", LogFamily.Fs);

        // ---- 注册表/实例 ----
        yield return new TestCaseData("Registry.locked: another CKAN instance holds the lock", LogFamily.Reg);
        yield return new TestCaseData("RegistryVersionNotSupported: registry is too new", LogFamily.Reg);
        yield return new TestCaseData("注册表版本不支持，请升级 CKAN", LogFamily.Reg);
        yield return new TestCaseData("instancename is invalid", LogFamily.Reg);

        // ---- 存档 ----
        yield return new TestCaseData("Saving persistent.sfs failed", LogFamily.Save);
        yield return new TestCaseData("loadmeta.json corrupted", LogFamily.Save);
        yield return new TestCaseData("存档写入失败", LogFamily.Save);

        // ---- UI ----
        yield return new TestCaseData("Navigation failed: CachePage", LogFamily.Ui);
        yield return new TestCaseData("Binding error on ThemeName", LogFamily.Ui);
        yield return new TestCaseData("主题降级为浅色（资源缺失）", LogFamily.Ui);
        yield return new TestCaseData("导航失败，已回退到欢迎页", LogFamily.Ui);

        // ---- 兜底 ----
        yield return new TestCaseData("Some generic message", LogFamily.Other);
        yield return new TestCaseData("ModuleManager loaded 42 patches", LogFamily.Other);

        // ---- 权重优先级（同一条命中多族时取权重最高者）----
        yield return new TestCaseData("401 Unauthorized while downloading mod", LogFamily.Sec);
        yield return new TestCaseData("Failed to download: connection reset by peer", LogFamily.Download);
        yield return new TestCaseData("RegistryVersionNotSupported while installing", LogFamily.Reg);
    }

    [TestCaseSource(nameof(ClassificationSnippets))]
    public void Classify_RealWorldSnippet(string snippet, LogFamily expected) =>
        Assert.That(Classify(snippet), Is.EqualTo(expected));

    [Test]
    public void Classify_SearchesDetailText()
    {
        // 异常栈常把关键信息放在 Detail 里，只看 Message 会漏判。
        var family = Classify("Install failed", detail: "System.Exception: SHA256 mismatch for RealChute.zip");

        Assert.That(family, Is.EqualTo(LogFamily.Download));
    }

    [Test]
    public void Classify_SearchesLoggerName()
    {
        // Core 的 logger 名自带语义（CKAN.RegistryManager 之类），忽略它会漏判。
        var family = _classifier.Classify(
            LogSourceKind.Log4Net,
            LogLevel.Error,
            "log4net:CKAN.RegistryManager",
            "unexpected reply",
            null);

        Assert.That(family, Is.EqualTo(LogFamily.Reg));
    }

    [Test]
    public void Classify_NoRuleHitFromUiSourceFallsBackToUi()
    {
        var family = _classifier.Classify(LogSourceKind.Ui, LogLevel.Info, "ui:shell", "page switched", null);

        Assert.That(family, Is.EqualTo(LogFamily.Ui));
    }

    [Test]
    public void Classify_NoRuleHitFromCoreSourceStaysOther()
    {
        var family = _classifier.Classify(LogSourceKind.Log4Net, LogLevel.Info, "log4net:CKAN.Core", "generic", null);

        Assert.That(family, Is.EqualTo(LogFamily.Other));
    }

    [Test]
    public void Classify_EmptyMessageAndDetailFromCoreStaysOther()
    {
        var family = _classifier.Classify(LogSourceKind.Log4Net, LogLevel.Debug, null, null, null);

        Assert.That(family, Is.EqualTo(LogFamily.Other));
    }

    [Test]
    public void RuleSet_DefaultsToBuiltinRules()
    {
        Assert.That(_classifier.RuleSet.Origin, Is.EqualTo(BuiltinLogRules.Origin));
        Assert.That(_classifier.RuleCount, Is.GreaterThanOrEqualTo(19));
        Assert.That(_classifier.LastLoadError, Is.Null);
    }

    [Test]
    public void BuiltinRules_AllHavePatternAndWeight()
    {
        var rules = BuiltinLogRules.Create().Rules;

        Assert.That(rules, Is.Not.Empty);
        Assert.Multiple(() =>
        {
            foreach (var rule in rules)
            {
                Assert.That(rule.Pattern, Is.Not.Empty, "规则缺少正则");
                Assert.That(rule.Weight, Is.GreaterThan(0), "规则缺少权重");
                Assert.That(rule.Family, Is.Not.EqualTo(LogFamily.Other), "内置规则不应兜底 Other");
            }
        });
    }

    [Test]
    public void Load_FromRuleFileOverridesBuiltin()
    {
        using var temp = new TempWorkspace("log-rules");
        var path = temp.Combine("log-rules.json");
        File.WriteAllText(
            path,
            """
            {
              // 用户可覆盖的规则表（14 篇 §3.3）
              "rules": [
                { "family": "Install", "weight": 900, "pattern": "boom", "keywords": ["boom"] }
              ]
            }
            """);

        var classifier = new LogClassifier(path);

        Assert.That(classifier.RuleSet.Origin, Is.EqualTo(path));
        Assert.That(classifier.RuleCount, Is.EqualTo(1));
        Assert.That(classifier.Classify(LogSourceKind.Log4Net, LogLevel.Error, "log4net:CKAN.X", "boom", null), Is.EqualTo(LogFamily.Install));
        // 文件里的规则表生效后，内置规则不再参与：SHA256 不再归 Download。
        Assert.That(classifier.Classify(LogSourceKind.Log4Net, LogLevel.Error, "log4net:CKAN.X", "SHA256 mismatch", null), Is.EqualTo(LogFamily.Other));
    }

    [Test]
    public void Load_WithBrokenJsonFallsBackToBuiltinAndReportsError()
    {
        using var temp = new TempWorkspace("log-rules-broken");
        var path = temp.Combine("log-rules.json");
        File.WriteAllText(path, "{ this is not json");

        var classifier = new LogClassifier(path);

        Assert.That(classifier.RuleSet.Origin, Is.EqualTo(BuiltinLogRules.Origin));
        Assert.That(classifier.LastLoadError, Is.Not.Null.And.Not.Empty);
        Assert.That(classifier.Classify(LogSourceKind.Log4Net, LogLevel.Error, "log4net:CKAN.X", "SHA256 mismatch", null), Is.EqualTo(LogFamily.Download));
    }

    [Test]
    public void Reload_ReportsFailureButKeepsBuiltinRules()
    {
        using var temp = new TempWorkspace("log-rules-reload");
        var path = temp.Combine("log-rules.json");
        File.WriteAllText(path, """{"rules": []}""");
        var classifier = new LogClassifier(path);

        var result = classifier.Reload();

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("内置规则"));
        Assert.That(classifier.RuleCount, Is.GreaterThanOrEqualTo(19));
    }

    [Test]
    public void Load_SkipsOnlyTheBrokenRegexRule()
    {
        using var temp = new TempWorkspace("log-rules-badregex");
        var path = temp.Combine("log-rules.json");
        File.WriteAllText(
            path,
            """
            {
              "rules": [
                { "family": "Net", "weight": 900, "pattern": "([unclosed" },
                { "family": "Solve", "weight": 800, "pattern": "zzz", "keywords": ["zzz"] }
              ]
            }
            """);

        var classifier = new LogClassifier(path);

        Assert.That(classifier.RuleCount, Is.EqualTo(1));
        Assert.That(classifier.Classify(LogSourceKind.Log4Net, LogLevel.Error, "log4net:CKAN.X", "zzz", null), Is.EqualTo(LogFamily.Solve));
    }

    [Test]
    public void Load_RuleWithoutPatternIsSkipped()
    {
        using var temp = new TempWorkspace("log-rules-nopattern");
        var path = temp.Combine("log-rules.json");
        File.WriteAllText(path, """{"rules": [{"family": "Net", "weight": 10}]}""");

        var classifier = new LogClassifier(path);

        Assert.That(classifier.RuleCount, Is.Zero);
        Assert.That(classifier.Classify(LogSourceKind.Log4Net, LogLevel.Error, "log4net:CKAN.X", "anything", null), Is.EqualTo(LogFamily.Other));
    }

    [Test]
    public void Load_MissingFileUsesBuiltin()
    {
        using var temp = new TempWorkspace("log-rules-missing");

        var classifier = new LogClassifier(temp.Combine("nope.json"));

        Assert.That(classifier.RuleSet.Origin, Is.EqualTo(BuiltinLogRules.Origin));
        Assert.That(classifier.LastLoadError, Is.Null);
    }

    [Test]
    public void ComposeText_JoinsSourceMessageAndDetail()
    {
        var text = LogClassifier.ComposeText("s", "m", "d");

        Assert.That(text, Is.EqualTo("s\nm\nd"));
    }
}
