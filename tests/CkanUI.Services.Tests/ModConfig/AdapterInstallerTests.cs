using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.ModConfig;
using CkanUI.Services.ModConfig;
using NUnit.Framework;

namespace CkanUI.Services.Tests.ModConfig;

/// <summary>
/// ADP-04：第三方 adapter 的离线导入机制（json/zip 来源、zip-slip 与体积防线、原子替换、哈希与清单、ETag 更新检查）。
/// </summary>
/// <remarks>
/// 关键不变式：校验不过一律不落盘；zip 里任何一个越界条目都要整包拒绝（不做“跳过这条”的宽容）；
/// 清单损坏只能降级成“未安装”，不能让枚举整体失败。
/// 每个用例一个临时根目录（Path.GetTempPath 下的 Guid 目录），TearDown 清理，绝不碰本机真实 CKAN 目录。
/// </remarks>
[TestFixture]
public sealed class AdapterInstallerTests
{
    private TempDirectory _temp = null!;

    /// <summary>安装根目录：<临时目录>/adapters，避免污染 %LocalAppData%。</summary>
    private string Root => Path.Combine(_temp.Path, "adapters");

    [SetUp]
    public void SetUp() => _temp = ModConfigFixtures.CreateTempDirectory("adapter-install");

    [TearDown]
    public void TearDown()
    {
        _temp?.Dispose();
        _temp = null!;
    }

    // ---------- 正常安装 ----------

    [Test]
    public async Task InstallAsync_JsonFile_CopiesAdapterAndWritesManifest()
    {
        var installer = CreateInstaller();
        var path = _temp.File("packs/demo-adapter.json", AdapterJson("demo-adapter", "演示 Adapter"));

        var result = await installer.InstallAsync(AdapterPackageSource.FromFile(path));

        Assert.That(result.IsSuccess, Is.True, result.Message);
        var installed = result.Value!;
        var directory = Path.Combine(Root, "demo-adapter");
        Assert.Multiple(() =>
        {
            Assert.That(installed.AdapterDirectory, Is.EqualTo(directory));
            Assert.That(installed.Format, Is.EqualTo(AdapterPackageFormat.Json));
            Assert.That(installed.ReplacedExisting, Is.False);
            Assert.That(File.Exists(Path.Combine(directory, "adapter.json")), Is.True, "AdapterCatalog 只认这个入口名");
            Assert.That(File.Exists(Path.Combine(directory, "installed.json")), Is.True, "清单必须落在 adapter 目录里");
            Assert.That(installed.Manifest.Id, Is.EqualTo("demo-adapter"));
            Assert.That(installed.Manifest.Name, Is.EqualTo("演示 Adapter"));
            Assert.That(installed.Manifest.SchemaVersion, Is.EqualTo(1));
            Assert.That(installed.Manifest.Source, Is.EqualTo(path));
            Assert.That(installed.Manifest.SourceKind, Is.EqualTo(AdapterPackageSourceKind.LocalFile));
            Assert.That(installed.Manifest.EntryPoint, Is.Null, "单个 .json 来源没有入口概念");
            Assert.That(installed.InstallDiagnostics, Is.Not.Null);
        });
    }

    [Test]
    public async Task InstallAsync_Zip_CopiesFilesRelativeToEntryDirectory()
    {
        var installer = CreateInstaller();
        var zip = CreateZip(
            "demo-pack.zip",
            Text("demo-pack/adapter.json", AdapterJson("demo-adapter", "演示 Adapter")),
            Text("demo-pack/templates/settings.cfg", "key = value"),
            Text("demo-pack/README.md", "# 演示包"));

        var result = await installer.InstallAsync(AdapterPackageSource.FromFile(zip));

        Assert.That(result.IsSuccess, Is.True, result.Message);
        var installed = result.Value!;
        var directory = Path.Combine(Root, "demo-adapter");
        Assert.Multiple(() =>
        {
            Assert.That(installed.Format, Is.EqualTo(AdapterPackageFormat.Zip));
            Assert.That(installed.Manifest.EntryPoint, Is.EqualTo("demo-pack/adapter.json"));
            Assert.That(File.ReadAllText(Path.Combine(directory, "templates", "settings.cfg")), Is.EqualTo("key = value"));
            Assert.That(File.ReadAllText(Path.Combine(directory, "README.md")), Is.EqualTo("# 演示包"));
            Assert.That(Directory.Exists(Path.Combine(directory, "demo-pack")), Is.False, "落盘要剥掉入口目录，不能多套一层");
        });
    }

