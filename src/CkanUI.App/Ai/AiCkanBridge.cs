using CkanUI.Localization;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Ai;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Navigation;

namespace CkanUI.App.Ai;

/// <summary>
/// AI 与 CKAN 之间的宿主桥（宿主侧唯一实现）。
/// </summary>
/// <remarks>
/// <para>三条纪律，缺一条就会在启动早期或坏输入上炸界面：</para>
/// <list type="number">
/// <item><b>只读快照</b>：<see cref="IRepositoryService.Current"/> / <see cref="IRegistryService.Current"/> 都是内存里的
/// 对象图，这里不触发刷新、不读磁盘、不联网——AI 的每一次提问都不该变成一次仓库同步。</item>
/// <item><b>绝不抛异常</b>：AI 的话是外部输入。任一服务缺失、快照为空、实现方自己抛了，都只能降级成空列表 / 空上下文；
/// 唯一会动界面的动作是 <see cref="NavigateAsync(string, System.Threading.CancellationToken)"/>，它把失败折成
/// <see cref="OperationResult{T}"/> 而不是让调用方去 catch。</item>
/// <item><b>键与外壳一致</b>：<see cref="ListPages"/> 给出的键必须能在 <c>ServiceRegistration.PageTypes</c> 里找到，
/// 否则「帮我打开 XX 页」会导向一个不存在的页面。这条不变量由 <c>AiCkanBridgeTests</c> 锁住。</item>
/// </list>
/// <para>
/// 构造期不做任何 IO：所有服务都通过可空注入拿，解析时也允许为 <c>null</c>（插件宿主、设计时、单测）。
/// DI 里 <see cref="INavigationService"/> 的注册比这里晚也没关系——单例是惰性解析的，谁也不该在构造期要页面。
/// </para>
/// </remarks>
public sealed class AiCkanBridge : IAiCkanBridge
{
    /// <summary>搜索结果上限：AI 一次看不下更多，也挡住「把仓库当数据集导出」这种用法。</summary>
    public const int MaxSearchLimit = 50;

    /// <summary>已安装列表上限。</summary>
    public const int MaxInstalledLimit = 500;

    /// <summary>注册表没有快照时的空字典（不共享可变实例）。</summary>
    private static readonly IReadOnlyDictionary<string, InstalledModuleInfo> NoInstalled =
        new Dictionary<string, InstalledModuleInfo>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 宿主页面表：键与 <c>ShellViewModel._baseNavItems</c> 一致，顺序跟 <c>SectionSpecs</c> 的分节走。
    /// 插件页不在表里——插件可以停用，列进来就会留下点了没反应的键。
    /// </summary>
    private static readonly (string Key, string TitleKey, string FallbackTitle)[] HostPages =
    [
        ("home", LocalizationKeys.NavRailHome, "主页"),
        // 模组分节：装什么、装进去长什么样、哪些待应用、元数据打哪来、单个 mod 怎么配。
        ("mods", LocalizationKeys.NavRailManageMods, "模组"),
        ("content", LocalizationKeys.NavRailContent, "内容预览"),
        ("changes", LocalizationKeys.NavRailChanges, "变更集"),
        ("repos", LocalizationKeys.NavRailRepos, "仓库"),
        ("modconfig", LocalizationKeys.NavRailModConfig, "Mod 配置"),
        // 实例分节（存档 / 资产 / KSP 设置是插件页，不在这里）。
        ("instances", LocalizationKeys.NavRailInstances, "实例"),
        ("playtime", LocalizationKeys.NavRailPlayTime, "游戏时长"),
        ("unmanaged", LocalizationKeys.NavRailUnmanaged, "未管理文件"),
        ("installhistory", LocalizationKeys.NavRailInstallHistory, "安装历史"),
        // 整合包。
        ("modpacks", LocalizationKeys.NavRailModPacks, "导入导出"),
        ("modpack-editor", LocalizationKeys.NavRailModPackEditor, "modpack 编辑器"),
        // 数据分节。
        ("cache", LocalizationKeys.NavRailCache, "下载缓存"),
        ("downloadstats", LocalizationKeys.NavRailDownloadStats, "下载统计"),
        ("dedup", LocalizationKeys.NavRailDedup, "跨实例去重"),
        // 工具与其余宿主页。
        ("plugins", LocalizationKeys.NavRailPlugins, "插件"),
        ("logs", LocalizationKeys.NavRailLogs, "日志"),
        ("settings", LocalizationKeys.NavRailSettings, "设置"),
    ];

    private readonly IRepositoryService? _repository;
    private readonly IRegistryService? _registry;
    private readonly IInstanceService? _instances;
    private readonly INavigationService? _navigation;
    private readonly Func<string, string>? _translate;

