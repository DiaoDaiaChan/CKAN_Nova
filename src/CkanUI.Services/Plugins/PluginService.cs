using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Services.Abstractions.Home;
using log4net;

namespace CkanUI.Services.Plugins;

/// <summary>
/// UI 插件宿主默认实现（12 篇 F-17 / 06 篇 UI-33）。
/// </summary>
/// <remarks>
/// <para>与原版 <c>CKAN.GUI.PluginController</c> 的差异，逐条都有理由：</para>
/// <list type="bullet">
/// <item><description><b>默认不激活</b>：原版 <c>LoadAssembly(dll, activate: true)</c> 启动即激活插件目录里的一切；
/// 这里 <see cref="DiscoverAsync"/> 只加载成 Dormant，必须用户在插件页显式点「激活」。</description></item>
/// <item><description><b>失败可见</b>：原版 <c>ActivatePlugin</c>/<c>DeactivatePlugin</c> 在 catch 里直接 <c>return</c>，
/// 用户没有任何反馈；这里全部折成 <see cref="PluginLoadResult"/>（中文原因 + 分类 + 技术细节）并留在 <see cref="Failures"/>。</description></item>
/// <item><description><b>同路径去重</b>：原版按「同名比版本」覆盖，容易把用户刚放进去的旧版静默丢掉；
/// 这里同一文件路径只允许加载一次（<see cref="PluginLoadFailure.AlreadyLoaded"/>），换版本请走重载或卸载后再加载。</description></item>
/// <item><description><b>释放</b>：每个插件一个可卸载 <c>AssemblyLoadContext</c>，失败路径与卸载路径都会 <c>Unload</c>。</description></item>
/// </list>
/// <para>线程口径：与 <c>CacheService</c> 一致，同步核心 + <c>Task.FromResult</c>。插件代码（构造、<c>Initialize</c>、
/// <c>Deinitialize</c>、元数据 getter）一律在锁外调用，避免插件回调宿主时自锁；表结构只在锁内改。</para>
/// </remarks>
public sealed class PluginService : IPluginService
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(PluginService));

    private readonly object _gate = new();
    private readonly Dictionary<string, PluginEntry> _loaded = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<PluginInfo> _failures = [];
    private readonly IPluginAssemblyLoader _loader;
    private readonly IPluginActivationStore _activation;
    private readonly Version _hostVersion;
    private readonly string _hostVersionText;

    /// <summary>用默认插件目录、默认内置目录与默认加载器构造（DI 用）。</summary>
    public PluginService()
        : this(DefaultPluginsDirectory())
    {
    }

    /// <summary>指定插件目录（默认加载器、默认内置目录、默认激活状态文件）。</summary>
    /// <param name="pluginsDirectory">插件目录的绝对或相对路径。</param>
    public PluginService(string pluginsDirectory)
        : this(pluginsDirectory, new AssemblyLoadContextPluginLoader(), null)
    {
    }

    /// <summary>测试与定制用构造：注入加载器与宿主版本。</summary>
    /// <param name="pluginsDirectory">插件目录。</param>
    /// <param name="loader">程序集加载器（测试可注入假实现）。</param>
    /// <param name="hostVersion">宿主版本字符串；<c>null</c> 时取本程序集版本。</param>
    public PluginService(string pluginsDirectory, IPluginAssemblyLoader loader, string? hostVersion = null)
        : this(pluginsDirectory, loader, hostVersion, DefaultBundledDirectory(), null)
    {
    }

    /// <summary>全量构造：额外指定内置插件目录与激活状态存储（DI 与测试用）。</summary>
    /// <param name="pluginsDirectory">用户插件目录。</param>
    /// <param name="loader">程序集加载器。</param>
    /// <param name="hostVersion">宿主版本字符串；<c>null</c> 时取本程序集版本。</param>
    /// <param name="bundledDirectory">
    /// 内置（首方）插件目录；<c>null</c> 或空白表示「没有内置目录」，此时只扫用户目录。
    /// </param>
    /// <param name="activationStore">激活状态存储；<c>null</c> 用默认路径（<c>%LocalAppData%/CKAN/plugin-activation.json</c>）。</param>
    /// <remarks>单独一个全量构造而不是继续加可选参数到上面那个：多一个可选参数会让既有
    /// <c>new PluginService(dir, loader, hostVersion)</c> 的调用点在阅读时分不清哪些参数被跳过了。</remarks>
    public PluginService(
        string pluginsDirectory,
        IPluginAssemblyLoader loader,
        string? hostVersion,
        string? bundledDirectory,
        IPluginActivationStore? activationStore)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginsDirectory);
        ArgumentNullException.ThrowIfNull(loader);

        PluginsDirectory = Path.GetFullPath(pluginsDirectory);
        BundledDirectory = string.IsNullOrWhiteSpace(bundledDirectory)
            ? string.Empty
            : Path.GetFullPath(bundledDirectory);
        _loader = loader;
        _activation = activationStore ?? new PluginActivationStore();
        (_hostVersion, _hostVersionText) = ResolveHostVersion(hostVersion);
    }

    /// <inheritdoc />
    public string PluginsDirectory { get; }

    /// <inheritdoc />
    public string BundledDirectory { get; }

    /// <inheritdoc />
    public IReadOnlyList<PluginInfo> Bundled
    {
        get
        {
            lock (_gate)
            {
                return _loaded.Values
                    .Where(entry => entry.Info.IsBundled)
                    .Select(entry => entry.Info)
                    .OrderBy(info => info.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<PluginInfo> Active => Snapshot(PluginState.Active);

    /// <inheritdoc />
    public IReadOnlyList<PluginInfo> Dormant => Snapshot(PluginState.Dormant);

    /// <inheritdoc />
    public IReadOnlyList<PluginInfo> Failures
    {
        get
        {
            lock (_gate)
            {
                return _failures
                    .OrderBy(info => info.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
        }
    }

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <summary>默认插件目录：<c>%LocalAppData%/CKAN/plugins</c>（与 CKAN 的配置目录同级）。</summary>
    public static string DefaultPluginsDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CKAN",
            "plugins");

    /// <summary>
    /// 默认内置插件目录：<c>AppContext.BaseDirectory/plugins</c>（宿主发布目录下的 plugins 子目录）。
    /// </summary>
    /// <remarks>安装目录只读时这里只是「建不出来 + 扫不到」，不会影响用户目录里的插件。</remarks>
    public static string DefaultBundledDirectory() => Path.Combine(AppContext.BaseDirectory, "plugins");

    /// <inheritdoc />
    public Task<OperationResult<IReadOnlyList<PluginInfo>>> DiscoverAsync(CancellationToken ct = default) =>
        DiscoverAsync(activateBundled: false, ct);

    /// <inheritdoc />
    public Task<OperationResult<IReadOnlyList<PluginInfo>>> DiscoverAsync(bool activateBundled, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            return Task.FromResult(OperationResult<IReadOnlyList<PluginInfo>>.Ok(DiscoverCore(activateBundled)));
        }
        catch (Exception ex)
        {
            Log.Error("扫描插件目录失败：" + PluginsDirectory, ex);
            return Task.FromResult(OperationResult<IReadOnlyList<PluginInfo>>.Fail(
                "扫描插件目录失败：" + ex.Message, ex.ToString(), retryable: true));
        }
    }

    /// <inheritdoc />
    public Task<PluginLoadResult> LoadAsync(string pluginPath, bool activate = false, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            return Task.FromResult(LoadCore(pluginPath, activate));
        }
        catch (Exception ex)
        {
            Log.Error("加载插件时发生未预期错误：" + pluginPath, ex);
            return Task.FromResult(PluginLoadResult.Fail(
                PluginLoadFailure.LoadFailed, "加载插件时发生未预期的错误：" + ex.Message, ex.ToString()));
        }
    }

    /// <inheritdoc />
    public Task<PluginLoadResult> ActivateAsync(string pluginId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            var entry = Find(pluginId);
            if (entry is null)
            {
                return Task.FromResult(PluginLoadResult.Fail(PluginLoadFailure.NotFound, "没有找到插件：" + pluginId));
            }

            return Task.FromResult(ActivateCore(entry));
        }
        catch (Exception ex)
        {
            Log.Error("激活插件时发生未预期错误：" + pluginId, ex);
            return Task.FromResult(PluginLoadResult.Fail(
                PluginLoadFailure.ActivationFailed, "激活插件时发生未预期的错误：" + ex.Message, ex.ToString()));
        }
    }

    /// <inheritdoc />
    public Task<OperationResult> DeactivateAsync(string pluginId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            var entry = Find(pluginId);
            if (entry is null)
            {
                return Task.FromResult(OperationResult.Fail("没有找到插件：" + pluginId));
            }

            return Task.FromResult(DeactivateCore(entry));
        }
        catch (Exception ex)
        {
            Log.Error("停用插件时发生未预期错误：" + pluginId, ex);
            return Task.FromResult(OperationResult.Fail("停用插件时发生未预期的错误：" + ex.Message, ex.ToString()));
        }
    }

    /// <inheritdoc />
    public Task<PluginLoadResult> ReloadAsync(string pluginId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            return Task.FromResult(ReloadCore(pluginId));
        }
        catch (Exception ex)
        {
            Log.Error("重载插件时发生未预期错误：" + pluginId, ex);
            return Task.FromResult(PluginLoadResult.Fail(
                PluginLoadFailure.LoadFailed, "重载插件时发生未预期的错误：" + ex.Message, ex.ToString()));
        }
    }

    /// <inheritdoc />
    public Task<OperationResult> UnloadAsync(string pluginId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            return Task.FromResult(UnloadCore(pluginId));
        }
        catch (Exception ex)
        {
            Log.Error("卸载插件时发生未预期错误：" + pluginId, ex);
            return Task.FromResult(OperationResult.Fail("卸载插件时发生未预期的错误：" + ex.Message, ex.ToString()));
        }
    }

    /// <inheritdoc />
    public Task<PluginLoadResult> AddFromPathAsync(string sourcePath, bool activate = false, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            return Task.FromResult(AddFromPathCore(sourcePath, activate));
        }
        catch (Exception ex)
        {
            Log.Error("新增插件时发生未预期错误：" + sourcePath, ex);
            return Task.FromResult(PluginLoadResult.Fail(
                PluginLoadFailure.LoadFailed, "新增插件时发生未预期的错误：" + ex.Message, ex.ToString()));
        }
    }

    /// <inheritdoc />
    public Task<OperationResult> InvokeToolItemAsync(string pluginId, string itemKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(pluginId) || string.IsNullOrWhiteSpace(itemKey))
        {
            return Task.FromResult(OperationResult.Fail("插件标识与工具项键都不能为空。"));
        }

        var entry = Find(pluginId.Trim());
        if (entry is null)
        {
            return Task.FromResult(OperationResult.Fail("插件「" + pluginId + "」还没有加载。"));
        }

        if (entry.Info.State != PluginState.Active)
        {
            return Task.FromResult(OperationResult.Fail(
                "插件「" + entry.Info.DisplayName + "」未激活，工具项不可用。"));
        }

        var item = entry.Info.ToolItems
            .FirstOrDefault(candidate => candidate is not null &&
                string.Equals(candidate.Key, itemKey.Trim(), StringComparison.OrdinalIgnoreCase));
        if (item is null)
        {
            return Task.FromResult(OperationResult.Fail(
                "插件「" + entry.Info.DisplayName + "」没有声明键为「" + itemKey + "」的工具项。"));
        }

        return InvokeToolItemCoreAsync(entry, item, ct);
    }

    /// <inheritdoc />
    public IReadOnlyList<PluginHomeWidgetBinding> ActiveHomeWidgets
    {
        get
        {
            lock (_gate)
            {
                return _loaded.Values
                    .Where(entry => entry.Info.State == PluginState.Active)
                    .SelectMany(entry => (entry.Info.HomeWidgets ?? Array.Empty<PluginHomeWidgetDescriptor>())
                        .Where(widget => widget is not null && !string.IsNullOrWhiteSpace(widget.Key))
                        .Select(widget => new PluginHomeWidgetBinding(entry.Info.Id, entry.Info.DisplayName, widget)))
                    .OrderBy(binding => binding.PluginName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(binding => binding.Widget.Title, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<PluginHomeWidgetSnapshot>> GetHomeWidgetAsync(
        string pluginId,
        string widgetKey,
        PluginHomeWidgetContext context,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(pluginId) || string.IsNullOrWhiteSpace(widgetKey))
        {
            return OperationResult<PluginHomeWidgetSnapshot>.Fail("插件标识与部件键都不能为空。");
        }

        var entry = Find(pluginId.Trim());
        if (entry is null)
        {
            return OperationResult<PluginHomeWidgetSnapshot>.Fail("插件「" + pluginId + "」还没有加载。");
        }

        if (entry.Info.State != PluginState.Active)
        {
            return OperationResult<PluginHomeWidgetSnapshot>.Fail("插件「" + entry.Info.DisplayName + "」未激活，主页部件不可用。");
        }

        var widget = entry.Info.HomeWidgets
            .FirstOrDefault(candidate => candidate is not null &&
                string.Equals(candidate.Key, widgetKey.Trim(), StringComparison.OrdinalIgnoreCase));
        if (widget is null)
        {
            return OperationResult<PluginHomeWidgetSnapshot>.Fail(
                "插件「" + entry.Info.DisplayName + "」没有声明键为「" + widgetKey + "」的主页部件。");
        }

        try
        {
            ct.ThrowIfCancellationRequested();
            var result = await entry.Load.Plugin.GetHomeWidgetAsync(widget.Key, context ?? new PluginHomeWidgetContext(), ct)
                .ConfigureAwait(false);
            return result;
        }
        catch (OperationCanceledException)
        {
            return OperationResult<PluginHomeWidgetSnapshot>.Fail("主页部件「" + widget.Title + "」已取消。");
        }
        catch (Exception ex)
        {
            Log.Error("插件主页部件读取失败：" + entry.Info.Id + "/" + widget.Key, ex);
            return OperationResult<PluginHomeWidgetSnapshot>.Fail(
                "主页部件「" + widget.Title + "」读取失败：" + ex.Message,
                ex.ToString(),
                retryable: true);
        }
    }

    private async Task<OperationResult> InvokeToolItemCoreAsync(
        PluginEntry entry,
        PluginToolItemDescriptor item,
        CancellationToken ct)
    {
        // 插件代码一律在锁外调用（与 ActivateCore 同口径）：插件回调里再问宿主不会自锁。
        try
        {
            ct.ThrowIfCancellationRequested();
            await entry.Load.Plugin.ExecuteToolItemAsync(item.Key, ct).ConfigureAwait(false);
            Log.Info("已执行插件工具项：" + entry.Info.Id + "/" + item.Key);
            return OperationResult.Ok();
        }
        catch (OperationCanceledException)
        {
            return OperationResult.Fail("工具项「" + item.Title + "」已取消。");
        }
        catch (Exception ex)
        {
            Log.Error("插件工具项执行失败：" + entry.Info.Id + "/" + item.Key, ex);
            return OperationResult.Fail(
                "工具项「" + item.Title + "」执行失败：" + ex.Message,
                ex.ToString(),
                retryable: true);
        }
    }

    private IReadOnlyList<PluginInfo> Snapshot(PluginState state)
    {
        lock (_gate)
        {
            return _loaded.Values
                .Where(entry => entry.Info.State == state)
                .Select(entry => entry.Info)
                .OrderBy(info => info.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    private IReadOnlyList<PluginInfo> Snapshot()
    {
        lock (_gate)
        {
            return _loaded.Values
                .Select(entry => entry.Info)
                .OrderBy(info => info.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    private PluginEntry? Find(string pluginId)
    {
        if (string.IsNullOrWhiteSpace(pluginId))
        {
            return null;
        }

        lock (_gate)
        {
            return _loaded.TryGetValue(pluginId, out var entry) ? entry : null;
        }
    }

    private PluginEntry? FindByPathLocked(string path) =>
        _loaded.Values.FirstOrDefault(entry => string.Equals(entry.Path, path, StringComparison.OrdinalIgnoreCase));

    private IReadOnlyList<PluginInfo> DiscoverCore(bool activateBundled)
    {
        EnsureDirectory();

        // 顺序即优先级：内置目录先扫，用户目录后扫。同一 Id 时先到先得（见 LoadCore 的 DuplicateId 分支），
        // 用户目录里那份会被 DiscoverOne 记成 DuplicateId 失败条目——否则它会「凭空消失」，用户无从解释。
        foreach (var file in EnumeratePluginFiles(BundledDirectory))
        {
            if (!IsLoadedByPath(file))
            {
                DiscoverOne(file, isBundled: true, activateBundled: activateBundled);
            }
        }

        foreach (var file in EnumeratePluginFiles(PluginsDirectory))
        {
            if (!IsLoadedByPath(file))
            {
                DiscoverOne(file, isBundled: false, activateBundled: false);
            }
        }

        return Snapshot();
    }

    /// <summary>
    /// 枚举目录顶层的 <c>*.dll</c>（按路径排序，结果稳定）。目录不存在或读不到时返回空数组并记日志。
    /// </summary>
    private static string[] EnumeratePluginFiles(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return [];
        }

        try
        {
            var files = Directory.GetFiles(directory, "*.dll", SearchOption.TopDirectoryOnly);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            return files;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("枚举插件目录失败：" + directory, ex);
            return [];
        }
    }

    private bool IsLoadedByPath(string path)
    {
        lock (_gate)
        {
            return FindByPathLocked(path) is not null;
        }
    }

    /// <summary>
    /// 发现阶段加载单个 dll，并按安全口径决定要不要激活它。
    /// </summary>
    /// <param name="file">dll 绝对路径。</param>
    /// <param name="isBundled">是否来自内置目录。</param>
    /// <param name="activateBundled">提供方是否要求激活内置插件（只对内置插件有意义）。</param>
    /// <remarks>
    /// 激活条件只有两个：<b>(a)</b> 用户上次显式激活过（持久化状态），且它来自内置目录；
    /// <b>(b)</b> 提供方显式传了 <c>activateBundled:true</c>，且它来自内置目录。
    /// 用户目录里的插件发现后永远是 Dormant，恢复激活态的活由 <see cref="PluginBootstrapper"/> 干——
    /// 那样「自动恢复」只发生在启动路径上，页面上的「刷新」永远只做发现，不会偷偷激活用户以为停用了的插件。
    /// </remarks>
    private void DiscoverOne(string file, bool isBundled, bool activateBundled)
    {
        var loaded = LoadCore(file, activate: false, isBundled);
        if (!loaded.IsSuccess || loaded.Plugin is null)
        {
            if (loaded.Failure == PluginLoadFailure.DuplicateId && loaded.Plugin is { IsBundled: true })
            {
                // 用户目录里那份与内置插件撞 Id：内置优先，但用户那份要留痕（否则它在插件页里凭空消失）。
                RecordFailure(
                    PluginLoadFailure.DuplicateId,
                    "用户插件目录里的「" + Path.GetFileName(file) + "」与内置插件「" + loaded.Plugin.DisplayName +
                    "」使用了同一个标识「" + loaded.Plugin.Id + "」，已跳过（内置插件优先）。",
                    null,
                    file);
            }

            return;
        }

        if (!isBundled)
        {
            return;
        }

        // 用户显式停用过的插件必须保持停用：activateBundled 是「宿主为自己带的插件背书」的默认值，
        // 不是覆盖用户决定的指令。否则在插件页点一次「停用」、下次启动又被自动拉起来——
        // 入口会周期性地出现又消失，比不能停用更让人摸不着头脑。
        var explicitChoice = _activation.Load().TryGetValue(loaded.Plugin.Id, out var persisted)
            ? persisted.Activated
            : (bool?)null;

        var shouldActivate = explicitChoice ?? activateBundled;
        if (!shouldActivate)
        {
            return;
        }

        var entry = Find(loaded.Plugin.Id);
        if (entry is not null)
        {
            ActivateCore(entry);
        }
    }

    private PluginLoadResult LoadCore(string pluginPath, bool activate, bool isBundled = false)
    {
        if (string.IsNullOrWhiteSpace(pluginPath))
        {
            return PluginLoadResult.Fail(PluginLoadFailure.NotFound, "没有指定插件文件路径。");
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(pluginPath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return PluginLoadResult.Fail(PluginLoadFailure.LoadFailed, "插件路径不合法：" + pluginPath, ex.ToString());
        }

        lock (_gate)
        {
            var existing = FindByPathLocked(fullPath);
            if (existing is not null)
            {
                return PluginLoadResult.Fail(
                    PluginLoadFailure.AlreadyLoaded,
                    "该文件已经加载为插件「" + existing.Info.DisplayName + "」，请先卸载或重载。",
                    plugin: existing.Info);
            }
        }

        var attempt = _loader.TryLoad(fullPath);
        var load = attempt.Load;
        if (load is null)
        {
            return RecordFailure(attempt.Failure, attempt.Message ?? "加载插件失败。", attempt.Detail, fullPath);
        }

        PluginInfo info;
        IReadOnlyList<PluginPageDescriptor> pages;
        IReadOnlyList<PluginToolItemDescriptor> toolItems;
        IReadOnlyList<PluginHomeWidgetDescriptor> homeWidgets;
        try
        {
            var plugin = load.Plugin;
            info = new PluginInfo(
                Trim(plugin.Id),
                Trim(plugin.Name),
                Trim(plugin.Version),
                Trim(plugin.Author),
                Trim(plugin.Description),
                Trim(plugin.MinimumHostVersion),
                fullPath,
                PluginState.Dormant)
            {
                IsBundled = isBundled,
            };
            pages = plugin.Pages ?? Array.Empty<PluginPageDescriptor>();
            toolItems = plugin.ToolItems ?? Array.Empty<PluginToolItemDescriptor>();
            homeWidgets = plugin.HomeWidgets ?? Array.Empty<PluginHomeWidgetDescriptor>();
        }
        catch (Exception ex)
        {
            load.Release();
            return RecordFailure(
                PluginLoadFailure.MetadataFailed,
                "读取插件元数据失败：" + Path.GetFileName(fullPath) + "（" + ex.Message + "）",
                ex.ToString(),
                fullPath);
        }

        if (string.IsNullOrEmpty(info.Id))
        {
            load.Release();
            return RecordFailure(PluginLoadFailure.LoadFailed, "插件没有提供唯一标识（Id）：" + Path.GetFileName(fullPath), null, fullPath);
        }

        if (string.IsNullOrEmpty(info.Name))
        {
            load.Release();
            return RecordFailure(PluginLoadFailure.MetadataFailed, "插件「" + info.Id + "」没有提供显示名（Name）。", null, fullPath);
        }

        if (RequiresNewerHost(info.MinimumHostVersion))
        {
            load.Release();
            return RecordFailure(
                PluginLoadFailure.HostVersionTooOld,
                "插件「" + info.Name + "」要求宿主版本 " + info.MinimumHostVersion + " 或更高，当前宿主为 " + _hostVersionText + "。",
                null,
                fullPath);
        }

        PluginEntry entry;
        lock (_gate)
        {
            if (_loaded.TryGetValue(info.Id, out var duplicate))
            {
                load.Release();
                return PluginLoadResult.Fail(
                    PluginLoadFailure.DuplicateId,
                    "插件标识「" + info.Id + "」已被「" + duplicate.Info.DisplayName + "」占用，请先卸载它。",
                    plugin: duplicate.Info);
            }

            entry = new PluginEntry(info with { Pages = pages, ToolItems = toolItems, HomeWidgets = homeWidgets }, load, fullPath);
            _loaded[info.Id] = entry;
            _failures.RemoveAll(failure => string.Equals(failure.AssemblyPath, fullPath, StringComparison.OrdinalIgnoreCase));
        }

        Log.Info("已加载插件：" + entry.Info.Id + "（" + entry.Info.DisplayName + "）from " + fullPath);
        RaiseChanged();

        if (!activate)
        {
            return PluginLoadResult.Ok(entry.Info, "已加载插件「" + entry.Info.DisplayName + "」（未激活）。");
        }

        return ActivateCore(entry);
    }

    private PluginLoadResult ActivateCore(PluginEntry entry)
    {
        if (entry.Info.State == PluginState.Active)
        {
            return PluginLoadResult.Ok(entry.Info, "插件「" + entry.Info.DisplayName + "」已经是激活状态。");
        }

        try
        {
            entry.Load.Plugin.Initialize();
        }
        catch (Exception ex)
        {
            Log.Error("插件初始化失败：" + entry.Info.Id, ex);
            return PluginLoadResult.Fail(
                PluginLoadFailure.ActivationFailed,
                "插件「" + entry.Info.DisplayName + "」初始化失败：" + ex.Message,
                ex.ToString(),
                entry.Info);
        }

        bool stillLoaded;
        lock (_gate)
        {
            stillLoaded = _loaded.ContainsKey(entry.Info.Id);
            if (stillLoaded)
            {
                entry.SetState(PluginState.Active);
            }
        }

        if (!stillLoaded)
        {
            return PluginLoadResult.Fail(
                PluginLoadFailure.LoadFailed,
                "插件「" + entry.Info.DisplayName + "」已被卸载，无法激活。",
                plugin: entry.Info);
        }

        Log.Info("已激活插件：" + entry.Info.Id);

        // 持久化：用户显式激活过 = 下次启动可以恢复。写失败只记日志（存储实现自己吞），
        // 本次激活已经生效，不因为一个 JSON 文件不可写就回滚激活状态。
        _activation.Save(entry.Info.Id, activated: true);
        RaiseChanged();
        return PluginLoadResult.Ok(entry.Info, "已激活插件「" + entry.Info.DisplayName + "」。");
    }

    private OperationResult DeactivateCore(PluginEntry entry)
    {
        if (entry.Info.State != PluginState.Active)
        {
            return OperationResult.Ok();
        }

        try
        {
            entry.Load.Plugin.Deinitialize();
        }
        catch (Exception ex)
        {
            Log.Error("插件停用失败：" + entry.Info.Id, ex);
            return OperationResult.Fail("停用插件「" + entry.Info.DisplayName + "」失败：" + ex.Message, ex.ToString());
        }

        lock (_gate)
        {
            if (_loaded.ContainsKey(entry.Info.Id))
            {
                entry.SetState(PluginState.Dormant);
            }
        }

        Log.Info("已停用插件：" + entry.Info.Id);

        // 持久化「用户显式停用」：下次启动不会被恢复成激活（默认不启用，停用就是停用）。
        _activation.Save(entry.Info.Id, activated: false);
        RaiseChanged();
        return OperationResult.Ok();
    }

    private OperationResult UnloadCore(string pluginId)
    {
        PluginEntry? entry;
        lock (_gate)
        {
            if (!_loaded.TryGetValue(pluginId, out entry))
            {
                return OperationResult.Fail("没有找到插件：" + pluginId);
            }

            _loaded.Remove(pluginId);
        }

        if (entry.Info.State == PluginState.Active)
        {
            try
            {
                entry.Load.Plugin.Deinitialize();
            }
            catch (Exception ex)
            {
                // 停用失败也要卸载：用户点的是「卸载」，卡在这里只会让插件永远走不掉。
                Log.Warn("停用插件失败（继续卸载）：" + pluginId, ex);
            }
        }

        entry.Load.Release();

        // 卸载 = 用户不希望它再被激活：清掉持久化标记，否则下次启动会被自动恢复成激活。
        // 重载路径（ReloadCore）会紧接着按原激活态重新 ActivateCore 并写回 true。
        _activation.Save(entry.Info.Id, activated: false);
        Log.Info("已卸载插件：" + pluginId);
        RaiseChanged();
        return OperationResult.Ok();
    }

    private PluginLoadResult ReloadCore(string pluginId)
    {
        var entry = Find(pluginId);
        if (entry is null)
        {
            return PluginLoadResult.Fail(PluginLoadFailure.NotFound, "没有找到插件：" + pluginId);
        }

        var path = entry.Path;
        var wasActive = entry.Info.State == PluginState.Active;

        var unloaded = UnloadCore(pluginId);
        if (!unloaded.IsSuccess)
        {
            return PluginLoadResult.Fail(PluginLoadFailure.LoadFailed, unloaded.Message ?? "卸载旧插件失败。", unloaded.Detail);
        }

        // 重载保持原激活状态：用户点「重载」是想让新代码生效，不是想把它停用。
        return LoadCore(path, wasActive);
    }

    private PluginLoadResult AddFromPathCore(string sourcePath, bool activate)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return PluginLoadResult.Fail(PluginLoadFailure.NotFound, "没有指定插件文件路径。");
        }

        string source;
        try
        {
            source = Path.GetFullPath(sourcePath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return PluginLoadResult.Fail(PluginLoadFailure.LoadFailed, "插件路径不合法：" + sourcePath, ex.ToString());
        }

        if (!File.Exists(source))
        {
            return PluginLoadResult.Fail(PluginLoadFailure.NotFound, "找不到插件文件：" + source);
        }

        if (!string.Equals(Path.GetExtension(source), ".dll", StringComparison.OrdinalIgnoreCase))
        {
            return PluginLoadResult.Fail(PluginLoadFailure.NotAnAssembly, "只能添加 .dll 插件文件：" + Path.GetFileName(source));
        }

        EnsureDirectory();
        var target = Path.Combine(PluginsDirectory, Path.GetFileName(source));
        if (!string.Equals(target, source, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                File.Copy(source, target, overwrite: true);
            }
            catch (Exception ex)
            {
                return RecordFailure(PluginLoadFailure.LoadFailed, "复制插件到插件目录失败：" + Path.GetFileName(source) + "（" + ex.Message + "）", ex.ToString(), target);
            }
        }

        PluginEntry? existing;
        lock (_gate)
        {
            existing = FindByPathLocked(target);
        }

        if (existing is not null)
        {
            // 同一个文件名再添加一次 = 用新文件替换旧插件。
            UnloadCore(existing.Info.Id);
        }

        return LoadCore(target, activate);
    }

    private PluginLoadResult RecordFailure(PluginLoadFailure failure, string message, string? detail, string assemblyPath)
    {
        var info = new PluginInfo(
            string.Empty,
            Path.GetFileNameWithoutExtension(assemblyPath),
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            assemblyPath,
            PluginState.Failed,
            message);

        lock (_gate)
        {
            _failures.RemoveAll(existing => string.Equals(existing.AssemblyPath, assemblyPath, StringComparison.OrdinalIgnoreCase));
            _failures.Add(info);
        }

        Log.Warn("插件加载失败（" + failure + "）：" + assemblyPath + " - " + message + (detail is null ? string.Empty : " | " + detail));
        RaiseChanged();
        return PluginLoadResult.Fail(failure, message, detail, info);
    }

    private void EnsureDirectory()
    {
        try
        {
            Directory.CreateDirectory(PluginsDirectory);
        }
        catch (Exception ex)
        {
            Log.Warn("创建插件目录失败：" + PluginsDirectory, ex);
        }
    }

    private bool RequiresNewerHost(string minimumHostVersion)
    {
        if (string.IsNullOrWhiteSpace(minimumHostVersion))
        {
            return false;
        }

        // 解析不了（例如 "1.2.0-beta"）视为不限制：宁可放行再让插件自己报错，也不要因为版本串格式把人挡在门外。
        return Version.TryParse(minimumHostVersion, out var required) && _hostVersion < required;
    }

    private void RaiseChanged()
    {
        var handler = Changed;
        if (handler is null)
        {
            return;
        }

        foreach (var subscriber in handler.GetInvocationList())
        {
            try
            {
                ((EventHandler)subscriber)(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                // 工程约定 §12.4：订阅方异常只记日志，不打断其它订阅方。
                Log.Warn("插件状态变更事件的订阅方抛出异常（已忽略）。", ex);
            }
        }
    }

    private static (Version Version, string Text) ResolveHostVersion(string? explicitVersion)
    {
        if (!string.IsNullOrWhiteSpace(explicitVersion) && Version.TryParse(explicitVersion.Trim(), out var parsed))
        {
            return (parsed, explicitVersion.Trim());
        }

        var assemblyVersion = typeof(PluginService).Assembly.GetName().Version ?? new Version(0, 0, 0, 0);
        return (assemblyVersion, assemblyVersion.ToString(3));
    }

    private static string Trim(string? value) => value?.Trim() ?? string.Empty;

    private sealed class PluginEntry
    {
        public PluginEntry(PluginInfo info, PluginAssemblyLoad load, string path)
        {
            Info = info;
            Load = load;
            Path = path;
        }

        public PluginInfo Info { get; private set; }

        public PluginAssemblyLoad Load { get; }

        public string Path { get; }

        public void SetState(PluginState state) => Info = Info with { State = state };
    }
}