using System.Globalization;
using System.Security.Cryptography;

namespace CkanUI.Services.Abstractions.ModConfig;

/// <summary>adapter 包的来源形态（ADP-04：导入第三方 adapter）。</summary>
public enum AdapterPackageSourceKind
{
    /// <summary>本地文件：单个 adapter.json，或内部含 adapter.json 的 .zip 包。</summary>
    LocalFile = 0,

    /// <summary>http(s) 地址：与本地文件同样的两种容器，内容先经 <see cref="IAdapterPackageFetcher"/> 取回。</summary>
    RemoteUrl = 1,
}

/// <summary>adapter 包的容器形态（按内容判定，不只看扩展名——扩展名写错的文件在真实场景里并不罕见）。</summary>
public enum AdapterPackageFormat
{
    /// <summary>纯 adapter.json。</summary>
    Json = 0,

    /// <summary>zip 包：内部有唯一的 adapter.json，以及可选的模板/说明文件。</summary>
    Zip = 1,
}

/// <summary>更新检查的三种结论（ADP-04）。</summary>
public enum AdapterUpdateStatus
{
    /// <summary>服务器回 304：按 ETag 判定远端未变，没有取回内容。</summary>
    NotModified = 0,

    /// <summary>远端返回了内容，但 sha256 与已装一致：没有更新。</summary>
    UpToDate = 1,

    /// <summary>远端 sha256 与已装不同：有更新。</summary>
    UpdateAvailable = 2,
}

/// <summary>
/// adapter 包来源：本地文件路径 <b>或</b> https 地址，二选一。
/// </summary>
/// <remarks>
/// 刻意用工厂方法而不是公开构造函数：来源里"路径"和"URL"共用一个字符串字段，
/// 让调用方直接 new 很容易造出 Kind 与内容不一致的怪对象（比如 Kind=RemoteUrl 却传了个盘符路径）。
/// <para>
/// <see cref="EntryPoint"/> 只在 zip 包里有意义：包内多个目录都可能带 adapter.json 时，
/// 用它显式指定入口（相对 zip 根，/ 分隔）；为 null 表示"自动找唯一的 adapter.json"。
/// </para>
/// </remarks>
public sealed record AdapterPackageSource
{
    private AdapterPackageSource(AdapterPackageSourceKind kind, string location, Uri? remoteUri, string? entryPoint)
    {
        Kind = kind;
        Location = location;
        RemoteUri = remoteUri;
        EntryPoint = entryPoint;
    }

    /// <summary>来源形态。</summary>
    public AdapterPackageSourceKind Kind { get; }

    /// <summary>原始来源文本：本地文件的路径，或远端的绝对 URL（用于清单里留档）。</summary>
    public string Location { get; }

    /// <summary>远端地址的解析结果；本地来源为 null。</summary>
    public Uri? RemoteUri { get; }

    /// <summary>zip 内的入口文件（相对路径，/ 分隔）；null 表示自动选唯一的 adapter.json。</summary>
    public string? EntryPoint { get; }

    /// <summary>是否需要先取回网络内容。</summary>
    public bool IsRemote => Kind == AdapterPackageSourceKind.RemoteUrl;

    /// <summary>本地文件来源。</summary>
    /// <param name="path">本地 .json 或 .zip 的路径。</param>
    /// <param name="entryPoint">zip 内入口（可空）。</param>
    public static AdapterPackageSource FromFile(string path, string? entryPoint = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return new AdapterPackageSource(AdapterPackageSourceKind.LocalFile, path, null, NormalizeEntryPoint(entryPoint));
    }

    /// <summary>远端来源（只接受 http/https 绝对地址；离线环境由调用方注入假实现）。</summary>
    /// <param name="uri">绝对地址。</param>
    /// <param name="entryPoint">zip 内入口（可空）。</param>
    public static AdapterPackageSource FromUrl(Uri uri, string? entryPoint = null)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri
            || (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException($"adapter 包地址必须是 http(s) 绝对地址：{uri}", nameof(uri));
        }

