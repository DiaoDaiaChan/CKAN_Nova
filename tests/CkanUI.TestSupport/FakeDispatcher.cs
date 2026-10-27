using CkanUI.Services.Abstractions;

namespace CkanUI.TestSupport;

/// <summary>同步执行的 dispatcher（05 篇）。</summary>
public sealed class FakeDispatcher : IDispatcher
{
    public bool IsOnUiThread => true;

    public void Post(Action action) => action();

    public Task InvokeAsync(Action action)
    {
        action();
        return Task.CompletedTask;
    }

    public Task<T> InvokeAsync<T>(Func<T> func) => Task.FromResult(func());
}
