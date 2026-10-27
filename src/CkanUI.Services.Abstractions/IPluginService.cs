using CkanUI.Services.Abstractions.Dtos;

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
}
