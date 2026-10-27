using CkanUI.ViewModels;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

[TestFixture]
public sealed class ShellViewModelTests
{
    [Test]
    public void StatusText_DefaultsToReady()
    {
        var vm = new ShellViewModel();
        Assert.That(vm.StatusText, Is.EqualTo("就绪"));
    }
}
