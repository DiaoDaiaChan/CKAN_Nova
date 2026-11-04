using System.Net;

namespace CkanUI.Plugin.ModPackShare.Tests;

/// <summary>
/// 假 HTTP 处理器：用例给它一个「请求 → 响应」的函数，全程不连网。
/// 记录每次请求的地址，用来断言 owner/repo 未给 ref 时确实按 main → master 依次尝试。
/// </summary>
internal sealed class StubHttpHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

    public StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        _respond = respond;
    }

    /// <summary>收到的全部请求地址（按顺序）。</summary>
    public List<Uri> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Requests.Add(request.RequestUri!);
        return Task.FromResult(_respond(request));
    }
}

/// <summary>
/// 长度未知的响应体：声明不了 Content-Length，只能边读边计数。
/// 用来钉「流式读取也要停在 8 MiB」这条边界——只看响应头是挡不住分块传输的。
/// </summary>
internal sealed class UnknownLengthContent : HttpContent
{
    private readonly long _bytes;

    public UnknownLengthContent(long bytes)
    {
        _bytes = bytes;
    }

    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
    {
        var chunk = new byte[81920];
        Array.Fill(chunk, (byte)'{');
        var remaining = _bytes;
        while (remaining > 0)
        {
            var count = (int)Math.Min(chunk.Length, remaining);
            await stream.WriteAsync(chunk.AsMemory(0, count)).ConfigureAwait(false);
            remaining -= count;
        }
    }

    protected override bool TryComputeLength(out long length)
    {
        // 明确告诉 HttpClient 长度未知，逼它走流式计数路径。
        length = 0;
        return false;
    }
}
