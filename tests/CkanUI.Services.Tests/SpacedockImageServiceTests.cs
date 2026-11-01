using System.Net;
using System.Text;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Spacedock;
using NUnit.Framework;

namespace CkanUI.Services.Tests;

[TestFixture]
public sealed class SpacedockImageServiceTests
{
    private sealed class DelegatingHandlerStub : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> Handler { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.NotFound);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(Handler(request));
        }
    }

    [Test]
    public async Task FetchScreenshotAsync_EmptyUrl_ReturnsFail()
    {
        using var service = new SpacedockImageService();
        var result = await service.FetchScreenshotAsync(string.Empty);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("为空"));
    }

    [Test]
    public async Task FetchScreenshotAsync_InvalidUrl_ReturnsFail()
    {
        using var service = new SpacedockImageService();
        var result = await service.FetchScreenshotAsync("https://spacedock.info/user/someuser");

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("无法从 URL 解析"));
    }

    [Test]
    public async Task FetchScreenshotAsync_ApiReturnsHttpError_ReturnsFail()
    {
        var stub = new DelegatingHandlerStub
        {
            Handler = req => new HttpResponseMessage(HttpStatusCode.NotFound)
        };

        using var service = new SpacedockImageService(stub);
        var result = await service.FetchScreenshotAsync("https://spacedock.info/mod/999/NoSuchMod");

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("404"));
    }

    [Test]
    public async Task FetchScreenshotAsync_ApiReturnsNoBackgroundOrThumbnail_ReturnsFail()
    {
        var stub = new DelegatingHandlerStub
        {
            Handler = req =>
            {
                if (req.RequestUri?.AbsolutePath.Contains("/api/mod/123") == true)
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("""{"id":123,"name":"TestMod"}""", Encoding.UTF8, "application/json")
                    };
                }
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
        };

        using var service = new SpacedockImageService(stub);
        var result = await service.FetchScreenshotAsync("https://spacedock.info/mod/123/TestMod");

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("未提供背景图"));
    }

    [Test]
    public async Task FetchScreenshotAsync_Success_ReturnsBytesAndUsesCache()
    {
        var expectedBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }; // PNG header magic
        int apiCallCount = 0;
        int imgCallCount = 0;

        var stub = new DelegatingHandlerStub
        {
            Handler = req =>
            {
                var url = req.RequestUri?.ToString() ?? string.Empty;
                if (url.Contains("/api/mod/314"))
                {
                    apiCallCount++;
                    var json = """{"id":314,"name":"PiMod","background":"/static/backgrounds/pimod.png"}""";
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(json, Encoding.UTF8, "application/json")
                    };
                }
                if (url.Contains("/static/backgrounds/pimod.png"))
                {
                    imgCallCount++;
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(expectedBytes)
                    };
                }
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
        };

        using var service = new SpacedockImageService(stub);
        var result1 = await service.FetchScreenshotAsync("https://spacedock.info/mod/314/PiMod");

        Assert.That(result1.IsSuccess, Is.True);
        Assert.That(result1.Value, Is.EqualTo(expectedBytes));
        Assert.That(apiCallCount, Is.EqualTo(1));
        Assert.That(imgCallCount, Is.EqualTo(1));

        // 第二次获取应该命中内存缓存，不再发送 HTTP 请求
        var result2 = await service.FetchScreenshotAsync("https://spacedock.info/mod/314/PiMod");
        Assert.That(result2.IsSuccess, Is.True);
        Assert.That(result2.Value, Is.EqualTo(expectedBytes));
        Assert.That(apiCallCount, Is.EqualTo(1));
        Assert.That(imgCallCount, Is.EqualTo(1));
    }
}
