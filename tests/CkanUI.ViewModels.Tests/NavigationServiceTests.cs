using System.Threading.Tasks;
using CkanUI.Services.Abstractions;
using CkanUI.ViewModels.Navigation;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

[TestFixture]
public sealed class NavigationServiceTests
{
    private sealed class LifecycleTrackingPage : PageViewModel
    {
        private readonly List<string> _log;
        private readonly string _name;

        public LifecycleTrackingPage(string name, List<string> log)
        {
            _name = name;
            _log = log;
        }

        public int ActivationCount { get; private set; }
        public int DeactivationCount { get; private set; }

        protected override void OnActivated()
        {
            ActivationCount++;
            _log.Add($"{_name}.Activated");
        }

        protected override void OnDeactivated()
        {
            DeactivationCount++;
            _log.Add($"{_name}.Deactivated");
        }
    }

    private sealed class LeakTestPage : PageViewModel
    {
        private readonly EventSource _source;

        public LeakTestPage(EventSource source)
        {
            _source = source;
        }

        protected override void OnActivated()
        {
            _source.Subscribe();
        }

        protected override void OnDeactivated()
        {
            _source.Unsubscribe();
        }
    }

    private sealed class EventSource
    {
        public int TotalSubscribes { get; private set; }
        public int TotalUnsubscribes { get; private set; }
        public int ActiveSubscribers => TotalSubscribes - TotalUnsubscribes;

        public void Subscribe() => TotalSubscribes++;
        public void Unsubscribe() => TotalUnsubscribes++;
    }

    [Test]
    public async Task NavigateAsync_Order_DeactivatesOldBeforeNavigatedAndBeforeActivatingNew()
    {
        var log = new List<string>();
        var page1 = new LifecycleTrackingPage("Page1", log);
        var page2 = new LifecycleTrackingPage("Page2", log);

        var nav = new NavigationService(key => key switch
        {
            "p1" => page1,
            "p2" => page2,
            _ => null
        });

        nav.Navigated += (newPage, key) =>
        {
            log.Add($"Navigated({key})");
        };

        // 首次切到 p1
        var success1 = await nav.NavigateAsync("p1");
        Assert.That(success1, Is.True);
        Assert.That(nav.CurrentPage, Is.SameAs(page1));
        Assert.That(nav.CurrentKey, Is.EqualTo("p1"));

        // 清空日志，检验切到 p2 时的调用顺序
        log.Clear();
        var success2 = await nav.NavigateAsync("p2");
        Assert.That(success2, Is.True);
        Assert.That(nav.CurrentPage, Is.SameAs(page2));
        Assert.That(nav.CurrentKey, Is.EqualTo("p2"));

        // 验证铁律：Deactivate 必须在 Navigated 事件之前，且旧页面失活在先
        var expectedOrder = new[]
        {
            "Page1.Deactivated",
            "Page2.Activated",
            "Navigated(p2)"
        };
        Assert.That(log, Is.EqualTo(expectedOrder));
    }

    [Test]
    public async Task NavigateAsync_SameKey_DoesNotReactivate()
    {
        var log = new List<string>();
        var page1 = new LifecycleTrackingPage("Page1", log);
        var nav = new NavigationService(_ => page1);

        await nav.NavigateAsync("p1");
        Assert.That(page1.ActivationCount, Is.EqualTo(1));

        log.Clear();
        var res = await nav.NavigateAsync("p1");
        Assert.That(res, Is.True);
        Assert.That(page1.ActivationCount, Is.EqualTo(1));
        Assert.That(page1.DeactivationCount, Is.EqualTo(0));
        Assert.That(log, Is.Empty);
    }

    [Test]
    public async Task NavigateAsync_ExceptionInFactory_SwallowedAndReturnsFalse()
    {
        var nav = new NavigationService(_ => throw new InvalidOperationException("Boom"));

        var result = await nav.NavigateAsync("error");
        Assert.That(result, Is.False);
        Assert.That(nav.CurrentPage, Is.Null);
    }

    [Test]
    public async Task NavigateAsync_TwentyTransitions_NoEventLeak()
    {
        // ARC-03 验收条件：切换 20 次无事件泄漏（用计数断言：订阅/退订成对，累计订阅数 - 退订数 == 当前活跃页数）
        var eventSource = new EventSource();
        var pageA = new LeakTestPage(eventSource);
        var pageB = new LeakTestPage(eventSource);

        var nav = new NavigationService(key => key switch
        {
            "A" => pageA,
            "B" => pageB,
            _ => null
        });

        // 进行 20 次页面来回切换
        for (var i = 0; i < 20; i++)
        {
            var target = (i % 2 == 0) ? "A" : "B";
            var success = await nav.NavigateAsync(target);
            Assert.That(success, Is.True);

            // 任何时刻当前活跃页面为 1，累计订阅 - 退订应严格等于 1
            Assert.That(eventSource.ActiveSubscribers, Is.EqualTo(1), $"Leak detected at iteration {i}");
        }

        // 20 次切换后：因为每次切换订阅 1 次，共切 20 次，订阅 20 次，退订 19 次，当前活跃 1
        Assert.That(eventSource.TotalSubscribes, Is.EqualTo(20));
        Assert.That(eventSource.TotalUnsubscribes, Is.EqualTo(19));
        Assert.That(eventSource.TotalSubscribes - eventSource.TotalUnsubscribes, Is.EqualTo(1));
    }
}
