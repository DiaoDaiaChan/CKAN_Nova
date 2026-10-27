using System;
using System.Collections.Generic;

namespace CkanUI.Services.Abstractions.Dtos;

/// <summary>
/// 一次单文件下载请求。
/// </summary>
/// <remarks>
/// <see cref="Urls"/> 的顺序就是镜像优先级：Core 的 `NetAsyncDownloader` 会按顺序逐个尝试，
/// 前一个失败才换下一个（`Core/Net/NetAsyncDownloader.DownloadPart.cs:74-82`），
/// 因此调用方把「首选下载主机」的排序结果直接放进来即可（12 篇 F-15）。
/// </remarks>
/// <param name="Urls">候选下载地址，按优先级排列，至少一个。</param>
/// <param name="DestinationPath">最终落盘路径（已存在的文件会被原子覆盖）。</param>
/// <param name="ExpectedSize">元数据声明的字节数；&gt;0 时下载完成后校验，不符则失败。</param>
/// <param name="ExpectedSha256">元数据声明的 SHA256 十六进制串；非空时下载完成后校验。</param>
/// <param name="MimeType">Accept 头；空串表示不限制。</param>
/// <param name="DisplayName">UI 标题/进度文案用的名字；空时用文件名。</param>
public sealed record DownloadRequest(
    IReadOnlyList<Uri> Urls,
    string DestinationPath,
    long ExpectedSize = 0,
    string? ExpectedSha256 = null,
    string MimeType = "",
    string? DisplayName = null)
{
    /// <summary>主地址（日志与错误文案用）；没有地址时为 null。</summary>
    public Uri? PrimaryUrl => Urls is { Count: > 0 } ? Urls[0] : null;

    /// <summary>UI 标题：优先用调用方给的名字，否则用文件名。</summary>
    public string Title
        => string.IsNullOrWhiteSpace(DisplayName)
            ? System.IO.Path.GetFileName(DestinationPath)
            : DisplayName!;
}

/// <summary>
/// 一次成功下载的结果清单（失败统一走 <see cref="OperationResult{T}"/> 的 message/detail）。
/// </summary>
/// <param name="DestinationPath">最终落盘的绝对路径。</param>
/// <param name="SourceUrl">实际使用的主地址（多镜像时不一定等于最后一个成功的地址）。</param>
/// <param name="BytesReceived">落盘字节数（= 文件长度）。</param>
/// <param name="TotalBytes">元数据声明的总字节数；未声明时等于 <paramref name="BytesReceived"/>。</param>
/// <param name="Sha256">落盘文件的 SHA256 十六进制串（大写，无分隔符）；计算失败时为 null。</param>
/// <param name="ETag">服务器返回的 ETag（已去引号）；没有时为 null。</param>
/// <param name="Elapsed">从开始到落盘的耗时。</param>
public sealed record DownloadTaskInfo(
    string DestinationPath,
    Uri? SourceUrl,
    long BytesReceived,
    long TotalBytes,
    string? Sha256,
    string? ETag,
    TimeSpan Elapsed)
{
    /// <summary>文件大小是否与元数据一致（未声明大小时视为一致）。</summary>
    public bool SizeMatches => TotalBytes <= 0 || BytesReceived == TotalBytes;
}
