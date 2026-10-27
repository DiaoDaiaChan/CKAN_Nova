using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Dialogs;
using CkanUI.ViewModels.Dialogs.Mods;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

/// <summary>
/// 三个模组对话框族（F-34 推荐 / F-35 提供者选择 / F-36 冲突详情）：
/// 返回值语义、单选互斥、全选跳过禁用项、空列表禁用确认。
/// </summary>
[TestFixture]
public sealed class ModDialogsTests
{
    private const string Message = "所选模组推荐、建议或支持下列模组：";

    // ---------- F-34 推荐模组 ----------

    [Test]
    public void 推荐_默认勾选标记为默认的候选()
    {
        var vm = new RecommendationDialogViewModel(Request(
            Candidate("A", RecommendationKind.Recommended),
            Candidate("B", RecommendationKind.Recommended),
            Candidate("C", RecommendationKind.Suggested, selectedByDefault: false)));

        Assert.That(vm.SelectedCount, Is.EqualTo(2));
        Assert.That(vm.SelectedIdentifiers, Is.EquivalentTo(new[] { "A", "B" }));
        Assert.That(vm.HasSelection, Is.True);
    }

    [Test]
    public void 推荐_记住取消全选偏好时初始全不勾选()
    {
        var vm = new RecommendationDialogViewModel(Request(
            Candidate("A", RecommendationKind.Recommended),
            Candidate("B", RecommendationKind.Recommended)));

        vm.SuppressRecommendations = true;

        Assert.That(vm.SelectedCount, Is.EqualTo(0));
        Assert.That(vm.HasSelection, Is.False);
    }

    [Test]
    public void 推荐_全选跳过DLC与冲突项()
    {
        var vm = new RecommendationDialogViewModel(Request(
            Candidate("A", RecommendationKind.Recommended, selectedByDefault: false),
            Candidate("Dlc", RecommendationKind.Recommended, dlc: true),
            Candidate("Conflict", RecommendationKind.Recommended, conflicting: true)));

        vm.CheckAllCommand.Execute(null);

        Assert.That(vm.SelectedIdentifiers, Is.EquivalentTo(new[] { "A" }));
        Assert.That(vm.SelectedCount, Is.EqualTo(1));
    }

    [Test]
    public void 推荐_全不选后仍可确认并返回空集合()
    {
        var vm = new RecommendationDialogViewModel(Request(
            Candidate("A", RecommendationKind.Recommended),
            Candidate("B", RecommendationKind.Suggested)));

        vm.UncheckAllCommand.Execute(null);
        vm.PrimaryCommand.Execute(null);

        Assert.That(vm.Result, Is.Not.Null);
        Assert.That(vm.Result!.Identifiers, Is.Empty);
        Assert.That(vm.IsConfirmed, Is.True);
        Assert.That(vm.IsClosed, Is.True);
    }

    [Test]
    public void 推荐_取消返回null()
    {
        var vm = new RecommendationDialogViewModel(Request(Candidate("A", RecommendationKind.Recommended)));

        vm.SecondaryCommand.Execute(null);

        Assert.That(vm.Result, Is.Null);
        Assert.That(vm.IsConfirmed, Is.False);
        Assert.That(vm.IsClosed, Is.True);
    }

    [Test]
    public void 推荐_空列表禁用确认()
    {
        var vm = new RecommendationDialogViewModel(Request());

        vm.PrimaryCommand.Execute(null);

        Assert.That(vm.HasCandidates, Is.False);
        Assert.That(vm.CanConfirm, Is.False);
        Assert.That(vm.ValidationError, Is.Not.Null.And.Not.Empty);
        Assert.That(vm.IsClosed, Is.False);
        Assert.That(vm.Result, Is.Null);
    }

