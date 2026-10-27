using Autofac;
using CKAN;
using CKAN.Configuration;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Services.Settings;

/// <summary>
/// 首选下载主机服务实现（F-15 / UI-30）。
/// 对接 Core IConfiguration.PreferredHosts 配置与已知主机提取。
/// </summary>
public sealed class PreferredHostsService : IPreferredHostsService
{
    private static readonly string[] DefaultKnownHosts =
    [
        "github.com",
        "spacedock.info",
        "archive.org",
        "gitlab.com"
    ];

    private readonly IConfiguration _configuration;
    private readonly ISettingsService _settingsService;
    private readonly GameInstanceManager? _gameInstanceManager;
    private readonly RepositoryDataManager? _repositoryDataManager;

    public PreferredHostsService(
        IConfiguration configuration,
        ISettingsService settingsService,
        GameInstanceManager? gameInstanceManager = null,
        RepositoryDataManager? repositoryDataManager = null)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _gameInstanceManager = gameInstanceManager;
        _repositoryDataManager = repositoryDataManager;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<HostPriorityItemDto>> GetPreferredHostsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var preferred = _configuration.PreferredHosts;
        var result = new List<HostPriorityItemDto>();

        if (preferred is not null)
        {
            var priority = 1;
            foreach (var host in preferred)
            {
                var isPlaceholder = string.IsNullOrEmpty(host);
                result.Add(new HostPriorityItemDto(isPlaceholder ? null : host, priority++, isPlaceholder));
            }
        }

        return Task.FromResult<IReadOnlyList<HostPriorityItemDto>>(result);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> GetKnownHostsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. 尝试从 Core 容器提取已知主机
        try
        {
            GameInstanceManager? manager = null;
            RepositoryDataManager? repoData = null;

            if (CKAN.ServiceLocator.Container is not null && CKAN.ServiceLocator.Container.IsRegistered<GameInstanceManager>())
            {
                manager = CKAN.ServiceLocator.Container.Resolve<GameInstanceManager>();
            }

            if (CKAN.ServiceLocator.Container is not null && CKAN.ServiceLocator.Container.IsRegistered<RepositoryDataManager>())
            {
                repoData = CKAN.ServiceLocator.Container.Resolve<RepositoryDataManager>();
            }

            if (manager?.CurrentInstance is not null && repoData is not null)
            {
                var registryManager = RegistryManager.Instance(manager.CurrentInstance, repoData, null, false);
                if (registryManager?.registry is not null)
                {
                    foreach (var h in registryManager.registry.GetAllHosts())
                    {
                        if (!string.IsNullOrWhiteSpace(h))
                        {
                            known.Add(h.Trim());
                        }
                    }
                }
            }
        }
        catch
        {
            // 从当前注册表提取所有主机时跳过（未打开实例或无注册表）
        }

        // 2. 补充预置的常见主流主机
        foreach (var def in DefaultKnownHosts)
        {
            known.Add(def);
        }

        var sorted = known.OrderBy(h => h, StringComparer.OrdinalIgnoreCase).ToList();
        return Task.FromResult<IReadOnlyList<string>>(sorted);
    }

    /// <inheritdoc />
    public async Task<OperationResult<bool>> SavePreferredHostsAsync(IEnumerable<string?> hosts, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(hosts);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var list = hosts.Select(h => string.IsNullOrWhiteSpace(h) ? null : h.Trim()).ToList();

            // 通过 ISettingsService 保存以触发事件广播与统一格式落盘
            var wireValue = System.Text.Json.JsonSerializer.Serialize(list);
            var result = await _settingsService.SetAsync(SettingKeys.PreferredHosts, wireValue, cancellationToken);

            if (!result.IsSuccess)
            {
                return OperationResult<bool>.Fail(result.Message ?? "保存首选主机配置失败");
            }

            return OperationResult<bool>.Ok(true);
        }
        catch (Exception ex)
        {
            return OperationResult<bool>.Fail($"保存首选主机失败: {ex.Message}");
        }
    }
}
