using System.Globalization;
using System.Security;
using System.Text;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Assets;

namespace CkanUI.Services.Assets;

/// <summary>旗帜库实现：GameData 下所有 <c>Flags</c> 目录里的图片，外加 <c>settings.cfg</c> 的 <c>FLAG</c> 行。</summary>
/// <remarks>
/// 旗帜在 KSP 里用「相对 GameData、正斜杠、不带扩展名」的路径引用，例如 <c>Squad/Flags/default</c>；
/// 本工具导入的旗帜落在 <c>GameData/CkanUI/Flags</c>，避免和官方 / 模组目录混在一起。
/// </remarks>
public sealed class FlagLibraryService : IFlagLibraryService
{
    /// <summary>旗帜目录名。</summary>
    public const string FlagsDirectoryName = "Flags";

    /// <summary>本工具自有旗帜目录（相对 GameData）。</summary>
    public const string OwnedFlagsRelativePath = "CkanUI/Flags";

    /// <summary>默认旗帜的配置键。</summary>
    public const string DefaultFlagKey = "FLAG";

    /// <summary>游戏设置文件名。</summary>
    public const string SettingsFileName = "settings.cfg";

    private static readonly string[] SupportedExtensions = [".png", ".jpg", ".jpeg"];

    private readonly IErrorReporter _errors;

    /// <summary>构造服务。</summary>
    /// <param name="errors">错误上报口。</param>
    public FlagLibraryService(IErrorReporter errors) =>
        _errors = errors ?? throw new ArgumentNullException(nameof(errors));

