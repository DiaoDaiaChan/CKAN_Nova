using CkanUI.Services.Abstractions;

namespace CkanUI.Services.Abstractions.Profiles;

/// <summary>
/// 一个「实例配置档」：同一个游戏安装下的一套 GameData + CKAN 状态。
/// </summary>
/// <remarks>
/// 配置档不是副本。切换时宿主把当前生效的 <c>GameData</c> 与 <c>CKAN</c> 目录整体换出、把目标配置档的目录换入
/// （同卷 rename，毫秒级），因此多个整合包可以共用一份游戏本体，不需要为每个整合包复制整个实例。
/// 目录布局：<c>&lt;GameDir&gt;/CKANProfiles/&lt;名字&gt;/{GameData,CKAN,modpack.ckan}</c>。
/// 当前生效的那一份不在 <c>CKANProfiles</c> 里，而是回到游戏根目录下的 <c>GameData</c> 与 <c>CKAN</c>。
/// </remarks>
public sealed record InstanceProfileInfo(
    string Name,
    string InstanceName,
    bool IsActive,
    string Directory,
    string GameDataPath,
    long SizeBytes,
    int FileCount,
    DateTime? LastUsedUtc)
{
    /// <summary>是否由用户指定为「启动游戏时自动切到这一档」。</summary>
    public bool IsLaunchProfile { get; init; }

    /// <summary>导入来源（.ckan 路径或 URL）；手工创建的配置档为 null。</summary>
    public string? Source { get; init; }

    /// <summary>manifest 里记录的模组数（.ckan 的 depends + recommends）；未知为 0。</summary>
    public int ModCount { get; init; }
}

/// <summary>切换结果。</summary>
/// <param name="InstanceName">实例名。</param>
/// <param name="PreviousProfile">切换前生效的配置档；首次接管时为 null。</param>
/// <param name="Profile">切换后生效的配置档。</param>
/// <param name="AdoptedLiveState">是否把切换前的 GameData/CKAN 收进了 <paramref name="PreviousProfile"/> 那一档（首次接管为 true）。</param>
public sealed record InstanceProfileSwitchResult(
    string InstanceName,
    string? PreviousProfile,
    string Profile,
    bool AdoptedLiveState);

/// <summary>
/// 实例配置档（整合包切换）服务：让同一个游戏安装承载多套互不干扰的模组状态。
/// </summary>
/// <remarks>
/// 安全边界：
/// ① 任何写操作前先确认游戏进程没有在跑（<see cref="IInstanceService.IsGameRunning"/>）；
/// ② 切换前必须释放注册表锁（<see cref="IRegistryService.CloseAsync"/>），否则 registry.json 被占用；
/// ③ 名字只允许字母/数字/空格/下划线/点/连字符，不允许路径分隔符与 <c>..</c>；
/// ④ 目录换出/换入失败时把已经换出的部分换回去，绝不留下半个配置档。
/// </remarks>
public interface IInstanceProfileService
{
    /// <summary>列出某个实例的全部配置档（含当前生效的那一档，IsActive=true）。</summary>
    Task<OperationResult<IReadOnlyList<InstanceProfileInfo>>> ListAsync(string instanceName, CancellationToken ct = default);

    /// <summary>
    /// 新建一个空配置档。<paramref name="adoptCurrent"/> 为 true 时把当前的 GameData/CKAN 收进新档
    /// （用于第一次给已有实例建立「原版」基线）。
    /// </summary>
    Task<OperationResult<InstanceProfileInfo>> CreateAsync(
        string instanceName,
        string profileName,
        bool adoptCurrent = false,
        string? source = null,
        CancellationToken ct = default);

    /// <summary>切换到指定配置档：换出当前 GameData/CKAN，换入目标档。</summary>
    Task<OperationResult<InstanceProfileSwitchResult>> SwitchAsync(string instanceName, string profileName, CancellationToken ct = default);

    /// <summary>删除一个非当前生效的配置档（连同其 GameData/CKAN）。</summary>
    Task<OperationResult<bool>> DeleteAsync(string instanceName, string profileName, CancellationToken ct = default);

    /// <summary>读取「启动游戏时自动切到哪一档」；没设置返回 null。</summary>
    Task<OperationResult<string?>> GetLaunchProfileAsync(string instanceName, CancellationToken ct = default);

    /// <summary>设置（传 null 清除）启动配置档。</summary>
    Task<OperationResult<bool>> SetLaunchProfileAsync(string instanceName, string? profileName, CancellationToken ct = default);

    /// <summary>
    /// 启动游戏前调用：若设置了启动配置档且当前不是它，则切换过去。
    /// 没有设置、已经是它、或实例没有配置档时返回成功且不做任何事。
    /// </summary>
    Task<OperationResult<bool>> ApplyLaunchProfileAsync(string instanceName, CancellationToken ct = default);
}
