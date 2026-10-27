using System.Reflection;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using log4net;

namespace CkanUI.Services.Plugins;

/// <summary>
/// 用可卸载 <see cref="System.Runtime.Loader.AssemblyLoadContext"/> 反射加载插件 dll 的默认实现（12 篇 F-17）。
/// </summary>
/// <remarks>
/// <para>找插件类型的口径与原版 <c>PluginController</c> 的「文件名点两次」约定（<c>"{0}.{0}"</c>）不同：
/// 原版要求插件类型名等于文件名，插件改名就加载不到；这里改成扫程序集里所有公开、非抽象、实现了
/// <see cref="ICkanUiPlugin"/> 的类型，取第一个。一个程序集只取一个插件（多插件请拆多个 dll）。</para>
/// <para><b>用流加载而不是 <c>LoadFromAssemblyPath</c></b>：后者会在 Windows 上一直占着 dll 文件句柄，
/// 用户就没法在插件运行时替换/删除 dll（07 篇 H-5 验收要求「删除 dll 后重启」）。读成字节再
/// <c>LoadFromStream</c> 之后，插件目录里的文件可以随便动。</para>
/// </remarks>
public sealed class AssemblyLoadContextPluginLoader : IPluginAssemblyLoader
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(AssemblyLoadContextPluginLoader));

    /// <inheritdoc />
    public PluginAssemblyLoadAttempt TryLoad(string assemblyPath)
    {
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(assemblyPath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return PluginAssemblyLoadAttempt.Fail(PluginLoadFailure.LoadFailed, "插件路径不合法：" + assemblyPath, ex.ToString());
        }

        if (!File.Exists(fullPath))
        {
            return PluginAssemblyLoadAttempt.Fail(PluginLoadFailure.NotFound, "找不到插件文件：" + fullPath);
        }

        var fileName = Path.GetFileName(fullPath);
        var probeDirectory = Path.GetDirectoryName(fullPath) ?? AppContext.BaseDirectory;
        var context = new PluginLoadContext(probeDirectory);
        var released = 0;

        void Release()
        {
            if (Interlocked.Exchange(ref released, 1) != 0)
            {
                return;
            }

            try
            {
                context.Unload();
            }
            catch (Exception ex)
            {
                Log.Warn("卸载插件加载上下文失败：" + fullPath, ex);
            }
        }

        Assembly assembly;
        try
        {
            using var stream = File.OpenRead(fullPath);
            assembly = context.LoadFromStream(stream);
        }
        catch (BadImageFormatException ex)
        {
            Release();
            return PluginAssemblyLoadAttempt.Fail(PluginLoadFailure.NotAnAssembly, "「" + fileName + "」不是有效的 .NET 程序集。", ex.ToString());
        }
        catch (FileLoadException ex)
        {
            Release();
            return PluginAssemblyLoadAttempt.Fail(PluginLoadFailure.LoadFailed, "加载「" + fileName + "」失败：" + ex.Message, ex.ToString());
        }
        catch (Exception ex)
        {
            Release();
            return PluginAssemblyLoadAttempt.Fail(PluginLoadFailure.LoadFailed, "加载「" + fileName + "」失败：" + ex.Message, ex.ToString());
        }

        Type? pluginType;
        try
        {
            pluginType = FindPluginType(assembly);
        }
        catch (ReflectionTypeLoadException ex)
        {
            Release();
            return PluginAssemblyLoadAttempt.Fail(
                PluginLoadFailure.MissingDependency,
                "「" + fileName + "」缺少依赖程序集，无法读取其中的类型：" + DescribeLoaderExceptions(ex),
                ex.ToString());
        }
        catch (Exception ex)
        {
            Release();
            return PluginAssemblyLoadAttempt.Fail(PluginLoadFailure.LoadFailed, "读取「" + fileName + "」的类型失败：" + ex.Message, ex.ToString());
        }

        if (pluginType is null)
        {
            Release();
            return PluginAssemblyLoadAttempt.Fail(
                PluginLoadFailure.NoPluginType,
                "「" + fileName + "」里没有实现 ICkanUiPlugin 的公开类型。",
                null);
        }

        ICkanUiPlugin plugin;
        try
        {
            plugin = Activator.CreateInstance(pluginType) as ICkanUiPlugin
                ?? throw new InvalidOperationException("插件类型无法转换成 ICkanUiPlugin：" + pluginType.FullName);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            Release();
            return PluginAssemblyLoadAttempt.Fail(
                PluginLoadFailure.InstantiationFailed,
                "创建插件实例失败（构造函数抛异常）：" + ex.InnerException.Message,
                ex.ToString());
        }
        catch (MissingMethodException ex)
        {
            Release();
            return PluginAssemblyLoadAttempt.Fail(
                PluginLoadFailure.InstantiationFailed,
                "创建插件实例失败：类型 " + pluginType.FullName + " 需要公共无参构造函数。",
                ex.ToString());
        }
        catch (Exception ex)
        {
            Release();
            return PluginAssemblyLoadAttempt.Fail(
                PluginLoadFailure.InstantiationFailed,
                "创建插件实例失败：" + ex.Message,
                ex.ToString());
        }

        return PluginAssemblyLoadAttempt.Ok(new PluginAssemblyLoad(plugin, pluginType.FullName ?? pluginType.Name, Release));
    }

    private static Type? FindPluginType(Assembly assembly)
    {
        foreach (var type in assembly.GetTypes())
        {
            if (!type.IsVisible || type.IsAbstract || !type.IsClass)
            {
                continue;
            }

            if (typeof(ICkanUiPlugin).IsAssignableFrom(type))
            {
                return type;
            }
        }

        return null;
    }

    private static string DescribeLoaderExceptions(ReflectionTypeLoadException ex)
    {
        var messages = new List<string>();
        foreach (var inner in ex.LoaderExceptions)
        {
            if (inner is null || string.IsNullOrWhiteSpace(inner.Message) || messages.Contains(inner.Message))
            {
                continue;
            }

            messages.Add(inner.Message);
            if (messages.Count >= 3)
            {
                break;
            }
        }

        return messages.Count == 0 ? "（未提供详细信息）" : string.Join("；", messages);
    }
}
