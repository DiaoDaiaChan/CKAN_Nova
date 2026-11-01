using System.Globalization;
using System.Security;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Assets;

namespace CkanUI.Services.Assets;

/// <summary>载具库实现：扫 <c>Ships</c> 下的三类 <c>.craft</c>，并维护伴生 <c>.loadmeta</c> 与缩略图。</summary>
public sealed class CraftLibraryService : ICraftLibraryService
{
    /// <summary>载具扩展名。</summary>
    public const string CraftExtension = ".craft";

    /// <summary>伴生元数据扩展名。</summary>
    public const string LoadMetaExtension = ".loadmeta";

    /// <summary>缩略图根目录名。</summary>
    public const string ThumbFolderRoot = "@thumbs";

    private readonly IErrorReporter _errors;

    /// <summary>构造服务。</summary>
    /// <param name="errors">错误上报口。</param>
    public CraftLibraryService(IErrorReporter errors) =>
        _errors = errors ?? throw new ArgumentNullException(nameof(errors));

    /// <inheritdoc />
    public string GetKindRelativePath(CraftKind kind) => kind switch
    {
        CraftKind.Sph => "Ships/SPH",
        CraftKind.Subassembly => "Ships/Subassemblies",
        _ => "Ships/VAB",
    };

    /// <inheritdoc />
    public Task<OperationResult<IReadOnlyList<CraftAsset>>> ScanAsync(string gameDir, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameDir);
        return Task.Run(() => ScanCore(gameDir, cancellationToken), cancellationToken);
    }

    /// <inheritdoc />
    public Task<OperationResult<CraftAsset>> ImportAsync(string gameDir, string sourceFile, CraftKind kind, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameDir);
        if (!string.Equals(Path.GetExtension(sourceFile), CraftExtension, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(OperationResult<CraftAsset>.Fail("只支持 .craft 载具文件。"));
        }

        if (!File.Exists(sourceFile))
        {
            return Task.FromResult(OperationResult<CraftAsset>.Fail("找不到要导入的载具文件。"));
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var dir = Path.Combine(gameDir, GetKindRelativePath(kind));
            Directory.CreateDirectory(dir);
            var name = UniqueName(dir, Sanitize(Path.GetFileNameWithoutExtension(sourceFile)), allowExisting: null);
            var target = Path.Combine(dir, name + CraftExtension);
            File.Copy(sourceFile, target, overwrite: false);
            CopySibling(sourceFile, target, LoadMetaExtension, deleteSource: false);
            return Task.FromResult(OperationResult<CraftAsset>.Ok(Read(gameDir, target, kind)));
        }
        catch (OperationCanceledException)
        {
            return Task.FromResult(OperationResult<CraftAsset>.Fail("导入载具已取消。"));
        }
        catch (Exception ex) when (IsIoFailure(ex))
        {
            _errors.Report(ex, "载具库", "导入载具失败");
            return Task.FromResult(OperationResult<CraftAsset>.Fail("导入载具失败。", ex.Message, retryable: true));
        }
    }

    /// <inheritdoc />
    public Task<OperationResult<CraftAsset>> DuplicateAsync(string gameDir, CraftAsset craft, string newName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(craft);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var dir = Path.GetDirectoryName(craft.FilePath)!;
            var name = UniqueName(dir, Sanitize(newName), allowExisting: null);
            var target = Path.Combine(dir, name + CraftExtension);
            File.Copy(craft.FilePath, target, overwrite: false);
            CopySibling(craft.FilePath, target, LoadMetaExtension, deleteSource: false);
            CopyThumbnail(gameDir, craft.Kind, craft.Name, name, deleteSource: false);
            return Task.FromResult(OperationResult<CraftAsset>.Ok(Read(gameDir, target, craft.Kind)));
        }
        catch (OperationCanceledException)
        {
            return Task.FromResult(OperationResult<CraftAsset>.Fail("复制载具已取消。"));
        }
        catch (Exception ex) when (IsIoFailure(ex))
        {
            _errors.Report(ex, "载具库", "复制载具失败");
            return Task.FromResult(OperationResult<CraftAsset>.Fail("复制载具失败。", ex.Message, retryable: true));
        }
    }

    /// <inheritdoc />
    public Task<OperationResult<CraftAsset>> RenameAsync(string gameDir, CraftAsset craft, string newName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(craft);
        var name = Sanitize(newName);
        if (string.Equals(name, craft.Name, StringComparison.Ordinal))
        {
            return Task.FromResult(OperationResult<CraftAsset>.Ok(craft));
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var dir = Path.GetDirectoryName(craft.FilePath)!;
            name = UniqueName(dir, name, allowExisting: craft.Name);
            var target = Path.Combine(dir, name + CraftExtension);
            File.Move(craft.FilePath, target);
            CopySibling(craft.FilePath, target, LoadMetaExtension, deleteSource: true);
            CopyThumbnail(gameDir, craft.Kind, craft.Name, name, deleteSource: true);
            return Task.FromResult(OperationResult<CraftAsset>.Ok(Read(gameDir, target, craft.Kind)));
        }
        catch (OperationCanceledException)
        {
            return Task.FromResult(OperationResult<CraftAsset>.Fail("重命名载具已取消。"));
        }
        catch (Exception ex) when (IsIoFailure(ex))
        {
            _errors.Report(ex, "载具库", "重命名载具失败");
            return Task.FromResult(OperationResult<CraftAsset>.Fail("重命名载具失败。", ex.Message, retryable: true));
        }
    }

    /// <inheritdoc />
    public Task<OperationResult> DeleteAsync(string gameDir, CraftAsset craft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(craft);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            SafeDelete(craft.FilePath);
            SafeDelete(Path.ChangeExtension(craft.FilePath, LoadMetaExtension));
            if (!string.IsNullOrWhiteSpace(craft.ThumbnailPath))
            {
                SafeDelete(craft.ThumbnailPath);
            }

            return Task.FromResult(OperationResult.Ok());
        }
        catch (OperationCanceledException)
        {
            return Task.FromResult(OperationResult.Fail("删除载具已取消。"));
        }
        catch (Exception ex) when (IsIoFailure(ex))
        {
            _errors.Report(ex, "载具库", "删除载具失败");
            return Task.FromResult(OperationResult.Fail("删除载具失败。", ex.Message, retryable: true));
        }
    }

    private OperationResult<IReadOnlyList<CraftAsset>> ScanCore(string gameDir, CancellationToken cancellationToken)
    {
        try
        {
            var crafts = new List<CraftAsset>();
            foreach (var kind in new[] { CraftKind.Vab, CraftKind.Sph, CraftKind.Subassembly })
            {
                cancellationToken.ThrowIfCancellationRequested();
                var dir = Path.Combine(gameDir, GetKindRelativePath(kind));
                if (!Directory.Exists(dir))
                {
                    continue;
                }

                foreach (var file in Directory.EnumerateFiles(dir, "*" + CraftExtension, SearchOption.TopDirectoryOnly))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    crafts.Add(Read(gameDir, file, kind));
                }
            }

            crafts.Sort(static (left, right) => string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase));
            return OperationResult<IReadOnlyList<CraftAsset>>.Ok(crafts);
        }
        catch (OperationCanceledException)
        {
            return OperationResult<IReadOnlyList<CraftAsset>>.Fail("扫描载具已取消。");
        }
        catch (Exception ex) when (IsIoFailure(ex))
        {
            _errors.Report(ex, "载具库", "扫描载具目录失败");
            return OperationResult<IReadOnlyList<CraftAsset>>.Fail("扫描载具目录失败。", ex.Message, retryable: true);
        }
    }

    private static CraftAsset Read(string gameDir, string filePath, CraftKind kind)
    {
        var meta = ReadLoadMeta(Path.ChangeExtension(filePath, LoadMetaExtension));
        var thumbnail = ThumbnailPath(gameDir, kind, Path.GetFileNameWithoutExtension(filePath));
        var info = new FileInfo(filePath);
        return new CraftAsset(
            Path.GetFileNameWithoutExtension(filePath),
            filePath,
            kind,
            File.Exists(thumbnail) ? thumbnail : null,
            meta.GetValueOrDefault("version"),
            int.TryParse(meta.GetValueOrDefault("partCount"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parts) ? parts : null,
            int.TryParse(meta.GetValueOrDefault("stageCount"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var stages) ? stages : null,
            info.Exists ? info.Length : 0,
            info.Exists ? new DateTimeOffset(info.LastWriteTimeUtc) : DateTimeOffset.UtcNow);
    }

    private static Dictionary<string, string> ReadLoadMeta(string path)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (!File.Exists(path))
            {
                return values;
            }

            foreach (var line in File.ReadLines(path))
            {
                var index = line.IndexOf('=');
                if (index <= 0)
                {
                    continue;
                }

                var key = line[..index].Trim();
                var value = line[(index + 1)..].Trim();
                if (key.Length > 0 && value.Length > 0)
                {
                    values[key] = value;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            // 元数据坏掉不影响列表可用性。
            values.Clear();
        }

        return values;
    }

    private static string ThumbnailPath(string gameDir, CraftKind kind, string name) =>
        Path.Combine(gameDir, "Ships", ThumbFolderRoot, kind switch
        {
            CraftKind.Sph => "SPH",
            CraftKind.Subassembly => "Subassemblies",
            _ => "VAB",
        }, name + ".png");

    private static void CopyThumbnail(string gameDir, CraftKind kind, string oldName, string newName, bool deleteSource)
    {
        var from = ThumbnailPath(gameDir, kind, oldName);
        if (!File.Exists(from))
        {
            return;
        }

        var to = ThumbnailPath(gameDir, kind, newName);
        File.Copy(from, to, overwrite: true);
        if (deleteSource)
        {
            SafeDelete(from);
        }
    }

    private static void CopySibling(string sourceCraft, string targetCraft, string extension, bool deleteSource)
    {
        var from = Path.ChangeExtension(sourceCraft, extension);
        if (!File.Exists(from))
        {
            return;
        }

        var to = Path.ChangeExtension(targetCraft, extension);
        File.Copy(from, to, overwrite: true);
        if (deleteSource)
        {
            SafeDelete(from);
        }
    }

    private static void SafeDelete(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static bool IsIoFailure(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or SecurityException;

    private static string Sanitize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "未命名";
        }

        var invalid = Path.GetInvalidFileNameChars();
        var builder = new System.Text.StringBuilder(name.Length);
        foreach (var ch in name.Trim())
        {
            builder.Append(invalid.Contains(ch) ? '_' : ch);
        }

        var result = builder.ToString().Trim();
        return result.Length == 0 ? "未命名" : result;
    }

    private static string UniqueName(string dir, string desired, string? allowExisting)
    {
        var candidate = desired;
        var index = 2;
        while (true)
        {
            var exists = File.Exists(Path.Combine(dir, candidate + CraftExtension));
            if (!exists || string.Equals(candidate, allowExisting, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }

            candidate = string.Create(CultureInfo.InvariantCulture, $"{desired} ({index})");
            index++;
        }
    }
}
