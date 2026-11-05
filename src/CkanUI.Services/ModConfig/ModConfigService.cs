using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Services.Abstractions.ModConfig;

namespace CkanUI.Services.ModConfig;

/// <summary>
/// 把"adapter 声明 + 磁盘现状 + 注册表"合成页面需要的只读模型（CFG-02/CFG-03 的读侧）。
/// <para>
/// 这一层只读不写：任何写盘都必须走 <see cref="IAdapterWriter"/>（§3 的硬约束）。
/// 每个 adapter 的失败都就地降级成诊断，绝不让一个坏 adapter 拖垮整页。
/// </para>
/// </summary>
public sealed class ModConfigService : IModConfigService
{
    private readonly IAdapterCatalog _catalog;
    private readonly IInstanceService _instances;
    private readonly IRegistryService _registry;
    private readonly IGameRunningProbe _gameRunningProbe;

    /// <summary>上一次成功组装的缓存键（实例 + 目录 + 版本 + 注册表规模）。</summary>
    private string? _cachedKey;

    /// <summary>与 <see cref="_cachedKey"/> 配套的上一次结果。</summary>
    private OperationResult<ModConfigInstanceModel>? _cached;

    /// <summary>构造。</summary>
    /// <param name="catalog">适配器目录。</param>
    /// <param name="instances">实例服务（取游戏目录）。</param>
    /// <param name="registry">注册表（判定已装与版本）。</param>
    /// <param name="gameRunningProbe">运行探测；为 null 时用真实实现。</param>
    public ModConfigService(
        IAdapterCatalog catalog,
        IInstanceService instances,
        IRegistryService registry,
        IGameRunningProbe? gameRunningProbe = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(instances);
        ArgumentNullException.ThrowIfNull(registry);

        _catalog = catalog;
        _instances = instances;
        _registry = registry;
        _gameRunningProbe = gameRunningProbe ?? new InstanceGameRunningProbe(instances);

        // 注册表一变（安装/卸载/刷新），上次读出来的配置模型就可能过期，直接丢缓存。
        _registry.Changed += OnRegistryChanged;
    }

    private void OnRegistryChanged(RegistrySnapshot snapshot) => _cachedKey = null;


    /// <inheritdoc />
    public Task<OperationResult<ModConfigInstanceModel>> LoadAsync(
        string instanceName,
        bool forceReload = false,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        // 重复进同一个页面时不必重新扫适配器源文件：键里带上实例、目录、版本与注册表规模，
        // 任何一项变化都会自然落空；注册表事件额外把缓存清掉，防止规模相同内容不同。
        var key = BuildCacheKey(instanceName);
        if (!forceReload && _cachedKey == key && _cached is { } cached)
        {
            return Task.FromResult(cached);
        }

        var result = LoadCore(instanceName, ct);
        _cachedKey = key;
        _cached = result;
        return Task.FromResult(result);
    }

    private string BuildCacheKey(string instanceName)
    {
        var instance = FindInstance(instanceName);
        var installed = _registry.Current.Installed.Count;
        return string.Join('\u0001',
            instanceName,
            instance?.GameDir ?? "-",
            instance?.GameVersion ?? "-",
            installed.ToString(System.Globalization.CultureInfo.InvariantCulture),
            BuildAdapterFingerprint());
    }

    /// <summary>
    /// 适配器源文件的输入指纹（路径+长度+最后写入时间）：adapter.json 被改过就落空，
    /// 只 stat 这几个文件，成本与 11k 个 .cfg 的重扫不是一个量级。
    /// </summary>
    private string BuildAdapterFingerprint()
    {
        var parts = new List<string>();
        foreach (var adapter in _catalog.Adapters)
        {
            if (string.IsNullOrWhiteSpace(adapter.SourcePath))
            {
                continue;
            }

            try
            {
                var info = new FileInfo(adapter.SourcePath);
                parts.Add(info.Exists
                    ? $"{info.FullName}#{info.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)}#{info.LastWriteTimeUtc.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture)}"
                    : $"{adapter.SourcePath}#-");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                parts.Add($"{adapter.SourcePath}#?");
            }
        }

        return string.Join('\u0002', parts);
    }

