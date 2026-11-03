namespace CkanUI.Services.Abstractions;

/// <summary>
/// 插件加载上下文的生命周期探针（宿主侧只读）。
/// </summary>
/// <remarks>
/// <para>
/// .NET 没有公开「这个 <c>AssemblyLoadContext</c> 正在卸载」的查询口子：<c>Unload()</c> 只是开始回收，
/// 程序集从 <c>AppDomain</c> 消失要等 GC。宿主在插件重载后会遇到「同一个类型名有两份程序集」的局面，
/// 如果在旧的那份上继续解析类型，页面跑的是旧代码，旧 ALC 也永远收不了尾。
/// </para>
/// <para>
/// 由插件加载上下文实现，宿主通过 <c>AssemblyLoadContext.GetLoadContext(assembly) is IPluginAssemblyContext</c>
/// 判定"这一份正在卸载"，从而跳过它去解析新加载的那一份。
/// </para>
/// </remarks>
public interface IPluginAssemblyContext
{
    /// <summary>已经调用过 <c>Unload()</c>（卸载进行中，不再是可用的解析目标）。</summary>
    bool IsUnloading { get; }
}
