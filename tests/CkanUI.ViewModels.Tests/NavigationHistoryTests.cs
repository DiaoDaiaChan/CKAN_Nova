using System.Threading.Tasks;
using CkanUI.ViewModels;
using CkanUI.ViewModels.Navigation;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

/// <summary>F-25 前进/后退历史：栈语义与外壳按钮可用性。</summary>
[TestFixture]
public sealed class NavigationHistoryTests
{
    private static NavigationService CreateService(params string[] keys)
    {
        return new NavigationService(key => System.Array.IndexOf(keys, key) >= 0 ? new object() : null);
    }

    [Test]
    public void FreshService_CannotGoBackOrForward()
    {
        var nav = CreateService("a", "b");
        Assert.That(nav.CanGoBack, Is.False);
        Assert.That(nav.CanGoForward, Is.False);
    }

    [Test]
    public async Task NavigateTwice_EnablesBack_AndBackReturnsPreviousKey()
    {
        var nav = CreateService("a", "b");
        Assert.That(await nav.NavigateAsync("a"), Is.True);
        Assert.That(await nav.NavigateAsync("b"), Is.True);
        Assert.That(nav.CanGoBack, Is.True);
        Assert.That(nav.CanGoForward, Is.False);

        Assert.That(await nav.GoBackAsync(), Is.True);
        Assert.That(nav.CurrentKey, Is.EqualTo("a"));
        Assert.That(nav.CanGoBack, Is.False);
        Assert.That(nav.CanGoForward, Is.True);
    }

    [Test]
    public async Task GoForward_RestoresTheAbandonedPage()
    {
        var nav = CreateService("a", "b");
        await nav.NavigateAsync("a");
        await nav.NavigateAsync("b");
        await nav.GoBackAsync();

        Assert.That(await nav.GoForwardAsync(), Is.True);
        Assert.That(nav.CurrentKey, Is.EqualTo("b"));
        Assert.That(nav.CanGoForward, Is.False);
        Assert.That(nav.CanGoBack, Is.True);
    }

    [Test]
    public async Task NewNavigationAfterBack_ClearsForwardHistory()
    {
        var nav = CreateService("a", "b", "c");
        await nav.NavigateAsync("a");
        await nav.NavigateAsync("b");
        await nav.GoBackAsync();
        Assert.That(nav.CanGoForward, Is.True);

        await nav.NavigateAsync("c");
        Assert.That(nav.CanGoForward, Is.False);
        Assert.That(nav.CanGoBack, Is.True);
    }

    [Test]
    public async Task GoBack_OnEmptyHistory_ReturnsFalse_AndKeepsCurrentPage()
    {
        var nav = CreateService("a");
        await nav.NavigateAsync("a");

        Assert.That(await nav.GoBackAsync(), Is.False);
        Assert.That(await nav.GoForwardAsync(), Is.False);
        Assert.That(nav.CurrentKey, Is.EqualTo("a"));
    }

    [Test]
    public async Task SameKeyNavigation_DoesNotGrowHistory()
    {
        var nav = CreateService("a");
        await nav.NavigateAsync("a");
        await nav.NavigateAsync("a");

        Assert.That(nav.CanGoBack, Is.False);
    }

    [Test]
    public async Task UnknownKey_DoesNotTouchHistory()
    {
        var nav = CreateService("a");
        await nav.NavigateAsync("a");

        Assert.That(await nav.NavigateAsync("missing"), Is.False);
        Assert.That(nav.CanGoBack, Is.False);
        Assert.That(nav.CurrentKey, Is.EqualTo("a"));
    }

    [Test]
    public async Task HistoryChanged_FiresOnNavigationAndOnBack()
    {
        var nav = CreateService("a", "b");
        var count = 0;
        nav.HistoryChanged += () => count++;

        await nav.NavigateAsync("a");
        await nav.NavigateAsync("b");
        var afterNavigate = count;
        await nav.GoBackAsync();

        Assert.That(afterNavigate, Is.GreaterThan(0));
        Assert.That(count, Is.GreaterThan(afterNavigate));
    }

    [Test]
    public async Task ShellViewModel_TracksHistoryAvailability()
    {
        var nav = CreateService("a", "b");
        var shell = new ShellViewModel(nav, key => "T:" + key);

        await shell.NavigateCommand.ExecuteAsync("a");
        Assert.That(shell.CanGoBack, Is.False);

        await shell.NavigateCommand.ExecuteAsync("b");
        Assert.That(shell.CanGoBack, Is.True);

        await shell.BackCommand.ExecuteAsync(null);
        Assert.That(nav.CurrentKey, Is.EqualTo("a"));
        Assert.That(shell.CanGoBack, Is.False);
        Assert.That(shell.CanGoForward, Is.True);

        await shell.ForwardCommand.ExecuteAsync(null);
        Assert.That(nav.CurrentKey, Is.EqualTo("b"));
        Assert.That(shell.CanGoForward, Is.False);
    }

    [Test]
    public void ShellViewModel_ExposesLocalizedToolTips()
    {
        var shell = new ShellViewModel(new NavigationService(_ => null), key => "T:" + key);
        Assert.That(shell.BackToolTip, Is.EqualTo("T:Ui_Common_Nav_Back"));
        Assert.That(shell.ForwardToolTip, Is.EqualTo("T:Ui_Common_Nav_Forward"));
    }
}
