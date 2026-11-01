using System.Collections.Generic;
using System.Linq;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Dialogs;
using CkanUI.ViewModels.Dialogs.Mods;
using CkanUI.ViewModels.Pages;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

/// <summary>
/// F-36 推荐与建议审计对话框测试（UI-17 / INT-07）：
/// 覆盖扫描过滤、按原发起模组分组、多选勾选、全选/全不选、组内全选/全不选、确认导出变更请求等场景。
/// </summary>
[TestFixture]
public sealed class AuditRecommendationsDialogTests
{
    private sealed class FakeChangeRequestSink : IChangeRequestSink
    {
        public IReadOnlyCollection<ChangeRequest>? ReceivedRequests { get; private set; }
        public event Action? RequestsApplied { add { } remove { } }

        public void SetRequests(IReadOnlyCollection<ChangeRequest>? requests)
        {
            ReceivedRequests = requests;
        }
    }

    private static InstalledModuleInfo CreateInstalled(
        string identifier,
        string version = "1.0.0",
        IReadOnlyList<string>? recommends = null,
        IReadOnlyList<string>? suggests = null)
    {
        return new InstalledModuleInfo(
            identifier,
            version,
            IsAutoInstalled: false,
            SourceRepository: "default",
            InstallTime: null)
        {
            Facts = new ModSearchFacts
            {
                Recommends = recommends,
                Suggests = suggests,
            }
        };
    }

    private static ModRowData CreateRow(
        string identifier,
        string name,
        string version = "1.2.0",
        string? description = "A test mod")
    {
        return new ModRowData(
            Identifier: identifier,
            Name: name,
            Author: "Author",
            InstalledVersion: null,
            LatestVersion: version,
            DownloadCount: 1000,
            Tags: new[] { "plugin" },
            SizeBytes: 1024,
            IsInstalled: false,
            HasUpdate: false,
            IsCompatible: true,
            IsAutoInstalled: false,
            IsSelected: false,
            IsImplicit: false)
        {
            Facts = new ModSearchFacts
            {
                Abstract = description,
            }
        };
    }

    [Test]
    public void 用例1_空已安装列表时无推荐候选()
    {
        var registry = RegistrySnapshot.Empty();
        var repo = RepositorySnapshot.Empty;

        var vm = new AuditRecommendationsDialogViewModel(registry, repo);

        Assert.That(vm.TotalCount, Is.EqualTo(0));
        Assert.That(vm.Groups, Is.Empty);
        Assert.That(vm.HasCandidates, Is.False);
        Assert.That(vm.CanApply, Is.False);
        Assert.That(vm.SummaryText, Does.Contain("未发现待安装的推荐或建议模组"));
    }

    [Test]
    public void 用例2_提取推荐与建议并按原发起模组分组呈现()
    {
        var installed = new Dictionary<string, InstalledModuleInfo>
        {
            ["ModA"] = CreateInstalled("ModA", recommends: new[] { "TargetMod" }),
            ["ModB"] = CreateInstalled("ModB", recommends: new[] { "TargetMod" }, suggests: new[] { "SuggestMod" }),
        };
        var registry = new RegistrySnapshot("test", "1.0", installed, new HashSet<string>(), DateTime.UtcNow);
        var repo = new RepositorySnapshot(
            new[]
            {
                CreateRow("ModA", "Mod A Name"),
                CreateRow("ModB", "Mod B Name"),
                CreateRow("TargetMod", "Target Mod Name", "2.0.0", "Target abstract"),
                CreateRow("SuggestMod", "Suggest Mod Name", "1.5.0", "Suggest abstract")
            },
            new[] { "default" },
            DateTime.UtcNow,
            false);

        var vm = new AuditRecommendationsDialogViewModel(registry, repo);

        Assert.That(vm.TotalCount, Is.EqualTo(2));
        Assert.That(vm.Groups, Has.Count.EqualTo(2));

        var groupA = vm.Groups.FirstOrDefault(g => g.SourceIdentifier == "ModA");
        Assert.That(groupA, Is.Not.Null);
        Assert.That(groupA!.SourceName, Is.EqualTo("Mod A Name"));
        Assert.That(groupA.Items, Has.Count.EqualTo(1));
        Assert.That(groupA.Items[0].Identifier, Is.EqualTo("TargetMod"));

        var groupB = vm.Groups.FirstOrDefault(g => g.SourceIdentifier == "ModB");
        Assert.That(groupB, Is.Not.Null);
        Assert.That(groupB!.SourceName, Is.EqualTo("Mod B Name"));
        Assert.That(groupB.Items, Has.Count.EqualTo(2));
        Assert.That(groupB.Items.Select(i => i.Identifier), Is.EquivalentTo(new[] { "TargetMod", "SuggestMod" }));
    }

