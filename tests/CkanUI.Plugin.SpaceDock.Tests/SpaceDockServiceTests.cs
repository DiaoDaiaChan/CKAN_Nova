using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Services.Spacedock;
using NUnit.Framework;

namespace CkanUI.Plugin.SpaceDock.Tests;

[TestFixture]
public sealed class SpaceDockServiceTests
{
    [Test]
    public async Task BrowseAsync_ParsesPageAndAbsoluteizesUrls()
    {
        const string json = """
            {
              "total": 3473,
              "count": 1,
              "pages": 116,
              "page": 2,
              "result": [
                {
                  "id": 21,
                  "name": "BetterBurnTime",
                  "author": "TestAuthor",
                  "short_description": "A short description",
                  "downloads": 1234,
                  "followers": 5,
                  "license": "MIT",
                  "background": "/static/backgrounds/burn.png",
                  "website": "https://example.test",
                  "source_code": "/mod/21/source",
                  "url": "/mod/21/BetterBurnTime",
                  "versions": [
                    {
                      "friendly_version": "1.10",
                      "game_version": "1.12",
                      "download_path": "/mod/21/BetterBurnTime/download/1.10",
                      "changelog": "Fixed things",
                      "downloads": 10,
                      "created": "2025-01-02T03:04:05+00:00"
                    }
                  ]
                }
              ]
            }
            """;
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, json));
        using var service = new SpaceDockService(handler);

        var result = await service.BrowseAsync(3102, 2, 30, SpaceDockSortOrder.Updated, ascending: false);

        Assert.That(result.IsSuccess, Is.True, result.Message);
        var page = result.Value!;
        Assert.That(page.Total, Is.EqualTo(3473));
        Assert.That(page.Page, Is.EqualTo(2));
        Assert.That(page.Pages, Is.EqualTo(116));
        Assert.That(page.Mods, Has.Count.EqualTo(1));

        var mod = page.Mods[0];
        Assert.That(mod.Id, Is.EqualTo(21));
        Assert.That(mod.Name, Is.EqualTo("BetterBurnTime"));
        Assert.That(mod.Url, Is.EqualTo("https://spacedock.info/mod/21/BetterBurnTime"));
        Assert.That(mod.BackgroundUrl, Is.EqualTo("https://spacedock.info/static/backgrounds/burn.png"));
        Assert.That(mod.Website, Is.EqualTo("https://example.test"));
        Assert.That(mod.SourceCode, Is.EqualTo("https://spacedock.info/mod/21/source"));
        Assert.That(mod.LatestVersion!.DownloadUrl, Is.EqualTo("https://spacedock.info/mod/21/BetterBurnTime/download/1.10"));
        Assert.That(mod.LatestVersion.GameVersion, Is.EqualTo("1.12"));
        Assert.That(mod.LatestVersion.Created, Is.Not.Null);

        Assert.That(handler.Requests.Single(), Is.EqualTo(
            "https://spacedock.info/api/browse?game_id=3102&page=2&count=30&orderby=updated&order=desc"));
    }

    [Test]
    public async Task SearchAsync_ParsesBareArray()
    {
        const string json = """
            [
              {
                "id": 7,
                "name": "Search Result",
                "author": "Author",
                "short_description": "Found by search",
                "downloads": 3,
                "followers": 1,
                "url": "/mod/7/SearchResult",
                "versions": []
              }
            ]
            """;
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, json));
        using var service = new SpaceDockService(handler);

        var result = await service.SearchAsync("better burn");

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(result.Value, Has.Count.EqualTo(1));
        Assert.That(result.Value![0].Name, Is.EqualTo("Search Result"));
        Assert.That(handler.Requests.Single(), Is.EqualTo("https://spacedock.info/api/search/mod?query=better%20burn"));
    }

    [Test]
    public async Task GetModAsync_ReadsDescription()
    {
        const string json = """
            {
              "id": 21,
              "name": "Detail Mod",
              "author": "Author",
              "short_description": "Short",
              "description": "<p>Full description</p>",
              "downloads": 5,
              "followers": 2,
              "url": "/mod/21/DetailMod",
              "versions": []
            }
            """;
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, json));
        using var service = new SpaceDockService(handler);

        var result = await service.GetModAsync(21);

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(result.Value!.Description, Is.EqualTo("<p>Full description</p>"));
        Assert.That(handler.Requests.Single(), Is.EqualTo("https://spacedock.info/api/mod/21"));
    }

    [Test]
    public async Task GetGamesAsync_ParsesGameIdAndShortName()
    {
        const string json = """
            [
              { "id": 3102, "name": "Kerbal Space Program", "short": "kerbal-space-program" },
              { "id": 1, "name": "Other Game", "short": "other" }
            ]
            """;
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, json));
        using var service = new SpaceDockService(handler);

        var result = await service.GetGamesAsync();

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(result.Value, Has.Count.EqualTo(2));
        Assert.That(result.Value![0].Id, Is.EqualTo(3102));
        Assert.That(result.Value[0].Short, Is.EqualTo("kerbal-space-program"));
        Assert.That(handler.Requests.Single(), Is.EqualTo("https://spacedock.info/api/games"));
    }

    [Test]
    public async Task ServerError_IsRetryableFailure()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        using var service = new SpaceDockService(handler);

        var result = await service.GetGamesAsync();

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Retryable, Is.True);
        Assert.That(result.Message, Does.Contain("HTTP 500"));
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        public List<string> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.AbsoluteUri);
            return Task.FromResult(_responder(request));
        }
    }
}
