using CkanUI.App.Plugins;
using NUnit.Framework;

namespace CkanUI.App.Tests;

/// <summary>
/// 插件页实例缓存：一个导航键一个实例，插件下线时连实例一起释放。
/// </summary>
/// <remarks>
/// 这几条守的是「同一个页签反复进出不会越积越多」，以及「插件停用后页面实例真的被 Dispose」。
/// 缓存存在的理由见 <see cref="PluginPageCache"/> 的注释：外壳把页面视图常驻舞台，视图只在
/// 挂载/卸载时开关轮询——实例不缓存、不释放，旧实例的定时器就永远在跑。
/// </remarks>
[TestFixture]
public class PluginPageCacheTests
{
    [Test]
    public void 同一个导航键复用同一个实例()
    {
        var cache = new PluginPageCache();
        var created = 0;

        var first = cache.GetOrCreate("performance", () => { created++; return new object(); });
        var second = cache.GetOrCreate("performance", () => { created++; return new object(); });

        Assert.Multiple(() =>
        {
            Assert.That(created, Is.EqualTo(1), "同一个导航键只该构造一次");
            Assert.That(ReferenceEquals(first, second), Is.True);
            Assert.That(cache.Count, Is.EqualTo(1));
        });
    }

    [Test]
    public void 工厂返回空时不缓存下次还会再试()
    {
        var cache = new PluginPageCache();
        var attempts = 0;

        var first = cache.GetOrCreate("performance", () => { attempts++; return null; });
        var second = cache.GetOrCreate("performance", () => { attempts++; return null; });

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.Null);
            Assert.That(second, Is.Null);
            Assert.That(attempts, Is.EqualTo(2), "构造失败不该被记忆成「这个键没有页面」");
            Assert.That(cache.Count, Is.Zero);
        });
    }

    [Test]
    public void 只保留活着的键并释放其余实例()
    {
        var cache = new PluginPageCache();
        var alive = new FakePage();
        var orphan = new FakePage();
        cache.GetOrCreate("performance", () => alive);
        cache.GetOrCreate("gone", () => orphan);

        var removed = cache.RetainOnly(["performance"]);

        Assert.Multiple(() =>
        {
            Assert.That(removed, Is.EqualTo(new[] { "gone" }));
            Assert.That(orphan.Disposed, Is.True, "插件下线后页面实例必须被释放（定时器、事件订阅都在里面）");
            Assert.That(alive.Disposed, Is.False);
            Assert.That(cache.Count, Is.EqualTo(1));
        });
    }

    [Test]
    public void 键名比较忽略大小写()
    {
        var cache = new PluginPageCache();
        var page = new FakePage();
        cache.GetOrCreate("performance", () => page);

        var removed = cache.RetainOnly(["PERFORMANCE"]);

        Assert.Multiple(() =>
        {
            Assert.That(removed, Is.Empty);
            Assert.That(page.Disposed, Is.False);
        });
    }

    [Test]
    public void 释放抛异常也照样摘引用并继续释放其它页面()
    {
        var cache = new PluginPageCache();
        var throwing = new ThrowingPage();
        var normal = new FakePage();
        cache.GetOrCreate("broken", () => throwing);
        cache.GetOrCreate("other", () => normal);

        var removed = cache.RetainOnly(Array.Empty<string>());

        Assert.Multiple(() =>
        {
            Assert.That(removed, Is.EqualTo(new[] { "broken", "other" }));
            Assert.That(normal.Disposed, Is.True, "前一个插件写坏 Dispose 不该连累后面的页面");
            Assert.That(cache.Count, Is.Zero);
        });
    }

    private class FakePage : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }

    private sealed class ThrowingPage : IDisposable
    {
        public void Dispose() => throw new InvalidOperationException("插件写坏的 Dispose");
    }
}
