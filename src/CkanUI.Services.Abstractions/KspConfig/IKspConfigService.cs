namespace CkanUI.Services.Abstractions.KspConfig;

/// <summary>
/// KSP 原生配置（settings.cfg）读取、修改写盘与官方多语言字典提取服务。
/// </summary>
public interface IKspConfigService
{
    /// <summary>
    /// 读取指定实例或当前活动实例的 settings.cfg 配置文件与官方字典。
    /// </summary>
    /// <param name="instancePath">KSP 游戏根目录（为 null 时使用 IInstanceService 当前激活实例）。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>解析后的完整配置文档。</returns>
    Task<KspConfigDocument> LoadConfigAsync(string? instancePath = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 保存变更到 settings.cfg 文件。写盘前自动创建 .bak 备份，保全行结构、注释与输入设备绑定。
    /// </summary>
    /// <param name="document">原配置文档基线。</param>
    /// <param name="modifiedSettings">发生修改的配置项列表。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>保存是否成功。</returns>
    Task<bool> SaveConfigAsync(KspConfigDocument document, IEnumerable<KspSettingItem> modifiedSettings, CancellationToken cancellationToken = default);

    /// <summary>
    /// 从 GameData/Squad/Localization/dictionary.cfg 直接提取官方本地化字典。
    /// </summary>
    /// <param name="instancePath">KSP 游戏根目录。</param>
    /// <param name="language">目标语言标识（为 null 时自动遵循 settings.cfg 中的 LANGUAGE 设定，缺省回退到 zh-cn）。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>词条键（如 #autoLOC_149894）到官方译文的映射。</returns>
    Task<IReadOnlyDictionary<string, string>> LoadOfficialLocalizationAsync(string? instancePath = null, string? language = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 从最新的 .bak 备份文件回滚恢复 settings.cfg。
    /// </summary>
    /// <param name="instancePath">KSP 游戏根目录。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>恢复是否成功。</returns>
    Task<bool> RestoreBackupAsync(string? instancePath = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取当前实例的备份文件路径（若不存在返回 null）。
    /// </summary>
    /// <param name="instancePath">KSP 游戏根目录。</param>
    string? GetBackupPath(string? instancePath = null);
}
