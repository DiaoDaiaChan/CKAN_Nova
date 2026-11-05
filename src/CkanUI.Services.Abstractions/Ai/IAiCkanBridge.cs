using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Services.Abstractions.Ai;

/// <summary>AI 看到的宿主上下文（一次快照，短小，别把 3000 行仓库塞给模型）。</summary>
/// <param name="InstanceName">当前实例名。</param>
/// <param name="GameDir">实例游戏目录；没有实例时为空。</param>
/// <param name="KspVersion">实例的 KSP 版本；未知时为空。</param>
/// <param name="InstalledCount">已安装模组数（注册表口径）。</param>
/// <param name="AvailableCount">仓库索引里的可选模组数。</param>
/// <param name="RepositoryUpdatedAt">仓库索引更新时间；无索引时为空。</param>
/// <param name="RepositoryFromCache">索引是否来自本地缓存（没联网刷新过）。</param>
public sealed record AiBridgeContext(
    string InstanceName,
    string? GameDir,
    string? KspVersion,
    int InstalledCount,
    int AvailableCount,
    DateTime? RepositoryUpdatedAt,
    bool RepositoryFromCache);

/// <summary>搜索结果里的一行（只给模型看有用的字段，不含 CKAN 类型）。</summary>
public sealed record AiModSummary(
    string Identifier,
    string Name,
    string? Author,
    string? Abstract,
    string? InstalledVersion,
    string? LatestVersion,
    long DownloadCount,
    bool IsInstalled,
    bool IsCompatible,
    bool HasUpdate,
    IReadOnlyList<string> Tags);

/// <summary>一个模组的详情（回答「这个 mod 是干什么的」用）。</summary>
public sealed record AiModDetail(
    string Identifier,
    string Name,
    string? Author,
    string? Abstract,
    string? Description,
    IReadOnlyList<string> Licenses,
    string? Homepage,
    string? InstalledVersion,
    string? LatestVersion,
    long DownloadCount,
    IReadOnlyList<string> Tags,
    bool IsInstalled,
    bool IsCompatible,
    IReadOnlyList<string> DependsOn,
    IReadOnlyList<string> Recommends,
    IReadOnlyList<string> ConflictsWith);

/// <summary>一条已安装模组。</summary>
public sealed record AiInstalledMod(string Identifier, string Version, bool IsAutoInstalled);

/// <summary>可导航页面（给「帮我打开模组页」这类指令用）。</summary>
public sealed record AiPageRef(string Key, string Title);

/// <summary>安装计划里的一步（依赖会被摊平成多步，按安装顺序给出）。</summary>
/// <param name="Identifier">模组标识符。</param>
/// <param name="Name">显示名。</param>
/// <param name="Action">动作的英文名：Install / Upgrade / Downgrade / Remove / Replace。</param>
/// <param name="FromVersion">变更前版本；新装时为空。</param>
/// <param name="ToVersion">变更后版本；卸载时为空。</param>
public sealed record AiInstallStep(
    string Identifier,
    string Name,
    string Action,
    string? FromVersion,
    string? ToVersion);

/// <summary>
/// 「装这个模组会发生什么」的预览：AI 必须先给用户看这份清单，用户点头才允许动磁盘。
/// </summary>
/// <param name="Identifier">用户点名要装的标识符。</param>
/// <param name="Steps">含依赖在内的完整步骤。</param>
/// <param name="Conflicts">挡住安装的硬冲突（版本不兼容等）。</param>
/// <param name="Warnings">不挡路但要说清的提示（自动安装的依赖、卸载他人依赖等）。</param>
/// <param name="EstimatedDownloadBytes">预计下载字节数。</param>
public sealed record AiInstallPlan(
    string Identifier,
    IReadOnlyList<AiInstallStep> Steps,
    IReadOnlyList<string> Conflicts,
    IReadOnlyList<string> Warnings,
    long EstimatedDownloadBytes)
{
    /// <summary>没有任何步骤（标识符不在仓库里、或已经是最新）。</summary>
    public bool IsEmpty => Steps.Count == 0;

    /// <summary>可以执行：有步骤且没有硬冲突。</summary>
    public bool CanApply => !IsEmpty && Conflicts.Count == 0;
}

