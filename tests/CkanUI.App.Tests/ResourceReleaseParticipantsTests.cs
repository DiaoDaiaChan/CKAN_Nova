using System.Globalization;
using Avalonia.Media;
using CkanUI.App.Converters;
using CkanUI.App.Lifecycle;
using CkanUI.Services.Abstractions;
using CkanUI.ViewModels.Pages;
using NUnit.Framework;

namespace CkanUI.App.Tests;

/// <summary>
/// 分层资源释放的参与方（18 篇 §3）：L0 清字形缓存与背景位图，L1 丢页面行投影。
/// </summary>
/// <remarks>
/// 这里不验「释放了多少字节」，只验三件会悄悄坏掉的事：字形缓存清得掉且能重建、
/// 某一页释放失败不会连累其它页、跨线程时不冒险 Dispose 位图。
/// </remarks>
[TestFixture]
public sealed class ResourceReleaseParticipantsTests
{
    [Test]
    public void NavGlyphCache_IsClearedAndRebuiltOnDemand()
    {
        var converter = NavGlyphToGeometryConverter.Instance;
        Assert.That(converter.Convert("Chart", typeof(Geometry), null, CultureInfo.InvariantCulture), Is.Not.Null);

        Assert.That(NavGlyphToGeometryConverter.ClearCache(), Is.GreaterThanOrEqualTo(1));
        Assert.That(NavGlyphToGeometryConverter.ClearCache(), Is.EqualTo(0), "第二次没有可清的条目");

        // 清空只丢加速层：下一次绑定会重新解析路径，图标照常出现。
        Assert.That(converter.Convert("Chart", typeof(Geometry), null, CultureInfo.InvariantCulture), Is.Not.Null);
    }

    [Test]
    public void PageProjections_AreSummedAndOneFailingPageDoesNotStopTheRest()
    {
        var participant = new PageProjectionCacheParticipant(new IPageProjectionTrim[]
        {
            new FakePage("模组", 3),
            new ThrowingPage("内容预览"),
            new FakePage("模组文本", 2),
        });

        Assert.That(participant.Name, Is.EqualTo("页面行投影"));
        Assert.That(participant.TrimAppCache(), Is.EqualTo(5), "抛异常的那页算 0，其它页照常累加");
    }

    [Test]
    public void PageProjections_WithoutPages_ReportZero()
    {
        var participant = new PageProjectionCacheParticipant(Array.Empty<IPageProjectionTrim>());

        Assert.That(participant.TrimAppCache(), Is.EqualTo(0));
    }

    [Test]
    public void ViewParticipant_OnTheUiThreadReleasesInline()
    {
        var dispatcher = new RecordingDispatcher { IsOnUiThread = true };
        var participant = new UiViewResourceParticipant(dispatcher);
        NavGlyphToGeometryConverter.Instance.Convert("Chart", typeof(Geometry), null, CultureInfo.InvariantCulture);

        Assert.That(participant.Name, Is.EqualTo("界面字形与背景位图"));
        Assert.That(participant.TrimViewResources(), Is.GreaterThanOrEqualTo(1));
        Assert.That(dispatcher.Posted, Is.EqualTo(0), "已经在 UI 线程就不该再排队");
    }

    [Test]
    public void ViewParticipant_FromAnotherThreadPostsTheBitmapRelease()
    {
        var dispatcher = new RecordingDispatcher { IsOnUiThread = false };
        var participant = new UiViewResourceParticipant(dispatcher);
        NavGlyphToGeometryConverter.Instance.Convert("Chart", typeof(Geometry), null, CultureInfo.InvariantCulture);

        var released = participant.TrimViewResources();

        // 字形几何能就地清；位图必须排回 UI 线程，本次报告里不虚增它的数。
        Assert.That(released, Is.GreaterThanOrEqualTo(1));
        Assert.That(dispatcher.Posted, Is.EqualTo(1));
    }

    private sealed class FakePage(string name, int count) : IPageProjectionTrim
    {
        public string PageName => name;

        public int TrimProjection() => count;
    }

    private sealed class ThrowingPage(string name) : IPageProjectionTrim
    {
        public string PageName => name;

        public int TrimProjection() => throw new InvalidOperationException("正在被枚举");
    }

    private sealed class RecordingDispatcher : IDispatcher
    {
        public bool IsOnUiThread { get; set; }

        public int Posted { get; private set; }

        public void Post(Action action)
        {
            Posted++;
            action();
        }

        public Task InvokeAsync(Action action)
        {
            action();
            return Task.CompletedTask;
        }

        public Task<T> InvokeAsync<T>(Func<T> func) => Task.FromResult(func());
    }
}
