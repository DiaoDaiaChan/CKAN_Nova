using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Services.Plugins;

/// <summary>
/// 一次成功的插件程序集加载：插件实例 + 释放委托（12 篇 F-17）。
/// </summary>
/// <remarks>
/// <para>不实现 <see cref="IDisposable"/>：宿主只需要「什么时候把可卸载上下文还回去」这一个动作，
/// 用一个幂等的 <see cref="Release"/> 而不是 Dispose 语义，避免服务层因为持有 IDisposable 字段被 CA1001 追着要析构。</para>
/// <para><see cref="Release"/> 之后 <see cref="Plugin"/> 引用仍在，但再次调用插件方法属于未定义行为——
/// 服务层保证释放前先从表里摘掉条目，页面拿到的都是快照。</para>
/// </remarks>
public sealed class PluginAssemblyLoad
{
    private readonly Action _release;
    private int _released;

    /// <summary>构造一次加载结果。</summary>
    /// <param name="plugin">插件实例。</param>
    /// <param name="pluginTypeName">实现 <see cref="ICkanUiPlugin"/> 的类型全名（日志用）。</param>
    /// <param name="release">释放动作（卸载可卸载 <c>AssemblyLoadContext</c>）。</param>
    public PluginAssemblyLoad(ICkanUiPlugin plugin, string pluginTypeName, Action release)
    {
        Plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
        PluginTypeName = pluginTypeName ?? string.Empty;
        _release = release ?? throw new ArgumentNullException(nameof(release));
    }

    /// <summary>插件实例。</summary>
    public ICkanUiPlugin Plugin { get; }

    /// <summary>实现契约的类型全名。</summary>
    public string PluginTypeName { get; }

    /// <summary>释放程序集（幂等：重复调用只有第一次生效）。</summary>
    public void Release()
    {
        if (Interlocked.Exchange(ref _released, 1) != 0)
        {
            return;
        }

        _release();
    }
}

/// <summary>
/// <see cref="IPluginAssemblyLoader.TryLoad"/> 的结果（12 篇 F-17）。
/// </summary>
/// <remarks>
/// 用结果类型而不是异常传递失败：插件是第三方代码，失败是常态（缺依赖、版本不符、构造抛异常），
/// 用异常会让调用方分不清「插件代码抛了」和「宿主自己坏了」。
/// </remarks>
/// <param name="Load">成功时的加载结果；失败为 <c>null</c>。</param>
/// <param name="Failure">失败分类。</param>
/// <param name="Message">中文可读原因。</param>
/// <param name="Detail">技术细节（异常文本），可能为 <c>null</c>。</param>
public sealed record PluginAssemblyLoadAttempt(
    PluginAssemblyLoad? Load,
    PluginLoadFailure Failure,
    string? Message,
    string? Detail)
{
    /// <summary>是否成功。</summary>
    public bool IsSuccess => Load is not null;

    /// <summary>成功结果。</summary>
    public static PluginAssemblyLoadAttempt Ok(PluginAssemblyLoad load) =>
        new(load, PluginLoadFailure.None, null, null);

    /// <summary>失败结果。</summary>
    public static PluginAssemblyLoadAttempt Fail(PluginLoadFailure failure, string message, string? detail = null) =>
        new(null, failure, message, detail);
}