/// <summary>一次安装的结果。</summary>
/// <param name="Identifier">用户点名要装的标识符。</param>
/// <param name="Applied">是否真的动了磁盘（用户拒绝、被冲突挡住时为 <c>false</c>）。</param>
/// <param name="Summary">一句话给模型转述给用户（中文）。</param>
/// <param name="Installed">装上的标识符。</param>
/// <param name="Removed">卸掉的标识符。</param>
/// <param name="Failed">失败的标识符。</param>
/// <param name="Warnings">安装器给出的告警。</param>
public sealed record AiInstallOutcome(
    string Identifier,
    bool Applied,
    string Summary,
    IReadOnlyList<string> Installed,
    IReadOnlyList<string> Removed,
    IReadOnlyList<string> Failed,
    IReadOnlyList<string> Warnings);

/// <summary>界面快照里的一个字段（只放能一句话说清的值，集合只给条数）。</summary>
/// <param name="Name">属性名（模型按它复述）。</param>
/// <param name="Kind">值种类：text / number / bool / enum / time / collection / secret。</param>
/// <param name="Value">当前值的可读形式；敏感字段固定为 <c>***</c>。</param>
public sealed record AiUiField(string Name, string Kind, string? Value);

/// <summary>页面上一个可执行命令（由反射枚举得出，不是手写清单）。</summary>
/// <param name="Name">命令属性名，直接喂给 <c>ckan_run_page_action</c>。</param>
/// <param name="Label">去掉 Command 后缀的短名，给模型念给用户听。</param>
/// <param name="CanExecute">当前是否可点（按钮灰不灰）。</param>
/// <param name="ParameterType">参数类型名；无参命令为 <c>null</c>。</param>
public sealed record AiPageAction(string Name, string Label, bool CanExecute, string? ParameterType);

/// <summary>当前界面的一份快照。</summary>
/// <param name="PageKey">当前页面键。</param>
/// <param name="PageTitle">当前页面标题（已本地化）。</param>
/// <param name="ViewModel">当前页面视图模型的类型名。</param>
/// <param name="Fields">可读字段。</param>
/// <param name="Actions">可执行命令。</param>
public sealed record AiUiState(
    string PageKey,
    string PageTitle,
    string ViewModel,
    IReadOnlyList<AiUiField> Fields,
    IReadOnlyList<AiPageAction> Actions);

/// <summary>一个可写界面设置项。</summary>
/// <param name="Key">字段名，直接喂给 <c>ckan_set_setting</c> 的 key。</param>
/// <param name="Kind">值种类：text / number / bool / enum / complex。</param>
/// <param name="Value">当前值；复杂对象为 <c>null</c>。</param>
/// <param name="Choices">可选值（枚举型字段给出，其余为空）。</param>
public sealed record AiSettingEntry(string Key, string Kind, string? Value, IReadOnlyList<string> Choices);

/// <summary>
/// AI 与 CKAN 之间的桥：只读查询走快照（不联网、不阻塞 UI），导航与安装是仅有的两个会改变状态的动作。
/// </summary>
/// <remarks>
/// <para>放在 Abstractions 里的理由和插件页契约一致：插件工程不引用 App，桥的实现住在 App（它拿得到
/// <c>INavigationService</c>、安装器与对话框），插件只认这个接口。所有方法都不抛异常——AI 是外部输入，
/// 坏参数不能把界面打崩。</para>
/// <para><b>安装是有副作用的动作</b>：<see cref="InstallAsync"/> 必须先把 <see cref="PlanInstallAsync"/> 的步骤摊给用户看、
/// 拿到明确同意才写盘；用户拒绝时返回 <c>Applied=false</c> 而不是失败（拒绝不是错误）。</para>
/// </remarks>
public interface IAiCkanBridge
{
    /// <summary>当前实例与仓库的概况。</summary>
    AiBridgeContext GetContext();

    /// <summary>按关键词搜索仓库（name / identifier / author / abstract / tags 命中，按相关度与下载量排序）。</summary>
    IReadOnlyList<AiModSummary> SearchMods(string? query, int limit = 10);

    /// <summary>按标识符取详情；找不到返回 <c>null</c>。</summary>
    AiModDetail? GetModDetail(string identifier);

    /// <summary>列出已安装模组（最多 <paramref name="limit"/> 条）。</summary>
    IReadOnlyList<AiInstalledMod> ListInstalled(int limit = 50);

    /// <summary>可导航的页面键与标题。</summary>
    IReadOnlyList<AiPageRef> ListPages();