        return new AdapterPackageSource(AdapterPackageSourceKind.RemoteUrl, uri.AbsoluteUri, uri, NormalizeEntryPoint(entryPoint));
    }

    /// <summary>入口名归一：统一 / 分隔并去空白；空串降级为 null（= 自动选）。</summary>
    private static string? NormalizeEntryPoint(string? entryPoint)
        => string.IsNullOrWhiteSpace(entryPoint) ? null : entryPoint.Trim().Replace('\\', '/');
}

/// <summary>一份 adapter 包的哈希信息：sha256（小写十六进制）与字节数。</summary>
/// <param name="Sha256">SHA-256，64 位小写十六进制。</param>
/// <param name="SizeBytes">参与哈希的原文字节数。</param>
public sealed record AdapterHashInfo(string Sha256, long SizeBytes)
{
    /// <summary>算一段内容（或一个包文件）的 sha256。</summary>
    /// <param name="payload">原文。</param>
    public static AdapterHashInfo Compute(ReadOnlySpan<byte> payload)
        => new(Convert.ToHexStringLower(SHA256.HashData(payload)), payload.Length);

    /// <summary>与另一份哈希是否相同（大小写无关，null 一律不等）。</summary>
    /// <param name="otherSha256">另一份 sha256。</param>
    public bool Matches(string? otherSha256)
        => !string.IsNullOrWhiteSpace(otherSha256)
            && string.Equals(Sha256, otherSha256.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>界面上显示的短哈希（前 12 位足够了，完整值在清单里）。</summary>
    public string ShortSha256 => Sha256.Length > 12 ? Sha256[..12] : Sha256;
}

/// <summary>
/// 安装清单（落盘为 adapter 目录里的 installed.json）。
/// </summary>
/// <param name="Id">adapter id（同时也是目录名，kebab-case）。</param>
/// <param name="Name">adapter.json 声明的名称。</param>
/// <param name="SchemaVersion">adapter.json 的 schemaVersion（当前只支持 1）。</param>
/// <param name="Source">来源原文：本地路径或远端 URL。</param>
/// <param name="SourceKind">来源形态。</param>
/// <param name="Sha256">安装时那份包内容的 sha256（更新检查的比较基准）。</param>
/// <param name="SizeBytes">包内容的字节数。</param>
/// <param name="InstalledAt">安装时间（UTC，ISO-8601 精确到秒）。</param>
/// <param name="DeclaredVersion">adapter.json 里声明的 version（schema v1 没有这个字段，通常为 null）。</param>
/// <param name="EntryPoint">zip 内的入口文件；单个 .json 来源为 null。</param>
/// <param name="SourceEtag">同一来源上一次响应带回来的 ETag（下次条件请求用；本地来源为 null）。</param>
/// <param name="ManifestVersion">清单格式版本（为将来加字段留的位）。</param>
public sealed record AdapterInstallManifest(
    string Id,
    string Name,
    int SchemaVersion,
    string Source,
    AdapterPackageSourceKind SourceKind,
    string Sha256,
    long SizeBytes,
    DateTimeOffset InstalledAt,
    string? DeclaredVersion = null,
    string? EntryPoint = null,
    string? SourceEtag = null,
    int ManifestVersion = 1)
{
    /// <summary>界面上显示的短哈希。</summary>
    public string ShortSha256 => Sha256.Length > 12 ? Sha256[..12] : Sha256;
}

/// <summary>一次成功安装的产出。</summary>
/// <param name="Manifest">写进 installed.json 的清单。</param>
/// <param name="AdapterDirectory">adapter 落盘目录（&lt;根&gt;/&lt;id&gt;）。</param>
/// <param name="Hash">本次安装算出的哈希（与 <see cref="Manifest"/> 里的 sha256 一致）。</param>
/// <param name="Format">容器形态。</param>
/// <param name="ReplacedExisting">是否覆盖了同 id 的既有安装。</param>
/// <param name="Diagnostics">安装期的非阻断诊断（例如 adapter.json 里的未知属性）。</param>
public sealed record AdapterInstallResult(
    AdapterInstallManifest Manifest,
    string AdapterDirectory,
    AdapterHashInfo Hash,
    AdapterPackageFormat Format,
    bool ReplacedExisting,
    IReadOnlyList<ModConfigDiagnostic>? Diagnostics = null)
{
    /// <summary>非阻断诊断。</summary>
    public IReadOnlyList<ModConfigDiagnostic> InstallDiagnostics => Diagnostics ?? [];
}

/// <summary>一次更新检查的结论。</summary>
/// <param name="Status">三态结论。</param>
/// <param name="AdapterId">被检查的 adapter id。</param>
/// <param name="InstalledSha256">已装版本的 sha256（比较基准）。</param>
/// <param name="RemoteSha256">远端内容的 sha256；304 时为 null。</param>
/// <param name="RemoteEtag">远端返回的 ETag（调用方可留到下次条件请求）。</param>
/// <param name="RemoteSizeBytes">远端内容的字节数；304 时为 0。</param>
/// <param name="Message">给用户看的一句话。</param>
public sealed record AdapterUpdateCheckResult(
    AdapterUpdateStatus Status,
    string AdapterId,
    string? InstalledSha256,
    string? RemoteSha256,
    string? RemoteEtag = null,
    long RemoteSizeBytes = 0,
    string? Message = null)
{
    /// <summary>是否有更新（= 只有 <see cref="AdapterUpdateStatus.UpdateAvailable"/> 才算）。</summary>
    public bool HasUpdate => Status == AdapterUpdateStatus.UpdateAvailable;
}

/// <summary>一个 &lt;根&gt; 目录下所有已装 adapter 的清单快照。</summary>
/// <param name="Adapters">按 id 排序的清单。</param>
/// <param name="Diagnostics">读清单时发现的降级项（清单损坏等），不阻断枚举。</param>
public sealed record AdapterInstalledIndex(
    IReadOnlyList<AdapterInstallManifest> Adapters,
    IReadOnlyList<ModConfigDiagnostic>? Diagnostics = null)
{
    /// <summary>降级诊断（清单损坏等）。</summary>
    public IReadOnlyList<ModConfigDiagnostic> IndexDiagnostics => Diagnostics ?? [];

    /// <summary>按 id 查一份清单。</summary>
    /// <param name="adapterId">adapter id。</param>
    public AdapterInstallManifest? Find(string? adapterId)
    {
        if (string.IsNullOrEmpty(adapterId))
        {
            return null;
        }

        foreach (var manifest in Adapters)
        {
            if (string.Equals(manifest.Id, adapterId, StringComparison.OrdinalIgnoreCase))
            {
                return manifest;
            }
        }

        return null;
    }
}

/// <summary>清单里的字符串写法（与 installed.json 的字段值一一对应，改这里就要改文档）。</summary>
public static class AdapterPackageWireNames
{
    /// <summary>来源形态的写法。</summary>
    /// <param name="kind">来源形态。</param>
    public static string ToWireName(AdapterPackageSourceKind kind)
        => kind == AdapterPackageSourceKind.RemoteUrl ? "remote-url" : "local-file";

    /// <summary>解析来源形态。</summary>
    /// <param name="text">清单里的原文。</param>
    /// <param name="kind">解析结果。</param>
    public static bool TryParseSourceKind(string? text, out AdapterPackageSourceKind kind)
    {
        kind = AdapterPackageSourceKind.LocalFile;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();
        if (string.Equals(trimmed, "remote-url", StringComparison.OrdinalIgnoreCase))
        {
            kind = AdapterPackageSourceKind.RemoteUrl;
            return true;
        }

        if (string.Equals(trimmed, "local-file", StringComparison.OrdinalIgnoreCase))
        {
            kind = AdapterPackageSourceKind.LocalFile;
            return true;
        }

        return false;
    }

    /// <summary>时间戳格式（ISO-8601 往返格式，UTC）。</summary>
    /// <param name="value">时间。</param>
    public static string FormatTimestamp(DateTimeOffset value)
        => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
}
