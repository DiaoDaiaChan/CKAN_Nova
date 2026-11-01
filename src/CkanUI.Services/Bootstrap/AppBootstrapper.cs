using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Services.Logging;
using CkanUI.Services.Repositories;
using log4net;

namespace CkanUI.Services.Bootstrap;

/// <summary>
/// 启动/关闭编排（02 篇 §1 Ⅰ–⑩、§5）。顺序不可交换的理由见 02 篇；
/// 这里只做编排，真正的 Core 交互都在 InstanceService / RegistryService / RepositoryService 内部。
/// </summary>
public sealed class AppBootstrapper : IAppBootstrapper
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(AppBootstrapper));

    private readonly IInstanceService _instances;
    private readonly IRegistryService _registry;
    private readonly IRepositoryService _repositories;
    private readonly IErrorReporter _errors;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private bool _started;

    public AppBootstrapper(IInstanceService instances,
                           IRegistryService registry,
                           IRepositoryService repositories,
                           IErrorReporter errors)
    {
        _instances = instances ?? throw new ArgumentNullException(nameof(instances));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _repositories = repositories ?? throw new ArgumentNullException(nameof(repositories));
        _errors = errors ?? throw new ArgumentNullException(nameof(errors));
    }

    public async Task<OperationResult> StartupAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_started) return OperationResult.Ok();

            // ①：日志必须最先就位，否则后面任何失败都看不到原因。
            LoggingSetup.Initialize();

            // ⑥⑦⑧：由 InstanceService.SwitchAsync 一并完成（含锁定检测与 RegistryManager.Instance）。
            var listed = await _instances.RefreshAsync(cancellationToken).ConfigureAwait(false);
            if (!listed.IsSuccess)
            {
                Log.Error($"读取实例列表失败：{listed.Message} {listed.Detail}");
                return OperationResult.Fail("读取游戏实例列表失败。", listed.Detail, listed.Retryable);
            }

            var target = _instances.Current ?? _instances.Instances.FirstOrDefault();
            if (target is null)
            {
                // 没有实例不是启动失败：主窗口要能打开并引导用户去添加（实例向导属 M1）。
                Log.Info("没有可用的游戏实例，进入无实例模式。");
                _errors.Report("启动", "还没有可用的游戏实例", "请先在“实例”页添加一个 KSP 目录。");
                _started = true;
                return OperationResult.Ok();
            }

            var switched = await _instances.SwitchAsync(target.Name, cancellationToken).ConfigureAwait(false);
            if (!switched.IsSuccess)
            {
                Log.Error($"切换到实例“{target.Name}”失败：{switched.Message} {switched.Detail}");
                _errors.Report("启动", switched.Message ?? "切换游戏实例失败", switched.Detail);
                return OperationResult.Fail(switched.Message ?? "切换游戏实例失败。", switched.Detail, switched.Retryable);
            }

            // ⑨pre：先把仓库列表读出来再刷新索引。列表为空时服务层会补上内置的官方 CKAN 源
            // （离线写入，不需要用户先点「恢复为官方默认仓库列表」），首次启动后台刷新才有东西可下。
            var repositories = await _repositories.LoadRepositoriesAsync(cancellationToken).ConfigureAwait(false);
            if (!repositories.IsSuccess)
            {
                Log.Warn($"读取仓库列表失败：{repositories.Message} {repositories.Detail}");
            }
            else if (repositories.Value is { Count: > 0 } listedRepositories)
            {
                Log.Info($"仓库列表：{string.Join("、", listedRepositories.Select(row => row.Name))}。");
            }

            // ⑨：先出本地缓存让列表立刻可见，联网刷新放后台，失败只上报不影响启动。
            var cached = await _repositories.LoadCachedAsync(cancellationToken).ConfigureAwait(false);
            if (!cached.IsSuccess)
            {
                Log.Warn($"读取仓库缓存失败：{cached.Message} {cached.Detail}");
            }

            _ = RefreshRepositoriesInBackground();
            _started = true;
            Log.Info($"启动序列完成，当前实例：{target.Name}");
            return OperationResult.Ok();
        }
        catch (OperationCanceledException)
        {
            return OperationResult.Fail("启动已取消。", retryable: true);
        }
        catch (Exception ex)
        {
            _errors.Report(ex, "启动序列", "CKAN UI 启动时发生错误。");
            return OperationResult.Fail("启动失败。", ex.ToString(), retryable: true);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task RefreshRepositoriesInBackground()
    {
        try
        {
            await Task.Delay(250).ConfigureAwait(false);
            var refreshed = await _repositories.RefreshAsync(CancellationToken.None).ConfigureAwait(false);
            if (!refreshed.IsSuccess)
            {
                Log.Warn($"后台刷新仓库索引失败：{refreshed.Message} {refreshed.Detail}");
            }
        }
        catch (Exception ex)
        {
            Log.Warn("后台刷新仓库索引抛出异常。", ex);
        }
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_started) return;
            _started = false;
            Log.Info("关闭序列开始。");

            // ⑩：先退订事件（由各服务的 Dispose 完成），再 DisposeAll，避免已释放对象回调。
            var closed = await _registry.CloseAsync(cancellationToken).ConfigureAwait(false);
            if (!closed.IsSuccess) Log.Warn($"关闭注册表失败：{closed.Message} {closed.Detail}");

            Log.Info("关闭序列完成。");
        }
        catch (Exception ex)
        {
            Log.Warn("关闭序列抛出异常。", ex);
        }
        finally
        {
            _gate.Release();
        }
    }
}
