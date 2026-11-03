using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using CkanUI.Services.Abstractions.Logs;
using CkanUI.ViewModels.Pages.Logs;

using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

/// <summary>
/// 日志页两档视角（界面日志 / 游戏日志）：换档就是换来源勾选，附带跟随状态文案与全文导入。
/// 为什么这些断言值得存在：玩家看到的第一屏必须是「对的那一路日志」，
/// 分档只换了标题却没换过滤，等于给了个假开关。
/// </summary>
[TestFixture]
public sealed class LogChannelTests
{
    [Test]
    public void SwitchingToGame_SelectsOnlyTheGameSource_AndBackRestoresGuiSources()
    {
        var (_, _, page) = LogTestData.CreatePage();

        Assert.That(page.Channel, Is.EqualTo(LogChannel.Gui));
        Assert.That(page.IsGuiChannel, Is.True);

        page.ShowGameLogCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(page.Channel, Is.EqualTo(LogChannel.Game));
            Assert.That(page.IsGameChannel, Is.True);
            Assert.That(Selected(page), Is.EqualTo(new[] { LogSourceKind.Game }));
        });

        page.ShowGuiLogCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(page.IsGuiChannel, Is.True);
            Assert.That(Selected(page), Is.EquivalentTo(new[]
            {
                LogSourceKind.Log4Net,
                LogSourceKind.User,
                LogSourceKind.Exception,
                LogSourceKind.Child,
            }));
        });
    }

    [Test]
    public void SwitchingChannel_OnlyQueriesTheRowsOfThatChannel_AndRaisesChannelNotifications()
    {
        var (store, _, page) = LogTestData.CreatePage();
        store.Append(LogTestData.Entry(LogLevel.Info, "gui line"));
        page.Activate();
        page.Refresh();
        Assert.That(page.Rows.Select(r => r.Message), Is.EqualTo(new[] { "gui line" }));

        var raised = new List<string?>();
        page.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        page.ShowGameLogCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(raised, Does.Contain(nameof(LogPageViewModel.Channel)));
            Assert.That(raised, Does.Contain(nameof(LogPageViewModel.IsGameChannel)));
            Assert.That(page.Rows, Is.Empty);
        });

        store.Append(LogTestData.Entry(LogLevel.Error, "game line", kind: LogSourceKind.Game, source: "game:Default"));
        page.Refresh();

        Assert.That(page.Rows.Select(r => r.Message), Is.EqualTo(new[] { "game line" }));
    }

    [Test]
    public void EmptyHint_MentionsTheGameLogWhileTheGameChannelIsEmpty()
    {
        var (_, _, page) = LogTestData.CreatePage();

        page.ShowGameLogCommand.Execute(null);

        Assert.That(page.EmptyHint, Does.Contain("游戏日志"));
    }

    [Test]
    public void StatusText_CoversDetachedMissingErrorAndFollowing()
    {
        var (store, query, _) = LogTestData.CreatePage();
        var tail = new FakeKspLogTailService();
        var page = new LogPageViewModel(store, query, gameLog: tail);

        Assert.That(page.GameLogStatusText, Is.EqualTo("没有选中实例，游戏日志不可用。"));

        tail.InstanceName = "Default";
        page.ShowGameLogCommand.Execute(null);
        Assert.That(page.GameLogStatusText, Does.Contain("KSP.log"));

        tail.IsWatching = true;
        tail.GameLogPath = @"D:KSPKSP.log";
        tail.ImportedLineCount = 42;
        Assert.That(page.GameLogStatusText, Does.Contain(@"D:KSPKSP.log").And.Contain("42"));

        tail.LastError = "拒绝访问";
        Assert.That(page.GameLogStatusText, Does.Contain("拒绝访问"));

        var bare = new LogPageViewModel(store, query);
        Assert.That(bare.GameLogStatusText, Is.EqualTo("未接入游戏日志"));
    }

    [Test]
    public async Task ImportCommand_PullsTheWholeFileAndReportsTheCount()
    {
        var (store, query, _) = LogTestData.CreatePage();
        var tail = new FakeKspLogTailService { ImportResult = 17 };
        var page = new LogPageViewModel(store, query, gameLog: tail);
        page.ShowGameLogCommand.Execute(null);

        await page.ImportGameLogCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(tail.ImportCalls, Is.EqualTo(1));
            Assert.That(page.Message, Does.Contain("17"));
        });
    }

    [Test]
    public async Task ImportCommand_WithoutAServiceExplainsItselfInsteadOfThrowing()
    {
        var (store, query, _) = LogTestData.CreatePage();
        var page = new LogPageViewModel(store, query);

        await page.ImportGameLogCommand.ExecuteAsync(null);

        Assert.That(page.Message, Does.Contain("未接入游戏日志"));
    }

    private static LogSourceKind[] Selected(LogPageViewModel page) =>
        page.Filters.Sources.Where(o => o.IsSelected).Select(o => o.Kind).ToArray();

    private sealed class FakeKspLogTailService : IKspLogTailService
    {
        public string? InstanceName { get; set; }

        public string? GameLogPath { get; set; }

        public bool IsWatching { get; set; }

        public long ImportedLineCount { get; set; }

        public string? LastError { get; set; }

        public int ImportResult { get; set; }

        public int ImportCalls { get; private set; }

        public event EventHandler? StatusChanged;

        public void Attach(string? gameDirectory, string? instanceName) => InstanceName = instanceName;

        public void Detach() => InstanceName = null;

        public Task<int> ImportFromStartAsync(CancellationToken cancellationToken = default)
        {
            ImportCalls++;
            return Task.FromResult(ImportResult);
        }

        public Task<int> PumpAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);

        public void Raise() => StatusChanged?.Invoke(this, EventArgs.Empty);

        public void Dispose()
        {
        }
    }
}
