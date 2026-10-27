using System.IO.Compression;
using System.Text;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Services.Content;
using CkanUI.TestSupport;
using NUnit.Framework;

namespace CkanUI.Services.Tests;

[TestFixture]
public sealed class ContentServiceTests
{
    private sealed class FakeZipStore : IContentZipStore
    {
        public Dictionary<string, string> CachedPaths { get; } = new(StringComparer.Ordinal);
        public HashSet<string> CachedIdentifiers { get; } = new(StringComparer.Ordinal);
        public Func<string, Task<OperationResult<string>>>? EnsureHandler { get; set; }

        public bool IsCached(string identifier) => CachedIdentifiers.Contains(identifier);

        public string? GetCachedPath(string identifier) =>
            CachedPaths.TryGetValue(identifier, out var path) ? path : null;

        public Task<OperationResult<string>> EnsureCachedAsync(
            string identifier,
            IProgress<OperationProgress>? progress,
            CancellationToken ct)
        {
            if (EnsureHandler is not null)
            {
                return EnsureHandler(identifier);
            }

            if (CachedIdentifiers.Contains(identifier) && CachedPaths.TryGetValue(identifier, out var path))
            {
                return Task.FromResult(OperationResult<string>.Ok(path));
            }

            return Task.FromResult(OperationResult<string>.Fail("未缓存。"));
        }
    }