    /// <summary>
    /// 全部依赖可选：宿主没装齐（单测、设计时、插件宿主）时桥退化成「查不到但也不炸」。
    /// </summary>
    /// <param name="repository">仓库索引快照。</param>
    /// <param name="registry">注册表快照（已安装）。</param>
    /// <param name="instances">当前实例。</param>
    /// <param name="navigation">页面导航（唯一会动界面的依赖）。</param>
    /// <param name="translate">取译文委托；为 <c>null</c> 时页面标题回落到内置中文。</param>
    public AiCkanBridge(
        IRepositoryService? repository = null,
        IRegistryService? registry = null,
        IInstanceService? instances = null,
        INavigationService? navigation = null,
        Func<string, string>? translate = null)
    {
        _repository = repository;
        _registry = registry;
        _instances = instances;
        _navigation = navigation;
        _translate = translate;
    }

    /// <inheritdoc />
    public AiBridgeContext GetContext()
    {
        var instance = TryRead(() => _instances?.Current);
        var repository = TryRead(() => _repository?.Current);
        var registry = TryRead(() => _registry?.Current);

        return new AiBridgeContext(
            InstanceName: Clean(instance?.Name) ?? string.Empty,
            GameDir: Clean(instance?.GameDir),
            KspVersion: Clean(instance?.GameVersion),
            InstalledCount: InstalledOf(registry).Count,
            AvailableCount: AvailableOf(repository).Count,
            RepositoryUpdatedAt: repository?.UpdatedAt,
            RepositoryFromCache: repository?.IsFromCache ?? true);
    }

    /// <inheritdoc />
    public IReadOnlyList<AiModSummary> SearchMods(string? query, int limit = 10)
    {
        var take = Math.Clamp(limit, 1, MaxSearchLimit);
        var rows = Available;
        if (rows.Count == 0)
        {
            return Array.Empty<AiModSummary>();
        }

        var installed = Installed;
        var needle = (query ?? string.Empty).Trim();

        IEnumerable<ModRowData> ordered;
        if (needle.Length == 0)
        {
            // 空查询等于「有什么就给什么」：按下载量给最热的那几条，AI 再拿标识符去问详情。
            ordered = rows
                .OrderByDescending(row => row.DownloadCount)
                .ThenBy(row => row.Identifier, StringComparer.OrdinalIgnoreCase);
        }
        else
        {
            ordered = rows
                .Select(row => (Row: row, Score: Score(row, needle)))
                .Where(hit => hit.Score >= 0)
                .OrderByDescending(hit => hit.Score)
                .ThenByDescending(hit => hit.Row.DownloadCount)
                .ThenBy(hit => hit.Row.Identifier, StringComparer.OrdinalIgnoreCase)
                .Select(hit => hit.Row);
        }

        return ordered.Take(take).Select(row => ToSummary(row, installed)).ToArray();
    }

    /// <inheritdoc />
    public AiModDetail? GetModDetail(string identifier)
    {
        var wanted = (identifier ?? string.Empty).Trim();
        if (wanted.Length == 0)
        {
            return null;
        }

        var installed = Installed;
        if (FindAvailable(wanted) is { } row)
        {
            var info = FindInstalled(installed, row.Identifier);
            var facts = row.Facts;
            return new AiModDetail(
                Identifier: row.Identifier,
                Name: row.Name,
                Author: row.Author,
                Abstract: Clean(facts.Abstract),
                Description: Clean(facts.Description),
                Licenses: facts.Licenses ?? Array.Empty<string>(),
                Homepage: Clean(facts.Homepage),
                InstalledVersion: Clean(info?.Version ?? row.InstalledVersion),
                LatestVersion: Clean(row.LatestVersion),
                DownloadCount: row.DownloadCount,
                Tags: row.Tags ?? Array.Empty<string>(),
                IsInstalled: info is not null || row.IsInstalled,
                IsCompatible: row.IsCompatible,
                DependsOn: facts.DependsOn ?? Array.Empty<string>(),
                Recommends: facts.Recommends ?? Array.Empty<string>(),
                ConflictsWith: facts.ConflictsWith ?? Array.Empty<string>());
        }

        // 仓库里没有：手装 / 已下架 / 私有模组。只要能证明它真的装了，就按注册表口径回答。
        var installedInfo = FindInstalled(installed, wanted);
        if (installedInfo is null)
        {
            return null;
        }

        var installedFacts = installedInfo.Facts;
        return new AiModDetail(
            Identifier: installedInfo.Identifier,
            // 注册表投影没有名称（ModListProjection 同样用标识符兜底），别在这里编一个假名字。
            Name: installedInfo.Identifier,
            Author: null,
            Abstract: Clean(installedFacts.Abstract),
            Description: Clean(installedFacts.Description),
            Licenses: installedFacts.Licenses ?? Array.Empty<string>(),
            Homepage: Clean(installedFacts.Homepage),
            InstalledVersion: Clean(installedInfo.Version),
            LatestVersion: null,
            DownloadCount: 0,
            Tags: Array.Empty<string>(),
            IsInstalled: true,
            IsCompatible: true,
            DependsOn: installedFacts.DependsOn ?? Array.Empty<string>(),
            Recommends: installedFacts.Recommends ?? Array.Empty<string>(),
            ConflictsWith: installedFacts.ConflictsWith ?? Array.Empty<string>());
    }

