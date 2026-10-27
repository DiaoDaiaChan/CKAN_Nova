using CkanUI.ViewModels;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

[TestFixture]
public sealed class PageViewModelTests
{
    private sealed class TestPage : PageViewModel
    {
        public int ActivatedCalls { get; private set; }
        public int DeactivatedCalls { get; private set; }

        protected override void OnActivated()
        {
            ActivatedCalls++;
        }

        protected override void OnDeactivated()
        {
            DeactivatedCalls++;
        }
    }

    [Test]
    public void ActivateAndDeactivate_AreIdempotent()
    {
        var page = new TestPage();
        Assert.That(page.IsActive, Is.False);

        // 首次激活
        page.Activate();
        Assert.That(page.IsActive, Is.True);
        Assert.That(page.ActivatedCalls, Is.EqualTo(1));

        // 重复激活，幂等不重复调用
        page.Activate();
        page.Activate();
        Assert.That(page.IsActive, Is.True);
        Assert.That(page.ActivatedCalls, Is.EqualTo(1));

        // 首次停用
        page.Deactivate();
        Assert.That(page.IsActive, Is.False);
        Assert.That(page.DeactivatedCalls, Is.EqualTo(1));

        // 重复停用，幂等不重复调用
        page.Deactivate();
        page.Deactivate();
        Assert.That(page.IsActive, Is.False);
        Assert.That(page.DeactivatedCalls, Is.EqualTo(1));
    }
}
