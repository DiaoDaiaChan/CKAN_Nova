using System.Globalization;
using System.Security;
using System.Text;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Assets;

namespace CkanUI.Services.Assets;

/// <summary>加载图库实现：目录清单（存在 <c>GameData/CkanUI/loading-image-folders.txt</c>）与目录里的图片。</summary>
/// <remarks>
/// 只扫目录第一层：LoadingScreenManager 默认不递归，递归扫 GameData 会把几十万张模组贴图拖进列表。
/// 清单写在实例自己的 GameData 里而不是全局设置里，这样换实例（RP-1 / 原版）各用各的目录。
/// </remarks>
public sealed class LoadingImageService : ILoadingImageService
{
    /// <summary>清单文件名（相对 <c>GameData/CkanUI</c>）。</summary>
    public const string FolderListFileName = "loading-image-folders.txt";

    private static readonly string[] SupportedExtensions = [".png", ".jpg", ".jpeg"];

    private static readonly string[] ModFolderNames = ["LoadingImages", "LoadingScreens"];

    private readonly IErrorReporter _errors;

    /// <summary>构造服务。</summary>
    /// <param name="errors">错误上报口。</param>
    public LoadingImageService(IErrorReporter errors) =>
        _errors = errors ?? throw new ArgumentNullException(nameof(errors));