    /// <inheritdoc />
    public IReadOnlyList<AiInstalledMod> ListInstalled(int limit = 50)
    {
        var take = Math.Clamp(limit, 1, MaxInstalledLimit);
        var installed = Installed;
        if (installed.Count == 0)
        {
            return Array.Empty<AiInstalledMod>();
        }

        return installed.Values
            .Where(info => info is not null)
            .OrderBy(info => info.Identifier, StringComparer.OrdinalIgnoreCase)
            .ThenBy(info => info.Version, StringComparer.OrdinalIgnoreCase)
            .Take(take)
            .Select(info => new AiInstalledMod(info.Identifier, info.Version, info.IsAutoInstalled))
            .ToArray();
    }

    /// <inheritdoc />
    public IReadOnlyList<AiPageRef> ListPages() =>
        HostPages.Select(page => new AiPageRef(page.Key, ResolveTitle(page.TitleKey, page.FallbackTitle))).ToArray();

    /// <inheritdoc />
    public async Task<OperationResult<bool>> NavigateAsync(string pageKey, CancellationToken cancellationToken = default)
    {
        var wanted = (pageKey ?? string.Empty).Trim();
        if (wanted.Length == 0)
        {
            return OperationResult<bool>.Fail(
                "页面键为空，未执行导航。",
                "可用页面：" + KnownKeys());
        }

        var page = HostPages.FirstOrDefault(
            candidate => string.Equals(candidate.Key, wanted, StringComparison.OrdinalIgnoreCase));
        if (page.Key is null)
        {
            // 不把键直接拼进导航调用：导航服务对未知键的行为并不统一（有的抛、有的落空白页）。
            return OperationResult<bool>.Fail(
                $"未知的页面键：{wanted}。",
                "可用页面：" + KnownKeys());
        }

        if (_navigation is null)
        {
            return OperationResult<bool>.Fail("宿主导航服务不可用，未执行导航。");
        }

        try
        {
            var navigated = await _navigation
                .NavigateAsync(page.Key, parameter: null, cancellationToken)
                .ConfigureAwait(true);
            return navigated
                ? OperationResult<bool>.Ok(true)
                : OperationResult<bool>.Fail($"导航到「{wanted}」失败。", "页面未切换。");
        }
        catch (OperationCanceledException)
        {
            return OperationResult<bool>.Fail($"导航到「{wanted}」已取消。", retryable: true);
        }
        catch (Exception ex)
        {
            return OperationResult<bool>.Fail($"导航到「{wanted}」时出错。", ex.Message);
        }
    }

    // ---- 快照读取（每个入口都兜住实现方抛异常） --------------------------------

    private IReadOnlyList<ModRowData> Available => AvailableOf(TryRead(() => _repository?.Current));

    private IReadOnlyDictionary<string, InstalledModuleInfo> Installed =>
        InstalledOf(TryRead(() => _registry?.Current));

    private static IReadOnlyList<ModRowData> AvailableOf(RepositorySnapshot? snapshot) =>
        snapshot?.Available ?? Array.Empty<ModRowData>();

    private static IReadOnlyDictionary<string, InstalledModuleInfo> InstalledOf(RegistrySnapshot? snapshot) =>
        snapshot?.Installed ?? NoInstalled;

    /// <summary>读一次快照；服务缺失或实现方在未就绪时抛异常，都当成「没有快照」。</summary>
    private static T? TryRead<T>(Func<T?> read)
        where T : class
    {
        try
        {
            return read();
        }
        catch (Exception)
        {
            // 启动早期（仓库还没加载、注册表未开锁）读快照抛异常是已知形态：
            // AI 面板不该因此整块消失，退化成空结果即可。
            return null;
        }
    }

    // ---- 匹配与打分 ------------------------------------------------------------

