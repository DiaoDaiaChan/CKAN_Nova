using CkanUI.Services.Abstractions;
using CkanUI.Services.Instances;

namespace CkanUI.Services.ModConfig;

/// <summary>
/// "KSP 是否正在运行"的判定入口。
/// <para>
/// 抽成独立接口是为了让写盘器在单测里能确定地"假装游戏在跑"（T-CFG-08），
/// 生产实现走 <see cref="IInstanceService"/> 加进程探测，两条判断互为兜底。
/// </para>
/// </summary>
public interface IGameRunningProbe
{
    /// <summary>该实例对应的游戏进程是否在运行。</summary>
    bool IsGameRunning(string instanceName, string gameDir);
}

/// <summary>基于 <see cref="IInstanceService"/> 与 <see cref="IGameProcessProbe"/> 的实现。</summary>
public sealed class InstanceGameRunningProbe : IGameRunningProbe
{
    private readonly IInstanceService? _instances;
    private readonly IGameProcessProbe _processProbe;

    /// <summary>构造。</summary>
    /// <param name="instances">实例服务；为 null 时只用进程探测。</param>
    /// <param name="processProbe">进程探测；为 null 时用真实实现。</param>
    public InstanceGameRunningProbe(IInstanceService? instances = null, IGameProcessProbe? processProbe = null)
    {
        _instances = instances;
        _processProbe = processProbe ?? new GameProcessProbe();
    }

    /// <inheritdoc />
    public bool IsGameRunning(string instanceName, string gameDir)
    {
        if (_instances is not null && !string.IsNullOrWhiteSpace(instanceName))
        {
            try
            {
                if (_instances.IsGameRunning(instanceName))
                {
                    return true;
                }
            }
            catch (Exception)
            {
                // 实例层异常不该阻断写盘判定，继续走进程探测兜底。
            }
        }

        if (string.IsNullOrWhiteSpace(gameDir))
        {
            return false;
        }

        foreach (var imagePath in _processProbe.GetProcessImagePaths())
        {
            if (GameProcessProbe.IsInsideGameDir(imagePath, gameDir))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>测试替身用的固定结果探针。</summary>
public sealed class FixedGameRunningProbe : IGameRunningProbe
{
    private readonly bool _running;

    /// <summary>构造。</summary>
    /// <param name="running">固定返回的运行状态。</param>
    public FixedGameRunningProbe(bool running) => _running = running;

    /// <inheritdoc />
    public bool IsGameRunning(string instanceName, string gameDir) => _running;
}