    [Test]
    public void 推荐_总是取消全选会清空并写进结果()
    {
        var vm = new RecommendationDialogViewModel(Request(
            Candidate("A", RecommendationKind.Recommended),
            Candidate("B", RecommendationKind.Suggested)));

        vm.SuppressRecommendations = true;
        vm.PrimaryCommand.Execute(null);

        Assert.That(vm.Result, Is.Not.Null);
        Assert.That(vm.Result!.SuppressRecommendations, Is.True);
        Assert.That(vm.Result.Identifiers, Is.Empty);
    }

    [Test]
    public void 推荐_冲突项不可勾选且给出原因()
    {
        var vm = new RecommendationDialogViewModel(Request(
            Candidate("Bad", RecommendationKind.Recommended, conflicting: true, conflictReason: "Bad 与当前变更集冲突")));

        var row = vm.Groups.Single(g => g.Kind == RecommendationKind.Recommended).Items[0];
        row.IsSelected = true;

        Assert.That(row.CanSelect, Is.False);
        Assert.That(row.IsSelected, Is.False);
        Assert.That(row.HasBlockReason, Is.True);
        Assert.That(row.BlockReason, Does.Contain("冲突"));
        Assert.That(vm.HasConflictNotice, Is.True);
        Assert.That(vm.ConflictNotice, Does.Contain("Bad"));
    }

    [Test]
    public void 推荐_分组全选只影响本组()
    {
        var vm = new RecommendationDialogViewModel(Request(
            Candidate("A", RecommendationKind.Recommended, selectedByDefault: false),
            Candidate("B", RecommendationKind.Recommended, selectedByDefault: false),
            Candidate("C", RecommendationKind.Suggested, selectedByDefault: false)));

        var recommended = vm.Groups.Single(g => g.Kind == RecommendationKind.Recommended);
        var suggested = vm.Groups.Single(g => g.Kind == RecommendationKind.Suggested);
        recommended.SelectAllCommand.Execute(null);

        Assert.That(recommended.SelectedCount, Is.EqualTo(2));
        Assert.That(suggested.SelectedCount, Is.EqualTo(0));
        Assert.That(vm.SelectedIdentifiers, Is.EquivalentTo(new[] { "A", "B" }));
    }

    [Test]
    public void 推荐_空分组隐藏且有计数文案()
    {
        var vm = new RecommendationDialogViewModel(Request(Candidate("A", RecommendationKind.Recommended)));

        var supported = vm.Groups.Single(g => g.Kind == RecommendationKind.SupportedBy);

        Assert.That(supported.HasItems, Is.False);
        Assert.That(supported.IsEmpty, Is.True);
        Assert.That(vm.Groups.Single(g => g.Kind == RecommendationKind.Recommended).CountText, Is.EqualTo("1/1"));
        Assert.That(vm.SummaryText, Does.Contain("已选 1 项"));
    }

    // ---------- F-35 虚拟模组（provides） ----------

    [Test]
    public void 虚拟模组_默认选中第一个提供者()
    {
        var vm = new VirtualModuleDialogViewModel(VirtualRequest(
            Provider("First"),
            Provider("Second")));

        Assert.That(vm.SelectedProvider, Is.Not.Null);
        Assert.That(vm.SelectedProvider!.Identifier, Is.EqualTo("First"));
        Assert.That(vm.Providers.Count(p => p.IsSelected), Is.EqualTo(1));
    }

    [Test]
    public void 虚拟模组_只能单选()
    {
        var vm = new VirtualModuleDialogViewModel(VirtualRequest(
            Provider("First"),
            Provider("Second")));

        vm.Providers[1].IsSelected = true;

        Assert.That(vm.Providers[0].IsSelected, Is.False);
        Assert.That(vm.Providers[1].IsSelected, Is.True);
        Assert.That(vm.SelectedProvider!.Identifier, Is.EqualTo("Second"));
    }

    [Test]
    public void 虚拟模组_未选中时确认被拒()
    {
        var vm = new VirtualModuleDialogViewModel(VirtualRequest(Provider("First"), Provider("Second")));

        vm.Providers[0].IsSelected = false;
        vm.PrimaryCommand.Execute(null);

        Assert.That(vm.SelectedProvider, Is.Null);
        Assert.That(vm.ValidationError, Does.Contain("选择"));
        Assert.That(vm.IsClosed, Is.False);
        Assert.That(vm.Result, Is.Null);
    }