    [Test]
    public void 用例3_过滤已安装模组()
    {
        var installed = new Dictionary<string, InstalledModuleInfo>
        {
            ["ModA"] = CreateInstalled("ModA", recommends: new[] { "AlreadyInstalled", "UninstalledMod" }),
            ["AlreadyInstalled"] = CreateInstalled("AlreadyInstalled")
        };
        var registry = new RegistrySnapshot("test", "1.0", installed, new HashSet<string>(), DateTime.UtcNow);
        var repo = new RepositorySnapshot(
            new[]
            {
                CreateRow("AlreadyInstalled", "Already Installed Mod"),
                CreateRow("UninstalledMod", "Uninstalled Mod")
            },
            new[] { "default" },
            DateTime.UtcNow,
            false);

        var vm = new AuditRecommendationsDialogViewModel(registry, repo);

        Assert.That(vm.TotalCount, Is.EqualTo(1));
        Assert.That(vm.Items[0].Identifier, Is.EqualTo("UninstalledMod"));
        Assert.That(vm.Groups, Has.Count.EqualTo(1));
        Assert.That(vm.Groups[0].Items[0].Identifier, Is.EqualTo("UninstalledMod"));
    }

    [Test]
    public void 用例4_过滤待安装队列中的模组()
    {
        var installed = new Dictionary<string, InstalledModuleInfo>
        {
            ["ModA"] = CreateInstalled("ModA", recommends: new[] { "PendingMod", "AvailableMod" }),
        };
        var registry = new RegistrySnapshot("test", "1.0", installed, new HashSet<string>(), DateTime.UtcNow);
        var repo = new RepositorySnapshot(
            new[]
            {
                CreateRow("PendingMod", "Pending Mod"),
                CreateRow("AvailableMod", "Available Mod")
            },
            new[] { "default" },
            DateTime.UtcNow,
            false);

        var vm = new AuditRecommendationsDialogViewModel(registry, repo, pendingInstallIdentifiers: new[] { "PendingMod" });

        Assert.That(vm.TotalCount, Is.EqualTo(1));
        Assert.That(vm.Items[0].Identifier, Is.EqualTo("AvailableMod"));
    }

    [Test]
    public void 用例5_模组名称版本与简介从仓库快照正确映射()
    {
        var installed = new Dictionary<string, InstalledModuleInfo>
        {
            ["ModA"] = CreateInstalled("ModA", recommends: new[] { "TargetMod" }),
        };
        var registry = new RegistrySnapshot("test", "1.0", installed, new HashSet<string>(), DateTime.UtcNow);
        var repo = new RepositorySnapshot(
            new[] { CreateRow("TargetMod", "Display Name Target", "3.4.1", "Detailed mod abstract") },
            new[] { "default" },
            DateTime.UtcNow,
            false);

        var vm = new AuditRecommendationsDialogViewModel(registry, repo);

        Assert.That(vm.Items, Has.Count.EqualTo(1));
        var item = vm.Items[0];
        Assert.That(item.Identifier, Is.EqualTo("TargetMod"));
        Assert.That(item.Name, Is.EqualTo("Display Name Target"));
        Assert.That(item.Version, Is.EqualTo("3.4.1"));
        Assert.That(item.Description, Is.EqualTo("Detailed mod abstract"));
        Assert.That(item.HasDescription, Is.True);
    }

