using System.Net;
using System.Text;
using CkanUI.Plugin.ModPackShare.Sources;
using CkanUI.TestSupport;

namespace CkanUI.Plugin.ModPackShare.Tests;

/// <summary>
/// 来源解析与下载边界。
/// 解析是纯函数，必须把 GitHub 的四种链接形态与 owner/repo 简写都钉死；
/// 下载只允许把「看起来像 .ckan 的 JSON」写进缓存目录，任何一步不合规都要给出中文原因而不是异常。
/// </summary>
[TestFixture]
internal sealed class GitHubModPackSourceTests
{
    [Test]
    public void Parse_GitHubBlobLink_RewritesToRawHost()
    {
        var parsed = GitHubModPackSource.Parse("https://github.com/kOS-dev/ckan-modpack/blob/main/packs/rp1.ckan");

        Assert.That(parsed.IsSuccess, Is.True, parsed.Message);
        var source = parsed.Value!;
        Assert.Multiple(() =>
        {
            Assert.That(source.IsLocal, Is.False);
            Assert.That(source.DisplayName, Is.EqualTo("rp1"));
            Assert.That(source.CandidateUrls, Has.Count.EqualTo(1));
            Assert.That(
                source.CandidateUrls[0].ToString(),
                Is.EqualTo("https://raw.githubusercontent.com/kOS-dev/ckan-modpack/main/packs/rp1.ckan"));
        });
    }

    [Test]
    public void Parse_GitHubRawSegment_AlsoRewritesToRawHost()
    {
        var parsed = GitHubModPackSource.Parse(
            "https://github.com/kOS-dev/ckan-modpack/raw/dev/packs/rp1.ckan");

        Assert.That(parsed.IsSuccess, Is.True, parsed.Message);
        Assert.That(
            parsed.Value!.CandidateUrls[0].ToString(),
            Is.EqualTo("https://raw.githubusercontent.com/kOS-dev/ckan-modpack/dev/packs/rp1.ckan"));
    }

    [Test]
    public void Parse_RawGitHubHost_KeepsUrlAsIs()
    {
        const string url = "https://raw.githubusercontent.com/kOS-dev/ckan-modpack/master/rp1.ckan";
        var parsed = GitHubModPackSource.Parse(url);

        Assert.That(parsed.IsSuccess, Is.True, parsed.Message);
        Assert.That(parsed.Value!.IsGitHub, Is.True);
        Assert.That(parsed.Value.CandidateUrls[0].ToString(), Is.EqualTo(url));
    }

    [Test]
    public void Parse_ShorthandWithRef_UsesOnlyThatRef()
    {
        var parsed = GitHubModPackSource.Parse("kOS-dev/ckan-modpack/packs/rp1.ckan@dev");

        Assert.That(parsed.IsSuccess, Is.True, parsed.Message);
        var source = parsed.Value!;
        Assert.Multiple(() =>
        {
            Assert.That(source.CandidateUrls, Has.Count.EqualTo(1));
            Assert.That(
                source.CandidateUrls[0].ToString(),
                Is.EqualTo("https://raw.githubusercontent.com/kOS-dev/ckan-modpack/dev/packs/rp1.ckan"));
        });
    }

    [Test]
    public void Parse_ShorthandWithoutRef_TriesMainThenMaster()
    {
        var parsed = GitHubModPackSource.Parse("kOS-dev/ckan-modpack");

        Assert.That(parsed.IsSuccess, Is.True, parsed.Message);
        var source = parsed.Value!;
        Assert.Multiple(() =>
        {
            Assert.That(source.CandidateUrls, Has.Count.EqualTo(2));
            Assert.That(
                source.CandidateUrls[0].ToString(),
                Is.EqualTo("https://raw.githubusercontent.com/kOS-dev/ckan-modpack/main/modpack.ckan"));
            Assert.That(
                source.CandidateUrls[1].ToString(),
                Is.EqualTo("https://raw.githubusercontent.com/kOS-dev/ckan-modpack/master/modpack.ckan"));
            Assert.That(source.DisplayName, Is.EqualTo("modpack"));
        });
    }

    [Test]
    public void Parse_ExistingLocalFile_WinsOverShorthandShape()
    {
        using var temp = new TempWorkspace("modpackshare-parse");
        var path = temp.Combine("packs", "rp1.ckan");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{\"name\":\"rp1\"}");

        var parsed = GitHubModPackSource.Parse(path);

        Assert.That(parsed.IsSuccess, Is.True, parsed.Message);
        Assert.Multiple(() =>
        {
            Assert.That(parsed.Value!.IsLocal, Is.True);
            Assert.That(parsed.Value.LocalPath, Is.EqualTo(Path.GetFullPath(path)));
            Assert.That(parsed.Value.DisplayName, Is.EqualTo("rp1"));
            Assert.That(parsed.Value.CandidateUrls, Is.Empty);
        });
    }

    [Test]
    public void Parse_MissingLocalCkan_ReportsMissingFile()
    {
        var parsed = GitHubModPackSource.Parse("packs/does-not-exist.ckan");

        Assert.That(parsed.IsSuccess, Is.False);
        Assert.That(parsed.Message, Does.Contain("找不到本地文件"));
    }