    [Test]
    public async Task InstallAsync_ZipWithParentRelativeEntry_RejectsWholePackage()
    {
        var installer = CreateInstaller();
        var zip = CreateZip(
            "slip.zip",
            Text("pack/adapter.json", AdapterJson("demo-adapter", "演示 Adapter")),
            Text("../evil.txt", "pwned"));

        var result = await installer.InstallAsync(AdapterPackageSource.FromFile(zip));

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Detail, Does.StartWith("adapter-zip-slip"));
            Assert.That(Directory.Exists(Path.Combine(Root, "demo-adapter")), Is.False, "越界即整包拒绝，不能留半成品");
            Assert.That(File.Exists(Path.Combine(Root, "evil.txt")), Is.False, "文件不能跑到安装根旁边");
        });
    }

    [Test]
    public async Task InstallAsync_ZipWithAbsoluteEntry_RejectsWholePackage()
    {
        var installer = CreateInstaller();
        var zip = CreateZip(
            "absolute.zip",
            Text("pack/adapter.json", AdapterJson("demo-adapter", "演示 Adapter")),
            Text("/abs/evil.txt", "pwned"));

        var result = await installer.InstallAsync(AdapterPackageSource.FromFile(zip));

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Detail, Does.StartWith("adapter-zip-slip"));
            Assert.That(Directory.Exists(Path.Combine(Root, "demo-adapter")), Is.False);
        });
    }

    [Test]
    public async Task InstallAsync_ZipEntryOutsideEntryDirectory_RejectsWholePackage()
    {
        var installer = CreateInstaller();
        var zip = CreateZip(
            "outside.zip",
            Text("pack/adapter.json", AdapterJson("demo-adapter", "演示 Adapter")),
            Text("readme.txt", "在入口目录外"));

        var result = await installer.InstallAsync(AdapterPackageSource.FromFile(zip));

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Detail, Does.StartWith("adapter-zip-entry-outside-root"));
            Assert.That(Directory.Exists(Path.Combine(Root, "demo-adapter")), Is.False);
        });
    }

    [Test]
    public async Task InstallAsync_ZipWithoutAdapterJson_Rejects()
    {
        var installer = CreateInstaller();
        var zip = CreateZip("empty-pack.zip", Text("readme.txt", "没有 adapter.json"));

        var result = await installer.InstallAsync(AdapterPackageSource.FromFile(zip));

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Detail, Does.StartWith("adapter-zip-no-adapter-json"));
            Assert.That(Directory.Exists(Root), Is.False);
        });
    }

    [Test]
    public async Task InstallAsync_ZipWithTwoAdapterJson_RequiresEntryPoint()
    {
        // 两个平级目录各带 adapter.json：自动选会歧义，必须由调用方指定入口。
        // 注意实现比“选入口”更严：其余条目必须落在入口所在目录内（见 docs/design/adapter-import.md 已知限制）。
        var installer = CreateInstaller();
        var zip = CreateZip(
            "two-entries.zip",
            Text("pack/adapter.json", AdapterJson("demo-adapter", "演示 Adapter")),
            Text("pack/legacy/adapter.json", AdapterJson("legacy-mod", "旧版 Adapter")),
            Text("pack/notes.txt", "说明"));

        var ambiguous = await installer.InstallAsync(AdapterPackageSource.FromFile(zip));
        var chosen = await installer.InstallAsync(AdapterPackageSource.FromFile(zip, "pack/adapter.json"));

        Assert.Multiple(() =>
        {
            Assert.That(ambiguous.IsSuccess, Is.False);
            Assert.That(ambiguous.Detail, Does.StartWith("adapter-zip-ambiguous-adapter-json"), "多入口必须让调用方显式选");
            Assert.That(chosen.IsSuccess, Is.True, chosen.Message);
            Assert.That(chosen.Value!.Manifest.Id, Is.EqualTo("demo-adapter"));
            Assert.That(chosen.Value!.Manifest.EntryPoint, Is.EqualTo("pack/adapter.json"));
            Assert.That(File.ReadAllText(Path.Combine(Root, "demo-adapter", "notes.txt")), Is.EqualTo("说明"));
            Assert.That(File.Exists(Path.Combine(Root, "demo-adapter", "legacy", "adapter.json")), Is.True, "入口目录内的其它文件照常落盘");
            Assert.That(Directory.Exists(Path.Combine(Root, "legacy-mod")), Is.False, "只有被选中的入口算 adapter");
        });
    }

    [Test]
    public async Task InstallAsync_ZipWithDeclaredEntryName_InstallsThatFileAsAdapterJson()
    {
        var installer = CreateInstaller();
        var zip = CreateZip(
            "renamed.zip",
            Text("pkg/manifest.adapter.json", AdapterJson("renamed-adapter", "改名的入口")),
            Text("pkg/templates/settings.cfg", "key = value"));

        var result = await installer.InstallAsync(AdapterPackageSource.FromFile(zip, "pkg/manifest.adapter.json"));

        Assert.That(result.IsSuccess, Is.True, result.Message);
        var directory = Path.Combine(Root, "renamed-adapter");
        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.Manifest.EntryPoint, Is.EqualTo("pkg/manifest.adapter.json"));
            Assert.That(File.Exists(Path.Combine(directory, "adapter.json")), Is.True, "入口无论叫什么，落盘都归一成 adapter.json");
            Assert.That(new FileInfo(Path.Combine(directory, "adapter.json")).Length, Is.GreaterThan(0));
            Assert.That(File.Exists(Path.Combine(directory, "manifest.adapter.json")), Is.False, "入口文件不要重复落盘");
        });
    }

    // ---------- 校验失败不落盘 ----------

    [Test]
    public async Task InstallAsync_SchemaInvalid_DoesNotWriteAnything()
    {
        var installer = CreateInstaller();
        var path = _temp.File("packs/broken.json", """
            { "schemaVersion": 1, "id": "demo-adapter", "name": "缺 files" }
            """);

        var result = await installer.InstallAsync(AdapterPackageSource.FromFile(path));

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Detail, Does.StartWith("adapter-schema-invalid"));
            Assert.That(result.Detail, Does.Contain("files"), "诊断要能指到具体字段");
            Assert.That(Directory.Exists(Root), Is.False, "schema 不过连安装根目录都不该建");
        });
    }

    [Test]
    public async Task InstallAsync_PackageOverSizeLimit_Rejects()
    {
        var installer = CreateInstaller(limits: new AdapterInstallerLimits(MaxPackageBytes: 64));
        var path = _temp.File("packs/demo-adapter.json", AdapterJson("demo-adapter", "演示 Adapter"));

        var result = await installer.InstallAsync(AdapterPackageSource.FromFile(path));

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Detail, Does.StartWith("adapter-package-too-large"));
            Assert.That(Directory.Exists(Root), Is.False);
        });
    }

    [Test]
    public async Task InstallAsync_ZipExtractedOverLimit_Rejects()
    {
        var installer = CreateInstaller(limits: new AdapterInstallerLimits(MaxExtractedBytes: 1024));
        var zip = CreateZip(
            "big.zip",
            Text("pack/adapter.json", AdapterJson("demo-adapter", "演示 Adapter")),
            Filler("pack/payload.bin", 4096));

        var result = await installer.InstallAsync(AdapterPackageSource.FromFile(zip));

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Detail, Does.StartWith("adapter-zip-too-large"));
            Assert.That(Directory.Exists(Root), Is.False);
        });
    }

    [Test]
    public async Task InstallAsync_ZipTooManyEntries_Rejects()
    {
        var installer = CreateInstaller(limits: new AdapterInstallerLimits(MaxEntryCount: 2));
        var zip = CreateZip(
            "many.zip",
            Text("pack/adapter.json", AdapterJson("demo-adapter", "演示 Adapter")),
            Text("pack/a.txt", "a"),
            Text("pack/b.txt", "b"));

        var result = await installer.InstallAsync(AdapterPackageSource.FromFile(zip));

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Detail, Does.StartWith("adapter-zip-too-many-entries"));
            Assert.That(Directory.Exists(Root), Is.False);
        });
    }

    [Test]
    public async Task InstallAsync_MissingFile_ReturnsFailureWithoutThrowing()
    {
        var installer = CreateInstaller();
        var missing = Path.Combine(_temp.Path, "nope.json");

        var result = await installer.InstallAsync(AdapterPackageSource.FromFile(missing));

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False, "服务层不许把异常抛给调用方");
            Assert.That(result.Detail, Does.StartWith("adapter-source-missing"));
            Assert.That(result.Retryable, Is.False, "文件不存在重试也没用");
        });
    }

    // ---------- 覆盖安装 / 清单 / 哈希 ----------

    [Test]
    public async Task InstallAsync_SameIdTwice_ReplacesDirectoryAndRefreshesManifest()
    {
        var installer = CreateInstaller();
        var first = _temp.File("v1/demo-adapter.json", AdapterJson("demo-adapter", "第一版"));
        var second = _temp.File("v2/demo-adapter.json", AdapterJson("demo-adapter", "第二版"));

        var one = await installer.InstallAsync(AdapterPackageSource.FromFile(first));
        var two = await installer.InstallAsync(AdapterPackageSource.FromFile(second));

        Assert.That(one.IsSuccess, Is.True, one.Message);
        Assert.That(two.IsSuccess, Is.True, two.Message);
        var reread = await installer.GetInstalledAsync("demo-adapter");
        Assert.Multiple(() =>
        {
            Assert.That(one.Value!.ReplacedExisting, Is.False);
            Assert.That(two.Value!.ReplacedExisting, Is.True);
            Assert.That(two.Value!.Manifest.Name, Is.EqualTo("第二版"));
            Assert.That(two.Value!.Manifest.Source, Is.EqualTo(second));
            Assert.That(two.Value!.Manifest.Sha256, Is.Not.EqualTo(one.Value!.Manifest.Sha256), "内容变了，哈希必须变");
            Assert.That(two.Value!.Manifest.InstalledAt, Is.GreaterThanOrEqualTo(one.Value!.Manifest.InstalledAt));
            Assert.That(Directory.GetDirectories(Root), Has.Length.EqualTo(1), "替换后不能留下 .staging-/.backup- 目录");
            Assert.That(reread.IsSuccess, Is.True, reread.Message);
            Assert.That(reread.Value!.Name, Is.EqualTo("第二版"), "清单必须跟着更新");
            Assert.That(reread.Value!.Sha256, Is.EqualTo(two.Value!.Manifest.Sha256));
        });
    }

    [Test]
    public async Task InstallAsync_ManifestHash_MatchesPayloadHash()
    {
        var installer = CreateInstaller();
        var path = _temp.File("packs/demo-adapter.json", AdapterJson("demo-adapter", "演示 Adapter"));
        var payload = File.ReadAllBytes(path);
        var expected = Convert.ToHexStringLower(SHA256.HashData(payload));

        var result = await installer.InstallAsync(AdapterPackageSource.FromFile(path));

        Assert.That(result.IsSuccess, Is.True, result.Message);
        var manifestText = File.ReadAllText(Path.Combine(Root, "demo-adapter", "installed.json"));
        var reread = await installer.GetInstalledAsync("demo-adapter");
        Assert.Multiple(() =>
        {
            Assert.That(expected, Does.Match("^[0-9a-f]{64}$"), "sha256 一律 64 位小写十六进制");
            Assert.That(result.Value!.Hash.Sha256, Is.EqualTo(expected));
            Assert.That(result.Value!.Hash.SizeBytes, Is.EqualTo(payload.Length));
            Assert.That(result.Value!.Manifest.Sha256, Is.EqualTo(expected));
            Assert.That(result.Value!.Manifest.SizeBytes, Is.EqualTo(payload.Length));
            Assert.That(manifestText, Does.Contain(expected), "清单里必须是同一个哈希");
            Assert.That(manifestText, Does.Contain("\"sourceKind\": \"local-file\""), "清单要记来源形态");
            Assert.That(reread.IsSuccess, Is.True, reread.Message);
            Assert.That(reread.Value!.Sha256, Is.EqualTo(expected));
            Assert.That(reread.Value!.InstalledAt, Is.EqualTo(result.Value!.Manifest.InstalledAt), "清单往返要精确到 tick");
        });
    }

    [Test]
    public async Task InstallAsync_RefreshesInjectedCatalog()
    {
        var catalog = new AdapterCatalog([Path.Combine(_temp.Path, "no-builtin")], [Root]);
        var installer = CreateInstaller(catalog: catalog);
        var path = _temp.File("packs/demo-adapter.json", AdapterJson("demo-adapter", "演示 Adapter"));

        Assert.That(catalog.Adapters, Is.Empty, "安装前目录里没有 adapter");
        var result = await installer.InstallAsync(AdapterPackageSource.FromFile(path));

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Message);
            Assert.That(catalog.Adapters, Has.Count.EqualTo(1), "装完要自动刷新，否则界面得手工重载");
            Assert.That(catalog.Adapters[0].Id, Is.EqualTo("demo-adapter"));
        });
    }

    // ---------- 远端来源 ----------

    [Test]
    public async Task InstallAsync_RemoteUrl_RecordsEtagAndRemoteKind()
    {
        var payload = Encoding.UTF8.GetBytes(AdapterJson("remote-adapter", "远端 Adapter"));
        var fetcher = FetcherReturning(payload, "etag-9");
        var installer = CreateInstaller(fetcher);
        var uri = new Uri("https://example.invalid/remote-adapter.json");

        var result = await installer.InstallAsync(AdapterPackageSource.FromUrl(uri));

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.Manifest.SourceKind, Is.EqualTo(AdapterPackageSourceKind.RemoteUrl));
            Assert.That(result.Value!.Manifest.Source, Is.EqualTo(uri.AbsoluteUri));
            Assert.That(result.Value!.Manifest.SourceEtag, Is.EqualTo("etag-9"), "ETag 要留档，下次才能走条件请求");
            Assert.That(fetcher.Calls, Is.EqualTo(1));
            Assert.That(fetcher.LastUri, Is.EqualTo(uri));
            Assert.That(fetcher.LastEtag, Is.Null, "首次安装没有历史 ETag");
        });
    }

    [Test]
    public async Task InstallAsync_RemoteUrlWithoutFetcher_Fails()
    {
        var installer = CreateInstaller();
        var uri = new Uri("https://example.invalid/remote-adapter.json");

        var result = await installer.InstallAsync(AdapterPackageSource.FromUrl(uri));

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Detail, Does.StartWith("adapter-fetcher-missing"), "离线环境不该假装能装远端包");
        });
    }

    [Test]
    public async Task InstallAsync_RemoteHttpFailure_IsReturnedNotThrown()
    {
        var fetcher = new FakeFetcher
        {
            Handler = (_, _) => Task.FromResult(OperationResult<AdapterHttpResponse>.Fail(
                "获取 adapter 包失败：HTTP 500。",
                "adapter-http-status: 500",
                retryable: true)),
        };
        var installer = CreateInstaller(fetcher);
        var uri = new Uri("https://example.invalid/remote-adapter.json");

        var result = await installer.InstallAsync(AdapterPackageSource.FromUrl(uri));

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Message, Is.EqualTo("获取 adapter 包失败：HTTP 500。"));
            Assert.That(result.Detail, Does.StartWith("adapter-http-status"));
            Assert.That(result.Retryable, Is.True, "5xx 是可重试的");
            Assert.That(Directory.Exists(Root), Is.False);
        });
    }

    // ---------- 更新检查（ETag） ----------

    [Test]
    public async Task CheckForUpdate_NotModified_ReportsNoUpdateAndSendsEtag()
    {
        var fetcher = FetcherReturning(null, "v1", notModified: true);
        var installer = CreateInstaller(fetcher);
        await InstallLocalAsync(installer);

        var result = await installer.CheckForUpdateAsync("demo-adapter", new Uri("https://example.invalid/adapter.json"), "v1");

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.Status, Is.EqualTo(AdapterUpdateStatus.NotModified));
            Assert.That(result.Value!.HasUpdate, Is.False, "304 一定没有更新");
            Assert.That(result.Value!.RemoteSha256, Is.Null, "304 没有响应体，算不出远端哈希");
            Assert.That(result.Value!.RemoteSizeBytes, Is.EqualTo(0));
            Assert.That(result.Value!.InstalledSha256, Is.Not.Null);
            Assert.That(fetcher.LastEtag, Is.EqualTo("v1"), "必须把 ETag 作为 If-None-Match 发出去");
        });
    }

    [Test]
    public async Task CheckForUpdate_DifferentPayload_ReportsUpdateAvailable()
    {
        var fetcher = FetcherReturning(Encoding.UTF8.GetBytes(AdapterJson("demo-adapter", "演示 Adapter 第二版")), "v2");
        var installer = CreateInstaller(fetcher);
        var installed = await InstallLocalAsync(installer);

        var result = await installer.CheckForUpdateAsync("demo-adapter", new Uri("https://example.invalid/adapter.json"));

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.Status, Is.EqualTo(AdapterUpdateStatus.UpdateAvailable));
            Assert.That(result.Value!.HasUpdate, Is.True);
            Assert.That(result.Value!.RemoteSha256, Is.Not.EqualTo(installed.Sha256));
            Assert.That(result.Value!.InstalledSha256, Is.EqualTo(installed.Sha256));
            Assert.That(result.Value!.RemoteEtag, Is.EqualTo("v2"));
            Assert.That(result.Value!.RemoteSizeBytes, Is.GreaterThan(0));
            Assert.That(result.Value!.Message, Does.Contain("远端有更新"));
        });
    }

    [Test]
    public async Task CheckForUpdate_SamePayload_ReportsUpToDate()
    {
        var installer = CreateInstaller();
        var path = _temp.File("packs/demo-adapter.json", AdapterJson("demo-adapter", "演示 Adapter"));
        var installed = await installer.InstallAsync(AdapterPackageSource.FromFile(path));
        Assert.That(installed.IsSuccess, Is.True, installed.Message);

        var fetcher = FetcherReturning(File.ReadAllBytes(path), "v3");
        var updating = CreateInstaller(fetcher);
        var result = await updating.CheckForUpdateAsync("demo-adapter", new Uri("https://example.invalid/adapter.json"));

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.Status, Is.EqualTo(AdapterUpdateStatus.UpToDate));
            Assert.That(result.Value!.HasUpdate, Is.False);
            Assert.That(result.Value!.RemoteSha256, Is.EqualTo(installed.Value!.Manifest.Sha256), "字节没变就是没更新");
        });
    }

    [Test]
    public async Task CheckForUpdate_HttpFailure_IsReturnedNotThrown()
    {
        var fetcher = new FakeFetcher
        {
            Handler = (_, _) => Task.FromResult(OperationResult<AdapterHttpResponse>.Fail(
                "获取 adapter 包失败：HTTP 503。",
                "adapter-http-status: 503",
                retryable: true)),
        };
        var installer = CreateInstaller(fetcher);
        await InstallLocalAsync(installer);

        var result = await installer.CheckForUpdateAsync("demo-adapter", new Uri("https://example.invalid/adapter.json"));

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Detail, Does.StartWith("adapter-http-status"));
            Assert.That(result.Retryable, Is.True);
        });
    }

    [Test]
    public async Task CheckForUpdate_FetcherThrows_IsContainedAsFailure()
    {
        var fetcher = new FakeFetcher
        {
            Handler = (_, _) => throw new HttpRequestException("连接被重置"),
        };
        var installer = CreateInstaller(fetcher);
        await InstallLocalAsync(installer);

        var result = await installer.CheckForUpdateAsync("demo-adapter", new Uri("https://example.invalid/adapter.json"));

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Detail, Does.StartWith("adapter-update-check-failed"));
            Assert.That(result.Retryable, Is.True);
        });
    }

    [Test]
    public async Task CheckForUpdate_NotInstalled_FailsBeforeTouchingNetwork()
    {
        var fetcher = FetcherReturning(Encoding.UTF8.GetBytes("x"), "v1");
        var installer = CreateInstaller(fetcher);

        var result = await installer.CheckForUpdateAsync("demo-adapter", new Uri("https://example.invalid/adapter.json"));

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Detail, Does.StartWith("adapter-not-installed"));
            Assert.That(fetcher.Calls, Is.EqualTo(0), "没装就没必要发请求");
        });
    }

    // ---------- 清单损坏 / 卸载 ----------

    [Test]
    public async Task GetInstalled_CorruptManifest_DegradesToNotInstalledWithWarning()
    {
        var installer = CreateInstaller();
        await InstallLocalAsync(installer);
        var directory = Path.Combine(Root, "demo-adapter");
        File.WriteAllText(Path.Combine(directory, "installed.json"), "{ 这不是 json");

        var single = await installer.GetInstalledAsync("demo-adapter");
        var list = await installer.ListInstalledAsync();

        Assert.Multiple(() =>
        {
            Assert.That(single.IsSuccess, Is.False, "清单坏了就当作未安装，而不是抛异常");
            Assert.That(single.Detail, Does.StartWith("adapter-manifest-corrupt"));
            Assert.That(list.IsSuccess, Is.True, "一个坏清单不能拖垮整次枚举");
            Assert.That(list.Value!.Adapters, Is.Empty);
            Assert.That(
                list.Value!.IndexDiagnostics.Any(d => d.Code == "adapter-manifest-corrupt" && d.Level == ModConfigDiagnosticLevel.Warning),
                Is.True,
                "必须留一条 warning，用户才知道要重装");
            Assert.That(File.Exists(Path.Combine(directory, "adapter.json")), Is.True, "降级不等于删文件");
        });
    }

    [Test]
    public async Task Uninstall_RemovesDirectory_AndSecondCallFails()
    {
        var installer = CreateInstaller();
        await InstallLocalAsync(installer);

        var first = await installer.UninstallAsync("demo-adapter");
        var second = await installer.UninstallAsync("demo-adapter");

        Assert.Multiple(() =>
        {
            Assert.That(first.IsSuccess, Is.True, first.Message);
            Assert.That(Directory.Exists(Path.Combine(Root, "demo-adapter")), Is.False);
            Assert.That(second.IsSuccess, Is.False);
            Assert.That(second.Detail, Does.StartWith("adapter-not-installed"));
        });
    }

    // ---------- HttpClient 端口的条件请求 ----------

    [Test]
    public async Task HttpFetcher_SendsIfNoneMatch_AndMapsNotModified()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotModified));
        using var fetcher = new HttpAdapterPackageFetcher(handler);

        var result = await fetcher.GetAsync(new Uri("https://example.invalid/adapter.json"), "v1");

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.Multiple(() =>
        {
            Assert.That(handler.LastIfNoneMatch, Is.EqualTo("v1"), "条件请求头必须发出去");
            Assert.That(result.Value!.NotModified, Is.True);
            Assert.That(result.Value!.Payload, Is.Null);
            Assert.That(result.Value!.ETag, Is.EqualTo("v1"), "服务器没回 ETag 时沿用请求里的");
        });
    }

    [Test]
    public async Task HttpFetcher_ServerError_ReturnsRetryableFailure()
    {
        using var serverError = new HttpAdapterPackageFetcher(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)));
        var failed = await serverError.GetAsync(new Uri("https://example.invalid/adapter.json"));

        using var notFound = new HttpAdapterPackageFetcher(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));
        var missing = await notFound.GetAsync(new Uri("https://example.invalid/adapter.json"));

        Assert.Multiple(() =>
        {
            Assert.That(failed.IsSuccess, Is.False);
            Assert.That(failed.Detail, Does.StartWith("adapter-http-status"));
            Assert.That(failed.Retryable, Is.True, "5xx 可重试");
            Assert.That(missing.IsSuccess, Is.False);
            Assert.That(missing.Retryable, Is.False, "404 重试也没用");
        });
    }

    // ---------- 夹具 ----------

    private AdapterInstaller CreateInstaller(
        IAdapterPackageFetcher? fetcher = null,
        AdapterInstallerLimits? limits = null,
        IAdapterCatalog? catalog = null)
        => new(fetcher, catalog, Root, limits);

    /// <summary>装一份本地 json，返回清单（多个用例要先有“已装”状态才能测更新/卸载）。</summary>
    private async Task<AdapterInstallManifest> InstallLocalAsync(AdapterInstaller installer)
    {
        var path = _temp.File("packs/demo-adapter.json", AdapterJson("demo-adapter", "演示 Adapter"));
        var result = await installer.InstallAsync(AdapterPackageSource.FromFile(path));
        Assert.That(result.IsSuccess, Is.True, result.Message);
        return result.Value!.Manifest;
    }

    private static string AdapterJson(string id, string name) => """
        {
          "schemaVersion": 1,
          "id": "§ID§",
          "name": "§NAME§",
          "match": { "ckanIdentifiers": ["§ID§"] },
          "files": [ { "path": "GameData/§ID§/settings.cfg", "layout": "flat", "mode": "editable", "id": "settings" } ]
        }
        """.Replace("§ID§", id, StringComparison.Ordinal).Replace("§NAME§", name, StringComparison.Ordinal);

    private string CreateZip(string fileName, params (string Name, byte[] Content)[] entries)
    {
        var path = Path.Combine(_temp.Path, fileName);
        using var stream = File.Create(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        foreach (var entry in entries)
        {
            var zipEntry = archive.CreateEntry(entry.Name, CompressionLevel.NoCompression);
            using var entryStream = zipEntry.Open();
            entryStream.Write(entry.Content, 0, entry.Content.Length);
        }

        return path;
    }

    private static (string Name, byte[] Content) Text(string name, string content)
        => (name, Encoding.UTF8.GetBytes(content));

    private static (string Name, byte[] Content) Filler(string name, int count)
    {
        var bytes = new byte[count];
        Array.Fill(bytes, (byte)'A');
        return (name, bytes);
    }

    private static FakeFetcher FetcherReturning(byte[]? payload, string? etag, bool notModified = false) => new()
    {
        Handler = (_, _) => Task.FromResult(OperationResult<AdapterHttpResponse>.Ok(
            notModified
                ? AdapterHttpResponse.Unchanged(etag)
                : new AdapterHttpResponse(payload, etag, false, payload?.Length ?? 0))),
    };

    /// <summary>假取件端口：记录调用参数，行为由用例注入。</summary>
    private sealed class FakeFetcher : IAdapterPackageFetcher
    {
        public Func<Uri, string?, Task<OperationResult<AdapterHttpResponse>>> Handler { get; set; } = null!;

        public int Calls { get; private set; }

        public Uri? LastUri { get; private set; }

        public string? LastEtag { get; private set; }

        public Task<OperationResult<AdapterHttpResponse>> GetAsync(Uri uri, string? etag = null, CancellationToken cancellationToken = default)
        {
            Calls++;
            LastUri = uri;
            LastEtag = etag;
            return Handler(uri, etag);
        }
    }

    /// <summary>假 HttpMessageHandler：走真实 HttpClient 管线，但一个字节都不出网。</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        public string? LastIfNoneMatch { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastIfNoneMatch = request.Headers.TryGetValues("If-None-Match", out var values)
                ? string.Join(",", values)
                : null;
            return Task.FromResult(_responder(request));
        }
    }
}