    [Test]
    public async Task PreviewAsync_未指定标识符_返回失败()
    {
        var store = new FakeZipStore();
        var service = new ContentService(store);

        var result = await service.PreviewAsync("");

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("未指定模组标识符"));
    }

    [Test]
    public async Task PreviewAsync_未缓存ZIP_返回中文提示()
    {
        var store = new FakeZipStore();
        var service = new ContentService(store);

        var result = await service.PreviewAsync("ModA");

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("尚未下载到缓存"));
    }

    [Test]
    public async Task PreviewAsync_空ZIP_返回成功且条目为空()
    {
        using var workspace = new TempWorkspace("content-empty-zip");
        var zipPath = workspace.Combine("empty.zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
        }

        var store = new FakeZipStore();
        store.CachedIdentifiers.Add("EmptyMod");
        store.CachedPaths["EmptyMod"] = zipPath;

        var service = new ContentService(store);
        var result = await service.PreviewAsync("EmptyMod");

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value, Is.Not.Null);
        Assert.That(result.Value!.TotalBytes, Is.EqualTo(0));
        Assert.That(result.Value.Entries, Is.Empty);
    }

    [Test]
    public async Task PreviewAsync_嵌套目录_正确合成隐式目录与深度()
    {
        using var workspace = new TempWorkspace("content-nested-zip");
        var zipPath = workspace.Combine("nested.zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("GameData/MyMod/Plugins/mod.dll");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("12345678");
        }

        var store = new FakeZipStore();
        store.CachedIdentifiers.Add("NestedMod");
        store.CachedPaths["NestedMod"] = zipPath;

        var service = new ContentService(store);
        var result = await service.PreviewAsync("NestedMod");

        Assert.That(result.IsSuccess, Is.True);
        var entries = result.Value!.Entries;
        Assert.That(entries.Select(e => e.RelativePath), Is.EquivalentTo(new[]
        {
            "GameData",
            "GameData/MyMod",
            "GameData/MyMod/Plugins",
            "GameData/MyMod/Plugins/mod.dll"
        }));

        var file = entries.First(e => e.RelativePath == "GameData/MyMod/Plugins/mod.dll");
        Assert.That(file.IsDirectory, Is.False);
        Assert.That(file.SizeBytes, Is.EqualTo(8));

        var dir = entries.First(e => e.RelativePath == "GameData/MyMod");
        Assert.That(dir.IsDirectory, Is.True);
        Assert.That(dir.SizeBytes, Is.EqualTo(0));
    }

    [Test]
    public async Task PreviewAsync_文件名含空格与非ASCII_正确解析()
    {
        using var workspace = new TempWorkspace("content-unicode-zip");
        var zipPath = workspace.Combine("unicode.zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create, Encoding.UTF8))
        {
            var entry = archive.CreateEntry("GameData/中文 模组/配置文件 说明.txt");
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write("测试文本");
        }

        var store = new FakeZipStore();
        store.CachedIdentifiers.Add("UnicodeMod");
        store.CachedPaths["UnicodeMod"] = zipPath;

        var service = new ContentService(store);
        var result = await service.PreviewAsync("UnicodeMod");

        Assert.That(result.IsSuccess, Is.True);
        var entries = result.Value!.Entries;
        Assert.That(entries.Any(e => e.RelativePath == "GameData/中文 模组/配置文件 说明.txt"), Is.True);
    }

    [Test]
    public async Task PreviewAsync_根级ckan文件_正确识别()
    {
        using var workspace = new TempWorkspace("content-ckan-zip");
        var zipPath = workspace.Combine("ckan.zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("module.ckan");
            using var writer = new StreamWriter(entry.Open());
            writer.Write(@"{ ""identifier"": ""test"" }");
        }

        var store = new FakeZipStore();
        store.CachedIdentifiers.Add("CkanMod");
        store.CachedPaths["CkanMod"] = zipPath;

        var service = new ContentService(store);
        var result = await service.PreviewAsync("CkanMod");

        Assert.That(result.IsSuccess, Is.True);
        var ckanEntry = result.Value!.Entries.FirstOrDefault(e => e.RelativePath == "module.ckan");
        Assert.That(ckanEntry, Is.Not.Null);
        Assert.That(ckanEntry!.IsDirectory, Is.False);
        Assert.That(ckanEntry.Depth, Is.EqualTo(0));
    }

    [Test]
    public async Task PreviewAsync_损坏ZIP_返回中文错误不暴露异常文本()
    {
        using var workspace = new TempWorkspace("content-corrupt-zip");
        var zipPath = workspace.Combine("corrupt.zip");
        await File.WriteAllBytesAsync(zipPath, new byte[] { 0x50, 0x4B, 0x00, 0x00, 0x12, 0x34 });

        var store = new FakeZipStore();
        store.CachedIdentifiers.Add("CorruptMod");
        store.CachedPaths["CorruptMod"] = zipPath;

        var service = new ContentService(store);
        var result = await service.PreviewAsync("CorruptMod");

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("损坏或无法读取"));
    }

    [Test]
    public async Task PreviewAsync_条目大小汇总_准确计算未压缩字节总数()
    {
        using var workspace = new TempWorkspace("content-size-zip");
        var zipPath = workspace.Combine("sizes.zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var e1 = archive.CreateEntry("a.txt");
            using (var w = new StreamWriter(e1.Open())) w.Write(new string('x', 100));

            var e2 = archive.CreateEntry("folder/b.txt");
            using (var w = new StreamWriter(e2.Open())) w.Write(new string('y', 250));
        }

        var store = new FakeZipStore();
        store.CachedIdentifiers.Add("SizesMod");
        store.CachedPaths["SizesMod"] = zipPath;

        var service = new ContentService(store);
        var result = await service.PreviewAsync("SizesMod");

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.TotalBytes, Is.EqualTo(350));
    }

    [Test]
    public async Task PreviewAsync_操作取消_返回已取消结果()
    {
        using var workspace = new TempWorkspace("content-cancel-zip");
        var zipPath = workspace.Combine("cancel.zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            archive.CreateEntry("file.txt");
        }

        var store = new FakeZipStore();
        store.CachedIdentifiers.Add("CancelMod");
        store.CachedPaths["CancelMod"] = zipPath;

        var service = new ContentService(store);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await service.PreviewAsync("CancelMod", cts.Token);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("已取消"));
    }

    [Test]
    public async Task EnsureCachedAsync_下载失败路径_返回失败结果()
    {
        var store = new FakeZipStore
        {
            EnsureHandler = id => Task.FromResult(OperationResult<string>.Fail("远程主机连接超时。", "HTTP 504", true))
        };

        var service = new ContentService(store);
        var result = await service.EnsureCachedAsync("FailMod");

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("远程主机连接超时"));
        Assert.That(result.Retryable, Is.True);
    }

    [Test]
    public async Task PreviewAsync_Zip包含父级路径遍历_返回安全拦截()
    {
        using var workspace = new TempWorkspace("content-traversal-zip");
        var zipPath = workspace.Combine("traversal.zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            archive.CreateEntry("GameData/../../secret.txt");
        }

        var store = new FakeZipStore();
        store.CachedIdentifiers.Add("TraversalMod");
        store.CachedPaths["TraversalMod"] = zipPath;

        var service = new ContentService(store);
        var result = await service.PreviewAsync("TraversalMod");

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("无效的路径，无法安全显示"));
    }
}
