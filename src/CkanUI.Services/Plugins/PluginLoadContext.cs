using System.Reflection;
using System.Runtime.Loader;
using CkanUI.Services.Abstractions;

namespace CkanUI.Services.Plugins;

/// <summary>
/// 每个插件一个的可卸载 <see cref="AssemblyLoadContext"/>（12 篇 F-17）。
/// </summary>
/// <remarks>
/// <para>隔离口径：</para>
/// <list type="bullet">
/// <item><description>契约程序集 <c>CkanUI.Services.Abstractions</c> 必须由默认上下文提供，
/// 否则插件里的 <see cref="ICkanUiPlugin"/> 与宿主的不是同一个类型，强制转换会失败（典型症状：
/// 类型明明实现了接口却报 InvalidCastException）。所以这里显式把宿主侧程序集交回默认上下文。</description></item>
/// <item><description>其它依赖先从插件自己的目录里找（<c>{目录}/{名字}.dll</c>），找不到再交回默认上下文——
/// 这与原版 <c>Assembly.UnsafeLoadFrom</c> 的探测顺序一致，但不会污染宿主。</description></item>
/// <item><description><c>isCollectible: true</c>：卸载后程序集可以真正从内存里回收（前提是宿主不残留引用）。</description></item>
/// </list>
/// </remarks>
internal sealed class PluginLoadContext : AssemblyLoadContext, IPluginAssemblyContext
{
    private static readonly string[] HostProvidedPrefixes =
    [
        "System",
        "Microsoft",
        "netstandard",
        "mscorlib",
        "WindowsBase",
        "CkanUI",
        "Avalonia",
    ];

    private readonly string _probeDirectory;
    private volatile bool _isUnloading;

    /// <inheritdoc />
    public bool IsUnloading => _isUnloading;

    /// <summary>开始卸载：先置标志，再交给基类（基类之后不会再加载新程序集进这个上下文）。</summary>
    public new void Unload()
    {
        _isUnloading = true;
        base.Unload();
    }

    /// <summary>为某个插件目录建立独立的可卸载上下文。</summary>
    /// <param name="probeDirectory">插件自己的目录（依赖探测根）。</param>
    public PluginLoadContext(string probeDirectory)
        : base("ckan-ui-plugin-" + Guid.NewGuid().ToString("N"), isCollectible: true)
    {
        _probeDirectory = probeDirectory;
    }

    /// <inheritdoc />
    protected override Assembly? Load(AssemblyName assemblyName)
    {
        var simpleName = assemblyName.Name;
        if (string.IsNullOrEmpty(simpleName))
        {
            return null;
        }

        if (IsHostProvided(simpleName))
        {
            return null;
        }

        var candidate = Path.Combine(_probeDirectory, simpleName + ".dll");
        return File.Exists(candidate) ? LoadFromAssemblyPath(candidate) : null;
    }

    private static bool IsHostProvided(string simpleName)
    {
        if (string.Equals(simpleName, typeof(ICkanUiPlugin).Assembly.GetName().Name, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (var prefix in HostProvidedPrefixes)
        {
            if (simpleName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        // 兜底：默认上下文里已经装了的程序集一律共用宿主那份（枚举在并发加载时可能抛，忽略即可）。
        try
        {
            foreach (var assembly in Default.Assemblies)
            {
                if (string.Equals(assembly.GetName().Name, simpleName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch (InvalidOperationException)
        {
            // 默认上下文正在变化，交给上面的前缀判断兜底。
        }

        return false;
    }
}
