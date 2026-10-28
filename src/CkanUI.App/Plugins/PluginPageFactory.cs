using CkanUI.Services.Abstractions;
using CkanUI.ViewModels.Navigation;

using log4net;
using Microsoft.Extensions.DependencyInjection;

namespace CkanUI.App.Plugins;

/// <summary>
/// 插件页签的视图模型解析与构造（12 篇 F-17、ARC-03 的 <c>ViewLocator</c> 约定）。
/// </summary>
/// <remarks>
/// <para>契约层只给字符串（<c>PluginPageDescriptor.ViewModelTypeName</c>），视图模型类型住在插件自己的程序集里，
/// 而插件程序集是加载在可卸载 <c>AssemblyLoadContext</c> 中的：这里按名字在「已加载程序集」里查找，
/// 刻意不做类型缓存——缓存 <see cref="Type"/> 会钉住整个 ALC，插件卸载后程序集再也回收不了。</para>
/// <para>构造走 <see cref="ActivatorUtilities"/>，所以插件页可以像内置页一样用构造函数注入宿主服务；
/// 构造失败只记日志并返回 <c>null</c>（导航随之失败），宿主绝不因插件代码抛异常而崩。</para>
/// </remarks>
public static class PluginPageFactory
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(PluginPageFactory));

    /// <summary>
    /// 按名字解析插件页的视图模型类型。
    /// </summary>
    /// <param name="typeName">程序集限定名或全名。</param>
    /// <returns>解析到的类型；解析不到返回 <c>null</c>。</returns>
    public static Type? ResolveViewModelType(string? typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return null;
        }

        var name = typeName.Trim();

        // 插件程序集加载在默认上下文里时，Type.GetType 一次就够。
        var direct = Type.GetType(name, throwOnError: false);
        if (direct is not null)
        {
            return direct;
        }

        // 可卸载 ALC 里的类型只能逐个程序集找：Assembly.GetType 不吃「, AssemblyName」后缀，先裁掉。
        var bareName = name.Split(',')[0].Trim();
        if (bareName.Length == 0)
        {
            return null;
        }

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.IsDynamic)
            {
                continue;
            }

            Type? candidate;
            try
            {
                candidate = assembly.GetType(bareName, throwOnError: false);
            }
            catch (Exception ex) when (ex is TypeLoadException or FileLoadException or BadImageFormatException)
            {
                continue;
            }

            if (candidate is not null)
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>构造插件页视图模型；失败只记日志并返回 <c>null</c>。</summary>
    /// <param name="services">宿主 DI 容器（插件页可注入宿主服务）。</param>
    /// <param name="viewModelType">已解析的视图模型类型。</param>
    public static object? Create(IServiceProvider services, Type viewModelType)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(viewModelType);

        try
        {
            return ActivatorUtilities.CreateInstance(services, viewModelType);
        }
        catch (Exception ex)
        {
            Log.Warn($"插件页面构造失败：{viewModelType.FullName}（{ex.Message}）", ex);
            return null;
        }
    }

    /// <summary>
    /// 导航键 → 插件页实例：找不到归属、类型解析不出、构造失败都返回 <c>null</c>。
    /// </summary>
    /// <param name="services">宿主 DI 容器。</param>
    /// <param name="key">导航键。</param>
    /// <param name="reservedKeys">宿主内置页签键（插件不得占用）。</param>
    public static object? CreateFor(IServiceProvider services, string key, IEnumerable<string>? reservedKeys)
    {
        ArgumentNullException.ThrowIfNull(services);

        var plugins = services.GetService<IPluginService>();
        if (plugins is null)
        {
            return null;
        }

        var entry = PluginPageCatalog
            .Collect(plugins.Active, reservedKeys)
            .FirstOrDefault(candidate => string.Equals(candidate.Key, key, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            return null;
        }

        var type = ResolveViewModelType(entry.ViewModelTypeName);
        if (type is null)
        {
            Log.Warn($"插件「{entry.PluginName}」的页签「{entry.Key}」无法解析视图模型类型：{entry.ViewModelTypeName}");
            return null;
        }

        return Create(services, type);
    }
}