    [Test]
    public void 虚拟模组_确认返回所选提供者()
    {
        var vm = new VirtualModuleDialogViewModel(VirtualRequest(Provider("First"), Provider("Second")));

        vm.Providers[1].IsSelected = true;
        vm.PrimaryCommand.Execute(null);

        Assert.That(vm.Result, Is.Not.Null);
        Assert.That(vm.Result!.Identifier, Is.EqualTo("Second"));
        Assert.That(vm.IsConfirmed, Is.True);
        Assert.That(vm.IsClosed, Is.True);
    }

    [Test]
    public void 虚拟模组_取消返回null()
    {
        var vm = new VirtualModuleDialogViewModel(VirtualRequest(Provider("First")));

        vm.SecondaryCommand.Execute(null);

        Assert.That(vm.Result, Is.Null);
        Assert.That(vm.IsClosed, Is.True);
    }

    [Test]
    public void 虚拟模组_空列表禁用确认()
    {
        var vm = new VirtualModuleDialogViewModel(new VirtualModuleRequest(Message, Array.Empty<VirtualModuleCandidate>(), "Foo"));

        vm.PrimaryCommand.Execute(null);

        Assert.That(vm.HasProviders, Is.False);
        Assert.That(vm.CanConfirm, Is.False);
        Assert.That(vm.ValidationError, Is.Not.Null.And.Not.Empty);
        Assert.That(vm.IsClosed, Is.False);
    }

    [Test]
    public void 虚拟模组_双击条目等价于确认()
    {
        var vm = new VirtualModuleDialogViewModel(VirtualRequest(Provider("First"), Provider("Second")));

        vm.ActivateCommand.Execute(vm.Providers[1]);

        Assert.That(vm.Result, Is.Not.Null);
        Assert.That(vm.Result!.Identifier, Is.EqualTo("Second"));
        Assert.That(vm.IsClosed, Is.True);
    }

    [Test]
    public void 虚拟模组_摘要含缓存与下载量()
    {
        var vm = new VirtualModuleDialogViewModel(VirtualRequest(
            new VirtualModuleCandidate("First", "First Mod", IsCached: true, DownloadCount: 1234)));

        var row = vm.Providers[0];

        Assert.That(row.AvailabilityText, Does.Contain("已缓存"));
        Assert.That(row.AvailabilityText, Does.Contain("1234"));
        Assert.That(vm.SummaryText, Does.Contain("Foo"));
    }

    // ---------- F-36 冲突/不一致详情 ----------

    [Test]
    public void 冲突_文本按分号拼接()
    {
        var vm = new ConflictDetailsDialogViewModel(new ConflictDetailsRequest(
            Message,
            new[]
            {
                new ConflictEntry("A 与 B 冲突", "A", "Mod A"),
                new ConflictEntry("C 需要 D", "C", "Mod C"),
            }));

        Assert.That(vm.ConflictCount, Is.EqualTo(2));
        Assert.That(vm.ConflictsText, Is.EqualTo("A 与 B 冲突; C 需要 D"));
        Assert.That(vm.SummaryText, Does.Contain("2"));
    }

    [Test]
    public async Task 冲突_复制把全文写入剪贴板()
    {
        string? copied = null;
        var vm = new ConflictDetailsDialogViewModel(
            new ConflictDetailsRequest(
                Message,
                new[] { new ConflictEntry("A 与 B 冲突", "A", "Mod A") },
                new[] { "推荐模组 X 未计入" }),
            text =>
            {
                copied = text;
                return Task.CompletedTask;
            });

        await vm.CopyCommand.ExecuteAsync(null);

        Assert.That(copied, Is.Not.Null);
        Assert.That(copied!, Does.Contain(Message));
        Assert.That(copied, Does.Contain("A 与 B 冲突"));
        Assert.That(copied, Does.Contain("推荐模组 X 未计入"));
        Assert.That(vm.CopyStatus, Does.Contain("已复制"));
        Assert.That(vm.HasCopyStatus, Is.True);
    }

