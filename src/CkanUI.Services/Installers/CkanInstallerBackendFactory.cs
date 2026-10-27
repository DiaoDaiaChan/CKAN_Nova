using System;

using CKAN;
using CKAN.Configuration;

namespace CkanUI.Services.Installers;

/// <summary>
/// 生产用后端工厂：每次从 <see cref="GameInstanceManager"/> 取当前活动实例，
/// 切换实例后自动拿到新实例对应的注册表与下载缓存。
/// </summary>
public sealed class CkanInstallerBackendFactory : IInstallerBackendFactory
{
    private readonly GameInstanceManager _instances;
    private readonly RepositoryDataManager _repositoryData;
    private readonly IConfiguration _configuration;
    private readonly IUser _user;

    public CkanInstallerBackendFactory(GameInstanceManager instances,
                                       RepositoryDataManager repositoryData,
                                       IConfiguration configuration,
                                       IUser user)
    {
        _instances = instances ?? throw new ArgumentNullException(nameof(instances));
        _repositoryData = repositoryData ?? throw new ArgumentNullException(nameof(repositoryData));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _user = user ?? throw new ArgumentNullException(nameof(user));
    }

    /// <inheritdoc />
    public bool TryCreate(out IInstallerBackend? backend)
    {
        backend = null;
        if (_instances.CurrentInstance is not { } instance)
        {
            return false;
        }

        backend = new CkanInstallerBackend(instance, _instances, _repositoryData, _configuration, _user);
        return true;
    }
}