    [Test]
    public void 用例6_全局全选与全局全不选行为正确()
    {
        var installed = new Dictionary<string, InstalledModuleInfo>
        {
            ["ModA"] = CreateInstalled("ModA", recommends: new[] { "Rec1", "Rec2" }),
        };
        var registry = new RegistrySnapshot("test", "1.0", installed, new HashSet<string>(), DateTime.UtcNow);
        var repo = new RepositorySnapshot(
            new[] { CreateRow("Rec1", "Rec 1"), CreateRow("Rec2", "Rec 2") },
            new[] { "default" },
            DateTime.UtcNow,
            false);

        var vm = new AuditRecommendationsDialogViewModel(registry, repo);

        // 默认全选
        Assert.That(vm.SelectedCount, Is.EqualTo(2));
        Assert.That(vm.CanApply, Is.True);

        // 全局全不选
        vm.UncheckAllCommand.Execute(null);
        Assert.That(vm.SelectedCount, Is.EqualTo(0));
        Assert.That(vm.CanApply, Is.False);
        Assert.That(vm.SelectedIdentifiers, Is.Empty);

        // 全局全选
        vm.CheckAllCommand.Execute(null);
        Assert.That(vm.SelectedCount, Is.EqualTo(2));
        Assert.That(vm.CanApply, Is.True);
        Assert.That(vm.SelectedIdentifiers, Is.EquivalentTo(new[] { "Rec1", "Rec2" }));
    }

    [Test]
    public void 用例7_组级全选与组级清空行为正确()
    {
        var installed = new Dictionary<string, InstalledModuleInfo>
        {
            ["ModA"] = CreateInstalled("ModA", recommends: new[] { "Rec1" }),
            ["ModB"] = CreateInstalled("ModB", recommends: new[] { "Rec2" }),
        };
        var registry = new RegistrySnapshot("test", "1.0", installed, new HashSet<string>(), DateTime.UtcNow);
        var repo = new RepositorySnapshot(
            new[] { CreateRow("Rec1", "Rec 1"), CreateRow("Rec2", "Rec 2") },
            new[] { "default" },
            DateTime.UtcNow,
            false);

        var vm = new AuditRecommendationsDialogViewModel(registry, repo);
        Assert.That(vm.Groups, Has.Count.EqualTo(2));

        var groupA = vm.Groups.First(g => g.SourceIdentifier == "ModA");
        var groupB = vm.Groups.First(g => g.SourceIdentifier == "ModB");

        // 清空 groupA
        groupA.ClearSelectionCommand.Execute(null);
        Assert.That(groupA.SelectedCount, Is.EqualTo(0));
        Assert.That(groupA.CountText, Is.EqualTo("0/1"));
        Assert.That(groupB.SelectedCount, Is.EqualTo(1));
        Assert.That(vm.SelectedCount, Is.EqualTo(1));
        Assert.That(vm.SelectedIdentifiers, Is.EquivalentTo(new[] { "Rec2" }));

        // 组全选 groupA
        groupA.SelectAllCommand.Execute(null);
        Assert.That(groupA.SelectedCount, Is.EqualTo(1));
        Assert.That(groupA.CountText, Is.EqualTo("1/1"));
        Assert.That(vm.SelectedCount, Is.EqualTo(2));
    }

    [Test]
    public void 用例8_未勾选时点击主按钮触发校验错误且不导出变更请求()
    {
        var installed = new Dictionary<string, InstalledModuleInfo>
        {
            ["ModA"] = CreateInstalled("ModA", recommends: new[] { "Rec1" }),
        };
        var registry = new RegistrySnapshot("test", "1.0", installed, new HashSet<string>(), DateTime.UtcNow);
        var repo = new RepositorySnapshot(
            new[] { CreateRow("Rec1", "Rec 1") },
            new[] { "default" },
            DateTime.UtcNow,
            false);

        var sink = new FakeChangeRequestSink();
        var vm = new AuditRecommendationsDialogViewModel(registry, repo, changeSink: sink);

        vm.UncheckAllCommand.Execute(null);
        vm.PrimaryCommand.Execute(null);

        Assert.That(vm.HasValidationError, Is.True);
        Assert.That(vm.ValidationError, Does.Contain("请先勾选"));
        Assert.That(vm.IsClosed, Is.False);
        Assert.That(vm.IsConfirmed, Is.False);
        Assert.That(sink.ReceivedRequests, Is.Null);
    }

