using System.IO.Compression;
using System.Text;

using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Services.ModPacks;

/// <summary>
/// 模组列表导入导出的纯逻辑层（与 <c>RepositoryCatalog</c> 同构）：所有分支都在这里，
/// Core 只在 <see cref="IModPackStore"/> 后面。
/// </summary>
/// <remarks>
/// 三个公开能力：
/// <list type="bullet">
/// <item>导出：校验目标路径 → 取已装模组（剔除自动安装项）→ 由 Core 生成元数据包 JSON → 落盘 → 可选打包缓存 zip。</item>
/// <item>预览：逐个解析 .ckan → 与已装模组、仓库索引比对 → 分类成会装/会升/会降/已装同版本/找不到/冲突，问题行排前面。</item>
/// <item>比对：两个实例的已装模组做全外连接，输出仅 A 有 / 仅 B 有 / 版本不同，顺序稳定。</item>
/// </list>
/// 失败一律返回 <see cref="OperationResult{T}"/> 的中文文案（可重试标记只在真有重试可能时置位）。
/// </remarks>
public sealed class ModPackCatalog
{
    /// <summary>没有实例时的统一文案（与 <c>ChangeSetService</c> 的口径一致）。</summary>
    public const string NoInstanceMessage = "当前没有选中的游戏实例，请先选择一个实例。";

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly IModPackStore _store;

