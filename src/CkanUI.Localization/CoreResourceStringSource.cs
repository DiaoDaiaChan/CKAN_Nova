using System.Globalization;
using System.Reflection;
using System.Resources;

namespace CkanUI.Localization;

/// <summary>
/// D11 的 L3 层：键带 <c>Core.</c> 前缀时，转发到上游 CKAN 的 CKAN.Properties.Resources。
/// </summary>
/// <remarks>
/// 为什么反射而不是直接写 CKAN.Properties.Resources.Foo：那张表由 Core 的 UseOurResourceManager
/// 目标在 obj 里生成（external/CKAN/Core/CKAN-core.csproj:91-93，把 internal 改写成 public），
/// 类型存在但属性名/可见性由构建目标决定；直接引用会让本地化层与上游构建脚本强绑死，
/// 换一个不含资源的构建方式就编译失败。反射让这层"在就生效、不在就静默降级"。
/// 为什么不把 209 条资源抄进本地语言包：抄一遍等于把 209 条 × 13 语言变成新的翻译债务，
/// 而且上游更新后立刻过期。
/// 查找链：只处理 Core. 前缀键，其余一律返回 false，绝不吃掉界面键。
/// </remarks>
public sealed class CoreResourceStringSource : IStringSource
{
    /// <summary>本条来源认领的键前缀（区分界面键与 Core 键的唯一依据）。</summary>
    public const string KeyPrefix = "Core.";

    private const string ResourceTypeName = "CKAN.Properties.Resources";
    private const string ResourceAssemblyName = "CKAN";

    private readonly Lazy<ResourceManager?> _manager;
    private readonly ILocalizationLog _log;

    // 诊断只报一次：缺资源是构建期事实，每取一条文案刷一屏警告没有价值。
    private int _warned;

    /// <summary>创建 L3 来源。</summary>
    /// <param name="log">日志出口。</param>
    public CoreResourceStringSource(ILocalizationLog? log = null)
    {
        _log = log ?? NullLocalizationLog.Instance;
        _manager = new Lazy<ResourceManager?>(Resolve);
    }

    /// <inheritdoc />
    public string Name => "core";

    /// <summary>上游资源是否可用（不可用时本层永远返回 false）。</summary>
    public bool IsAvailable => _manager.Value is not null;

    /// <inheritdoc />
    public bool TryGet(string locale, string key, out string value)
    {
        value = string.Empty;
        if (string.IsNullOrEmpty(key) || !key.StartsWith(KeyPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var manager = _manager.Value;
        if (manager is null)
        {
            return false;
        }

        var resourceKey = key[KeyPrefix.Length..];
        if (resourceKey.Length == 0)
        {
            return false;
        }

        try
        {
            var text = manager.GetString(resourceKey, ResolveCulture(locale));
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            value = text;
            return true;
        }
        catch (MissingManifestResourceException)
        {
            return false;
        }
        catch (MissingSatelliteAssemblyException)
        {
            // 上游把 13 种语言打进了同一个程序集（SingleAssemblyResourceManager），
            // 触发卫星程序集查找说明当前构建换了资源策略：降级为"这层没命中"，不影响界面。
            return false;
        }
    }

    private ResourceManager? Resolve()
    {
        try
        {
            var type = Type.GetType($"{ResourceTypeName}, {ResourceAssemblyName}", throwOnError: false) ?? FindLoadedType();
            if (type is null)
            {
                WarnUnavailable($"未找到 {ResourceTypeName} 类型（CKAN 程序集可能未加载）");
                return null;
            }

            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            if (type.GetProperty("ResourceManager", flags)?.GetValue(null) is ResourceManager manager)
            {
                return manager;
            }

            WarnUnavailable($"{ResourceTypeName} 上没有可读的 ResourceManager 属性");
            return null;
        }
        catch (FileLoadException ex)
        {
            WarnUnavailable(ex.Message);
            return null;
        }
        catch (TypeLoadException ex)
        {
            WarnUnavailable(ex.Message);
            return null;
        }
        catch (TargetInvocationException ex)
        {
            WarnUnavailable(ex.Message);
            return null;
        }
    }

    private static Type? FindLoadedType()
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (!string.Equals(assembly.GetName().Name, ResourceAssemblyName, StringComparison.Ordinal))
            {
                continue;
            }

            var type = assembly.GetType(ResourceTypeName, throwOnError: false);
            if (type is not null)
            {
                return type;
            }
        }

        return null;
    }

    private static CultureInfo? ResolveCulture(string locale)
    {
        if (string.IsNullOrWhiteSpace(locale))
        {
            return null;
        }

        try
        {
            return CultureInfo.GetCultureInfo(locale.Trim());
        }
        catch (CultureNotFoundException)
        {
            // 脏配置不是错误：交由 ResourceManager 按中立资源回退。
            return null;
        }
    }

    private void WarnUnavailable(string detail)
    {
        if (Interlocked.Exchange(ref _warned, 1) == 0)
        {
            _log.Warn($"[i18n] Core 资源层不可用：{detail}；Core. 前缀键将回退为 key 本身。");
        }
    }
}
