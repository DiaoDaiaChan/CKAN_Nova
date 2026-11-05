using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CkanUI.Plugin.Ai.Search;
using NUnit.Framework;

namespace CkanUI.Plugin.Ai.Tests;

[TestFixture]
public class SearchClientTests
{
    [Test]
    public void TryParseMarkdownHits_ParsesNumberedLinksAndSnippets()
    {
        var md = @"
1. [Scatterer atmospheric scattering](https://spacedock.info/mod/141/scatterer)
   Scatterer brings realistic atmospheric and water scattering effects to Kerbal Space Program.
2. [Environmental Visual Enhancements](https://forum.kerbalspaceprogram.com/topic/123-eve)
   Adds clouds and auroras to planets in KSP.
";
        var hits = McpSearchClient.TryParseMarkdownHits(md);
        Assert.That(hits, Is.Not.Null);
        Assert.That(hits, Has.Count.EqualTo(2));
        Assert.That(hits[0].Title, Is.EqualTo("Scatterer atmospheric scattering"));
        Assert.That(hits[0].Url, Is.EqualTo("https://spacedock.info/mod/141/scatterer"));
        Assert.That(hits[0].Snippet, Does.Contain("Scatterer brings realistic atmospheric"));
        Assert.That(hits[1].Title, Is.EqualTo("Environmental Visual Enhancements"));
    }

    [Test]
    public async Task WebSearchFallbackClient_FallsBackGracefully()
    {
        var handler = new DelegatingHandlerStub(req =>
        {
            if (req.RequestUri?.Host == "spacedock.info")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("[{\"id\":141,\"name\":\"Scatterer\",\"short_description\":\"Atmospheric scattering mod for KSP\",\"author\":\"blackrack\"}]")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var httpClient = new HttpClient(handler);
        var client = new WebSearchFallbackClient(httpClient);

        var result = await client.SearchAsync("Scatterer", CancellationToken.None);
        Assert.That(result.Hits, Is.Not.Empty);
        Assert.That(result.Hits[0].Title, Does.Contain("Scatterer"));
        Assert.That(result.Hits[0].Url, Does.Contain("spacedock.info/mod/141"));
    }

    private sealed class DelegatingHandlerStub : HttpMessageHandler
    {
        private readonly System.Func<HttpRequestMessage, HttpResponseMessage> _fn;
        public DelegatingHandlerStub(System.Func<HttpRequestMessage, HttpResponseMessage> fn) => _fn = fn;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_fn(request));
    }
}