    [Test]
    public async Task 冲突_剪贴板失败给出提示()
    {
        var vm = new ConflictDetailsDialogViewModel(
            new ConflictDetailsRequest(Message, new[] { new ConflictEntry("冲突") }),
            _ => throw new NotSupportedException("剪贴板不可用"));

        await vm.CopyCommand.ExecuteAsync(null);

        Assert.That(vm.CopyStatus, Does.Contain("复制失败"));
        Assert.That(vm.CopyStatus, Does.Contain("剪贴板不可用"));
    }

    [Test]
    public async Task 冲突_空列表不能复制()
    {
        var vm = new ConflictDetailsDialogViewModel(
            new ConflictDetailsRequest(Message, Array.Empty<ConflictEntry>()));

        await vm.CopyCommand.ExecuteAsync(null);

        Assert.That(vm.HasConflicts, Is.False);
        Assert.That(vm.CanCopy, Is.False);
        Assert.That(vm.ValidationError, Is.Not.Null.And.Not.Empty);
        Assert.That(vm.IsClosed, Is.False);
    }

    [Test]
    public void 冲突_关闭标记已确认()
    {
        var vm = new ConflictDetailsDialogViewModel(
            new ConflictDetailsRequest(Message, new[] { new ConflictEntry("冲突") }));

        vm.SecondaryCommand.Execute(null);

        Assert.That(vm.Acknowledged, Is.True);
        Assert.That(vm.IsClosed, Is.True);
    }

    [Test]
    public void 冲突_硬冲突与提示性不一致的文案不同()
    {
        var blocking = new ConflictDetailsDialogViewModel(
            new ConflictDetailsRequest(Message, new[] { new ConflictEntry("冲突") }, IsBlocking: true));
        var advisory = new ConflictDetailsDialogViewModel(
            new ConflictDetailsRequest(Message, new[] { new ConflictEntry("冲突") }, IsBlocking: false));

        Assert.That(blocking.SeverityText, Does.Contain("阻止"));
        Assert.That(advisory.SeverityText, Does.Contain("不影响"));
        Assert.That(blocking.WarningsText, Is.Empty);
        Assert.That(blocking.HasWarnings, Is.False);
    }

    [Test]
    public void 冲突_行带模组信息()
    {
        var vm = new ConflictDetailsDialogViewModel(new ConflictDetailsRequest(
            Message,
            new[] { new ConflictEntry("A 与 B 冲突", "A", "Mod A"), new ConflictEntry("裸理由") }));

        Assert.That(vm.Rows[0].HasModule, Is.True);
        Assert.That(vm.Rows[0].ModuleText, Is.EqualTo("Mod A（A）"));
        Assert.That(vm.Rows[1].HasModule, Is.False);
        Assert.That(vm.Rows[1].ModuleText, Is.Empty);
    }

    // ---------- 构造辅助 ----------

    private static RecommendationDialogRequest Request(params RecommendationCandidate[] candidates) =>
        new(Message, candidates);

    private static RecommendationCandidate Candidate(
        string identifier,
        RecommendationKind kind,
        bool selectedByDefault = true,
        bool dlc = false,
        bool conflicting = false,
        string? conflictReason = null) =>
        new(identifier, identifier + " Mod", kind, "说明", null, selectedByDefault, dlc, false, conflicting, conflictReason);

    private static VirtualModuleRequest VirtualRequest(params VirtualModuleCandidate[] providers) =>
        new(Message, providers, "Foo");

    private static VirtualModuleCandidate Provider(string identifier) =>
        new(identifier, identifier + " Mod", "说明", IsCached: false, IsInstalled: false, DownloadCount: 10);
}