    /// <summary>
    /// 相关度打分；不命中任何字段时返回 -1。
    /// 档位 10 分一档：100 名称精确 / 80 名称前缀 / 60 名称包含 / 50 标识符包含 / 30 标签 / 20 摘要，
    /// 只命中作者记 0（作者命中要能搜到，但不该压过名称与摘要）。
    /// </summary>
    private static int Score(in ModRowData row, string needle)
    {
        var name = row.Name ?? string.Empty;
        var identifier = row.Identifier ?? string.Empty;

        var tier = name.Equals(needle, StringComparison.OrdinalIgnoreCase) ? 100
            : name.StartsWith(needle, StringComparison.OrdinalIgnoreCase) ? 80
            : name.Contains(needle, StringComparison.OrdinalIgnoreCase) ? 60
            : identifier.Contains(needle, StringComparison.OrdinalIgnoreCase) ? 50
            : ContainsTag(row.Tags, needle) ? 30
            : ContainsText(row.Facts.Abstract, needle) ? 20
            : ContainsText(row.Author, needle) ? 0
            : -1;

        return tier < 0 ? -1 : tier + DownloadBump(row.DownloadCount);
    }

    /// <summary>下载量微调 0..9：小于一档（10 分），只决定同档先后，不会跨档翻盘。</summary>
    private static int DownloadBump(long downloadCount) =>
        downloadCount <= 0 ? 0 : (int)Math.Min(9, Math.Log10(downloadCount + 1.0));

    private static bool ContainsText(string? haystack, string needle) =>
        haystack is not null && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

    private static bool ContainsTag(IReadOnlyList<string>? tags, string needle)
    {
        if (tags is null)
        {
            return false;
        }

        foreach (var tag in tags)
        {
            if (tag is not null && tag.Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    // ---- 查找 ----------------------------------------------------------------

    /// <summary>先按标识符、再按名称找仓库行（AI 用户往往只说得出名字）。</summary>
    private ModRowData? FindAvailable(string wanted)
    {
        var rows = Available;
        foreach (var row in rows)
        {
            if (string.Equals(row.Identifier, wanted, StringComparison.OrdinalIgnoreCase))
            {
                return row;
            }
        }

        foreach (var row in rows)
        {
            if (string.Equals(row.Name, wanted, StringComparison.OrdinalIgnoreCase))
            {
                return row;
            }
        }

        return null;
    }

    /// <summary>
    /// 注册表字典的 Key 理论上就是标识符，但快照来自上游、比较器不由我们定：
    /// 直接 TryGetValue 之后再做一次不区分大小写（含 Value.Identifier）的兜底扫描。
    /// </summary>
    private static InstalledModuleInfo? FindInstalled(
        IReadOnlyDictionary<string, InstalledModuleInfo> installed,
        string identifier)
    {
        if (identifier.Length == 0 || installed.Count == 0)
        {
            return null;
        }

        if (installed.TryGetValue(identifier, out var direct) && direct is not null)
        {
            return direct;
        }

        foreach (var (key, info) in installed)
        {
            if (info is null)
            {
                continue;
            }

            if (string.Equals(key, identifier, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(info.Identifier, identifier, StringComparison.OrdinalIgnoreCase))
            {
                return info;
            }
        }

        return null;
    }

    // ---- 投影 ----------------------------------------------------------------

    private static AiModSummary ToSummary(
        in ModRowData row,
        IReadOnlyDictionary<string, InstalledModuleInfo> installed)
    {
        var info = FindInstalled(installed, row.Identifier);
        return new AiModSummary(
            Identifier: row.Identifier,
            Name: row.Name,
            Author: row.Author,
            Abstract: Clean(row.Facts.Abstract),
            InstalledVersion: Clean(info?.Version ?? row.InstalledVersion),
            LatestVersion: Clean(row.LatestVersion),
            DownloadCount: row.DownloadCount,
            IsInstalled: info is not null || row.IsInstalled,
            IsCompatible: row.IsCompatible,
            HasUpdate: row.HasUpdate,
            Tags: row.Tags ?? Array.Empty<string>());
    }

    // ---- 文案 ----------------------------------------------------------------

    private string ResolveTitle(string titleKey, string fallbackTitle)
    {
        try
        {
            var translated = _translate?.Invoke(titleKey);
            return string.IsNullOrWhiteSpace(translated) || string.Equals(translated, titleKey, StringComparison.Ordinal)
                ? fallbackTitle
                : translated;
        }
        catch (Exception)
        {
            return fallbackTitle;
        }
    }

    private static string KnownKeys() => string.Join("、", HostPages.Select(page => page.Key));

    /// <summary>空白折成 <c>null</c>：AI 拿到的字段宁可缺失，也不要是空串。</summary>
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
