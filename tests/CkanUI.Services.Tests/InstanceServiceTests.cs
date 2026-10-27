using CkanUI.Services.Abstractions;
using CkanUI.Services.Instances;
using CkanUI.TestSupport;
using CKAN;
using NUnit.Framework;

namespace CkanUI.Services.Tests;

[TestFixture]
public sealed class InstanceServiceTests
{
    [Test]
    public void Constructor_InitializesEmptySnapshot_AndEventsCanUnsubscribe()
    {
        var manager = new GameInstanceManager(new FakeUser(), new FakeConfiguration());
        using var temp = new TempWorkspace("instance-service");
        var repositoryData = new RepositoryDataManager(temp.Path);
        var service = new InstanceService(manager, repositoryData, new FakeDispatcher());
        var calls = 0;
        Action handler = () => calls++;
        service.InstancesChanged += handler;
        service.InstancesChanged -= handler;
        Assert.That(calls, Is.EqualTo(0));
        Assert.That(service.Instances, Is.Empty);
        GC.KeepAlive(service);
    }
}