    /// <inheritdoc />
    public IReadOnlyList<string> DefaultFolders(string gameDir)
    {
        var folders = new List<string>();
        void Add(string? path)
        {
            if (!string.IsNullOrWhiteSpace(path)
                && Directory.Exists(path)
                && !folders.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                folders.Add(path);
            }
        }

        Add(Path.Combine(gameDir, "Screenshots"));
        Add(Path.Combine(gameDir, "LoadingImages"));

        var gameData = Path.Combine(gameDir, "GameData");
        if (Directory.Exists(gameData))
        {
            try
            {
                foreach (var modDir in Directory.EnumerateDirectories(gameData, "*", SearchOption.TopDirectoryOnly))
                {
                    foreach (var name in ModFolderNames)
                    {
                        Add(Path.Combine(modDir, name));
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
            {
                _errors.Report(ex, "加载图库", "枚举模组加载图目录失败");
            }
        }

        return folders;
    }

    /// <inheritdoc />
    public OperationResult<IReadOnlyList<string>> LoadFolders(string gameDir)
    {
        try
        {
            var path = FolderListPath(gameDir);
            if (!File.Exists(path))
            {
                return OperationResult<IReadOnlyList<string>>.Ok(DefaultFolders(gameDir));
            }

            var folders = File.ReadAllLines(path)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return OperationResult<IReadOnlyList<string>>.Ok(folders.Length == 0 ? DefaultFolders(gameDir) : folders);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            _errors.Report(ex, "加载图库", "读取加载图目录清单失败");
            return OperationResult<IReadOnlyList<string>>.Fail("读取加载图目录清单失败。", ex.Message, retryable: true);
        }
    }

    /// <inheritdoc />
    public OperationResult SaveFolders(string gameDir, IReadOnlyList<string> folders)
    {
        ArgumentNullException.ThrowIfNull(folders);
        try
        {
            var path = FolderListPath(gameDir);
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var temp = path + ".tmp";
            File.WriteAllLines(temp, folders, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temp, path, overwrite: true);
            return OperationResult.Ok();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            _errors.Report(ex, "加载图库", "保存加载图目录清单失败");
            return OperationResult.Fail("保存加载图目录清单失败。", ex.Message, retryable: true);
        }
    }

    /// <inheritdoc />
    public Task<OperationResult<IReadOnlyList<LoadingImageAsset>>> ScanAsync(IReadOnlyList<string> folders, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(folders);
        return Task.Run(() => ScanCore(folders, cancellationToken), cancellationToken);
    }

    /// <inheritdoc />
    public Task<OperationResult<LoadingImageAsset>> ImportAsync(string targetFolder, string sourceFile, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(targetFolder))
        {
            return Task.FromResult(OperationResult<LoadingImageAsset>.Fail("请先选一个目录。"));
        }

        if (!IsSupportedImage(sourceFile))
        {
            return Task.FromResult(OperationResult<LoadingImageAsset>.Fail("只支持 png / jpg / jpeg 图片。"));
        }

        if (!File.Exists(sourceFile))
        {
            return Task.FromResult(OperationResult<LoadingImageAsset>.Fail("找不到要导入的图片。"));
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(targetFolder);
            var extension = Path.GetExtension(sourceFile);
            var name = UniqueName(targetFolder, Path.GetFileNameWithoutExtension(sourceFile), extension);
            var target = Path.Combine(targetFolder, name + extension);
            File.Copy(sourceFile, target, overwrite: false);
            return Task.FromResult(OperationResult<LoadingImageAsset>.Ok(Create(targetFolder, target)));
        }
        catch (OperationCanceledException)
        {
            return Task.FromResult(OperationResult<LoadingImageAsset>.Fail("导入加载图已取消。"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            _errors.Report(ex, "加载图库", "导入加载图失败");
            return Task.FromResult(OperationResult<LoadingImageAsset>.Fail("导入加载图失败。", ex.Message, retryable: true));
        }
    }

    /// <inheritdoc />
    public Task<OperationResult> DeleteAsync(LoadingImageAsset image, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(image.FilePath))
            {
                File.Delete(image.FilePath);
            }

            return Task.FromResult(OperationResult.Ok());
        }
        catch (OperationCanceledException)
        {
            return Task.FromResult(OperationResult.Fail("删除加载图已取消。"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            _errors.Report(ex, "加载图库", "删除加载图失败");
            return Task.FromResult(OperationResult.Fail("删除加载图失败。", ex.Message, retryable: true));
        }
    }

    /// <inheritdoc />
    public bool IsSupportedImage(string path) =>
        !string.IsNullOrWhiteSpace(path)
        && SupportedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private static string FolderListPath(string gameDir) =>
        Path.Combine(gameDir, "GameData", "CkanUI", FolderListFileName);

    private OperationResult<IReadOnlyList<LoadingImageAsset>> ScanCore(IReadOnlyList<string> folders, CancellationToken cancellationToken)
    {
        try
        {
            var images = new List<LoadingImageAsset>();
            foreach (var folder in folders)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                {
                    continue;
                }

                foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (IsSupportedImage(file))
                    {
                        images.Add(Create(folder, file));
                    }
                }
            }

            images.Sort(static (left, right) => right.ModifiedAt.CompareTo(left.ModifiedAt));
            return OperationResult<IReadOnlyList<LoadingImageAsset>>.Ok(images);
        }
        catch (OperationCanceledException)
        {
            return OperationResult<IReadOnlyList<LoadingImageAsset>>.Fail("扫描加载图已取消。");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            _errors.Report(ex, "加载图库", "扫描加载图失败");
            return OperationResult<IReadOnlyList<LoadingImageAsset>>.Fail("扫描加载图失败。", ex.Message, retryable: true);
        }
    }

    private static LoadingImageAsset Create(string folder, string filePath)
    {
        var info = new FileInfo(filePath);
        return new LoadingImageAsset(
            Path.GetFileName(filePath),
            filePath,
            folder,
            info.Exists ? info.Length : 0,
            info.Exists ? new DateTimeOffset(info.LastWriteTimeUtc) : DateTimeOffset.UtcNow);
    }

    private static string UniqueName(string dir, string desired, string extension)
    {
        var candidate = desired;
        var index = 2;
        while (File.Exists(Path.Combine(dir, candidate + extension)))
        {
            candidate = string.Create(CultureInfo.InvariantCulture, $"{desired} ({index})");
            index++;
        }

        return candidate;
    }
}
