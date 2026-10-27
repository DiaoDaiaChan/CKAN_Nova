namespace CkanUI.ViewModels.Pages.Settings;

/// <summary>
/// 设置页的分组标识（03 篇 §7 / 06 篇 UI-07 的七组划分）。
/// 分组只影响呈现顺序与标题，不改变键名；同一项永远只属于一组，避免两处写同一个键。
/// </summary>
public enum SettingGroupKey
{
    /// <summary>通用：与实例无关的通用行为。</summary>
    General,

    /// <summary>实例：启动时自动打开的实例。</summary>
    Instance,

    /// <summary>下载缓存：缓存目录与容量上限（F-50）。</summary>
    DownloadCache,

    /// <summary>仓库与网络：自动刷新间隔与首选主机顺序（F-15）。</summary>
    Repository,

    /// <summary>认证令牌：host + token 的增删与脱敏显示（F-11）。</summary>
    AuthToken,

    /// <summary>外观：主题、语言、密度；只写 ckan-ui.json（20 篇 P-01/P-08）。</summary>
    Appearance,

    /// <summary>高级：安装过滤器等直接面向配置值的项（F-16）。</summary>
    Advanced,
}