    /// <summary>导航到某个页面键；键不存在时返回失败而不是把界面留在空白上。</summary>
    Task<OperationResult<bool>> NavigateAsync(string pageKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// 计算「装这个模组会发生什么」：依赖、冲突、预计下载量。只读，不写盘，不弹窗。
    /// </summary>
    /// <param name="identifier">模组标识符。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<OperationResult<AiInstallPlan>> PlanInstallAsync(string identifier, CancellationToken cancellationToken = default);

    /// <summary>
    /// 安装（或升级到最新）一个模组：先算计划，再把步骤交给用户确认，同意后走安装器。
    /// </summary>
    /// <param name="identifier">模组标识符。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>用户拒绝时 <see cref="AiInstallOutcome.Applied"/> 为 <c>false</c>；真正的错误走失败结果。</returns>
    Task<OperationResult<AiInstallOutcome>> InstallAsync(string identifier, CancellationToken cancellationToken = default);

    /// <summary>切换界面主题（如 dark / light）与深色皮肤。</summary>
    Task<OperationResult<bool>> SetThemeAsync(string? theme, string? skin = null, CancellationToken cancellationToken = default);

    /// <summary>在模组管理页联动搜索关键字与筛选条件。</summary>
    Task<OperationResult<bool>> FilterModsAsync(string? query, string? filter = null, CancellationToken cancellationToken = default);

    /// <summary>切换当前活动的 KSP 实例。</summary>
    Task<OperationResult<bool>> SwitchInstanceAsync(string instanceName, CancellationToken cancellationToken = default);

    /// <summary>拉起当前实例的 KSP 游戏进程。</summary>
    Task<OperationResult<bool>> LaunchGameAsync(CancellationToken cancellationToken = default);

    /// <summary>更新个性化设置项（UiSettings 里任意可写字段，键名用 <see cref="ListSettings"/> 查）。</summary>
    Task<OperationResult<bool>> SetSettingAsync(string key, string value, CancellationToken cancellationToken = default);

    /// <summary>
    /// 当前界面快照：在哪一页、这一页的可读字段（筛选词、选中项、计数……）、可执行命令及其可用性。
    /// </summary>
    /// <remarks>
    /// 这是「AI 改完界面能自证」的唯一入口：所有写操作返回的 ok 只证明调用没抛，
    /// 只有重新读一次状态才能证明用户看到的界面真的变了。
    /// </remarks>
    AiUiState GetUiState();

    /// <summary>
    /// 枚举某一页上全部命令（反射该页视图模型的 <see cref="System.Windows.Input.ICommand"/> 属性）。
    /// </summary>
    /// <param name="pageKey">页面键；为空表示当前页。不会触发导航。</param>
    IReadOnlyList<AiPageAction> ListPageActions(string? pageKey = null);

    /// <summary>
    /// 执行某一页上的一个命令（按名字反射查找，参数按命令签名自动转换）。
    /// </summary>
    /// <param name="action">命令属性名，可省略 <c>Command</c> 后缀；大小写不敏感。</param>
    /// <param name="argument">命令参数；无参命令留空。</param>
    /// <param name="pageKey">页面键；为空表示当前页。非当前页会先导航过去（用户看得见）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>成功时带一句给模型转述的结论；命令不存在、不可执行、参数类型不匹配都走失败结果。</returns>
    Task<OperationResult<string>> RunPageActionAsync(
        string action,
        string? argument = null,
        string? pageKey = null,
        CancellationToken cancellationToken = default);

    /// <summary>枚举全部可写界面设置项及其当前值（反射 <c>UiSettings</c>，含取值范围提示）。</summary>
    IReadOnlyList<AiSettingEntry> ListSettings();

    /// <summary>
    /// 给当前页面上的一个可写字段赋值（输入框、下拉框、开关等两向绑定属性）。
    /// </summary>
    /// <remarks>
    /// 命令负责「点按钮」，本方法负责「往框里打字」：搜索框、过滤词这类控件通常没有对应的命令，
    /// 只会把值绑回视图模型。只接受 <see cref="GetUiState"/> 能读出来的标量字段（文本 / 布尔 / 数字 / 枚举 / 时间），
    /// 命令、集合、复杂对象和名字含密钥的字段一律拒绝。
    /// </remarks>
    /// <param name="field">字段名（<see cref="GetUiState"/> 返回的 <c>name</c>）。</param>
    /// <param name="value">新值的文本形式；布尔用 true/false，枚举用名字。</param>
    /// <param name="pageKey">页面键；为空表示当前页。非当前页会先导航过去（用户看得见）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>成功时带一句「旧值 → 新值」的结论，供模型转述。</returns>
    Task<OperationResult<string>> SetUiFieldAsync(
        string field,
        string? value,
        string? pageKey = null,
        CancellationToken cancellationToken = default);
}
