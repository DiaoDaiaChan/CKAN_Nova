using CkanUI.Services.Abstractions.Commands;
using CkanUI.Services.Commands;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests.CommandPalette;

/// <summary>
/// 命令目录：注册（单条/批量、重复 Id、all-or-nothing）、快照隔离与检索委托。
/// </summary>
[TestFixture]
public sealed class CommandCatalogTests
{
    private static CommandDescriptor Cmd(string id, string title) => new(id, title);

    [Test]
    public void Register_SingleCommand_AppearsInSnapshot()
    {
        var catalog = new CommandCatalog();

        var result = catalog.Register(Cmd("nav:mods", "管理模组"));

        Assert.That(result.IsSuccess, Is.True, "全新 Id 注册应当成功");
        Assert.That(catalog.Snapshot().Count, Is.EqualTo(1));
        Assert.That(catalog.Snapshot()[0].Id, Is.EqualTo("nav:mods"));
    }

    [Test]
    public void Register_DuplicateId_ReturnsFailureAndKeepsFirst()
    {
        var catalog = new CommandCatalog();
        catalog.Register(Cmd("nav:mods", "管理模组"));

        var result = catalog.Register(Cmd("nav:mods", "被插件顶掉的管理模组"));

        Assert.That(result.IsSuccess, Is.False, "重复 Id 一律拒绝，不做覆盖");
        Assert.That(result.Message, Is.EqualTo("命令 Id 已被占用"));
        Assert.That(result.Detail, Does.Contain("nav:mods"), "失败详情带上出问题的 Id，方便定位是哪一方注册冲突");
        Assert.That(catalog.Snapshot().Count, Is.EqualTo(1), "目录保持原样");
        Assert.That(catalog.Snapshot()[0].Title, Is.EqualTo("管理模组"));
    }

    [Test]
    public void RegisterBatch_AddsAllCommands()
    {
        var catalog = new CommandCatalog();

        var result = catalog.Register(new[] { Cmd("nav:mods", "管理模组"), Cmd("nav:logs", "日志") });

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(catalog.Snapshot().Count, Is.EqualTo(2));
    }

    [Test]
    public void RegisterBatch_EmptyCollection_IsSuccess()
    {
        var catalog = new CommandCatalog();

        var result = catalog.Register(Array.Empty<CommandDescriptor>());

        Assert.That(result.IsSuccess, Is.True, "空批次是合法的空操作");
        Assert.That(catalog.Snapshot(), Is.Empty);
    }

    [Test]
    public void RegisterBatch_ConflictWithExisting_AddsNothing()
    {
        var catalog = new CommandCatalog();
        catalog.Register(Cmd("nav:mods", "管理模组"));

        var result = catalog.Register(new[] { Cmd("nav:logs", "日志"), Cmd("nav:mods", "重复") });

        Assert.That(result.IsSuccess, Is.False, "批量注册是 all-or-nothing");
        Assert.That(catalog.Snapshot().Count, Is.EqualTo(1), "冲突时连合法的 nav:logs 也不该进目录");
    }

    [Test]
    public void RegisterBatch_DuplicateWithinBatch_AddsNothing()
    {
        var catalog = new CommandCatalog();

        var result = catalog.Register(new[] { Cmd("nav:logs", "日志"), Cmd("nav:logs", "日志（重复）") });

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Is.EqualTo("同一批次里命令 Id 重复"));
        Assert.That(catalog.Snapshot(), Is.Empty);
    }

    [Test]
    public void RegisterBatch_NullItem_ReturnsFailure()
    {
        var catalog = new CommandCatalog();
        var batch = new CommandDescriptor[] { Cmd("nav:logs", "日志"), null! };

        var result = catalog.Register(batch);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Is.EqualTo("命令集合里有空项"));
        Assert.That(catalog.Snapshot(), Is.Empty);
    }

    [Test]
    public void Snapshot_ReturnsCopy_UnaffectedByLaterRegistration()
    {
        var catalog = new CommandCatalog();
        catalog.Register(Cmd("nav:mods", "管理模组"));

        var snapshot = catalog.Snapshot();
        catalog.Register(Cmd("nav:logs", "日志"));

        Assert.That(snapshot.Count, Is.EqualTo(1), "快照是副本，后续注册不会改变已经拿到的列表");
        Assert.That(catalog.Snapshot().Count, Is.EqualTo(2));
    }

    [Test]
    public void Search_DelegatesToMatcher()
    {
        var matcher = new RecordingMatcher();
        var catalog = new CommandCatalog(matcher);
        var command = Cmd("nav:logs", "日志");
        catalog.Register(command);

        var results = catalog.Search("日志", 7);

        Assert.That(matcher.LastQuery, Is.EqualTo("日志"), "查询词原样交给匹配算法");
        Assert.That(matcher.LastLimit, Is.EqualTo(7), "上限原样透传");
        Assert.That(matcher.LastCommands.Count, Is.EqualTo(1), "目录把快照交给匹配算法");
        Assert.That(results.Count, Is.EqualTo(1));
        Assert.That(results[0].Command.Id, Is.EqualTo("nav:logs"));
    }

    [Test]
    public void Search_EmptyCatalog_ReturnsEmpty()
    {
        var catalog = new CommandCatalog();

        Assert.That(catalog.Search("日志"), Is.Empty, "空目录不调用匹配算法，直接返回空结果");
    }

    [Test]
    public void Search_EmptyQuery_ReturnsAllCommands()
    {
        var catalog = new CommandCatalog();
        catalog.Register(new[] { Cmd("nav:mods", "管理模组"), Cmd("nav:logs", "日志") });

        Assert.That(catalog.Search(string.Empty).Count, Is.EqualTo(2), "空查询列出全部命令");
    }

    /// <summary>记录入参的匹配算法替身，用来证明目录只做委托、不含自己的匹配逻辑。</summary>
    private sealed class RecordingMatcher : IFuzzyMatcher
    {
        public string? LastQuery { get; private set; }

        public int LastLimit { get; private set; }

        public IReadOnlyList<CommandDescriptor> LastCommands { get; private set; } = Array.Empty<CommandDescriptor>();

        public IReadOnlyList<CommandMatch> Search(IReadOnlyList<CommandDescriptor> commands, string? query, int limit = 50)
        {
            LastCommands = commands;
            LastQuery = query;
            LastLimit = limit;
            return commands.Count == 0
                ? Array.Empty<CommandMatch>()
                : new[] { new CommandMatch(commands[0], 1, CommandMatch.NoHighlights) };
        }

        public CommandMatch? Match(CommandDescriptor command, string? query) =>
            new CommandMatch(command, 1, CommandMatch.NoHighlights);
    }
}