    /// <inheritdoc />
    public Task<OperationResult<IReadOnlyList<FlagAsset>>> ScanAsync(string gameDir, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameDir);
        return Task.Run(() => ScanCore(gameDir, cancellationToken), cancellationToken);
    }

    /// <inheritdoc />
    public OperationResult<string?> ReadDefaultFlag(string gameDir)
    {
        try
        {
            var path = Path.Combine(gameDir, SettingsFileName);
            if (!File.Exists(path))
            {
                return OperationResult<string?>.Ok(null);
            }

            foreach (var line in File.ReadLines(path))
            {
                if (TryReadKey(line, DefaultFlagKey, out var value))
                {
                    return OperationResult<string?>.Ok(value);
                }
            }

            return OperationResult<string?>.Ok(null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            _errors.Report(ex, "旗帜库", "读取默认旗帜失败");
            return OperationResult<string?>.Fail("读取默认旗帜失败。", ex.Message, retryable: true);
        }
    }

    /// <inheritdoc />
    public Task<OperationResult> SetDefaultFlagAsync(string gameDir, FlagAsset flag, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(flag);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(WriteDefaultFlag(gameDir, flag.ConfigPath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            _errors.Report(ex, "旗帜库", "设置默认旗帜失败");
            return Task.FromResult(OperationResult.Fail("设置默认旗帜失败。", ex.Message, retryable: true));
        }
    }

    /// <inheritdoc />
    public Task<OperationResult<FlagAsset>> ImportAsync(string gameDir, string sourceFile, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameDir);
        if (!IsSupported(sourceFile))
        {
            return Task.FromResult(OperationResult<FlagAsset>.Fail("只支持 png / jpg / jpeg 图片。"));
        }

        if (!File.Exists(sourceFile))
        {
            return Task.FromResult(OperationResult<FlagAsset>.Fail("找不到要导入的图片。"));
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var dir = Path.Combine(gameDir, "GameData", OwnedFlagsRelativePath);
            Directory.CreateDirectory(dir);
            var name = UniqueName(dir, Path.GetFileNameWithoutExtension(sourceFile));
            var extension = Path.GetExtension(sourceFile);
            var target = Path.Combine(dir, name + extension);
            File.Copy(sourceFile, target, overwrite: false);
            return Task.FromResult(OperationResult<FlagAsset>.Ok(Create(gameDir, target)));
        }
        catch (OperationCanceledException)
        {
            return Task.FromResult(OperationResult<FlagAsset>.Fail("导入旗帜已取消。"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            _errors.Report(ex, "旗帜库", "导入旗帜失败");
            return Task.FromResult(OperationResult<FlagAsset>.Fail("导入旗帜失败。", ex.Message, retryable: true));
        }
    }

    /// <inheritdoc />
    public Task<OperationResult<FlagAsset>> RenameAsync(string gameDir, FlagAsset flag, string newName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(flag);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var dir = Path.GetDirectoryName(flag.FilePath)!;
            var desired = Sanitize(newName);
            if (string.Equals(desired, flag.Name, StringComparison.Ordinal))
            {
                return Task.FromResult(OperationResult<FlagAsset>.Ok(flag));
            }

            var name = UniqueName(dir, desired, allowExisting: flag.Name);
            var target = Path.Combine(dir, name + Path.GetExtension(flag.FilePath));
            File.Move(flag.FilePath, target);

            var renamed = Create(gameDir, target);
            var current = ReadDefaultFlag(gameDir);
            if (current.IsSuccess && string.Equals(current.Value, flag.ConfigPath, StringComparison.OrdinalIgnoreCase))
            {
                WriteDefaultFlag(gameDir, renamed.ConfigPath);
            }

            return Task.FromResult(OperationResult<FlagAsset>.Ok(renamed));
        }
        catch (OperationCanceledException)
        {
            return Task.FromResult(OperationResult<FlagAsset>.Fail("重命名旗帜已取消。"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            _errors.Report(ex, "旗帜库", "重命名旗帜失败");
            return Task.FromResult(OperationResult<FlagAsset>.Fail("重命名旗帜失败。", ex.Message, retryable: true));
        }
    }

    /// <inheritdoc />
    public Task<OperationResult> DeleteAsync(string gameDir, FlagAsset flag, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(flag);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(flag.FilePath))
            {
                File.Delete(flag.FilePath);
            }

            return Task.FromResult(OperationResult.Ok());
        }
        catch (OperationCanceledException)
        {
            return Task.FromResult(OperationResult.Fail("删除旗帜已取消。"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            _errors.Report(ex, "旗帜库", "删除旗帜失败");
            return Task.FromResult(OperationResult.Fail("删除旗帜失败。", ex.Message, retryable: true));
        }
    }

    private OperationResult<IReadOnlyList<FlagAsset>> ScanCore(string gameDir, CancellationToken cancellationToken)
    {
        try
        {
            var gameData = Path.Combine(gameDir, "GameData");
            if (!Directory.Exists(gameData))
            {
                return OperationResult<IReadOnlyList<FlagAsset>>.Ok([]);
            }

            var flags = new List<FlagAsset>();
            foreach (var file in Directory.EnumerateFiles(gameData, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsSupported(file))
                {
                    continue;
                }

                var relative = Path.GetRelativePath(gameData, file);
                var segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (!segments[..^1].Any(segment => string.Equals(segment, FlagsDirectoryName, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                flags.Add(Create(gameDir, file));
            }

            flags.Sort(static (left, right) => string.Compare(left.ConfigPath, right.ConfigPath, StringComparison.OrdinalIgnoreCase));
            return OperationResult<IReadOnlyList<FlagAsset>>.Ok(flags);
        }
        catch (OperationCanceledException)
        {
            return OperationResult<IReadOnlyList<FlagAsset>>.Fail("扫描旗帜已取消。");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            _errors.Report(ex, "旗帜库", "扫描旗帜失败");
            return OperationResult<IReadOnlyList<FlagAsset>>.Fail("扫描旗帜失败。", ex.Message, retryable: true);
        }
    }

    private static FlagAsset Create(string gameDir, string filePath)
    {
        var gameData = Path.Combine(gameDir, "GameData");
        var relative = Path.GetRelativePath(gameData, filePath);
        var configPath = Path.ChangeExtension(relative, null)!.Replace('\\', '/');
        var firstSegment = configPath.Split('/')[0];
        var info = new FileInfo(filePath);
        return new FlagAsset(
            Path.GetFileNameWithoutExtension(filePath),
            filePath,
            configPath,
            firstSegment,
            info.Exists ? info.Length : 0);
    }

    private OperationResult WriteDefaultFlag(string gameDir, string configPath)
    {
        var path = Path.Combine(gameDir, SettingsFileName);
        var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : [];
        var replaced = false;
        for (var i = 0; i < lines.Count; i++)
        {
            if (TryReadKey(lines[i], DefaultFlagKey, out _))
            {
                lines[i] = string.Create(CultureInfo.InvariantCulture, $"{DefaultFlagKey} = {configPath}");
                replaced = true;
                break;
            }
        }

        if (!replaced)
        {
            var insertAt = lines.FindIndex(line => line.TrimStart().StartsWith("SETTINGS_FILE_VERSION", StringComparison.OrdinalIgnoreCase));
            var entry = string.Create(CultureInfo.InvariantCulture, $"{DefaultFlagKey} = {configPath}");
            if (insertAt >= 0)
            {
                lines.Insert(insertAt + 1, entry);
            }
            else
            {
                if (lines.Count == 0)
                {
                    lines.Add("// KSP Game Settings");
                }

                lines.Add(entry);
            }
        }

        WriteAtomic(path, lines);
        return OperationResult.Ok();
    }

    private static void WriteAtomic(string path, IReadOnlyList<string> lines)
    {
        var temp = path + ".tmp";
        File.WriteAllLines(temp, lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temp, path, overwrite: true);
    }

    private static bool TryReadKey(string line, string key, out string value)
    {
        value = string.Empty;
        var trimmed = line.TrimStart();
        if (!trimmed.StartsWith(key, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var rest = trimmed[key.Length..].TrimStart();
        if (!rest.StartsWith('='))
        {
            return false;
        }

        value = rest[1..].Trim();
        return true;
    }

    private static bool IsSupported(string path) =>
        !string.IsNullOrWhiteSpace(path)
        && SupportedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private static string Sanitize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "未命名";
        }

        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(name.Length);
        foreach (var ch in name.Trim())
        {
            builder.Append(invalid.Contains(ch) ? '_' : ch);
        }

        var result = builder.ToString().Trim();
        return result.Length == 0 ? "未命名" : result;
    }

    private static string UniqueName(string dir, string desired, string? allowExisting = null)
    {
        var candidate = desired;
        var index = 2;
        while (true)
        {
            var exists = SupportedExtensions.Any(extension => File.Exists(Path.Combine(dir, candidate + extension)));
            if (!exists || string.Equals(candidate, allowExisting, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }

            candidate = string.Create(CultureInfo.InvariantCulture, $"{desired} ({index})");
            index++;
        }
    }
}
