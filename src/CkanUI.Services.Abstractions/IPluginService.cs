using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Services.Abstractions.Home;

namespace CkanUI.Services.Abstractions;

/// <summary>
/// UI 插件宿主（12 篇 F-17 / 06 篇 UI-33）：发现、加载、激活、停用、重载、卸载、新增。
/// </summary>
/// <remarks>
/// <para>语义对齐原版 <c>CKAN.GUI.PluginController</c>，但把它的四处「静默失败」改成结构化结果：
/// 加载失败、构造失败、激活失败、卸载失败都返回带中文原因的 <see cref="PluginLoadResult"/> / <c>OperationResult</c>，
/// 宿主绝不因为插件代码抛异常而崩（原版 <c>ActivatePlugin</c> 在 catch 里 <c>return</c>，用户什么也看不到）。</para>
/// <para>所有成员都是「命令 → 服务 → 事件」：状态变化后触发 <see cref="Changed"/>，页面只订阅不轮询。</para>
/// <para>实现约定：失败的程序集必须释放（可卸载 <c>AssemblyLoadContext</c> 要 <c>Unload</c>），
/// 重载先卸旧再装新；插件实例的引用一旦丢弃，下次 GC 才会真正回收程序集。</para>
/// </remarks>
public interface IPluginService
{
    /// <summary>插件目录（默认 <c>%LocalAppData%/CKAN/plugins</c>）。新增插件会复制到这里。</summary>
    string PluginsDirectory { get; }

    /// <summary>
    /// 宿主随包携带的内置（首方）插件目录（默认 <c>AppContext.BaseDirectory/plugins</c>）。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="PluginsDirectory"/> 的职责分界：这里是安装目录，由发布包写入、用户不该往里放东西；
    /// 那边是用户数据目录，<c>AddFromPathAsync</c> 只往那边拷。默认实现返回空串，表示「本实现没有内置目录」——
    /// 既有替身因此不必跟着改（工程约定 §12 第 9 条）。
    /// </remarks>
    string BundledDirectory => string.Empty;

    /// <summary>已加载的内置插件（<see cref="PluginInfo.IsBundled"/> 为 <c>true</c>，按显示名排序）。默认空列表。</summary>
    IReadOnlyList<PluginInfo> Bundled => PluginInfo.Empty;

    /// <summary>当前已激活的插件（按显示名排序）。</summary>
    IReadOnlyList<PluginInfo> Active { get; }

    /// <summary>已加载但未激活的插件（按显示名排序）。</summary>
    IReadOnlyList<PluginInfo> Dormant { get; }

    /// <summary>
    /// 最近一次加载/发现失败留下的条目（<see cref="PluginState.Failed"/>，按文件名排序）。
    /// 原版 <c>PluginController</c> 只把失败写进日志，用户在对话框里看不到；这里显式留痕，页面据此提示。
    /// </summary>
    IReadOnlyList<PluginInfo> Failures { get; }

    /// <summary>插件集合或状态发生变化（加载/激活/停用/重载/卸载/新增）。在锁外触发。</summary>
    event EventHandler? Changed;

    /// <summary>
    /// 扫描插件目录，把还没加载的 dll 全部加载为 Dormant（不激活——默认不启用）。
    /// 返回当前已知的全部插件；扫描中失败的 dll 不进列表，但会在结果消息里逐个点名。
    /// </summary>
    Task<OperationResult<IReadOnlyList<PluginInfo>>> DiscoverAsync(CancellationToken ct = default);

    /// <summary>
    /// 扫描插件目录，并允许提供方显式激活内置插件。
    /// </summary>
    /// <param name="activateBundled">
    /// <c>true</c> = 内置插件加载后立即激活（只有宿主自己才有资格为随包携带的首方插件背书）；
    /// <c>false</c> = 与 <see cref="DiscoverAsync(CancellationToken)"/> 完全一致：内置插件同样进 Dormant，
    /// 除非用户之前显式激活过它（读持久化状态）。
    /// </param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>当前已知的全部插件（内置在前、用户目录在后）。</returns>
    /// <remarks>
    /// <para>为什么是新重载而不是给原方法加参数：<see cref="DiscoverAsync(CancellationToken)"/> 的第一个位置参数是
    /// <c>CancellationToken</c>，在前面插一个 <c>bool</c> 会把所有 <c>DiscoverAsync(ct)</c> 的调用点一起改坏
    /// （工程约定 §12 第 9 条）。重载带默认实现，既有替身不必跟着改。</para>
    /// <para>安全口径不变：这个参数只影响内置目录；用户目录里的插件永远默认 Dormant。</para>
    /// </remarks>
    Task<OperationResult<IReadOnlyList<PluginInfo>>> DiscoverAsync(bool activateBundled, CancellationToken ct = default) =>
        DiscoverAsync(ct);