    /// <summary>构造目录逻辑层。</summary>
    public ModPackCatalog(IModPackStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <summary>是否有当前实例。</summary>
    public bool HasActiveInstance => _store.HasActiveInstance;

    /// <summary>当前实例名。</summary>
    public string? InstanceName => _store.InstanceName;

    /// <summary>
    /// 导出当前实例的已装模组为 .ckan 元数据包。
    /// 元数据包的字段语义完全交给上游 <c>RegistryManager.GenerateModpack</c>（F-45 / UI-24：
    /// 导出的文件必须能被 <c>ckan import</c> 接受），本方法只负责选条目、校验路径、落盘与可选打包。
    /// </summary>
    public OperationResult<ModPackExportResult> Export(ModPackExportRequest request)
    {
        if (request is null)
        {
            return FailExport("导出请求不能为空。");
        }

        if (!_store.HasActiveInstance)
        {
            return FailExport(NoInstanceMessage, retryable: true);
        }

        var path = NormalizeTargetPath(request.TargetPath, out var pathError);
        if (path is null)
        {
            return FailExport(pathError!);
        }

        IReadOnlyList<InstalledModuleInfo> installed;
        try
        {
            installed = _store.InstalledModules();
        }
        catch (Exception exception)
        {
            return FailExport("读取当前实例的已安装模组失败。", exception.Message, retryable: true);
        }

        var exportable = installed
            .Where(module => !module.IsAutoInstalled
                             && !string.IsNullOrWhiteSpace(module.Identifier)
                             && !string.IsNullOrWhiteSpace(module.Version))
            .OrderBy(module => module.Identifier, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (exportable.Count == 0)
        {
            return FailExport("当前实例还没有已安装的模组，没有可导出的内容。");
        }

        string json;
        try
        {
            json = _store.BuildModPackJson(request.UseRecommends, request.WithVersions);
        }
        catch (Exception exception)
        {
            return FailExport("生成元数据包失败。", exception.Message, retryable: true);
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            return FailExport("生成的元数据包为空，已取消导出。");
        }

        try
        {
            File.WriteAllText(path, json, Utf8NoBom);
        }
        catch (IOException exception)
        {
            return FailExport($"写入导出文件失败：{Path.GetFileName(path)} 可能正被占用。", exception.Message, retryable: true);
        }
        catch (UnauthorizedAccessException exception)
        {
            return FailExport($"没有权限写入导出文件：{path}", exception.Message, retryable: true);
        }
        catch (Exception exception)
        {
            return FailExport("写入导出文件失败。", exception.Message);
        }

        var fileBytes = TryLength(path);
        var warnings = new List<string>();
        string? archivePath = null;
        var bundledCount = 0;
        long bundleBytes = 0;

        if (request.BundleDownloads)
        {
            var archives = new List<string>();
            foreach (var module in exportable)
            {
                string? archive;
                try
                {
                    archive = _store.CachedArchive(module.Identifier, module.Version);
                }
                catch (Exception)
                {
                    archive = null;
                }

                if (string.IsNullOrWhiteSpace(archive) || !File.Exists(archive))
                {
                    warnings.Add($"模组 {module.Identifier} 的下载包不在缓存里，未打进归档。");
                    continue;
                }

                archives.Add(archive);
            }

            archivePath = Path.ChangeExtension(path, ".zip");
            try
            {
                using var zip = ZipFile.Open(archivePath, ZipArchiveMode.Create);
                zip.CreateEntryFromFile(path, Path.GetFileName(path));
                foreach (var archive in archives)
                {
                    zip.CreateEntryFromFile(archive, "Downloads/" + Path.GetFileName(archive));
                    bundleBytes += TryLength(archive);
                    bundledCount++;
                }
            }
            catch (Exception exception)
            {
                return FailExport($"元数据包已写入 {Path.GetFileName(path)}，但打包 ZIP 失败。", exception.Message, retryable: true);
            }

            if (bundledCount == 0)
            {
                warnings.Add("缓存里没有可用的下载包，归档里只有元数据包本身。");
            }
        }

        return OperationResult<ModPackExportResult>.Ok(new ModPackExportResult(
            path,
            exportable.Count,
            archivePath,
            bundledCount,
            bundleBytes,
            fileBytes,
            warnings));
    }

    /// <summary>
    /// 预览一组 .ckan 文件的效果：绝不落盘、绝不静默安装，问题条目全部列出来。
    /// </summary>
    public OperationResult<ModPackPreview> Preview(IReadOnlyCollection<string> sourcePaths)
    {
        if (sourcePaths is null || sourcePaths.Count == 0)
        {
            return FailPreview("请至少选择一个要导入的 .ckan 文件。");
        }

        var warnings = new List<string>();
        var files = new List<ModPackFile>();
        foreach (var raw in sourcePaths)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                warnings.Add("忽略了一个空路径。");
                continue;
            }

            string full;
            try
            {
                full = Path.GetFullPath(raw.Trim());
            }
            catch (Exception)
            {
                warnings.Add($"跳过无效路径：{raw.Trim()}");
                continue;
            }

            if (!File.Exists(full))
            {
                warnings.Add($"跳过不存在的文件：{full}");
                continue;
            }

            if (!full.EndsWith(".ckan", StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add($"跳过不是 .ckan 的文件：{Path.GetFileName(full)}");
                continue;
            }

            try
            {
                var file = _store.LoadModPackFile(full);
                if (file is null)
                {
                    warnings.Add($"无法解析元数据包：{Path.GetFileName(full)}");
                    continue;
                }

                files.Add(file);
            }
            catch (Exception exception)
            {
                warnings.Add($"无法解析 {Path.GetFileName(full)}：{exception.Message}");
            }
        }

        if (files.Count == 0)
        {
            return FailPreview("没有可导入的 .ckan 文件。",
                               warnings.Count == 0 ? null : string.Join("；", warnings));
        }

        var installed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var module in _store.InstalledModules())
            {
                if (!string.IsNullOrWhiteSpace(module.Identifier))
                {
                    installed[module.Identifier.Trim()] = module.Version;
                }
            }
        }
        catch (Exception exception)
        {
            return FailPreview("读取当前实例的已安装模组失败。", exception.Message, retryable: true);
        }

