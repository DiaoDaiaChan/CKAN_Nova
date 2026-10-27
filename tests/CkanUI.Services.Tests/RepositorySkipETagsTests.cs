using System;
using System.Threading;
using System.Threading.Tasks;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using NUnit.Framework;

namespace CkanUI.Services.Tests;

[TestFixture]
public sealed class RepositorySkipETagsTests
{
    private sealed class MockRepositoryService : IRepositoryService
    {
        public bool LastSkipETags { get; private set; }
        public int RefreshCalls { get; private set; }

        public RepositorySnapshot Current => RepositorySnapshot.Empty;
        public event Action<RepositorySnapshot>? Updated { add { } remove { } }
        public event Action<OperationProgress>? RefreshProgress { add { } remove { } }

        public Task<OperationResult<RepositorySnapshot>> LoadCachedAsync(CancellationToken ct = default)
            => Task.FromResult(OperationResult<RepositorySnapshot>.Ok(RepositorySnapshot.Empty));

        public Task<OperationResult<RepositorySnapshot>> RefreshAsync(CancellationToken ct = default)
        {
            RefreshCalls++;
            LastSkipETags = false;
            return Task.FromResult(OperationResult<RepositorySnapshot>.Ok(RepositorySnapshot.Empty));
        }

        public Task<OperationResult<RepositorySnapshot>> RefreshAsync(bool skipETags, CancellationToken ct = default)
        {
            RefreshCalls++;
            LastSkipETags = skipETags;
            return Task.FromResult(OperationResult<RepositorySnapshot>.Ok(RepositorySnapshot.Empty));
        }

        public void DropCachedSnapshot() { }
    }

    private sealed class DefaultOnlyRepositoryService : IRepositoryService
    {
        public int BaseCalls { get; private set; }

        public RepositorySnapshot Current => RepositorySnapshot.Empty;
        public event Action<RepositorySnapshot>? Updated { add { } remove { } }
        public event Action<OperationProgress>? RefreshProgress { add { } remove { } }

        public Task<OperationResult<RepositorySnapshot>> LoadCachedAsync(CancellationToken ct = default)
            => Task.FromResult(OperationResult<RepositorySnapshot>.Ok(RepositorySnapshot.Empty));

        public Task<OperationResult<RepositorySnapshot>> RefreshAsync(CancellationToken ct = default)
        {
            BaseCalls++;
            return Task.FromResult(OperationResult<RepositorySnapshot>.Ok(RepositorySnapshot.Empty));
        }

        public void DropCachedSnapshot() { }
    }

    [Test]
    public async Task RefreshAsync_WithSkipETags_PassesFlagToImplementation()
    {
        var service = new MockRepositoryService();
        var result = await service.RefreshAsync(skipETags: true);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(service.RefreshCalls, Is.EqualTo(1));
        Assert.That(service.LastSkipETags, Is.True);
    }

    [Test]
    public async Task RefreshAsync_DefaultInterfaceMethod_FallsBackToBaseRefresh()
    {
        IRepositoryService service = new DefaultOnlyRepositoryService();
        var result = await service.RefreshAsync(skipETags: true);

        Assert.That(result.IsSuccess, Is.True);
        var impl = (DefaultOnlyRepositoryService)service;
        Assert.That(impl.BaseCalls, Is.EqualTo(1));
    }
}
