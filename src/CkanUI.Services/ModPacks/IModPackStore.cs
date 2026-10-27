using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Services.ModPacks;

/// <summary>
/// 导入导出功能对 Core 的唯一触达面（与 <c>IInstallerBackend</c>、<c>IRepositoryStore</c> 同构）。
/// </summary>
/// <remarks>
/// 之所以把这些操作包成接口：注册表 / 元数据解析 / 下载缓存都在 Core 里且需要真实游戏实例，
/// 而分类、路径校验、文件写出、实例比对全是纯逻辑。把 Core 面收窄到一个接口后，
/// <see cref="ModPackCatalog"/> 可以完全离线测试（见 tests/CkanUI.Services.Tests/ModPackCatalogTests.cs）。
/// </remarks>
public interface IModPackStore
{
    /// <summary>是否有当前实例。</summary>
    bool HasActiveInstance { get; }

    /// <summary>当前实例名。</summary>
    string? InstanceName { get; }

    /// <summary>当前实例已安装的模组（含自动安装标记，过滤由调用方决定）。</summary>
    IReadOnlyList<InstalledModuleInfo> InstalledModules();

    /// <summary>指定实例已安装的模组（实例不存在时抛 <see cref="InvalidOperationException"/>）。</summary>
    IReadOnlyList<InstalledModuleInfo> InstalledModulesIn(string instanceName);

    /// <summary>
    /// 生成当前实例的元数据包 JSON：对应上游 <c>RegistryManager.GenerateModpack(recommends, with_versions).ToJson()</c>。
    /// </summary>
    string BuildModPackJson(bool useRecommends, bool withVersions);

    /// <summary>解析一个 <c>.ckan</c> 文件：对应上游 <c>CkanModule.FromFile</c> 的投影。</summary>
    ModPackFile LoadModPackFile(string path);

    /// <summary>查询仓库索引里是否存在该模组（以及请求的精确版本是否存在）。</summary>
    ModPackAvailability Lookup(string identifier, string? version);

    /// <summary>已下载缓存里该模组 zip 的路径；没有缓存返回 null。</summary>
    string? CachedArchive(string identifier, string version);
}
