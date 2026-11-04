using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CkanUI.Services.Abstractions;

namespace CkanUI.Plugin.ModPackShare.Sources;

/// <summary>一个整合包来源解析后的形态：本地文件，或一组按优先级尝试的下载地址。</summary>
/// <param name="SourceText">用户原始输入（原样存进配置档的 Source，便于以后追溯）。</param>
/// <param name="LocalPath">本地文件路径；远程来源为 null。</param>
/// <param name="CandidateUrls">按顺序尝试的下载地址；本地来源为空。</param>
/// <param name="DisplayName">给用户看的名字（通常是 .ckan 文件名去掉扩展名）。</param>
/// <param name="IsGitHub">是否走 GitHub 形态（决定失败文案里的提示）。</param>
public sealed record ModPackSource(
    string SourceText,
    string? LocalPath,
    IReadOnlyList<Uri> CandidateUrls,
    string DisplayName,
    bool IsGitHub)
{
    /// <summary>是否为本地文件来源。</summary>
    public bool IsLocal => LocalPath is not null;
}

/// <summary>下载完成的整合包。</summary>
/// <param name="FilePath">缓存目录里的 .ckan 绝对路径。</param>
/// <param name="SourceUrl">实际成功的下载地址；本地来源是原路径。</param>
/// <param name="DisplayName">显示名（文件名去扩展名）。</param>
/// <param name="Bytes">文件字节数。</param>
public sealed record DownloadedModPack(string FilePath, string SourceUrl, string DisplayName, long Bytes);

/// <summary>
/// GitHub 仓库 / 本地路径形态的 .ckan 来源解析与下载。
/// </summary>
/// <remarks>
/// <para>解析是纯函数（<see cref="Parse"/>），不碰网络也不碰磁盘（只有 File.Exists 一次探测）；
/// 下载单独一层，测试可以只钉解析规则，也可以注入假 <see cref="HttpMessageHandler"/> 钉下载失败路径。</para>
/// <para>为什么限制 8 MiB 并且要求首字符是 <c>{</c>：.ckan 是 JSON 元数据包，正常只有几十 KB。
/// 用户很可能贴了一个仓库主页或一个 zip 下载地址——先挡在缓存目录之外，比让它变成配置档里的坏文件好。</para>
/// </remarks>
public static class GitHubModPackSource
{
    /// <summary>允许的最大 .ckan 字节数（8 MiB）。</summary>
    public const int MaxBytes = 8 * 1024 * 1024;

    /// <summary>HTTP 超时（秒）。</summary>
    public const int TimeoutSeconds = 20;

    /// <summary>owner/repo 没给 ref 时依次尝试的分支。</summary>
    private static readonly string[] DefaultRefs = { "main", "master" };

    /// <summary>owner/repo 没给文件路径时假定的文件名（GitHub 仓库根目录的常见约定）。</summary>
    private const string DefaultFileName = "modpack.ckan";

    private static readonly Uri RawHost = new("https://raw.githubusercontent.com/");

    /// <summary>
    /// 解析用户输入。支持：本地路径、github.com blob/raw 链接、raw.githubusercontent.com 链接、
    /// <c>owner/repo[/path][@ref]</c> 简写、以及其它 https 直链。
    /// </summary>
    /// <returns>成功时给出候选地址；失败时给出中文原因（不抛异常）。</returns>
    public static OperationResult<ModPackSource> Parse(string? text)
    {
        var raw = (text ?? string.Empty).Trim().Trim('"', '\u201c', '\u201d', '\u2018', '\u2019');
        if (raw.Length == 0)
        {
            return OperationResult<ModPackSource>.Fail("先填入 .ckan 的 GitHub 地址或本地路径。");
        }

        // 本地文件优先：用户拖进来的路径往往也长得像 owner/repo/path。
        if (File.Exists(raw))
        {
            return OperationResult<ModPackSource>.Ok(new ModPackSource(
                raw,
                Path.GetFullPath(raw),
                Array.Empty<Uri>(),
                NameFromPath(raw),
                IsGitHub: false));
        }

        if (Uri.TryCreate(raw, UriKind.Absolute, out var absolute)
            && (absolute.Scheme == Uri.UriSchemeHttps || absolute.Scheme == Uri.UriSchemeHttp))
        {
            return ParseUrl(raw, absolute);
        }

        // 先按 owner/repo 简写解析，再回头判断「像本地路径但不存在」。
        // 反过来会把 kOS-dev/ckan-modpack 这种合法简写误报成本地文件缺失——右斜杠同时也是仓库分隔符。
        var shorthand = ParseRepositoryShorthand(raw);
        if (shorthand.IsSuccess)
        {
            return shorthand;
        }

        if (LooksLikeLocalPath(raw))
        {
            return OperationResult<ModPackSource>.Fail(
                $"找不到本地文件：{raw}。检查路径是否写错，或者改用 GitHub 地址。");
        }

        return shorthand;
    }