        var entries = new List<ModPackEntry>();
        var conflicts = new List<string>();
        var declared = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var declaredSources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            foreach (var reference in file.Entries)
            {
                if (string.IsNullOrWhiteSpace(reference.Identifier))
                {
                    continue;
                }

                var identifier = reference.Identifier.Trim();
                installed.TryGetValue(identifier, out var installedVersion);

                if (declared.TryGetValue(identifier, out var previousVersion))
                {
                    if (ModPackVersions.AreSame(previousVersion, reference.Version))
                    {
                        // 同版本重复声明：只保留一行，避免预览表出现重复项。
                        continue;
                    }

                    conflicts.Add($"{identifier} 在 {declaredSources[identifier]} 与 {file.FileName} 中被要求了不同版本。");
                    entries.Add(new ModPackEntry(
                        identifier,
                        file.FileName,
                        reference.Version,
                        installedVersion,
                        null,
                        ModPackEntryState.Conflict,
                        "不同来源要求的版本不一致，已跳过。"));
                    continue;
                }

                declared[identifier] = reference.Version;
                declaredSources[identifier] = file.FileName;
                entries.Add(Classify(identifier, file.FileName, reference, installedVersion));
            }
        }

        var ordered = entries
            .OrderBy(entry => Severity(entry.State))
            .ThenBy(entry => entry.Identifier, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var name = files.Count == 1
            ? (string.IsNullOrWhiteSpace(files[0].Name) ? files[0].FileName : files[0].Name)
            : $"{files.Count} 个元数据包";

        return OperationResult<ModPackPreview>.Ok(new ModPackPreview(
            files.Select(file => file.FilePath).ToList(),
            name,
            ordered,
            conflicts,
            warnings));
    }

    /// <summary>比对两个实例的模组差异（纯读取，不改任何实例）。</summary>
    public OperationResult<InstanceComparison> Compare(string leftInstance, string rightInstance)
    {
        if (string.IsNullOrWhiteSpace(leftInstance) || string.IsNullOrWhiteSpace(rightInstance))
        {
            return FailCompare("请选择要对比的两个实例。");
        }

        var left = leftInstance.Trim();
        var right = rightInstance.Trim();
        if (string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
        {
            return FailCompare("请选择两个不同的实例进行对比。");
        }

        IReadOnlyList<InstalledModuleInfo> leftModules;
        try
        {
            leftModules = _store.InstalledModulesIn(left);
        }
        catch (Exception exception)
        {
            return FailCompare($"读取实例「{left}」的模组列表失败。", exception.Message, retryable: true);
        }

        IReadOnlyList<InstalledModuleInfo> rightModules;
        try
        {
            rightModules = _store.InstalledModulesIn(right);
        }
        catch (Exception exception)
        {
            return FailCompare($"读取实例「{right}」的模组列表失败。", exception.Message, retryable: true);
        }

        return OperationResult<InstanceComparison>.Ok(
            new InstanceComparison(left, right, CompareSnapshots(leftModules, rightModules)));
    }

    /// <summary>
    /// 纯函数：两个已装模组快照的差异（仅左有 / 仅右有 / 版本不同）。标识按不区分大小写比较，
    /// 输出按标识排序，保证同一输入永远得到同一顺序。
    /// </summary>
    public static IReadOnlyList<InstanceDiffItem> CompareSnapshots(
        IReadOnlyList<InstalledModuleInfo> left,
        IReadOnlyList<InstalledModuleInfo> right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        var leftMap = ToMap(left);
        var rightMap = ToMap(right);
        var identifiers = new SortedSet<string>(leftMap.Keys, StringComparer.OrdinalIgnoreCase);
        identifiers.UnionWith(rightMap.Keys);

        var items = new List<InstanceDiffItem>();
        foreach (var identifier in identifiers)
        {
            leftMap.TryGetValue(identifier, out var leftVersion);
            rightMap.TryGetValue(identifier, out var rightVersion);
            if (leftVersion is not null && rightVersion is not null)
            {
                if (!ModPackVersions.AreSame(leftVersion, rightVersion))
                {
                    items.Add(new InstanceDiffItem(identifier, InstanceDiffKind.VersionDiffers, leftVersion, rightVersion));
                }

                continue;
            }

            items.Add(leftVersion is not null
                ? new InstanceDiffItem(identifier, InstanceDiffKind.OnlyInLeft, leftVersion, null)
                : new InstanceDiffItem(identifier, InstanceDiffKind.OnlyInRight, null, rightVersion));
        }

        return items;
    }

    private static Dictionary<string, string> ToMap(IReadOnlyList<InstalledModuleInfo> modules)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var module in modules)
        {
            if (!string.IsNullOrWhiteSpace(module.Identifier))
            {
                map[module.Identifier.Trim()] = module.Version ?? string.Empty;
            }
        }

        return map;
    }

    private ModPackEntry Classify(
        string identifier,
        string source,
        ModPackReference reference,
        string? installedVersion)
    {
        ModPackAvailability availability;
        try
        {
            availability = _store.Lookup(identifier, reference.Version);
        }
        catch (Exception exception)
        {
            return new ModPackEntry(
                identifier,
                source,
                reference.Version,
                installedVersion,
                null,
                ModPackEntryState.Missing,
                $"查询仓库索引失败：{exception.Message}");
        }

        if (!availability.Exists)
        {
            return new ModPackEntry(
                identifier,
                source,
                reference.Version,
                installedVersion,
                null,
                ModPackEntryState.Missing,
                "仓库索引里找不到这个模组。");
        }

        if (reference.Version is null)
        {
            return installedVersion is null
                ? new ModPackEntry(identifier, source, null, null, availability.LatestVersion, ModPackEntryState.Install, null)
                : new ModPackEntry(identifier, source, null, installedVersion, installedVersion, ModPackEntryState.SameVersion, "已安装（文件未锁定版本），将跳过。");
        }

        if (!availability.RequestedVersionExists)
        {
            return new ModPackEntry(
                identifier,
                source,
                reference.Version,
                installedVersion,
                null,
                ModPackEntryState.Conflict,
                $"仓库索引里没有 {reference.Version} 这个版本（最新 {availability.LatestVersion ?? "未知"}）。");
        }

        if (installedVersion is null)
        {
            return new ModPackEntry(identifier, source, reference.Version, null, reference.Version, ModPackEntryState.Install, null);
        }

        var comparison = ModPackVersions.Compare(reference.Version, installedVersion);
        if (comparison == 0)
        {
            return new ModPackEntry(identifier, source, reference.Version, installedVersion, installedVersion, ModPackEntryState.SameVersion, "已安装相同版本，将跳过。");
        }

        return comparison > 0
            ? new ModPackEntry(identifier, source, reference.Version, installedVersion, reference.Version, ModPackEntryState.Upgrade, null)
            : new ModPackEntry(identifier, source, reference.Version, installedVersion, reference.Version, ModPackEntryState.Downgrade, $"已安装更高的 {installedVersion}，本次会降级。");
    }

    private static int Severity(ModPackEntryState state) => state switch
    {
        ModPackEntryState.Conflict => 0,
        ModPackEntryState.Missing => 1,
        ModPackEntryState.Install => 2,
        ModPackEntryState.Upgrade => 3,
        ModPackEntryState.Downgrade => 4,
        _ => 5,
    };

    private static string? NormalizeTargetPath(string? targetPath, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            error = "导出路径不能为空。";
            return null;
        }

        var trimmed = targetPath.Trim();
        if (trimmed.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            error = "导出路径含有非法字符。";
            return null;
        }

        string full;
        try
        {
            full = Path.GetFullPath(trimmed);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            error = "导出路径无效。";
            return null;
        }

        var directory = Path.GetDirectoryName(full);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            error = $"导出目录不存在：{directory}";
            return null;
        }

        if (!full.EndsWith(".ckan", StringComparison.OrdinalIgnoreCase))
        {
            full += ".ckan";
        }

        return full;
    }

    private static long TryLength(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private static OperationResult<ModPackExportResult> FailExport(string message, string? detail = null, bool retryable = false)
        => OperationResult<ModPackExportResult>.Fail(message, detail, retryable);

    private static OperationResult<ModPackPreview> FailPreview(string message, string? detail = null, bool retryable = false)
        => OperationResult<ModPackPreview>.Fail(message, detail, retryable);

    private static OperationResult<InstanceComparison> FailCompare(string message, string? detail = null, bool retryable = false)
        => OperationResult<InstanceComparison>.Fail(message, detail, retryable);
}
