using System.Text;
using System.Text.Json;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.ModConfig;
using log4net;

namespace CkanUI.Services.ModConfig;

/// <summary>
/// 安装的落盘部分（ADP-04 内部助手，不对外暴露）：解包后的二次校验、staging 目录、原子替换与回滚。
/// </summary>
/// <remarks>
/// 由 <see cref="AdapterInstaller"/> 调用：<see cref="Prepare"/> 不碰磁盘（纯校验），<see cref="Write"/> 负责写盘，
/// 调用方必须把同一把进程内锁传进来，保证「写目录」与「读清单」互斥。
/// </remarks>
internal static class AdapterPackageInstaller
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(AdapterPackageInstaller));

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>取回来的包内容 + 响应 ETag（远端来源才可能有 ETag）。</summary>
    internal sealed record PackagePayload(byte[] Content, string? Etag);

    /// <summary>校验通过的包：入口原文、其余文件与 schema 诊断。</summary>
    internal sealed record PreparedPackage(
        AdapterPackageFormat Format,
        ModConfigAdapterDescription Adapter,
        string AdapterJson,
        string? EntryPoint,
        IReadOnlyList<AdapterPackageFile> Files,
        IReadOnlyList<ModConfigDiagnostic> Diagnostics);

    internal static OperationResult<PreparedPackage> Prepare(
        AdapterPackageSource source,
        PackagePayload payload,
        AdapterInstallerLimits limits)
    {
        var format = AdapterPackageReader.DetectFormat(source.Location, payload.Content);
        var adapterJsonBytes = payload.Content;
        string? entryPoint = null;
        IReadOnlyList<AdapterPackageFile> files = [];
        if (format == AdapterPackageFormat.Zip)
        {
            var contents = AdapterPackageReader.ReadZip(payload.Content, source.EntryPoint, limits);
            if (!contents.IsSuccess || contents.Value is null)
            {
                return OperationResult<PreparedPackage>.Fail(contents.Message ?? "解 zip 包失败。", contents.Detail, contents.Retryable);
            }

            entryPoint = contents.Value.EntryPoint;
            adapterJsonBytes = contents.Value.AdapterJsonBytes;
            files = contents.Value.Files;
        }
        else if (adapterJsonBytes.Length > limits.MaxAdapterJsonBytes)
        {
            return AdapterResults.Fail<PreparedPackage>(
                "adapter-package-too-large",
                $"adapter.json {adapterJsonBytes.Length} 字节，超过上限 {limits.MaxAdapterJsonBytes} 字节。",
                source.Location);
        }

        var json = AdapterPackageReader.DecodeUtf8(adapterJsonBytes);
        if (string.IsNullOrWhiteSpace(json))
        {
            return AdapterResults.Fail<PreparedPackage>("adapter-package-empty", "adapter.json 里没有内容。", source.Location);
        }

        var label = entryPoint is null ? source.Location : source.Location + "!" + entryPoint;
        var load = AdapterJsonReader.Read(json, label);
        if (!load.IsSuccess || load.Adapter is null)
        {
            var errors = new List<string>();
            foreach (var diagnostic in load.Diagnostics)
            {
                if (diagnostic.IsError)
                {
                    errors.Add(diagnostic.Code + ": " + diagnostic.Message);
                }
            }

            return AdapterResults.Fail<PreparedPackage>(
                "adapter-schema-invalid",
                "adapter.json 未通过 schema 校验，已放弃安装（没有落盘）。",
                errors.Count == 0 ? label : string.Join("；", errors));
        }

        if (!AdapterInstaller.IsSafeId(load.Adapter.Id))
        {
            return AdapterResults.Fail<PreparedPackage>("adapter-id-invalid", $"adapter.json 里的 id「{load.Adapter.Id}」不能作为目录名。", label);
        }

        return OperationResult<PreparedPackage>.Ok(new PreparedPackage(format, load.Adapter, json, entryPoint, files, load.Diagnostics));
    }

    internal static OperationResult<AdapterInstallResult> Write(
        AdapterPackageSource source,
        PackagePayload payload,
        PreparedPackage package,
        string rootDirectory,
        Lock gate)
    {
        var adapter = package.Adapter;
        var id = adapter.Id;
        var target = Path.Combine(rootDirectory, id);
        var stamp = Guid.NewGuid().ToString("N")[..8];
        var staging = Path.Combine(rootDirectory, AdapterPackageLayout.StagingPrefix + id + "-" + stamp);
        var backup = Path.Combine(rootDirectory, AdapterPackageLayout.BackupPrefix + id + "-" + stamp);
        var hash = AdapterHashInfo.Compute(payload.Content);
        var manifest = new AdapterInstallManifest(
            id,
            adapter.Name,
            adapter.SchemaVersion,
            source.Location,
            source.Kind,
            hash.Sha256,
            hash.SizeBytes,
            DateTimeOffset.UtcNow,
            DeclaredVersion: TryReadDeclaredVersion(package.AdapterJson),
            EntryPoint: package.EntryPoint,
            SourceEtag: payload.Etag);

        lock (gate)
        {
            var replaced = Directory.Exists(target);
            try
            {
                Directory.CreateDirectory(rootDirectory);
                Directory.CreateDirectory(staging);
                File.WriteAllText(Path.Combine(staging, AdapterPackageLayout.AdapterEntryFileName), package.AdapterJson, Utf8NoBom);
                var escaping = TryWriteFiles(staging, package.Files);
                if (escaping is not null)
                {
                    return AdapterResults.Fail<AdapterInstallResult>("adapter-zip-slip", $"zip 条目「{escaping}」落盘后会跑出安装目录，整包拒绝安装。", id);
                }

                File.WriteAllText(Path.Combine(staging, AdapterPackageLayout.ManifestFileName), AdapterManifestFile.Serialize(manifest), Utf8NoBom);

                if (replaced)
                {
                    Directory.Move(target, backup);
                }

                try
                {
                    Directory.Move(staging, target);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    if (replaced && Directory.Exists(backup) && !Directory.Exists(target))
                    {
                        TryMoveDirectory(backup, target);
                    }

                    return AdapterResults.Fail<AdapterInstallResult>("adapter-install-failed", $"把 adapter 落到 {target} 失败：{ex.Message}", id, retryable: true);
                }

                if (replaced)
                {
                    TryDeleteDirectory(backup);
                }

                Log.Info($"已安装 adapter「{id}」（{package.Format}，sha256={hash.ShortSha256}，{(replaced ? "覆盖" : "新增")}）。");
                return OperationResult<AdapterInstallResult>.Ok(new AdapterInstallResult(manifest, target, hash, package.Format, replaced, package.Diagnostics));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warn($"安装 adapter「{id}」失败。", ex);
                return AdapterResults.Fail<AdapterInstallResult>("adapter-io", $"安装 adapter 失败：{ex.Message}", id, retryable: true);
            }
            finally
            {
                if (Directory.Exists(staging))
                {
                    TryDeleteDirectory(staging);
                }
            }
        }
    }

    private static string? TryWriteFiles(string stagingRoot, IReadOnlyList<AdapterPackageFile> files)
    {
        foreach (var file in files)
        {
            var relative = file.RelativePath.Replace('/', Path.DirectorySeparatorChar);
            var full = Path.GetFullPath(Path.Combine(stagingRoot, relative));
            if (!IsInside(stagingRoot, full))
            {
                return file.RelativePath;
            }

            var directory = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllBytes(full, file.Content);
        }

        return null;
    }

    private static bool IsInside(string root, string candidate)
    {
        var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        return candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static string? TryReadDeclaredVersion(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json, AdapterManifestFile.DocumentOptions);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("version", out var element)
                && element.ValueKind == JsonValueKind.String)
            {
                var text = element.GetString();
                return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void TryMoveDirectory(string from, string to)
    {
        try
        {
            Directory.Move(from, to);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"回滚 adapter 目录失败（{from} → {to}），旧版本可能留在备份目录里。", ex);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"清理临时目录失败：{path}", ex);
        }
    }
}