    [Test]
    public void Parse_Empty_FailsWithHint()
    {
        var parsed = GitHubModPackSource.Parse("   ");

        Assert.That(parsed.IsSuccess, Is.False);
        Assert.That(parsed.Message, Does.Contain("GitHub"));
    }

    [Test]
    public void Parse_PlainHttpsFile_IsAcceptedAsDirectLink()
    {
        var parsed = GitHubModPackSource.Parse("https://example.com/packs/rp1.ckan");

        Assert.That(parsed.IsSuccess, Is.True, parsed.Message);
        Assert.That(parsed.Value!.IsGitHub, Is.False);
        Assert.That(parsed.Value.CandidateUrls, Has.Count.EqualTo(1));
    }

    [Test]
    public void Parse_GitHubRepositoryPage_FailsWithFileHint()
    {
        var parsed = GitHubModPackSource.Parse("https://github.com/kOS-dev/ckan-modpack");

        Assert.That(parsed.IsSuccess, Is.False);
        Assert.That(parsed.Message, Does.Contain("不是单个文件"));
    }

    [Test]
    public async Task Download_AllCandidates404_FailsAndWritesNothing()
    {
        using var temp = new TempWorkspace("modpackshare-download");
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        using var client = new HttpClient(handler);

        var result = await GitHubModPackSource.DownloadAsync("kOS-dev/ckan-modpack", client, temp.Combine("cache"));

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Message, Does.Contain("下载整合包失败"));
            Assert.That(handler.Requests, Has.Count.EqualTo(2), "未给 ref 时应依次尝试 main 与 master");
            Assert.That(Directory.Exists(temp.Combine("cache")), Is.False, "全部失败时不应创建缓存目录");
        });
    }

    [Test]
    public async Task Download_NonJsonBody_FailsWithChineseReason()
    {
        using var temp = new TempWorkspace("modpackshare-download");
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html>not a ckan</html>", Encoding.UTF8, "text/html"),
        });
        using var client = new HttpClient(handler);

        var result = await GitHubModPackSource.DownloadAsync(
            "https://example.com/packs/rp1.ckan", client, temp.Combine("cache"));

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Message, Does.Contain("不是 .ckan 元数据包"));
            Assert.That(Directory.Exists(temp.Combine("cache")), Is.False);
        });
    }

    [Test]
    public async Task Download_DeclaredContentLengthOverLimit_FailsBeforeReading()
    {
        using var temp = new TempWorkspace("modpackshare-download");
        var handler = new StubHttpHandler(_ =>
        {
            var content = new ByteArrayContent([(byte)'{', (byte)'}']);
            content.Headers.ContentLength = GitHubModPackSource.MaxBytes + 1;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        });
        using var client = new HttpClient(handler);

        var result = await GitHubModPackSource.DownloadAsync(
            "https://example.com/packs/rp1.ckan", client, temp.Combine("cache"));

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("8 MiB"));
    }

    [Test]
    public async Task Download_UnknownLengthStreamOverLimit_FailsWhileCounting()
    {
        using var temp = new TempWorkspace("modpackshare-download");
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new UnknownLengthContent(GitHubModPackSource.MaxBytes + 4096),
        });
        using var client = new HttpClient(handler);

        var result = await GitHubModPackSource.DownloadAsync(
            "https://example.com/packs/rp1.ckan", client, temp.Combine("cache"));

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Message, Does.Contain("8 MiB"));
            Assert.That(Directory.Exists(temp.Combine("cache")), Is.False);
        });
    }

    [Test]
    public async Task Download_LocalFile_IsCopiedIntoCacheByContentHash()
    {
        using var temp = new TempWorkspace("modpackshare-download");
        var path = temp.Combine("rp1.ckan");
        await File.WriteAllTextAsync(path, "{\"name\":\"rp1\"}");
        using var client = new HttpClient(new StubHttpHandler(_ => throw new InvalidOperationException("本地来源不应联网")));

        var result = await GitHubModPackSource.DownloadAsync(path, client, temp.Combine("cache"));

        Assert.That(result.IsSuccess, Is.True, result.Message);
        var downloaded = result.Value!;
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(downloaded.FilePath), Is.True);
            Assert.That(Path.GetFileName(downloaded.FilePath), Has.Length.EqualTo(21), "16 位哈希 + .ckan（5 字符）");
            Assert.That(
                Path.GetFileNameWithoutExtension(downloaded.FilePath),
                Does.Match("^[0-9a-f]{16}$"),
                "缓存名应当是内容哈希前 16 位，同一份内容重复下载不会堆文件");
            Assert.That(Path.GetExtension(downloaded.FilePath), Is.EqualTo(".ckan"));
            Assert.That(downloaded.Bytes, Is.EqualTo(new FileInfo(path).Length));
            Assert.That(downloaded.SourceUrl, Is.EqualTo(Path.GetFullPath(path)));
            Assert.That(downloaded.DisplayName, Is.EqualTo("rp1"));
        });
    }
}
