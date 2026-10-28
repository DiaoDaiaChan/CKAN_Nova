using System.Linq;
using CkanUI.Services.Abstractions.Logs;
using CkanUI.ViewModels.Pages.Logs;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

/// <summary>底部日志抽屉（14 篇 §4.1：所有页面可用、默认收起、最近 200 行 + 徽标）。</summary>
[TestFixture]
public sealed class LogDrawerViewModelTests
{
    private static (LogStoreLike Store, LogDrawerViewModel Drawer) Create(int capacity = 300)
    {
        var store = LogTestData.CreateStore(capacity);
        store.Append(LogTestData.Entry(LogLevel.Error, "boom 1"));
        store.Append(LogTestData.Entry(LogLevel.Error, "boom 2"));
        store.Append(LogTestData.Entry(LogLevel.Warn, "careful"));
        var drawer = new LogDrawerViewModel(store, LogTestData.CreateQuery(store));
        return (new LogStoreLike(store), drawer);
    }

    /// <summary>把 ILogStore 包一层：测试里既要追加日志又要读计数，直接暴露 store 比反复强转清楚。</summary>
    private sealed class LogStoreLike(Services.Logs.LogStore store)
    {
        public Services.Logs.LogStore Store { get; } = store;

        public void Append(LogEvent entry) => Store.Append(entry);
    }

    [Test]
    public void Constructor_ThrowsOnNullDependencies()
    {
        var store = LogTestData.CreateStore();

        Assert.That(() => new LogDrawerViewModel(null!, LogTestData.CreateQuery(store)), Throws.ArgumentNullException);
    }

    [Test]
    public void DesignConstructor_IsSafe()
    {
        var drawer = new LogDrawerViewModel();

        drawer.Refresh();
        drawer.Open();
        drawer.Close();

        Assert.That(drawer.Page, Is.Not.Null);
        Assert.That(drawer.RecentRows, Is.Empty);
        Assert.That(drawer.Title, Does.Contain("最近 0 行"));
        Assert.That(drawer.IsOpen, Is.False);
    }

    [Test]
    public void Refresh_FillsRecentRowsAndTitle()
    {
        var (store, drawer) = Create();

        drawer.Refresh();

        Assert.That(drawer.RecentRows.Count, Is.EqualTo(3));
        Assert.That(drawer.Title, Does.Contain("最近 3 行"));
        Assert.That(drawer.RecentRows[0].Message, Is.EqualTo("careful"), "新→旧：最新的警告在最上面");
        Assert.That(store.Store.Count, Is.EqualTo(3));
    }

    [Test]
    public void Refresh_LimitsRecentRowsToTwoHundred()
    {
        var store = LogTestData.CreateStore(300);
        for (var i = 0; i < 250; i++)
        {
            store.Append(LogTestData.Entry(LogLevel.Info, "line " + i));
        }

        var drawer = new LogDrawerViewModel(store, LogTestData.CreateQuery(store));
        drawer.Refresh();

        Assert.That(drawer.RecentRows.Count, Is.EqualTo(LogDrawerViewModel.RecentRowLimit));
        Assert.That(LogDrawerViewModel.RecentRowLimit, Is.EqualTo(200));
    }

    [Test]
    public void Badge_CountsNewErrorsAndWarningsWhileCollapsed()
    {
        var (_, drawer) = Create();

        drawer.Refresh();

        Assert.That(drawer.NewErrorCount, Is.EqualTo(2));
        Assert.That(drawer.NewWarningCount, Is.EqualTo(1));
        Assert.That(drawer.HasBadge, Is.True);
        Assert.That(drawer.IsBadgeVisible, Is.True, "收起时才提示徽标");
        Assert.That(drawer.BadgeText, Is.EqualTo("有 2 个错误"));
    }

    [Test]
    public void Open_ClearsBadgeAndHidesItAfterwards()
    {
        var (store, drawer) = Create();
        drawer.Refresh();

        drawer.Open();

        Assert.That(drawer.IsOpen, Is.True);
        Assert.That(drawer.NewErrorCount, Is.Zero);
        Assert.That(drawer.NewWarningCount, Is.Zero);
        Assert.That(drawer.IsBadgeVisible, Is.False);

        store.Append(LogTestData.Entry(LogLevel.Error, "boom 3"));
        drawer.Refresh();

        Assert.That(drawer.NewErrorCount, Is.EqualTo(1), "只报打开之后新增的条数，避免重复计数");
        Assert.That(drawer.IsBadgeVisible, Is.False, "展开时不再显示徽标");
    }

    [Test]
    public void Close_RevealsBadgeAgain()
    {
        var (store, drawer) = Create();
        drawer.Refresh();
        drawer.Open();

        drawer.Close();
        store.Append(LogTestData.Entry(LogLevel.Error, "boom 3"));
        drawer.Refresh();

        Assert.That(drawer.IsOpen, Is.False);
        Assert.That(drawer.IsBadgeVisible, Is.True);
    }

    [Test]
    public void BadgeText_IsEmptyWhenNothingNew()
    {
        var store = LogTestData.CreateStore();
        store.Append(LogTestData.Entry(LogLevel.Info, "ok"));
        var drawer = new LogDrawerViewModel(store, LogTestData.CreateQuery(store));

        drawer.Refresh();

        Assert.That(drawer.HasBadge, Is.False);
        Assert.That(drawer.BadgeText, Is.Empty);
    }

    [Test]
    public void ToggleCommand_FlipsOpenState()
    {
        var (_, drawer) = Create();

        drawer.ToggleCommand.Execute(null);
        Assert.That(drawer.IsOpen, Is.True);

        drawer.ToggleCommand.Execute(null);
        Assert.That(drawer.IsOpen, Is.False);
    }

    [Test]
    public void AttachAndDetach_ControlStoreSubscription()
    {
        var (store, drawer) = Create();
        drawer.Refresh();
        drawer.Attach();

        store.Append(LogTestData.Entry(LogLevel.Error, "boom 3"));
        Assert.That(drawer.RecentRows.Count, Is.EqualTo(4), "挂上之后新日志自动进抽屉");

        drawer.Detach();
        store.Append(LogTestData.Entry(LogLevel.Error, "boom 4"));
        Assert.That(drawer.RecentRows.Count, Is.EqualTo(4), "摘掉之后不再刷新");
    }

    [Test]
    public void AttachAndDetach_AreIdempotent()
    {
        var (store, drawer) = Create();
        drawer.Attach();
        drawer.Attach();
        drawer.Detach();
        drawer.Detach();

        store.Append(LogTestData.Entry(LogLevel.Error, "boom 3"));

        Assert.That(drawer.RecentRows.Count, Is.EqualTo(3), "重复挂载不应留下多重订阅，单次 Detach 就该干净");
    }

    [Test]
    public void OpenPageCommand_RaisesOpenPageRequested()
    {
        var (_, drawer) = Create();
        var raised = 0;
        drawer.OpenPageRequested += (_, _) => raised++;

        drawer.OpenPageCommand.Execute(null);

        Assert.That(raised, Is.EqualTo(1));
    }

    [Test]
    public void Page_SharesFilterStateWithDrawer()
    {
        var (_, drawer) = Create();
        drawer.Page.Filters.SearchText = "boom 2";

        drawer.Refresh();

        Assert.That(drawer.RecentRows.Count, Is.EqualTo(1), "抽屉跟随日志页的过滤条件");
        Assert.That(drawer.RecentRows[0].Message, Is.EqualTo("boom 2"));
    }
}
