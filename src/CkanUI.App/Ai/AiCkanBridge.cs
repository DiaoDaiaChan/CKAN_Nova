using System.Globalization;
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
/// <para>四条纪律，缺一条就会在启动早期或坏输入上炸界面：</para>
/// <list type="number">
/// <item><b>只读快照</b>：<see cref="IRepositoryService.Current"/> / <see cref="IRegistryService.Current"/> 都是内存里的
/// 对象图，这里不触发刷新、不读磁盘、不联网——AI 的每一次提问都不该变成一次仓库同步。</item>
/// <item><b>绝不抛异常</b>：AI 的话是外部输入。任一服务缺失、快照为空、实现方自己抛了，都只能降级成空列表 / 空上下文；
/// 两个会改变状态的入口——<see cref="NavigateAsync(string, System.Threading.CancellationToken)"/> 与
/// <see cref="InstallAsync(string, System.Threading.CancellationToken)"/>——都把失败折成
/// <see cref="OperationResult{T}"/> 而不是让调用方去 catch。</item>
/// <item><b>键与外壳一致</b>：<see cref="ListPages"/> 给出的键必须能在 <c>ServiceRegistration.PageTypes</c> 里找到，
/// 否则「帮我打开 XX 页」会导向一个不存在的页面。这条不变量由 <c>AiCkanBridgeTests</c> 锁住。</item>
/// <item><b>安装先确认</b>：安装是桥里第二个有副作用的动作（第一个是导航），必须有用户确认——没有对话框就宁可失败，
/// 也不让 AI 在没人点头时动磁盘。确认之后要重算一次计划再交给安装器，因为确认期间仓库可能已经刷新。</item>
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
        // 模组分节：装什么、装进去长什么样、哪些待应用、元数据打哪来（Mod 配置是插件页，不在这里）。
        ("mods", LocalizationKeys.NavRailManageMods, "模组"),
        ("content", LocalizationKeys.NavRailContent, "内容预览"),
        ("changes", LocalizationKeys.NavRailChanges, "变更集"),
        ("repos", LocalizationKeys.NavRailRepos, "仓库"),
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
    private readonly IChangeSetService? _changeSets;
    private readonly IInstallerService? _installer;
    private readonly IOperationQueue? _queue;
    private readonly IDialogService? _dialogs;

    /// <summary>
    /// 全部依赖可选：宿主没装齐（单测、设计时、插件宿主）时桥退化成「查不到但也不炸」。
    /// </summary>
    /// <param name="repository">仓库索引快照。</param>
    /// <param name="registry">注册表快照（已安装）。</param>
    /// <param name="instances">当前实例。</param>
    /// <param name="navigation">页面导航（会动界面的依赖之一）。</param>
    /// <param name="translate">取译文委托；为 <c>null</c> 时页面标题回落到内置中文。</param>
    /// <param name="changeSets">变更集计算（安装计划的唯一来源）；为 <c>null</c> 时安装整体不可用。</param>
    /// <param name="installer">安装器；为 <c>null</c> 时安装整体不可用。</param>
    /// <param name="queue">后台任务队列；非空时安装入队（底栏可见、可取消），为空时直接执行。</param>
    /// <param name="dialogs">确认对话框；为 <c>null</c> 时 AI 不允许安装（没人点头不动磁盘）。</param>
    public AiCkanBridge(
        IRepositoryService? repository = null,
        IRegistryService? registry = null,
        IInstanceService? instances = null,
        INavigationService? navigation = null,
        Func<string, string>? translate = null,
        IChangeSetService? changeSets = null,
        IInstallerService? installer = null,
        IOperationQueue? queue = null,
        IDialogService? dialogs = null)
    {
        _repository = repository;
        _registry = registry;
        _instances = instances;
        _navigation = navigation;
        _translate = translate;
        _changeSets = changeSets;
        _installer = installer;
        _queue = queue;
        _dialogs = dialogs;
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

    // ---- 安装（桥里第二个有副作用的动作） --------------------------------------

    /// <inheritdoc />
    public async Task<OperationResult<AiInstallPlan>> PlanInstallAsync(
        string identifier,
        CancellationToken cancellationToken = default)
    {
        var wanted = (identifier ?? string.Empty).Trim();
        if (wanted.Length == 0)
        {
            return OperationResult<AiInstallPlan>.Fail("缺少模组标识符。");
        }

        var changeSets = _changeSets;
        if (changeSets is null)
        {
            return OperationResult<AiInstallPlan>.Fail("安装能力未连接（缺少 IChangeSetService）。");
        }

        try
        {
            var built = await BuildPreviewAsync(changeSets, wanted, cancellationToken).ConfigureAwait(true);
            if (!built.IsSuccess)
            {
                return OperationResult<AiInstallPlan>.Fail(
                    built.Message ?? "无法计算安装计划。",
                    built.Detail,
                    built.Retryable);
            }

            var preview = built.Value;
            if (preview is null)
            {
                return OperationResult<AiInstallPlan>.Fail("变更集服务没有返回安装预览。");
            }

            return OperationResult<AiInstallPlan>.Ok(ToPlan(wanted, preview));
        }
        catch (OperationCanceledException)
        {
            return OperationResult<AiInstallPlan>.Fail("安装计划已取消。", retryable: true);
        }
        catch (Exception ex)
        {
            return OperationResult<AiInstallPlan>.Fail($"计算「{wanted}」的安装计划时出错。", ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<AiInstallOutcome>> InstallAsync(
        string identifier,
        CancellationToken cancellationToken = default)
    {
        var wanted = (identifier ?? string.Empty).Trim();
        try
        {
            var planned = await PlanInstallAsync(wanted, cancellationToken).ConfigureAwait(true);
            if (!planned.IsSuccess)
            {
                return OperationResult<AiInstallOutcome>.Fail(
                    planned.Message ?? "无法计算安装计划。",
                    planned.Detail,
                    planned.Retryable);
            }

            var plan = planned.Value;
            if (plan is null)
            {
                return OperationResult<AiInstallOutcome>.Fail("变更集服务没有返回安装预览。");
            }

            if (plan.Conflicts.Count > 0)
            {
                return OperationResult<AiInstallOutcome>.Fail(
                    "安装被冲突挡住：" + string.Join("；", plan.Conflicts),
                    "先解决冲突，再让 AI 安装。");
            }

            if (plan.IsEmpty)
            {
                return OperationResult<AiInstallOutcome>.Fail("没有可执行的安装步骤（可能已经是最新版本）。");
            }

            var changeSets = _changeSets;
            var installer = _installer;
            if (changeSets is null || installer is null)
            {
                // PlanInstallAsync 已经挡过 changeSets，这里实际只剩 installer 缺失这一种形态。
                return OperationResult<AiInstallOutcome>.Fail("安装能力未连接（缺少 IInstallerService）。");
            }

            var dialogs = _dialogs;
            if (dialogs is null)
            {
                return OperationResult<AiInstallOutcome>.Fail("没有可用的确认对话框，AI 不会在没人点头时动磁盘。");
            }

            var confirmed = await dialogs
                .ConfirmAsync("AI 想安装模组", ConfirmMessage(wanted, plan), ConfirmDetail(plan))
                .ConfigureAwait(true);
            if (!confirmed)
            {
                // 拒绝不是错误：磁盘没动，模型该把这句话原样转述给用户。
                return OperationResult<AiInstallOutcome>.Ok(new AiInstallOutcome(
                    wanted,
                    Applied: false,
                    Summary: "用户拒绝了这次安装，磁盘没有变化。",
                    Installed: Array.Empty<string>(),
                    Removed: Array.Empty<string>(),
                    Failed: Array.Empty<string>(),
                    Warnings: Array.Empty<string>()));
            }

            // 确认期间仓库可能刷新（用户也可能在别处动过东西）：重算一份再交安装器，
            // 不把几分钟前那份计划当成事实。
            var refreshed = await BuildPreviewAsync(changeSets, wanted, cancellationToken).ConfigureAwait(true);
            if (!refreshed.IsSuccess)
            {
                return OperationResult<AiInstallOutcome>.Fail(
                    refreshed.Message ?? "确认后重新计算安装计划失败。",
                    refreshed.Detail,
                    refreshed.Retryable);
            }

            var preview = refreshed.Value;
            if (preview is null)
            {
                return OperationResult<AiInstallOutcome>.Fail("确认后变更集服务没有返回安装预览。");
            }

            var report = await ApplyAsync(installer, preview, wanted, cancellationToken).ConfigureAwait(true);
            if (!report.IsSuccess)
            {
                return OperationResult<AiInstallOutcome>.Fail(
                    report.Message ?? "安装失败。",
                    report.Detail,
                    report.Retryable);
            }

            var done = report.Value;
            if (done is null)
            {
                return OperationResult<AiInstallOutcome>.Fail("安装器没有返回安装结果。");
            }

            return OperationResult<AiInstallOutcome>.Ok(new AiInstallOutcome(
                wanted,
                Applied: true,
                Summary: $"已安装 {CountOf(done.Installed)} 个、移除 {CountOf(done.Removed)} 个、失败 {CountOf(done.Failed)} 个。",
                Installed: done.Installed ?? Array.Empty<string>(),
                Removed: done.Removed ?? Array.Empty<string>(),
                Failed: done.Failed ?? Array.Empty<string>(),
                Warnings: done.Warnings ?? Array.Empty<string>()));
        }
        catch (OperationCanceledException)
        {
            return OperationResult<AiInstallOutcome>.Fail("安装已取消。", retryable: true);
        }
        catch (Exception ex)
        {
            return OperationResult<AiInstallOutcome>.Fail($"安装「{wanted}」时出错。", ex.Message);
        }
    }

    private static Task<OperationResult<ChangeSetPreview>> BuildPreviewAsync(
        IChangeSetService changeSets,
        string identifier,
        CancellationToken cancellationToken) =>
        changeSets.BuildAsync([new ChangeRequest(identifier, ChangeAction.Install)], cancellationToken);

    /// <summary>
    /// 走队列（底栏任务栏可见、可取消）或直接执行。队列回调是 fire-and-forget 语义，
    /// 异常必须在回调里折成 TCS 的结果，否则 <see cref="TaskCompletionSource{TResult}.Task"/> 永远等不到落点。
    /// </summary>
    private async Task<OperationResult<InstallReport>> ApplyAsync(
        IInstallerService installer,
        ChangeSetPreview preview,
        string identifier,
        CancellationToken cancellationToken)
    {
        var queue = _queue;
        if (queue is null)
        {
            return await installer.ApplyAsync(preview, progress: null, cancellationToken).ConfigureAwait(true);
        }

        var tcs = new TaskCompletionSource<OperationResult<InstallReport>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        queue.Enqueue(
            "install",
            "AI 安装 " + identifier,
            async token =>
            {
                try
                {
                    tcs.TrySetResult(await installer.ApplyAsync(preview, progress: null, token).ConfigureAwait(true));
                }
                catch (OperationCanceledException)
                {
                    tcs.TrySetCanceled(token);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            });

        return await tcs.Task.WaitAsync(cancellationToken).ConfigureAwait(true);
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

    // ---- 安装投影与文案 --------------------------------------------------------

    /// <summary>把 Core 的变更集投影成 AI 看得懂的安装计划（顺序＝安装顺序，原样保留）。</summary>
    private static AiInstallPlan ToPlan(string identifier, ChangeSetPreview preview) =>
        new(
            identifier,
            preview.Items
                .Select(item => new AiInstallStep(
                    item.Identifier,
                    item.Name,
                    item.Action.ToString(),
                    item.FromVersion,
                    item.ToVersion))
                .ToArray(),
            preview.Conflicts ?? Array.Empty<string>(),
            preview.Warnings ?? Array.Empty<string>(),
            preview.EstimatedDownloadBytes);

    private static string ConfirmMessage(string identifier, AiInstallPlan plan) =>
        $"AI 助手请求安装「{identifier}」，共 {plan.Steps.Count} 步，预计下载 {FormatBytes(plan.EstimatedDownloadBytes)}。允许这次安装吗？";

    /// <summary>确认框正文：一步一行，告警附在最后（模型和用户看的是同一份清单）。</summary>
    private static string ConfirmDetail(AiInstallPlan plan)
    {
        var lines = plan.Steps
            .Select(step => $"{step.Action} {step.Name} ({step.Identifier}) {step.FromVersion} -> {step.ToVersion}")
            .ToList();

        if (plan.Warnings.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("警告：");
            lines.AddRange(plan.Warnings.Select(warning => "· " + warning));
        }

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>体积：一位小数；小于 1 MB 用 KB，避免「0.0 MB」这种糊弄人的显示。</summary>
    private static string FormatBytes(long bytes)
    {
        const double OneMegabyte = 1024d * 1024d;
        var size = Math.Max(0, bytes);
        return size < OneMegabyte
            ? (size / 1024d).ToString("0.0", CultureInfo.InvariantCulture) + " KB"
            : (size / OneMegabyte).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
    }

    private static int CountOf(IReadOnlyList<string>? items) => items?.Count ?? 0;

    /// <summary>空白折成 <c>null</c>：AI 拿到的字段宁可缺失，也不要是空串。</summary>
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}