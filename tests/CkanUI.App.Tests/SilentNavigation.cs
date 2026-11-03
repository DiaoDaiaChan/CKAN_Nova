using System;
using System.Threading;
using System.Threading.Tasks;
using CkanUI.ViewModels.Navigation;

namespace CkanUI.App.Tests;

/// <summary>测试用导航桩：记录没人关心，但必须让外壳能建出导航项。</summary>
internal sealed class SilentNavigation : INavigationService
{
    public object? CurrentPage => null;

    public string CurrentKey => "mods";

    public event Action<object?, string>? Navigated;

    public Task<bool> NavigateAsync(string pageKey, object? parameter = null, CancellationToken ct = default)
    {
        Navigated?.Invoke(null, pageKey);
        return Task.FromResult(true);
    }
}
