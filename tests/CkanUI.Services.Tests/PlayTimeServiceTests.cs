using CkanUI.Services.PlayTime;
using CkanUI.TestSupport;
using NUnit.Framework;

namespace CkanUI.Services.Tests;

[TestFixture]
public sealed class PlayTimeServiceTests
{
    private TempWorkspace _temp = null!;

    [SetUp]
    public void SetUp() => _temp = new TempWorkspace("playtime");

    [TearDown]
    public void TearDown() => _temp.Dispose();

    private string TimesFile => _temp.Combine("times.json");

    [Test]
    public void MissingOrCorruptFileLoadsEmpty()
    {
        var service = new PlayTimeService(TimesFile);
        Assert.That(service.Load(), Is.Empty);
        File.WriteAllText(TimesFile, "{");
        Assert.That(service.Load(), Is.Empty);
    }

    [Test]
    public void SavesRoundedValuesAndReplacesNamedInstance()
    {
        var service = new PlayTimeService(TimesFile);
        Assert.That(service.Save("Alpha", 2.5), Is.True);
        Assert.That(service.Save("Alpha", 4.4), Is.True);
        var entry = service.Load().Single();
        Assert.That(entry.Hours, Is.EqualTo(4));
    }

    [Test]
    public void RejectsNegativeAndNonFiniteValues()
    {
        var service = new PlayTimeService(TimesFile);
        Assert.That(service.Save("Alpha", -1), Is.False);
        Assert.That(service.Save("Alpha", double.NaN), Is.False);
    }

    /// <summary>重置要同时清掉「手工基线」与「会话自动累计」，并且那一行还要留在列表里显示 0。</summary>
    [Test]
    public async Task ResetClearsManualBaselineAndSessionTotals()
    {
        var now = new DateTimeOffset(2025, 3, 1, 10, 0, 0, TimeSpan.Zero);
        var service = new PlayTimeService(TimesFile, () => now);

        // 手工填 5 小时，再自动累计 2 小时：页面上应显示 7 小时。
        Assert.That(service.Save("Alpha", 5), Is.True);
        Assert.That(service.BeginSession("Alpha"), Is.True);
        now = now.AddHours(2);
        Assert.That(service.EndSession(), Is.EqualTo(2).Within(0.001));
        Assert.That(service.Load().Single().Hours, Is.EqualTo(7));

        var result = await service.ResetAsync("Alpha");
        Assert.That(result.IsSuccess, Is.True);

        var entry = service.Load().Single();
        Assert.That(entry.InstanceName, Is.EqualTo("Alpha"));
        Assert.That(entry.Hours, Is.EqualTo(0));
    }

    /// <summary>重置正在计时的实例时，只有重置之后的时长会被记下来（重置前的不回算）。</summary>
    [Test]
    public async Task ResettingOpenSessionRestartsTimingFromNow()
    {
        var now = new DateTimeOffset(2025, 3, 1, 10, 0, 0, TimeSpan.Zero);
        var service = new PlayTimeService(TimesFile, () => now);
        Assert.That(service.BeginSession("Beta"), Is.True);
        now = now.AddHours(3);

        Assert.That((await service.ResetAsync("Beta")).IsSuccess, Is.True);
        Assert.That(service.Load().Single().Hours, Is.EqualTo(0));
        Assert.That(service.ActiveInstanceName, Is.EqualTo("Beta"));

        now = now.AddHours(1);
        Assert.That(service.EndSession(), Is.EqualTo(1).Within(0.001));
        Assert.That(service.Load().Single().Hours, Is.EqualTo(1));
    }

    /// <summary>空实例名与已取消的令牌都只返回失败结果，不碰任何文件（服务层不抛异常）。</summary>
    [Test]
    public async Task ResetRejectsEmptyNameAndCancelledToken()
    {
        var service = new PlayTimeService(TimesFile);
        var empty = await service.ResetAsync("  ");
        Assert.That(empty.IsSuccess, Is.False);
        Assert.That(empty.Message, Is.Not.Null.And.Not.Empty);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var cancelled = await service.ResetAsync("Alpha", cts.Token);
        Assert.That(cancelled.IsSuccess, Is.False);
        Assert.That(File.Exists(TimesFile), Is.False);
        Assert.That(service.Load(), Is.Empty);
    }

    /// <summary>本来就没有记录的实例也应该重置成功（幂等），并留下一条 0 行。</summary>
    [Test]
    public async Task ResetIsIdempotentForInstanceWithoutRecords()
    {
        var service = new PlayTimeService(TimesFile);
        Assert.That((await service.ResetAsync("Ghost")).IsSuccess, Is.True);
        Assert.That((await service.ResetAsync("Ghost")).IsSuccess, Is.True);
        Assert.That(service.Load().Single().Hours, Is.EqualTo(0));
    }
}
