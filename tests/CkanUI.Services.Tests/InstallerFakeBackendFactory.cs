using CkanUI.Services.Installers;

namespace CkanUI.Services.Tests;

/// <summary>假后端工厂：<see cref="Backend"/> 为 null 时模拟「没有活动游戏实例」。</summary>
internal sealed class InstallerFakeBackendFactory : IInstallerBackendFactory
{
    public InstallerFakeBackend? Backend { get; set; }

    public int CreateCount { get; private set; }

    /// <inheritdoc />
    public bool TryCreate(out IInstallerBackend? backend)
    {
        CreateCount++;
        backend = Backend;
        return Backend is not null;
    }
}