    /// <summary>
    /// 下载（或复制本地文件）到缓存目录，文件名是内容 SHA-256 的前 16 位。
    /// </summary>
    /// <remarks>
    /// 用内容哈希而不是来源文本做文件名：同一个整合包贴成 blob 链接还是 raw 链接只会下到同一个缓存文件，
    /// 不会在 ckan-ui/modpack-sources 里堆出一串重复的 .ckan。
    /// </remarks>
    public static async Task<OperationResult<DownloadedModPack>> DownloadAsync(
        string? sourceText,
        HttpClient client,
        string cacheDirectory,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDirectory);

        var parsed = Parse(sourceText);
        if (!parsed.IsSuccess || parsed.Value is null)
        {
            return OperationResult<DownloadedModPack>.Fail(parsed.Message ?? "来源无法识别。");
        }

        var source = parsed.Value;
        if (source.IsLocal)
        {
            try
            {
                var localBytes = await File.ReadAllBytesAsync(source.LocalPath!, ct).ConfigureAwait(false);
                var localCheck = Validate(localBytes, source.DisplayName);
                if (!localCheck.IsSuccess)
                {
                    return OperationResult<DownloadedModPack>.Fail(localCheck.Message!);
                }

                var cachedLocal = await CacheAsync(localBytes, cacheDirectory, ct).ConfigureAwait(false);
                return OperationResult<DownloadedModPack>.Ok(new DownloadedModPack(
                    cachedLocal,
                    source.LocalPath!,
                    source.DisplayName,
                    localBytes.LongLength));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return OperationResult<DownloadedModPack>.Fail($"读取本地文件失败：{ex.Message}");
            }
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));

        var failures = new List<string>();
        foreach (var url in source.CandidateUrls)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var response = await client
                    .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                    .ConfigureAwait(false);

                if (response.StatusCode is System.Net.HttpStatusCode.NotFound
                    or System.Net.HttpStatusCode.Forbidden)
                {
                    // 分支/文件不存在：换下一个候选（main → master），最后统一报错。
                    failures.Add($"{url} → HTTP {(int)response.StatusCode}");
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    failures.Add($"{url} → HTTP {(int)response.StatusCode}");
                    continue;
                }

                var declared = response.Content.Headers.ContentLength;
                if (declared is > MaxBytes)
                {
                    return OperationResult<DownloadedModPack>.Fail(
                        $"整合包超过 8 MiB（服务器声明 {declared} 字节），这不是一个正常的 .ckan 元数据包。");
                }

                var bytes = await ReadCappedAsync(response.Content, timeout.Token).ConfigureAwait(false);
                if (bytes is null)
                {
                    return OperationResult<DownloadedModPack>.Fail("整合包超过 8 MiB，已停止下载。");
                }

                if (bytes.Length == 0)
                {
                    failures.Add($"{url} → 空响应");
                    continue;
                }

                var check = Validate(bytes, source.DisplayName);
                if (!check.IsSuccess)
                {
                    return OperationResult<DownloadedModPack>.Fail(check.Message!);
                }

                var cached = await CacheAsync(bytes, cacheDirectory, ct).ConfigureAwait(false);
                return OperationResult<DownloadedModPack>.Ok(new DownloadedModPack(
                    cached,
                    url.ToString(),
                    source.DisplayName,
                    bytes.LongLength));
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return OperationResult<DownloadedModPack>.Fail(
                    $"下载超时（{TimeoutSeconds} 秒）。检查网络或改用本地文件。");
            }
            catch (HttpRequestException ex)
            {
                failures.Add($"{url} → {ex.Message}");
            }
        }

        var detail = failures.Count == 0 ? "没有可尝试的地址" : string.Join("；", failures);
        return OperationResult<DownloadedModPack>.Fail(
            "下载整合包失败：所有候选地址都不可用。确认仓库、分支和文件路径存在，或是把 .ckan 下载到本地后填本地路径。",
            detail);
    }

    /// <summary>解析 https 链接（GitHub blob/raw、raw.githubusercontent.com、其它直链）。</summary>
    private static OperationResult<ModPackSource> ParseUrl(string raw, Uri uri)
    {
        if (uri.Scheme == Uri.UriSchemeHttp && uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
        {
            // http 的 GitHub 链接改写成 https，不为了省一次重定向拒绝用户。
            uri = new UriBuilder(uri) { Scheme = Uri.UriSchemeHttps, Port = -1 }.Uri;
        }

        var segments = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.UnescapeDataString)
            .ToArray();

        if (uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
        {
            // https://github.com/{owner}/{repo}/blob/{ref}/{path}
            // https://github.com/{owner}/{repo}/raw/{ref}/{path}
            if (segments.Length >= 5
                && (segments[2].Equals("blob", StringComparison.OrdinalIgnoreCase)
                    || segments[2].Equals("raw", StringComparison.OrdinalIgnoreCase)))
            {
                return BuildRaw(raw, segments[0], segments[1], segments[3], string.Join('/', segments.Skip(4)));
            }

            return OperationResult<ModPackSource>.Fail(
                "这个 GitHub 链接不是单个文件。请用仓库里的 .ckan 文件页面（带 /blob/ 或 /raw/），" +
                "或者写成 owner/repo/path/to/xxx.ckan。",
                raw);
        }

        if (uri.Host.Equals("raw.githubusercontent.com", StringComparison.OrdinalIgnoreCase))
        {
            if (segments.Length < 4)
            {
                return OperationResult<ModPackSource>.Fail(
                    "raw.githubusercontent.com 链接缺少 分支/文件路径。", raw);
            }

            return OperationResult<ModPackSource>.Ok(new ModPackSource(
                raw,
                null,
                new[] { uri },
                NameFromPath(segments[^1]),
                IsGitHub: true));
        }

        if (segments.Length == 0 || !segments[^1].Contains('.', StringComparison.Ordinal))
        {
            return OperationResult<ModPackSource>.Fail(
                "这个链接看起来不是 .ckan 文件（地址末尾没有文件名）。", raw);
        }

        return OperationResult<ModPackSource>.Ok(new ModPackSource(
            raw,
            null,
            new[] { uri },
            NameFromPath(segments[^1]),
            IsGitHub: false));
    }

    /// <summary>解析 <c>owner/repo[/path][@ref]</c> 简写。</summary>
    private static OperationResult<ModPackSource> ParseRepositoryShorthand(string raw)
    {
        if (raw.Contains('\\') || raw.Contains(':'))
        {
            return OperationResult<ModPackSource>.Fail(
                $"既不是存在的本地文件，也不是可识别的地址：{raw}");
        }

        var body = raw;
        string? reference = null;
        var at = body.LastIndexOf('@');
        if (at > 0)
        {
            reference = body[(at + 1)..].Trim();
            body = body[..at];
        }

        var parts = body.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || parts[0].Length == 0 || parts[1].Length == 0)
        {
            return OperationResult<ModPackSource>.Fail(
                "来源无法识别。支持：.ckan 本地路径、GitHub 文件链接、owner/repo/path/to/xxx.ckan[@ref]、https 直链。",
                raw);
        }

        // packs/rp1.ckan 这种两段式本地相对路径会被误判成 owner/repo：repo 以 .ckan 结尾就按本地路径报错。
        if (parts[1].EndsWith(".ckan", StringComparison.OrdinalIgnoreCase))
        {
            return OperationResult<ModPackSource>.Fail(
                $"找不到本地文件：{raw}。检查路径是否写错，或者改用 GitHub 地址。",
                raw);
        }

        var owner = parts[0];
        var repo = parts[1];
        var path = parts.Length > 2 ? string.Join('/', parts.Skip(2)) : DefaultFileName;
        var refs = reference is { Length: > 0 } ? new[] { reference } : DefaultRefs;

        // 分支名里的 / 会让 owner/repo/path 的边界变模糊；常见写法是 @ref 单独给出，这里按段拼回去。
        var candidates = refs
            .Select(r => BuildRawUri(owner, repo, r, path))
            .ToArray();

        return OperationResult<ModPackSource>.Ok(new ModPackSource(
            raw,
            null,
            candidates,
            NameFromPath(path),
            IsGitHub: true));
    }

    private static OperationResult<ModPackSource> BuildRaw(string raw, string owner, string repo, string reference, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            path = DefaultFileName;
        }

        return OperationResult<ModPackSource>.Ok(new ModPackSource(
            raw,
            null,
            new[] { BuildRawUri(owner, repo, reference, path) },
            NameFromPath(path),
            IsGitHub: true));
    }

    private static Uri BuildRawUri(string owner, string repo, string reference, string path)
    {
        // 分段 Escape：路径里的空格/中文原样拼进 URI 会让 HttpClient 直接拒绝。
        var escaped = string.Join(
            '/',
            path.Split('/').Select(Uri.EscapeDataString));
        var referenceEncoded = string.Join(
            '/',
            reference.Split('/').Select(Uri.EscapeDataString));
        return new Uri($"{RawHost}{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}/{referenceEncoded}/{escaped}");
    }

    /// <summary>读流并计数，超过 8 MiB 立刻放弃；返回 null 表示超限。</summary>
    private static async Task<byte[]?> ReadCappedAsync(HttpContent content, CancellationToken ct)
    {
        await using var stream = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > MaxBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    /// <summary>内容是 JSON 对象才继续；BOM 与空白允许出现在第一个 <c>{</c> 之前。</summary>
    private static OperationResult Validate(byte[] bytes, string displayName)
    {
        if (bytes.Length > MaxBytes)
        {
            return OperationResult.Fail($"整合包超过 8 MiB（{bytes.LongLength} 字节），这不是一个正常的 .ckan 元数据包。");
        }

        var start = 0;
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            start = 3;
        }

        while (start < bytes.Length && bytes[start] is 0x20 or 0x09 or 0x0A or 0x0D)
        {
            start++;
        }

        if (start >= bytes.Length || bytes[start] != (byte)'{')
        {
            return OperationResult.Fail($"「{displayName}」不是 .ckan 元数据包：内容不是 JSON 对象（应以 {{ 开头）。");
        }

        return OperationResult.Ok();
    }

    private static async Task<string> CacheAsync(byte[] bytes, string cacheDirectory, CancellationToken ct)
    {
        Directory.CreateDirectory(cacheDirectory);
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()[..16];
        var path = Path.Combine(cacheDirectory, hash + ".ckan");
        if (!File.Exists(path) || new FileInfo(path).Length != bytes.LongLength)
        {
            await File.WriteAllBytesAsync(path, bytes, ct).ConfigureAwait(false);
        }

        return path;
    }

    /// <summary>
    /// 是否是「明显写成了本地路径」的形态。刻意不把单独的 <c>/</c> 算进来：
    /// owner/repo 简写同样含斜杠，判早了就会把合法仓库地址说成找不到文件。
    /// </summary>
    private static bool LooksLikeLocalPath(string raw)
        => raw.Contains('\\')
            || raw.Contains(':')
            || raw.StartsWith('.')
            || raw.EndsWith(".ckan", StringComparison.OrdinalIgnoreCase);

    private static string NameFromPath(string path)
    {
        var name = Path.GetFileName(path);
        if (name.EndsWith(".ckan", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^5];
        }

        return string.IsNullOrWhiteSpace(name) ? "整合包" : name;
    }

    /// <summary>给界面用的格式：字节数 → 人读文本。</summary>
    public static string FormatBytes(long bytes)
        => bytes >= 1024L * 1024L
            ? (bytes / (1024d * 1024d)).ToString("0.#", CultureInfo.InvariantCulture) + " MB"
            : bytes >= 1024L
                ? (bytes / 1024d).ToString("0.#", CultureInfo.InvariantCulture) + " KB"
                : bytes.ToString(CultureInfo.InvariantCulture) + " B";
}
