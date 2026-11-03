using System;
using System.IO;
using System.Text;

using CkanUI.Plugin.Ai.Settings;
using CkanUI.Plugin.Ai.Tests.Support;

using NUnit.Framework;

namespace CkanUI.Plugin.Ai.Tests;

/// <summary>设置读写：往返不丢字段、坏 JSON 回落默认、Key 只以保护器输出形式落盘。</summary>
[TestFixture]
public sealed class AiSettingsStoreTests
{
    private string _directory = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "ckan-ai-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException)
        {
            // 临时目录清不掉不影响结论：这里不测文件系统稳定性。
        }
    }

    private string SettingsPath => Path.Combine(_directory, AiSettingsStore.FileName);

    [Test]
    public void SaveThenLoadRoundTripsEveryField()
    {
        var store = new AiSettingsStore(SettingsPath, new MarkerSecretProtector());
        var settings = new AiSettings
        {
            BaseUrl = "http://127.0.0.1:9/v1",
            Model = "some-model",
            McpCommand = "node",
            McpArgs = new[] { "search.js", "--stdio" },
            Temperature = 1.1,
            MaxToolRounds = 3,
        };

        var saved = store.Save(settings);
        var loaded = store.Load();

        Assert.Multiple(() =>
        {
            Assert.That(saved.IsSuccess, Is.True, saved.Message);
            Assert.That(loaded.BaseUrl, Is.EqualTo("http://127.0.0.1:9/v1"));
            Assert.That(loaded.Model, Is.EqualTo("some-model"));
            Assert.That(loaded.McpCommand, Is.EqualTo("node"));
            Assert.That(loaded.McpArgs, Is.EqualTo(new[] { "search.js", "--stdio" }));
            Assert.That(loaded.Temperature, Is.EqualTo(1.1));
            Assert.That(loaded.MaxToolRounds, Is.EqualTo(3));
        });
    }

    [Test]
    public void OutOfRangeValuesAreClampedOnLoad()
    {
        var store = new AiSettingsStore(SettingsPath, new MarkerSecretProtector());

        store.Save(new AiSettings { Temperature = 5, MaxToolRounds = 99 });
        var loaded = store.Load();

        Assert.Multiple(() =>
        {
            Assert.That(loaded.Temperature, Is.EqualTo(2));
            Assert.That(loaded.MaxToolRounds, Is.EqualTo(AiSettings.MaxAllowedToolRounds));
        });
    }

    [Test]
    public void CorruptJsonFallsBackToDefaultsInsteadOfThrowing()
    {
        File.WriteAllText(SettingsPath, "{ 这不是 JSON，只是用户手改坏了文件");
        var store = new AiSettingsStore(SettingsPath, new MarkerSecretProtector());

        var loaded = store.Load();

        Assert.Multiple(() =>
        {
            Assert.That(loaded.BaseUrl, Is.EqualTo(AiSettings.DefaultBaseUrl));
            Assert.That(loaded.Model, Is.EqualTo(AiSettings.DefaultModel));
            Assert.That(loaded.MaxToolRounds, Is.EqualTo(AiSettings.DefaultMaxToolRounds));
            Assert.That(loaded.ApiKeyProtected, Is.Null);

            // 搜索服务默认不配：猜一个命令等于让用户白等一次启动超时，还查不到真正原因。
            Assert.That(loaded.McpCommand, Is.Empty);
        });
    }

    [Test]
    public void BlankMcpCommandStaysBlank_SoSearchIsSimplyUnavailable()
    {
        var store = new AiSettingsStore(SettingsPath, new MarkerSecretProtector());

        store.Save(new AiSettings { McpCommand = "   " });
        var loaded = store.Load();

        Assert.That(loaded.McpCommand, Is.Empty);
    }

    [Test]
    public void MissingFileReturnsDefaults()
    {
        var store = new AiSettingsStore(Path.Combine(_directory, "never-written.json"), new MarkerSecretProtector());

        var loaded = store.Load();

        Assert.That(loaded.BaseUrl, Is.EqualTo(AiSettings.DefaultBaseUrl));
    }

    [Test]
    public void ApiKeyLandsOnDiskOnlyAsProtectorOutput()
    {
        const string PlainKey = "sk-live-secret-123";
        var protector = new MarkerSecretProtector();
        var store = new AiSettingsStore(SettingsPath, protector);

        var saved = store.SaveWithApiKey(new AiSettings { Model = "m" }, PlainKey);
        var disk = File.ReadAllText(SettingsPath, Encoding.UTF8);

        Assert.Multiple(() =>
        {
            Assert.That(saved.IsSuccess, Is.True, saved.Message);
            Assert.That(disk, Does.Not.Contain(PlainKey), "仓库外也不许出现明文 Key 直接落盘");
            Assert.That(disk, Does.Contain(MarkerSecretProtector.Expect(PlainKey)));
            Assert.That(store.LoadApiKey(), Is.EqualTo(PlainKey), "明文只该在内存里由保护器解出来");
        });
    }

    [Test]
    public void ClearingApiKeyRemovesProtectedField()
    {
        var store = new AiSettingsStore(SettingsPath, new MarkerSecretProtector());
        store.SaveWithApiKey(new AiSettings(), "sk-to-clear");

        store.SaveWithApiKey(new AiSettings(), string.Empty);

        Assert.Multiple(() =>
        {
            Assert.That(store.LoadApiKey(), Is.Null);
            Assert.That(store.Load().ApiKeyProtected, Is.Null);
        });
    }

    [Test]
    public void SaveWithApiKeyFailsInsteadOfWritingPlaintextWhenProtectorIsMissing()
    {
        var store = new AiSettingsStore(SettingsPath, protector: null);

        var result = store.SaveWithApiKey(new AiSettings(), "sk-cannot-protect");

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(File.Exists(SettingsPath), Is.False, "加密不可用时宁可什么都不写，也不能把明文落盘");
        });
    }

    [Test]
    public void RotatingApiKeyReplacesOldCiphertextOnDisk()
    {
        var store = new AiSettingsStore(SettingsPath, new MarkerSecretProtector());
        store.SaveWithApiKey(new AiSettings(), "sk-old");

        store.SaveWithApiKey(new AiSettings(), "sk-new");
        var disk = File.ReadAllText(SettingsPath, Encoding.UTF8);

        Assert.Multiple(() =>
        {
            Assert.That(store.LoadApiKey(), Is.EqualTo("sk-new"));
            Assert.That(disk, Does.Not.Contain(MarkerSecretProtector.Expect("sk-old")));
        });
    }
}
