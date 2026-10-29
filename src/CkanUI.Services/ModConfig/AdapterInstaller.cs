using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.ModConfig;
using log4net;

namespace CkanUI.Services.ModConfig;

/// <summary>adapter 安装的硬上限（可注入：用例用极小上限就能覆盖拒绝分支，不必造 16 MB 的包）。</summary>
/// <param name="MaxPackageBytes">单个包（本地文件或网络响应体）的字节上限。</param>
/// <param name="MaxExtractedBytes">zip 解压后所有条目的总字节上限。</param>
/// <param name="MaxEntryCount">zip 条目数上限。</param>
/// <param name="MaxAdapterJsonBytes">adapter.json 本身的字节上限。</param>
public sealed record AdapterInstallerLimits(
    long MaxPackageBytes = 16L * 1024 * 1024,
    long MaxExtractedBytes = 32L * 1024 * 1024,
    int MaxEntryCount = 512,
    long MaxAdapterJsonBytes = 1024 * 1024)
{
    /// <summary>默认上限。</summary>
    public static AdapterInstallerLimits Default { get; } = new();
}

/// <summary>
/// adapter 安装实现（ADP-04）：从本地文件或 https 地址导入第三方 adapter，落盘到 &lt;根&gt;/&lt;id&gt;/，
/// 写 installed.json 清单（含 sha256），并用 ETag 条件请求检查更新。
/// </summary>
/// <remarks>
/// 校验顺序（任一不过都不落盘）：包体积 → 容器形态 → zip 条目名/条目数/解压体积 → 入口唯一性 →
/// adapter.json 的 schema（复用 <see cref="AdapterJsonReader"/>）→ 目标目录内的路径包含性。
/// <para>
/// 所有失败都返回 <see cref="OperationResult"/>（Detail 以稳定诊断码开头，例如 <c>adapter-zip-slip</c>）；
/// 不向调用方抛异常。诊断码清单见 docs/design/adapter-import.md。
/// </para>
/// </remarks>
public sealed class AdapterInstaller : IAdapterInstaller
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(AdapterInstaller));

    private readonly IAdapterPackageFetcher? _fetcher;
    private readonly IAdapterCatalog? _catalog;
    private readonly AdapterInstallerLimits _limits;
    private readonly Lock _manifestGate = new();

    /// <summary>构造。</summary>
    /// <param name="fetcher">HTTP 获取端口；为 null 时不能装远端来源（离线环境就该这样）。</param>
    /// <param name="catalog">装完要刷新的 adapter 目录；为 null 表示由调用方自己刷新。</param>
    /// <param name="adapterRootDirectory">安装根目录；为 null 时用 %LocalAppData%\CKAN\ui\adapters（与 AdapterCatalog 的默认用户目录一致）。</param>
    /// <param name="limits">体积/条目数上限；为 null 时用默认值。</param>
    public AdapterInstaller(
        IAdapterPackageFetcher? fetcher = null,
        IAdapterCatalog? catalog = null,
        string? adapterRootDirectory = null,
        AdapterInstallerLimits? limits = null)
    {
        _fetcher = fetcher;
        _catalog = catalog;
        _limits = limits ?? AdapterInstallerLimits.Default;
        if (_limits.MaxPackageBytes <= 0 || _limits.MaxExtractedBytes <= 0 || _limits.MaxEntryCount <= 0 || _limits.MaxAdapterJsonBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limits), _limits, "安装上限必须全为正数。");
        }

        AdapterRootDirectory = string.IsNullOrWhiteSpace(adapterRootDirectory)
            ? AdapterCatalog.GetDefaultUserDirectory()
            : Path.GetFullPath(adapterRootDirectory);
    }

    /// <inheritdoc />
    public string AdapterRootDirectory { get; }

    /// <inheritdoc />
    public async Task<OperationResult<AdapterInstallResult>> InstallAsync(AdapterPackageSource source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        try
        {
            var payload = await ReadPayloadAsync(source, cancellationToken).ConfigureAwait(false);
            if (!payload.IsSuccess || payload.Value is null)
            {
                return OperationResult<AdapterInstallResult>.Fail(payload.Message ?? "读取 adapter 包失败。", payload.Detail, payload.Retryable);
            }

            var package = AdapterPackageInstaller.Prepare(source, payload.Value, _limits);
            if (!package.IsSuccess || package.Value is null)
            {
                return OperationResult<AdapterInstallResult>.Fail(package.Message ?? "adapter 包校验失败。", package.Detail, package.Retryable);
            }

            // 写盘在助手内部持锁完成；刷新目录放在锁外，避免重扫描期间卡住 GetInstalledAsync。
            var install = AdapterPackageInstaller.Write(source, payload.Value, package.Value, AdapterRootDirectory, _manifestGate);
            if (install.IsSuccess)
            {
                ReloadCatalog();
            }

            return install;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return AdapterResults.Fail<AdapterInstallResult>("adapter-cancelled", "安装 adapter 已取消。");
        }
        catch (Exception ex)
        {
            Log.Warn($"安装 adapter「{source.Location}」时出现未预期错误。", ex);
            return AdapterResults.Fail<AdapterInstallResult>("adapter-install-failed", $"安装 adapter 失败：{ex.Message}", source.Location);
        }
    }

    /// <inheritdoc />
    public Task<OperationResult<AdapterInstallManifest>> GetInstalledAsync(string adapterId, CancellationToken cancellationToken = default)
    {
        if (!IsSafeId(adapterId))
        {
            return Task.FromResult(AdapterResults.Fail<AdapterInstallManifest>("adapter-id-invalid", $"adapter id「{adapterId}」不合法（只允许 kebab-case）。", adapterId));
        }

        var directory = Path.Combine(AdapterRootDirectory, adapterId);
        if (!Directory.Exists(directory))
        {
            return Task.FromResult(AdapterResults.Fail<AdapterInstallManifest>("adapter-not-installed", $"adapter「{adapterId}」未安装。", directory));
        }

        AdapterManifestRead read;
        lock (_manifestGate)
        {
            read = AdapterManifestFile.Read(directory);
        }

        if (read.Manifest is not null)
        {
            return Task.FromResult(OperationResult<AdapterInstallManifest>.Ok(read.Manifest));
        }

        var corrupt = read.Diagnostics.Count > 0;
        return Task.FromResult(AdapterResults.Fail<AdapterInstallManifest>(
            corrupt ? AdapterManifestFile.CorruptCode : "adapter-not-installed",
            corrupt ? $"adapter「{adapterId}」的安装清单损坏，按未安装处理。" : $"adapter「{adapterId}」的目录里没有 {AdapterPackageLayout.ManifestFileName}，按未安装处理。",
            directory));
    }

    /// <inheritdoc />
    public Task<OperationResult<AdapterInstalledIndex>> ListInstalledAsync(CancellationToken cancellationToken = default)
    {
        var manifests = new List<AdapterInstallManifest>();
        var diagnostics = new List<ModConfigDiagnostic>();
        lock (_manifestGate)
        {
            if (!Directory.Exists(AdapterRootDirectory))
            {
                return Task.FromResult(OperationResult<AdapterInstalledIndex>.Ok(new AdapterInstalledIndex(manifests, diagnostics)));
            }

            string[] directories;
            try
            {
                directories = Directory.GetDirectories(AdapterRootDirectory);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Task.FromResult(AdapterResults.Fail<AdapterInstalledIndex>("adapter-io", $"枚举 adapter 目录失败：{ex.Message}", AdapterRootDirectory, retryable: true));
            }

            foreach (var directory in directories)
            {
                if (Path.GetFileName(directory).StartsWith('.'))
                {
                    continue;
                }

                var read = AdapterManifestFile.Read(directory);
                if (read.Manifest is not null)
                {
                    manifests.Add(read.Manifest);
                }

                diagnostics.AddRange(read.Diagnostics);
            }
        }

        manifests.Sort(static (left, right) => string.CompareOrdinal(left.Id, right.Id));
        return Task.FromResult(OperationResult<AdapterInstalledIndex>.Ok(new AdapterInstalledIndex(manifests, diagnostics)));
    }

    /// <inheritdoc />
    public Task<OperationResult> UninstallAsync(string adapterId, CancellationToken cancellationToken = default)
    {
        if (!IsSafeId(adapterId))
        {
            return Task.FromResult(AdapterResults.Fail("adapter-id-invalid", $"adapter id「{adapterId}」不合法（只允许 kebab-case）。", adapterId));
        }

        var directory = Path.Combine(AdapterRootDirectory, adapterId);
        lock (_manifestGate)
        {
            if (!Directory.Exists(directory))
            {
                return Task.FromResult(AdapterResults.Fail("adapter-not-installed", $"adapter「{adapterId}」未安装。", directory));
            }

            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Task.FromResult(AdapterResults.Fail("adapter-io", $"删除 adapter 目录失败：{ex.Message}", directory, retryable: true));
            }
        }

        ReloadCatalog();
        Log.Info($"已卸载 adapter「{adapterId}」。");
        return Task.FromResult(OperationResult.Ok());
    }

    /// <inheritdoc />
    public async Task<OperationResult<AdapterUpdateCheckResult>> CheckForUpdateAsync(
        string adapterId,
        Uri remoteUri,
        string? etag = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(remoteUri);
        try
        {
            if (_fetcher is null)
            {
                return AdapterResults.Fail<AdapterUpdateCheckResult>("adapter-fetcher-missing", "当前没有可用的 HTTP 获取端口，无法检查 adapter 更新。");
            }

            var installed = await GetInstalledAsync(adapterId, cancellationToken).ConfigureAwait(false);
            if (!installed.IsSuccess || installed.Value is null)
            {
                return OperationResult<AdapterUpdateCheckResult>.Fail(installed.Message ?? "读不到已装 adapter 的清单。", installed.Detail, installed.Retryable);
            }

            var manifest = installed.Value;
            var effectiveEtag = string.IsNullOrWhiteSpace(etag) ? manifest.SourceEtag : etag.Trim();
            var response = await _fetcher.GetAsync(remoteUri, effectiveEtag, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccess || response.Value is null)
            {
                return OperationResult<AdapterUpdateCheckResult>.Fail(response.Message ?? "检查 adapter 更新失败。", response.Detail, response.Retryable);
            }

            var body = response.Value;
            if (body.NotModified)
            {
                return OperationResult<AdapterUpdateCheckResult>.Ok(new AdapterUpdateCheckResult(
                    AdapterUpdateStatus.NotModified,
                    manifest.Id,
                    manifest.Sha256,
                    null,
                    body.ETag ?? effectiveEtag,
                    0,
                    "远端未变化（304），无需更新。"));
            }

            var payload = body.Payload;
            if (payload is null || payload.Length == 0)
            {
                return AdapterResults.Fail<AdapterUpdateCheckResult>("adapter-http-empty", "远端返回 200，但响应体是空的。", remoteUri.AbsoluteUri, retryable: true);
            }

            if (payload.Length > _limits.MaxPackageBytes)
            {
                return AdapterResults.Fail<AdapterUpdateCheckResult>(
                    "adapter-package-too-large",
                    $"远端 adapter 包 {payload.Length} 字节，超过上限 {_limits.MaxPackageBytes} 字节。",
                    remoteUri.AbsoluteUri);
            }

            var remoteHash = AdapterHashInfo.Compute(payload);
            var same = string.Equals(remoteHash.Sha256, manifest.Sha256, StringComparison.OrdinalIgnoreCase);
            return OperationResult<AdapterUpdateCheckResult>.Ok(new AdapterUpdateCheckResult(
                same ? AdapterUpdateStatus.UpToDate : AdapterUpdateStatus.UpdateAvailable,
                manifest.Id,
                manifest.Sha256,
                remoteHash.Sha256,
                body.ETag ?? effectiveEtag,
                payload.Length,
                same
                    ? "远端内容与已装版本一致，没有更新。"
                    : $"远端有更新（{manifest.ShortSha256} → {remoteHash.ShortSha256}）。"));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return AdapterResults.Fail<AdapterUpdateCheckResult>("adapter-cancelled", "检查 adapter 更新已取消。");
        }
        catch (Exception ex)
        {
            Log.Warn($"检查 adapter「{adapterId}」更新时出现未预期错误。", ex);
            return AdapterResults.Fail<AdapterUpdateCheckResult>("adapter-update-check-failed", $"检查 adapter 更新失败：{ex.Message}", remoteUri.AbsoluteUri, retryable: true);
        }
    }

    private async Task<OperationResult<AdapterPackageInstaller.PackagePayload>> ReadPayloadAsync(AdapterPackageSource source, CancellationToken cancellationToken)
    {
        if (source.IsRemote)
        {
            if (_fetcher is null || source.RemoteUri is null)
            {
                return AdapterResults.Fail<AdapterPackageInstaller.PackagePayload>("adapter-fetcher-missing", "当前没有可用的 HTTP 获取端口，无法安装远端 adapter 包。", source.Location);
            }

            var response = await _fetcher.GetAsync(source.RemoteUri, null, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccess || response.Value is null)
            {
                return OperationResult<AdapterPackageInstaller.PackagePayload>.Fail(response.Message ?? "获取 adapter 包失败。", response.Detail, response.Retryable);
            }

            var payload = response.Value.Payload;
            if (payload is null || payload.Length == 0)
            {
                return AdapterResults.Fail<AdapterPackageInstaller.PackagePayload>("adapter-package-empty", "远端返回的 adapter 包是空的。", source.Location);
            }

            if (payload.Length > _limits.MaxPackageBytes)
            {
                return AdapterResults.Fail<AdapterPackageInstaller.PackagePayload>(
                    "adapter-package-too-large",
                    $"adapter 包 {payload.Length} 字节，超过上限 {_limits.MaxPackageBytes} 字节。",
                    source.Location);
            }

            return OperationResult<AdapterPackageInstaller.PackagePayload>.Ok(new AdapterPackageInstaller.PackagePayload(payload, response.Value.ETag));
        }

        var path = source.Location;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                return AdapterResults.Fail<AdapterPackageInstaller.PackagePayload>("adapter-source-missing", $"找不到 adapter 包文件：{path}", path);
            }

            if (info.Length == 0)
            {
                return AdapterResults.Fail<AdapterPackageInstaller.PackagePayload>("adapter-package-empty", $"adapter 包文件是空的：{path}", path);
            }

            if (info.Length > _limits.MaxPackageBytes)
            {
                return AdapterResults.Fail<AdapterPackageInstaller.PackagePayload>(
                    "adapter-package-too-large",
                    $"adapter 包 {info.Length} 字节，超过上限 {_limits.MaxPackageBytes} 字节。",
                    path);
            }

            var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
            return OperationResult<AdapterPackageInstaller.PackagePayload>.Ok(new AdapterPackageInstaller.PackagePayload(bytes, null));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return AdapterResults.Fail<AdapterPackageInstaller.PackagePayload>("adapter-source-unreadable", $"读不了 adapter 包文件：{ex.Message}", path);
        }
    }

    /// <summary>装完刷新 catalog；刷新失败只记 warning，不影响已经落盘的结果。</summary>
    private void ReloadCatalog()
    {
        try
        {
            _catalog?.Reload();
        }
        catch (Exception ex)
        {
            Log.Warn("adapter 安装后刷新目录失败，界面可能需要手动刷新。", ex);
        }
    }

    /// <summary>id 能不能当目录名：只允许 kebab-case 字符、长度 ≤ 64、首尾不是横线。</summary>
    internal static bool IsSafeId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 64 || id[0] == '-' || id[^1] == '-')
        {
            return false;
        }

        foreach (var ch in id)
        {
            var allowed = (ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9') || ch == '-';
            if (!allowed)
            {
                return false;
            }
        }

        return true;
    }
}
