using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Services.Spacedock;
using CkanUI.TestSupport;
using NUnit.Framework;

namespace CkanUI.Plugin.SpaceDock.Tests;

[TestFixture]
public sealed class SpaceDockInstallerTests
{
    [Test]
    public async Task InstallAsync_ExtractsOnlyGameDataSubtree()
    {
        using var workspace = new TempWorkspace("spacedock-gamedata");
        var game = workspace.Combine("game");
        Directory.CreateDirectory(game);
        var zip = workspace.Combine("mod.zip");
        CreateZip(
            zip,
            ("Docs/README.txt", "readme"),
            ("Wrapper/GameData/MyMod/plugin.dll", "binary"),
            ("Wrapper/GameData/MyMod/Textures/tex.png", "image"));

        using var installer = new SpaceDockInstaller(new BytesHandler(Array.Empty<byte>()));
        var result = await installer.InstallAsync(zip, game, "IgnoredName");

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(result.Value, Is.EqualTo(Path.Combine(game, "GameData", "MyMod")));
        Assert.That(File.Exists(Path.Combine(game, "GameData", "MyMod", "plugin.dll")), Is.True);
        Assert.That(File.Exists(Path.Combine(game, "GameData", "MyMod", "Textures", "tex.png")), Is.True);
        Assert.That(File.Exists(Path.Combine(game, "README.txt")), Is.False);
        Assert.That(Directory.Exists(Path.Combine(game, "Wrapper")), Is.False);
    }

    [Test]
    public async Task InstallAsync_WithoutGameData_WrapsUnderModNameAndStripsWrapper()
    {
        using var workspace = new TempWorkspace("spacedock-nowrap");
        var game = workspace.Combine("game");
        Directory.CreateDirectory(game);
        var zip = workspace.Combine("mod.zip");
        CreateZip(
            zip,
            ("Wrapper/plugin.dll", "binary"),
            ("Wrapper/Textures/tex.png", "image"));

        using var installer = new SpaceDockInstaller(new BytesHandler(Array.Empty<byte>()));
        var result = await installer.InstallAsync(zip, game, "Fancy Mod");

        Assert.That(result.IsSuccess, Is.True, result.Message);
        var modRoot = Path.Combine(game, "GameData", "Fancy Mod");
        Assert.That(result.Value, Is.EqualTo(modRoot));
        Assert.That(File.Exists(Path.Combine(modRoot, "plugin.dll")), Is.True);
        Assert.That(File.Exists(Path.Combine(modRoot, "Textures", "tex.png")), Is.True);
        Assert.That(Directory.Exists(Path.Combine(modRoot, "Wrapper")), Is.False);
    }

