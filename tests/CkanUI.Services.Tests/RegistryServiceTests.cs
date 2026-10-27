using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Services.Registry;
using CkanUI.TestSupport;
using CKAN;
using NUnit.Framework;

namespace CkanUI.Services.Tests;

[TestFixture]
public sealed class RegistryServiceTests
{
    [Test]
    public void EmptyRegistrySnapshot_IsStable_AndUnsubscribedHandlerIsNotCalled()
    {
        var manager = new GameInstanceManager(new FakeUser(), new FakeConfiguration());
        using var temp = new TempWorkspace("registry-service");
        var repositoryData = new RepositoryDataManager(temp.Path);
        using var service = new RegistryService(manager, repositoryData, new FakeDispatcher());
        var count = 0;
        Action<RegistrySnapshot?> handler = _ => count++;
        service.Changed += handler;
        service.Changed -= handler;
        Assert.That(service.Current.InstanceName, Is.EqualTo(string.Empty));
        Assert.That(service.Current.Installed, Is.Empty);
        Assert.That(count, Is.EqualTo(0));
    }
}