    private OperationResult<ModConfigInstanceModel> LoadCore(string instanceName, CancellationToken ct)
    {
        var instance = FindInstance(instanceName);
        if (instance is null)
        {
            return OperationResult<ModConfigInstanceModel>.Fail(
                string.IsNullOrWhiteSpace(instanceName)
                    ? "当前没有选中的游戏实例。"
                    : $"找不到游戏实例「{instanceName}」。");
        }

        var gameDir = instance.GameDir;
        if (string.IsNullOrWhiteSpace(gameDir) || !Directory.Exists(gameDir))
        {
            return OperationResult<ModConfigInstanceModel>.Fail($"实例「{instance.Name}」的游戏目录不存在：{gameDir}");
        }

        var registry = _registry.Current;
        var gameRunning = SafeIsGameRunning(instance);

        var mods = new List<ModConfigModSnapshot>();
        var pageDiagnostics = new List<ModConfigDiagnostic>(_catalog.Diagnostics);
        var matchedIdentifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var adapter in _catalog.Adapters)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var mod = BuildMod(adapter, instance, gameDir, registry);
                if (mod is null)
                {
                    continue;
                }

                mods.Add(mod);
                foreach (var identifier in adapter.Match.CkanIdentifiers)
                {
                    matchedIdentifiers.Add(identifier);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                // 单个 adapter 读文件失败只影响它自己（T-CFG-06 的读侧对应项）。
                pageDiagnostics.Add(ModConfigDiagnostic.Error(
                    "adapter-load-failed",
                    $"加载适配器「{adapter.Id}」失败：{ex.Message}",
                    adapter.SourcePath));
            }
        }

        var adapterless = 0;
        foreach (var identifier in registry.Installed.Keys)
        {
            if (!matchedIdentifiers.Contains(identifier))
            {
                adapterless++;
            }
        }

        var model = new ModConfigInstanceModel(
            InstanceName: instance.Name,
            GameDir: gameDir,
            GameVersion: instance.GameVersion,
            IsGameRunning: gameRunning,
            Mods: mods,
            AdapterlessModCount: adapterless,
            Diagnostics: pageDiagnostics);

        return OperationResult<ModConfigInstanceModel>.Ok(model);
    }

    private ModConfigModSnapshot? BuildMod(
        ModConfigAdapterDescription adapter,
        InstanceInfo instance,
        string gameDir,
        RegistrySnapshot registry)
    {
        var installedVersion = FindInstalledVersion(adapter, registry);
        var registryHit = installedVersion is not null || IsRegistryHit(adapter, registry);

        // 注册表没命中时再按 adapter 声明的 match.paths 探一次：CKAN 里被手工安装、
        // 或 fork 换了标识符的 mod 只能靠磁盘痕迹认出来（15 篇 §4.1 match.paths）。
        var matchedByPath = false;
        if (!registryHit && adapter.Match.Paths.Count > 0)
        {
            foreach (var path in adapter.Match.Paths)
            {
                if (Exists(gameDir, path, adapter.Match.CaseInsensitive))
                {
                    matchedByPath = true;
                    registryHit = true;
                    break;
                }
            }
        }

        var files = new List<ModConfigFileSnapshot>(adapter.Files.Count);
        var anyExists = false;
        var anyCreatable = false;
        var anyWritable = false;
        var modifiedCount = 0;
        IReadOnlyList<ModConfigDiagnostic>? adapterDiagnostics = adapter.Diagnostics;

        if (matchedByPath)
        {
            adapterDiagnostics = AppendDiagnostic(adapterDiagnostics, ModConfigDiagnostic.Warning(
                "matched-by-path",
                $"CKAN 注册表里没有「{adapter.Name}」，按 match.paths 命中了磁盘文件（安装版本无法确认）",
                adapter.SourcePath));
        }

        foreach (var file in adapter.Files)
        {
            var fullPath = ResolvePath(gameDir, file.Path, adapter.Match.CaseInsensitive);
            var exists = File.Exists(fullPath);
            var fileState = ModConfigDetector.DetectFileState(file, exists);

            if (exists)
            {
                anyExists = true;
            }

            if (fileState == ModConfigFileState.CreatableByTemplate)
            {
                anyCreatable = true;
            }

            var fields = new List<ModConfigFieldState>();
            var isLossy = false;
            var errorCount = 0;

            if (exists && file.Layout != ModConfigFileLayout.Custom)
            {
                byte[] bytes;
                try
                {
                    bytes = File.ReadAllBytes(fullPath);
                }
                catch (IOException ex)
                {
                    adapterDiagnostics = AppendDiagnostic(
                        adapterDiagnostics,
                        ModConfigDiagnostic.Error("file-read-failed", $"读取 {file.Path} 失败：{ex.Message}", fullPath));
                    bytes = [];
                }

                if (bytes.Length > 0)
                {
                    var document = ConfigNodeDocument.ParseBytes(bytes, fullPath);
                    isLossy = document.IsTextLossy;
                    errorCount = document.ParseErrorCount;
                    fields = ReadFields(adapter, file, document, out var fileModified);
                    modifiedCount += fileModified;
                }
            }

            if (fileState == ModConfigFileState.Editable && errorCount == 0 && !isLossy)
            {
                anyWritable = true;
            }

            files.Add(new ModConfigFileSnapshot(
                FilePath: fullPath,
                RelativePath: file.Path,
                Layout: file.Layout,
                Mode: file.Mode,
                State: fileState,
                Fields: fields,
                ReadonlyReason: file.ReadonlyReason,
                IsTextLossy: isLossy,
                ParseErrorCount: errorCount,
                DisplayName: file.Label ?? file.DisplayName));
        }

        var state = ModConfigDetector.DetectAdapterState(
            registryHit: registryHit,
            anyDeclaredFileExists: anyExists,
            supportedVersionRange: adapter.ModVersionRange,
            installedVersion: installedVersion,
            anyCreatableFile: anyCreatable,
            anyWritableFile: anyWritable,
            out var stateDiagnostic);

        if (stateDiagnostic is not null)
        {
            adapterDiagnostics = AppendDiagnostic(adapterDiagnostics, stateDiagnostic);
        }

        if (state == ModConfigAdapterState.NotInstalled)
        {
            // 没装的 mod 不占卡片（§5），但也不算"无适配器"。
            return null;
        }

        return new ModConfigModSnapshot(
            AdapterId: adapter.Id,
            Name: adapter.Name,
            InstalledVersion: installedVersion,
            State: state,
            Files: files,
            ModifiedCount: modifiedCount,
            Homepage: adapter.Homepage,
            Diagnostics: adapterDiagnostics,
            SupportedVersionRangeText: adapter.ModVersionRange is null or { Count: 0 }
                ? null
                : ModConfigVersionRange.FormatForDisplay(adapter.ModVersionRange));
    }

    private static List<ModConfigFieldState> ReadFields(
        ModConfigAdapterDescription adapter,
        ModConfigFileDescription file,
        ConfigNodeDocument document,
        out int modifiedCount)
    {
        modifiedCount = 0;
        var fields = new List<ModConfigFieldState>();

        foreach (var field in adapter.Fields)
        {
            if (field.FileIndex != file.Index)
            {
                continue;
            }

            // 节点按字段解析，不再一文件一个：同一份配置里 MANEUVER_TOOL_DEFAULTS 和
            // COMMNET_THROTTLING_SETTINGS 是不同作用域，各读各的键。
            var nodePath = field.ResolveNodePath(file);
            var pair = document.FindPair(field.Key, nodePath);
            var value = pair is null ? null : Unquote(pair.Value);
            var comment = pair?.Comment;
            var group = pair is null ? null : ResolveGroup(document, pair.LineNumber, comment);

            if (field.DefaultValue is not null
                && value is not null
                && !ModConfigValueComparer.AreEqual(field.Type, value, field.DefaultValue))
            {
                modifiedCount++;
            }

            fields.Add(new ModConfigFieldState(
                Key: field.Key,
                Label: field.Label ?? field.Key,
                Type: field.Type,
                Value: value,
                Comment: comment,
                Group: group,
                DefaultValue: field.DefaultValue,
                Unit: field.Unit,
                Min: field.Min,
                Max: field.Max,
                Values: field.Values,
                Risk: field.Risk,
                RequiresRestart: field.RequiresRestart,
                NodePath: nodePath));
        }

        return fields;
    }

    /// <summary>
    /// 从键所在行向上找"分组标题"注释块（§6.2：<c>// ####</c> 这类分节注释生成可折叠分组）。
    /// 规则：注释块里以 <c>#</c> 开头的那一行是标题；中间允许隔空行；遇到非注释内容就停。
    /// </summary>
    private static string? ResolveGroup(ConfigNodeDocument document, int lineNumber, string? comment)
    {
        var fromAttached = FindHeadingLine(comment);
        if (fromAttached is not null)
        {
            return fromAttached;
        }

        var maxScan = 40;
        for (var line = lineNumber - 1; line >= 1 && maxScan-- > 0; line--)
        {
            var content = document.GetLineContent(line).Trim();
            if (content.Length == 0)
            {
                // 标题与键之间常隔着空行，继续往上找。
                continue;
            }

            if (!content.StartsWith("//", StringComparison.Ordinal) && !content.StartsWith('#'))
            {
                return null;
            }

            var heading = FindHeadingText(content);
            if (heading is not null)
            {
                return heading;
            }
        }

        return null;
    }

    private static string? FindHeadingLine(string? comment)
    {
        if (string.IsNullOrEmpty(comment))
        {
            return null;
        }

        foreach (var raw in comment.Split('\n'))
        {
            var heading = FindHeadingText(raw);
            if (heading is not null)
            {
                return heading;
            }
        }

        return null;
    }

    private static string? FindHeadingText(string rawLine)
    {
        var text = rawLine.Trim();
        if (text.StartsWith("//", StringComparison.Ordinal))
        {
            text = text[2..].Trim();
        }

        if (!text.StartsWith('#'))
        {
            return null;
        }

        // 去掉连续的 # 与残留符号，剩下的才是标题文字。
        var title = text.TrimStart('#').Trim().TrimEnd('#').Trim();
        return title.Length == 0 ? null : title;
    }

    private static string? FindInstalledVersion(ModConfigAdapterDescription adapter, RegistrySnapshot registry)
    {
        foreach (var identifier in adapter.Match.CkanIdentifiers)
        {
            if (registry.Installed.TryGetValue(identifier, out var module))
            {
                return module.Version;
            }
        }

        return null;
    }

    /// <summary>
    /// 把 adapter 里声明的相对路径解析成绝对路径。文件/目录在不区分大小写的文件系统上
    /// 实际大小写常与声明不同（Linux 打包的 mod 尤其常见），此时按段逐级找回真实名字。
    /// 找不到就返回按声明拼出的路径，交给调用方按"不存在"处理。
    /// </summary>
    private static string ResolvePath(string gameDir, string relativePath, bool caseInsensitive)
    {
        var declared = Path.GetFullPath(Path.Combine(gameDir, relativePath));
        if (!caseInsensitive || File.Exists(declared) || Directory.Exists(declared))
        {
            return declared;
        }

        var segments = relativePath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        var current = Path.GetFullPath(gameDir);
        foreach (var segment in segments)
        {
            var candidate = Path.Combine(current, segment);
            if (File.Exists(candidate) || Directory.Exists(candidate))
            {
                current = candidate;
                continue;
            }

            if (!Directory.Exists(current))
            {
                return declared;
            }

            string? actual = null;
            try
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(current))
                {
                    if (string.Equals(Path.GetFileName(entry), segment, StringComparison.OrdinalIgnoreCase))
                    {
                        actual = entry;
                        break;
                    }
                }
            }
            catch (IOException)
            {
                return declared;
            }
            catch (UnauthorizedAccessException)
            {
                return declared;
            }

            if (actual is null)
            {
                return declared;
            }

            current = actual;
        }

        return current;
    }

    private static bool Exists(string gameDir, string relativePath, bool caseInsensitive)
    {
        var fullPath = ResolvePath(gameDir, relativePath, caseInsensitive);
        return File.Exists(fullPath) || Directory.Exists(fullPath);
    }

    private static bool IsRegistryHit(ModConfigAdapterDescription adapter, RegistrySnapshot registry)
    {
        foreach (var identifier in adapter.Match.CkanIdentifiers)
        {
            if (registry.Installed.ContainsKey(identifier))
            {
                return true;
            }
        }

        return false;
    }

    private InstanceInfo? FindInstance(string instanceName)
    {
        if (string.IsNullOrWhiteSpace(instanceName))
        {
            return _instances.Current;
        }

        foreach (var instance in _instances.Instances)
        {
            if (string.Equals(instance.Name, instanceName, StringComparison.OrdinalIgnoreCase))
            {
                return instance;
            }
        }

        return null;
    }

    private bool SafeIsGameRunning(InstanceInfo instance)
    {
        try
        {
            return _gameRunningProbe.IsGameRunning(instance.Name, instance.GameDir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return false;
        }
    }

    private static IReadOnlyList<ModConfigDiagnostic> AppendDiagnostic(
        IReadOnlyList<ModConfigDiagnostic>? existing,
        ModConfigDiagnostic diagnostic)
    {
        var list = existing is null ? new List<ModConfigDiagnostic>() : [.. existing];
        list.Add(diagnostic);
        return list;
    }

    /// <summary>去掉值两侧的引号（写回时按需再加，见 ConfigNodeDocument.TryQuote）。</summary>
    private static string Unquote(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
        {
            return trimmed[1..^1];
        }

        return trimmed;
    }
}
