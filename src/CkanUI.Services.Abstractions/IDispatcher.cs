namespace CkanUI.Services.Abstractions;

/// <summary>唯一跨线程封送门面（01 篇 P3 / 05 篇）。</summary>
public interface IDispatcher
{
    bool IsOnUiThread { get; }

    void Post(Action action);

    Task InvokeAsync(Action action);

    Task<T> InvokeAsync<T>(Func<T> func);
}