    [Test]
    public async Task InstallAsync_RejectsZipSlip()
    {
        using var workspace = new TempWorkspace("spacedock-slip");
        var game = workspace.Combine("game");
        Directory.CreateDirectory(game);
        var zip = workspace.Combine("evil.zip");
        CreateZip(zip, ("GameData/MyMod/../../evil.txt", "evil"));

        using var installer = new SpaceDockInstaller(new BytesHandler(Array.Empty<byte>()));
        var result = await installer.InstallAsync(zip, game, "MyMod");

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("不安全"));
        Assert.That(File.Exists(Path.Combine(game, "evil.txt")), Is.False);
        Assert.That(File.Exists(Path.Combine(game, "GameData", "evil.txt")), Is.False);
    }

    [Test]
    public async Task InstallAsync_RejectsAbsoluteEntry()
    {
        using var workspace = new TempWorkspace("spacedock-absolute");
        var game = workspace.Combine("game");
        Directory.CreateDirectory(game);
        var zip = workspace.Combine("absolute.zip");
        CreateZip(zip, ("/GameData/MyMod/evil.dll", "evil"));

        using var installer = new SpaceDockInstaller(new BytesHandler(Array.Empty<byte>()));
        var result = await installer.InstallAsync(zip, game, "MyMod");

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("不安全"));
    }

    [Test]
    public async Task InstallAsync_MixedLayout_WritesGameDataAndGameRoot()
    {
        using var workspace = new TempWorkspace("spacedock-mixed");
        var game = workspace.Combine("game");
        Directory.CreateDirectory(game);
        var zip = workspace.Combine("mod.zip");
        CreateZip(
            zip,
            ("Wrapper/GameData/MyMod/plugin.dll", "binary"),
            ("Wrapper/Ships/VAB/demo.craft", "craft"));

        using var installer = new SpaceDockInstaller(new BytesHandler(Array.Empty<byte>()));
        var result = await installer.InstallAsync(zip, game, "My Mod");

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(result.Value, Is.EqualTo(game));
        Assert.That(File.Exists(Path.Combine(game, "GameData", "MyMod", "plugin.dll")), Is.True);
        Assert.That(File.Exists(Path.Combine(game, "Ships", "VAB", "demo.craft")), Is.True);
        Assert.That(Directory.Exists(Path.Combine(game, "Wrapper")), Is.False);
    }

    [Test]
    public async Task InspectAsync_ReportsDetectedLayout()
    {
        using var workspace = new TempWorkspace("spacedock-inspect");
        var zip = workspace.Combine("mod.zip");
        CreateZip(
            zip,
            ("Wrapper/GameData/MyMod/plugin.dll", "binary"),
            ("Wrapper/GameData/MyMod/Textures/tex.png", "image"),
            ("Wrapper/Ships/VAB/demo.craft", "craft"));

        using var installer = new SpaceDockInstaller(new BytesHandler(Array.Empty<byte>()));
        var result = await installer.InspectAsync(zip, "My Mod");

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(result.Value, Is.Not.Null);
        Assert.That(result.Value!.Strategy, Is.EqualTo(SpaceDockInstallStrategy.MixedRoots));
        Assert.That(result.Value.InstalledRoot, Is.EqualTo("MyMod"));
        Assert.That(result.Value.FileCount, Is.EqualTo(3));
        Assert.That(result.Value.Targets, Is.EquivalentTo(new[]
        {
            SpaceDockInstallTarget.GameData,
            SpaceDockInstallTarget.GameRoot,
        }));
    }

    [Test]
    public async Task InspectAsync_UnsafeArchive_Fails()
    {
        using var workspace = new TempWorkspace("spacedock-inspect-unsafe");
        var zip = workspace.Combine("evil.zip");
        CreateZip(zip, ("GameData/MyMod/../../evil.txt", "evil"));

        using var installer = new SpaceDockInstaller(new BytesHandler(Array.Empty<byte>()));
        var result = await installer.InspectAsync(zip, "MyMod");

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("不安全"));
    }

    [Test]
    public async Task DownloadAsync_PreservesVersionAndAvoidsOverwrite()
    {
        using var workspace = new TempWorkspace("spacedock-download");
        var target = workspace.Combine("downloads");
        var bytes = new byte[] { 1, 2, 3, 4 };
        using var installer = new SpaceDockInstaller(new BytesHandler(bytes));
        var version = new SpaceDockVersionInfo(
            "1.10",
            "1.12",
            "https://spacedock.info/mod/21/BetterBurnTime/download/1.10",
            null,
            10,
            null);

        var first = await installer.DownloadAsync(version, target, "TestMod-1.0");
        var second = await installer.DownloadAsync(version, target, "TestMod-1.0");

        Assert.That(first.IsSuccess, Is.True, first.Message);
        Assert.That(Path.GetFileName(first.Value), Is.EqualTo("TestMod-1.0.zip"));
        Assert.That(File.ReadAllBytes(first.Value!), Is.EqualTo(bytes));
        Assert.That(second.IsSuccess, Is.True, second.Message);
        Assert.That(Path.GetFileName(second.Value), Is.EqualTo("TestMod-1.0 (1).zip"));
    }

    [Test]
    public async Task DownloadAsync_ServerError_IsRetryable()
    {
        using var workspace = new TempWorkspace("spacedock-download-error");
        using var installer = new SpaceDockInstaller(new StatusHandler(HttpStatusCode.InternalServerError));
        var version = new SpaceDockVersionInfo(
            "1.0",
            null,
            "https://spacedock.info/mod/21/Test/download/1.0",
            null,
            0,
            null);

        var result = await installer.DownloadAsync(version, workspace.Combine("downloads"), "Test");

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Retryable, Is.True);
        Assert.That(result.Message, Does.Contain("HTTP 500"));
    }

    private static void CreateZip(string path, params (string Name, string Content)[] entries)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, content) in entries)
        {
            var entry = archive.CreateEntry(name);
            using var writer = new StreamWriter(entry.Open());
            writer.Write(content);
        }
    }

    private sealed class BytesHandler : HttpMessageHandler
    {
        private readonly byte[] _bytes;

        public BytesHandler(byte[] bytes) => _bytes = bytes;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(_bytes) });
    }

    private sealed class StatusHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;

        public StatusHandler(HttpStatusCode status) => _status = status;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(_status));
    }
}
