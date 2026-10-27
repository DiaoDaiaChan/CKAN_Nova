using System;
using System.Net.Http;
using System.Threading.Tasks;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Services.Update;
using NUnit.Framework;

namespace CkanUI.Services.Tests;

[TestFixture]
public sealed class UpdateCheckServiceTests
{
    private static string MakeReleaseJson(string ver) => $$"""
    {
      "tag_name": "v{{ver}}",
      "html_url": "https://github.com/KSP-CKAN/CKAN/releases/tag/v{{ver}}",
      "body": "Test release notes for {{ver}}"
    }
    """;

    private static string MakeDevBuildJson(string ver) => $$"""
    {
      "version": "{{ver}}",
      "build": "https://ksp-ckan.s3-us-west-2.amazonaws.com/ckan-{{ver}}.zip"
    }
    """;

    [Test]
    public async Task CheckForUpdatesAsync_WhenNewerStableReleaseExists_ReturnsHasUpdateTrue()
    {
        var json = MakeReleaseJson("2.0.0");
        var service = new UpdateCheckService(
            settingsService: null,
            httpGetHandler: (url, ct) => Task.FromResult(json),
            currentVersionString: "v1.31.2");

        var result = await service.CheckForUpdatesAsync(includeDevBuilds: false);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value, Is.Not.Null);
        var update = result.Value!;
        Assert.That(update.HasUpdate, Is.True);
        Assert.That(update.CurrentVersion, Is.EqualTo("v1.31.2"));
        Assert.That(update.LatestVersion, Is.EqualTo("v2.0.0"));
        Assert.That(update.ReleaseNotes, Does.Contain("Test release notes for 2.0.0"));
        Assert.That(update.ReleaseUrl, Does.Contain("v2.0.0"));
        Assert.That(update.IsDevBuild, Is.False);
    }

    [Test]
    public async Task CheckForUpdatesAsync_WhenCurrentVersionIsLatest_ReturnsHasUpdateFalse()
    {
        var json = MakeReleaseJson("1.31.2");
        var service = new UpdateCheckService(
            settingsService: null,
            httpGetHandler: (url, ct) => Task.FromResult(json),
            currentVersionString: "v1.31.2");

        var result = await service.CheckForUpdatesAsync(includeDevBuilds: false);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value, Is.Not.Null);
        var update = result.Value!;
        Assert.That(update.HasUpdate, Is.False);
        Assert.That(update.CurrentVersion, Is.EqualTo("v1.31.2"));
        Assert.That(update.LatestVersion, Is.EqualTo("v1.31.2"));
        Assert.That(update.IsDevBuild, Is.False);
    }

    [Test]
    public async Task CheckForUpdatesAsync_WhenCurrentVersionIsNewerThanRelease_ReturnsHasUpdateFalse()
    {
        var json = MakeReleaseJson("1.30.0");
        var service = new UpdateCheckService(
            settingsService: null,
            httpGetHandler: (url, ct) => Task.FromResult(json),
            currentVersionString: "v1.31.2");

        var result = await service.CheckForUpdatesAsync(includeDevBuilds: false);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value, Is.Not.Null);
        Assert.That(result.Value!.HasUpdate, Is.False);
    }

    [Test]
    public async Task CheckForUpdatesAsync_WithDevBuildsEnabled_WhenNewerDevBuildExists_ReturnsDevBuildUpdate()
    {
        var devJson = MakeDevBuildJson("v1.32.0-dev");
        var stableJson = MakeReleaseJson("1.31.2");

        var service = new UpdateCheckService(
            settingsService: null,
            httpGetHandler: (url, ct) =>
            {
                if (url.Contains("amazon"))
                {
                    return Task.FromResult(devJson);
                }
                return Task.FromResult(stableJson);
            },
            currentVersionString: "v1.31.2");

        var result = await service.CheckForUpdatesAsync(includeDevBuilds: true);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value, Is.Not.Null);
        var update = result.Value!;
        Assert.That(update.HasUpdate, Is.True);
        Assert.That(update.IsDevBuild, Is.True);
        Assert.That(update.LatestVersion, Is.EqualTo("v1.32.0-dev"));
        Assert.That(update.ReleaseUrl, Does.Contain("amazon"));
    }

    [Test]
    public async Task CheckForUpdatesAsync_WithDevBuildsEnabled_WhenDevBuildMatchesCurrent_ReturnsHasUpdateFalse()
    {
        var devJson = MakeDevBuildJson("v1.31.2");
        var stableJson = MakeReleaseJson("1.31.2");

        var service = new UpdateCheckService(
            settingsService: null,
            httpGetHandler: (url, ct) =>
            {
                if (url.Contains("amazon"))
                {
                    return Task.FromResult(devJson);
                }
                return Task.FromResult(stableJson);
            },
            currentVersionString: "v1.31.2");

        var result = await service.CheckForUpdatesAsync(includeDevBuilds: true);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value, Is.Not.Null);
        Assert.That(result.Value!.HasUpdate, Is.False);
    }

    [Test]
    public async Task CheckForUpdatesAsync_WhenNetworkFails_ReturnsFailureWithoutThrowing()
    {
        var service = new UpdateCheckService(
            settingsService: null,
            httpGetHandler: (url, ct) => throw new HttpRequestException("Connection refused"),
            currentVersionString: "v1.31.2");

        var result = await service.CheckForUpdatesAsync(includeDevBuilds: false);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Value, Is.Null);
        Assert.That(result.Message, Does.Contain("网络错误") | Does.Contain("失败"));
    }

    [Test]
    public async Task CheckForUpdatesAsync_WhenJsonIsInvalid_ReturnsFailureWithoutThrowing()
    {
        var service = new UpdateCheckService(
            settingsService: null,
            httpGetHandler: (url, ct) => Task.FromResult("invalid json response"),
            currentVersionString: "v1.31.2");

        var result = await service.CheckForUpdatesAsync(includeDevBuilds: false);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Value, Is.Null);
    }
}
