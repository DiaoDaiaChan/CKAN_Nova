namespace CkanUI.Services.Installers;

/// <summary>
/// 按「当前活动实例」创建后端：切换实例后注册表/缓存都变了，后端不能跨实例复用。
/// </summary>
public interface IInstallerBackendFactory
{
    /// <summary>尝试创建后端；没有当前活动实例时返回 false。</summary>
    bool TryCreate(out IInstallerBackend? backend);
}
