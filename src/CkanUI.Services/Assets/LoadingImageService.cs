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

    /// <summary>ROLoadingImages（RP-1 元安装自带）的来源名。</summary>
    public const string RoLoadingImagesProvider = "ROLoadingImages";

    /// <summary>LoadingScreenManager 的来源名。</summary>
    public const string LoadingScreenManagerProvider = "LoadingScreenManager";

    /// <summary>停用标记：加在扩展名后面，两个加载图模组都只认原扩展名，所以改了就不会被读。</summary>
    public const string DisabledSuffix = ".disabled";

    // .dds 是 ROLoadingImages 实际吃的格式：它只认这个目录里的 dds，没有别的开关。
    private static readonly string[] SupportedExtensions = [".png", ".jpg", ".jpeg", ".dds"];

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

        // ROLoadingImages 写死读 GameData/ROLoadingImages/PluginData/Screens（见其 LoadingImageReplacer），
        // 目录名不带 Loading 前缀，靠 ModFolderNames 匹配不到，这里单列。
        Add(Path.Combine(gameData, "ROLoadingImages", "PluginData", "Screens"));

        // LoadingScreenManager 把用户配置的目录写在 FOLDER 节点里，默认 Screenshots 与 UserLoadingScreens。
        foreach (var (folder, _) in ReadLoadingScreenManagerFolders(gameDir))
        {
            Add(folder);
        }

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
    public Task<OperationResult<IReadOnlyList<LoadingImageAsset>>> ScanAsync(
        IReadOnlyList<string> folders,
        IReadOnlyCollection<string>? recursiveFolders = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(folders);
        return Task.Run(() => ScanCore(folders, recursiveFolders, cancellationToken), cancellationToken);
    }

    /// <inheritdoc />
    public Task<OperationResult<LoadingImageAsset>> ImportAsync(string targetFolder, string sourceFile, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(targetFolder))
        {
            return Task.FromResult(OperationResult<LoadingImageAsset>.Fail("请先选一个目录。"));
        }

        if (!IsSupportedImage(sourceFile) || sourceFile.EndsWith(DisabledSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(OperationResult<LoadingImageAsset>.Fail("只支持 png / jpg / jpeg / dds 图片。"));
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
    public Task<OperationResult<LoadingImageAsset>> SetEnabledAsync(
        LoadingImageAsset image,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (image.IsEnabled == enabled)
        {
            return Task.FromResult(OperationResult<LoadingImageAsset>.Ok(image));
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(image.FilePath))
            {
                return Task.FromResult(OperationResult<LoadingImageAsset>.Fail("找不到这张加载图。"));
            }

            var target = enabled ? StripDisabled(image.FilePath) : image.FilePath + DisabledSuffix;
            if (File.Exists(target))
            {
                return Task.FromResult(OperationResult<LoadingImageAsset>.Fail("同名文件已存在，先处理它再切换启停。", target));
            }

            File.Move(image.FilePath, target);
            return Task.FromResult(OperationResult<LoadingImageAsset>.Ok(Create(image.Folder, target)));
        }
        catch (OperationCanceledException)
        {
            return Task.FromResult(OperationResult<LoadingImageAsset>.Fail("切换加载图启停已取消。"));
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException)
        {
            _errors.Report(ex, "加载图库", "切换加载图启停失败");
            return Task.FromResult(OperationResult<LoadingImageAsset>.Fail(
                "没有权限改这个文件：确认游戏已关闭、目录可写，或先用「在文件夹中打开」手动改名。",
                ex.Message,
                retryable: true));
        }
        catch (IOException ex)
        {
            _errors.Report(ex, "加载图库", "切换加载图启停失败");
            return Task.FromResult(OperationResult<LoadingImageAsset>.Fail(
                "文件正被占用或目录只读：关掉游戏和图片查看器后再试。",
                ex.Message,
                retryable: true));
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
    /// <remarks>停用的文件（<c>a.dds.disabled</c>）也算可管理的图片，否则停用后卡片会从列表里消失、再也点不回来。</remarks>
    public bool IsSupportedImage(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var effective = StripDisabled(path);
        return SupportedExtensions.Contains(Path.GetExtension(effective), StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, LoadingImageFolderInfo> DescribeFolders(string gameDir)
    {
        var folders = new Dictionary<string, LoadingImageFolderInfo>(StringComparer.OrdinalIgnoreCase);
        void Add(string? path, string? provider, bool recursive)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            try
            {
                folders[Normalize(path)] = new LoadingImageFolderInfo(provider, recursive);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                // 记不住路径就不标来源，扫描本身不受影响。
            }
        }

        var gameData = Path.Combine(gameDir, "GameData");
        Add(Path.Combine(gameData, "ROLoadingImages", "PluginData", "Screens"), RoLoadingImagesProvider, recursive: false);
        foreach (var (folder, recursive) in ReadLoadingScreenManagerFolders(gameDir))
        {
            Add(folder, LoadingScreenManagerProvider, recursive);
        }

        return folders;
    }

    private static string Normalize(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    /// <summary>
    /// 读 LoadingScreenManager 的 FOLDER 节点。
    /// </summary>
    /// <remarks>
    /// LSM 除了主配置 <c>LoadingScreenManager.cfg</c>，还会读同目录下其它 <c>*.cfg</c> 里的 <c>FOLDER</c> 节点做追加/覆盖，
    /// 所以这里枚举整个 PluginData 目录。不解析 <c>fileMasks</c>：扫描统一按支持的扩展名过滤。
    /// </remarks>
    private IReadOnlyList<(string Folder, bool Recursive)> ReadLoadingScreenManagerFolders(string gameDir)
    {
        var pluginDir = Path.Combine(gameDir, "GameData", "LoadingScreenManager", "PluginData");
        if (!Directory.Exists(pluginDir))
        {
            return [];
        }

        try
        {
            var folders = new List<(string Folder, bool Recursive)>();
            foreach (var config in Directory.EnumerateFiles(pluginDir, "*.cfg", SearchOption.TopDirectoryOnly))
            {
                string? path = null;
                var ignoreSubfolders = false;
                var insideFolder = false;
                foreach (var raw in File.ReadAllLines(config))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (!insideFolder)
                    {
                        insideFolder = line.Equals("FOLDER", StringComparison.OrdinalIgnoreCase);
                        continue;
                    }

                    if (line == "}")
                    {
                        AddFolder(folders, gameDir, path, ignoreSubfolders);
                        path = null;
                        ignoreSubfolders = false;
                        insideFolder = false;
                        continue;
                    }

                    var separator = line.IndexOf('=');
                    if (separator < 0)
                    {
                        continue;
                    }

                    var key = line[..separator].Trim();
                    var value = line[(separator + 1)..].Trim().Trim('"').Replace('\\', '/');
                    if (key.Equals("path", StringComparison.OrdinalIgnoreCase))
                    {
                        path = value;
                    }
                    else if (key.Equals("ignoreSubfolders", StringComparison.OrdinalIgnoreCase))
                    {
                        ignoreSubfolders = bool.TryParse(value, out var parsed) && parsed;
                    }
                }

                AddFolder(folders, gameDir, path, ignoreSubfolders);
            }

            return folders;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            _errors.Report(ex, "加载图库", "读取 LoadingScreenManager 配置失败");
            return [];
        }
    }

    private static void AddFolder(List<(string Folder, bool Recursive)> folders, string gameDir, string? path, bool ignoreSubfolders)
    {
        if (string.IsNullOrWhiteSpace(path) || path.StartsWith("//", StringComparison.Ordinal))
        {
            return;
        }

        var resolved = Path.IsPathRooted(path) ? path : Path.Combine(gameDir, path);
        if (Directory.Exists(resolved) && !folders.Any(item => string.Equals(item.Folder, resolved, StringComparison.OrdinalIgnoreCase)))
        {
            folders.Add((resolved, !ignoreSubfolders));
        }
    }

    private static string FolderListPath(string gameDir) =>
        Path.Combine(gameDir, "GameData", "CkanUI", FolderListFileName);

    private OperationResult<IReadOnlyList<LoadingImageAsset>> ScanCore(
        IReadOnlyList<string> folders,
        IReadOnlyCollection<string>? recursiveFolders,
        CancellationToken cancellationToken)
    {
        try
        {
            var recursive = new HashSet<string>(
                (recursiveFolders ?? []).Select(Normalize),
                StringComparer.OrdinalIgnoreCase);
            // 同一个名字的 a.dds 与 a.dds.disabled 同时存在时只留一张卡片（优先启用态），
            // 否则画廊里会出现两张同名图，用户分不清哪张是游戏真正在用的。
            var images = new Dictionary<string, LoadingImageAsset>(StringComparer.OrdinalIgnoreCase);
            foreach (var folder in folders)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                {
                    continue;
                }

                var option = recursive.Contains(Normalize(folder)) ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                foreach (var file in Directory.EnumerateFiles(folder, "*", option))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!IsSupportedImage(file))
                    {
                        continue;
                    }

                    var asset = Create(folder, file);
                    var key = StripDisabled(asset.FilePath);
                    if (!images.TryGetValue(key, out var existing) || (!existing.IsEnabled && asset.IsEnabled))
                    {
                        images[key] = asset;
                    }
                }
            }

            var ordered = images.Values.ToList();
            ordered.Sort(static (left, right) => right.ModifiedAt.CompareTo(left.ModifiedAt));
            return OperationResult<IReadOnlyList<LoadingImageAsset>>.Ok(ordered);
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
        var isEnabled = !filePath.EndsWith(DisabledSuffix, StringComparison.OrdinalIgnoreCase);
        return new LoadingImageAsset(
            Path.GetFileName(StripDisabled(filePath)),
            filePath,
            folder,
            info.Exists ? info.Length : 0,
            info.Exists ? new DateTimeOffset(info.LastWriteTimeUtc) : DateTimeOffset.UtcNow,
            Provider: null,
            IsEnabled: isEnabled);
    }

    /// <summary>去掉 <c>.disabled</c> 后缀，得到游戏会读的那个名字。</summary>
    /// <param name="path">文件路径。</param>
    private static string StripDisabled(string path) =>
        path.EndsWith(DisabledSuffix, StringComparison.OrdinalIgnoreCase)
            ? path[..^DisabledSuffix.Length]
            : path;

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
