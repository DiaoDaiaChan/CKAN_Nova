namespace CkanUI.Services.Abstractions.Dtos;

/// <summary>
/// 插件加载/激活动作失败的结构化分类（12 篇 F-17）。
/// </summary>
/// <remarks>
/// 分类存在的意义是「可断言」：界面只显示中文消息，测试与日志按分类断言，
/// 这样文案改动不会把测试改红，而分类退化（例如把「版本太老」误报成「不是程序集」）会被抓住。
/// </remarks>
public enum PluginLoadFailure
{
    /// <summary>没有失败。</summary>
    None = 0,

    /// <summary>路径为空或文件不存在。</summary>
    NotFound = 1,

    /// <summary>不是可加载的 .NET 程序集（坏 PE、原生 dll、扩展名不对）。</summary>
    NotAnAssembly = 2,

    /// <summary>缺少依赖程序集（插件目录里少了它引用的 dll）。</summary>
    MissingDependency = 3,

    /// <summary>程序集里没有任何实现 <c>ICkanUiPlugin</c> 的公开类型。</summary>
    NoPluginType = 4,

    /// <summary>插件要求的最小宿主版本高于当前宿主。</summary>
    HostVersionTooOld = 5,

    /// <summary>构造插件实例时抛异常（含没有公共无参构造函数）。</summary>
    InstantiationFailed = 6,

    /// <summary>读取插件元数据时抛异常（属性 getter 抛了）。</summary>
    MetadataFailed = 7,

    /// <summary>同一路径的插件已经加载过（先卸载再加载）。</summary>
    AlreadyLoaded = 8,

    /// <summary>同一插件 Id 已被另一个文件占用。</summary>
    DuplicateId = 9,

    /// <summary>激活时 <c>Initialize</c> 抛异常。</summary>
    ActivationFailed = 10,

    /// <summary>其它未归类的加载失败（IO 权限、路径非法等）。</summary>
    LoadFailed = 11,
}