    /// <summary>
    /// 加载指定 dll。<paramref name="activate"/> 为 <c>false</c> 时只进 Dormant（原版 <c>LoadAssembly(dll, activate:false)</c>）。
    /// 同一路径重复加载会被拒绝（先卸载）。
    /// </summary>
    Task<PluginLoadResult> LoadAsync(string pluginPath, bool activate = false, CancellationToken ct = default);

    /// <summary>激活一个 Dormant 插件（调用它的 <c>Initialize</c>）。</summary>
    Task<PluginLoadResult> ActivateAsync(string pluginId, CancellationToken ct = default);

    /// <summary>停用一个 Active 插件（调用它的 <c>Deinitialize</c>），回到 Dormant。</summary>
    Task<OperationResult> DeactivateAsync(string pluginId, CancellationToken ct = default);

    /// <summary>重载：先卸旧（含 <c>Deinitialize</c> + ALC 卸载）再按原路径装新，并保持原激活状态。</summary>
    Task<PluginLoadResult> ReloadAsync(string pluginId, CancellationToken ct = default);

    /// <summary>卸载：从宿主移除该插件并释放其程序集；已激活的先停用。</summary>
    Task<OperationResult> UnloadAsync(string pluginId, CancellationToken ct = default);

    /// <summary>
    /// 新增插件：把用户选中的 dll 复制进 <see cref="PluginsDirectory"/> 再加载（同名文件先删后拷）。
    /// 源文件已在插件目录内时不复制，直接加载。
    /// </summary>
    Task<PluginLoadResult> AddFromPathAsync(string sourcePath, bool activate = false, CancellationToken ct = default);

    /// <summary>
    /// 执行插件声明的一个工具项（12 篇 F-17 的「往现有工具条插按钮」）。
    /// </summary>
    /// <param name="pluginId">插件标识。</param>
    /// <param name="itemKey">工具项键（<see cref="PluginToolItemDescriptor.Key"/>）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>成功/失败结果；插件代码抛异常不会冒泡成异常，只会变成带原因的失败。</returns>
    /// <remarks>只有在插件已激活、且确实声明了该键的工具项时才回调插件。默认实现返回不可重试失败，
    /// 让「没接线」的替身不必跟着改（工程约定 §12 第 9 条）。</remarks>
    Task<OperationResult> InvokeToolItemAsync(string pluginId, string itemKey, CancellationToken ct = default) =>
        Task.FromResult(OperationResult.Fail("当前实现未提供插件工具项调用能力。"));

    /// <summary>所有已激活插件声明的主页部件（默认空列表）。</summary>
    /// <remarks>只列 Active：停用的插件不该继续出现在主页的「添加部件」菜单里。默认实现返回空，既有替身不必跟着改。</remarks>
    IReadOnlyList<PluginHomeWidgetBinding> ActiveHomeWidgets => Array.Empty<PluginHomeWidgetBinding>();

    /// <summary>拉取一块插件主页部件的数据。</summary>
    /// <param name="pluginId">插件标识。</param>
    /// <param name="widgetKey">部件键。</param>
    /// <param name="context">当前实例上下文。</param>
    /// <param name="ct">取消令牌。</param>
    /// <remarks>只有插件已激活且确实声明了该键时才回调插件；插件抛异常会变成带原因的失败，不会冒泡。
    /// 默认实现返回不可重试失败，既有替身不必跟着改。</remarks>
    Task<OperationResult<PluginHomeWidgetSnapshot>> GetHomeWidgetAsync(
        string pluginId,
        string widgetKey,
        PluginHomeWidgetContext context,
        CancellationToken ct = default) =>
        Task.FromResult(OperationResult<PluginHomeWidgetSnapshot>.Fail("当前实现未提供插件主页部件。"));
}