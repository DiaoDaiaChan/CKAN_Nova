using CkanUI.Services.Abstractions;
using CkanUI.Services.Instances;
using CkanUI.TestSupport;
using CKAN;
using NUnit.Framework;

namespace CkanUI.Services.Tests;

/// <summary>
/// IInstanceService.RenameAsync 的失败路径：必须在碰 Core 之前就拒绝，
/// 否则会把「名字被占用」变成一次半改写的配置写入。
/// </summary>
/// <remarks>
/// 成功路径需要真实 KSP 目录（<c>GameInstanceManager.AddInstance</c> 会校验实例有效，
/// 而 <c>GameInstance.Version()</c> 依赖 CKAN 自己的 Autofac 容器），
/// 因此重命名的正例由实例页 VM 测试用假服务覆盖，这里只钉住拒绝语义。
/// </remarks>
[TestFixture]
public sealed class InstanceRenameTests
{
    private static (InstanceService Service, GameInstanceManager Manager, TempWorkspace Temp) Create()
    {
        var temp = new TempWorkspace("instance-rename");
        var manager = new GameInstanceManager(new FakeUser(), new FakeConfiguration());
        var repositoryData = new RepositoryDataManager(temp.Combine("repo"));
        return (new InstanceService(manager, repositoryData, new FakeDispatcher()), manager, temp);
    }

    [Test]
    public async Task RenameAsync_WithBlankName_FailsBeforeTouchingCore()
    {
        var (service, manager, temp) = Create();
        using var _ = temp;

        var result = await service.RenameAsync("Alpha", "   ");

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("实例名称"));
        Assert.That(manager.Instances, Is.Empty);
    }

    [Test]
    public async Task RenameAsync_ForUnknownInstance_Fails()
    {
        var (service, manager, temp) = Create();
        using var __ = temp;

        var result = await service.RenameAsync("Nope", "Gamma");

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("Nope"));
        Assert.That(manager.Instances, Is.Empty);
    }
}