    [Test]
    public void 用例9_勾选后确认一键批量加入待变更集并导出请求()
    {
        var installed = new Dictionary<string, InstalledModuleInfo>
        {
            ["ModA"] = CreateInstalled("ModA", recommends: new[] { "Rec1", "Rec2" }),
        };
        var registry = new RegistrySnapshot("test", "1.0", installed, new HashSet<string>(), DateTime.UtcNow);
        var repo = new RepositorySnapshot(
            new[]
            {
                CreateRow("Rec1", "Rec 1", "1.0.0"),
                CreateRow("Rec2", "Rec 2", "2.0.0")
            },
            new[] { "default" },
            DateTime.UtcNow,
            false);

        var sink = new FakeChangeRequestSink();
        var vm = new AuditRecommendationsDialogViewModel(registry, repo, changeSink: sink);

        vm.PrimaryCommand.Execute(null);

        Assert.That(vm.IsConfirmed, Is.True);
        Assert.That(vm.IsClosed, Is.True);
        Assert.That(vm.AppliedRequests, Has.Count.EqualTo(2));
        Assert.That(sink.ReceivedRequests, Is.Not.Null);
        Assert.That(sink.ReceivedRequests!.Count, Is.EqualTo(2));

        var req1 = sink.ReceivedRequests!.FirstOrDefault(r => r.Identifier == "Rec1");
        Assert.That(req1, Is.Not.Null);
        Assert.That(req1!.Action, Is.EqualTo(ChangeAction.Install));
        Assert.That(req1.TargetVersion, Is.EqualTo("1.0.0"));
    }

    [Test]
    public void 用例10_多来源指向同一推荐模组时勾选状态同步且去重导出()
    {
        var installed = new Dictionary<string, InstalledModuleInfo>
        {
            ["ModA"] = CreateInstalled("ModA", recommends: new[] { "SharedRec" }),
            ["ModB"] = CreateInstalled("ModB", suggests: new[] { "SharedRec" }),
        };
        var registry = new RegistrySnapshot("test", "1.0", installed, new HashSet<string>(), DateTime.UtcNow);
        var repo = new RepositorySnapshot(
            new[] { CreateRow("SharedRec", "Shared Mod", "1.1.0") },
            new[] { "default" },
            DateTime.UtcNow,
            false);

        var sink = new FakeChangeRequestSink();
        var vm = new AuditRecommendationsDialogViewModel(registry, repo, changeSink: sink);

        // 尽管两个组都有 SharedRec，唯一候选总数为 1
        Assert.That(vm.TotalCount, Is.EqualTo(1));
        Assert.That(vm.Groups, Has.Count.EqualTo(2));
        Assert.That(vm.Groups[0].Items[0], Is.SameAs(vm.Groups[1].Items[0]));

        // 取消组 A 的勾选，组 B 和全局均同步
        vm.Groups[0].ClearSelectionCommand.Execute(null);
        Assert.That(vm.Groups[1].SelectedCount, Is.EqualTo(0));
        Assert.That(vm.SelectedCount, Is.EqualTo(0));

        // 勾选后确认，导出的变更请求唯一去重
        vm.Groups[1].SelectAllCommand.Execute(null);
        vm.PrimaryCommand.Execute(null);

        Assert.That(vm.AppliedRequests, Has.Count.EqualTo(1));
        Assert.That(sink.ReceivedRequests!.Count, Is.EqualTo(1));
        Assert.That(sink.ReceivedRequests!.First().Identifier, Is.EqualTo("SharedRec"));
    }
}
