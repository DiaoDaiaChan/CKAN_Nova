namespace CkanUI.Services.Plugins;

/// <summary>
/// 把「从一个 dll 反射出插件实例」抽出来的窄接口（12 篇 F-17）。
/// </summary>
/// <remarks>
/// 抽它的唯一目的是可测：真实实现要读磁盘上的 dll，而测试要覆盖「加载失败不崩」「重复加载被拒」「卸载后列表清空」，
/// 这些都不该依赖真的编出一个插件程序集（工程约定 §12.6：后端保持 public 便于测试，不加 InternalsVisibleTo）。
/// 因此默认实现 <see cref="AssemblyLoadContextPluginLoader"/> 是 public 的，测试可以注入假的
/// <see cref="IPluginAssemblyLoader"/> 返回可控插件实例，也可以直接用真实实现去加载临时目录里的坏 dll。
/// </remarks>
public interface IPluginAssemblyLoader
{
    /// <summary>
    /// 加载指定 dll 并构造插件实例。不抛异常：所有失败都折进 <see cref="PluginAssemblyLoadAttempt"/>，
    /// 且失败的加载上下文已经被释放。
    /// </summary>
    /// <param name="assemblyPath">dll 的绝对路径。</param>
    PluginAssemblyLoadAttempt TryLoad(string assemblyPath);
}
