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
/// F-36 推荐审计对话框测试（UI-17 / INT-07）：
/// 1. 空已安装列表时无推荐候选
/// 2. 提取 recommendations 与 suggests 并聚合推荐来源
/// 3. 过滤已安装的模组
/// 4. 过滤处于待安装列表中的模组
/// 5. 模组元数据（名称、版本、简介）从仓库快照正确映射
/// 6. 默认全选可选项，全选与全不选命令行为正确
/// 7. 未勾选任何模组时点击主按钮触发校验错误且不提交
/// 8. 勾选模组后点击「加入待安装」将变更请求正确灌入 IChangeRequestSink 并关窗
/// 9. 点击「关闭」或取消时不提交变更且 AppliedRequests 为空
/// </summary>
[TestFixture]
public sealed class AuditRecommendationsDialogTests
{
    private sealed class FakeChangeRequestSink : IChangeRequestSink
    {
        public IReadOnlyCollection<ChangeRequest>? ReceivedRequests { get; private set; }

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
        Assert.That(vm.HasCandidates, Is.False);
        Assert.That(vm.CanApply, Is.False);
        Assert.That(vm.SummaryText, Does.Contain("未发现任何新的推荐或建议模组"));
    }

    [Test]
    public void 用例2_提取推荐与建议并合并多来源模组()
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
                CreateRow("TargetMod", "Target Mod Name", "2.0.0", "Target abstract"),
                CreateRow("SuggestMod", "Suggest Mod Name", "1.5.0", "Suggest abstract")
            },
            new[] { "default" },
            DateTime.UtcNow,
            false);

        var vm = new AuditRecommendationsDialogViewModel(registry, repo);

        Assert.That(vm.TotalCount, Is.EqualTo(2));
        var target = vm.Items.FirstOrDefault(i => i.Identifier == "TargetMod");
        Assert.That(target, Is.Not.Null);
        Assert.That(target!.Kind, Is.EqualTo(RecommendationKind.Recommended));
        Assert.That(target.SourceModules, Is.EquivalentTo(new[] { "ModA", "ModB" }));
        Assert.That(target.SourceModulesText, Does.Contain("ModA"));
        Assert.That(target.SourceModulesText, Does.Contain("ModB"));

        var suggest = vm.Items.FirstOrDefault(i => i.Identifier == "SuggestMod");
        Assert.That(suggest, Is.Not.Null);
        Assert.That(suggest!.Kind, Is.EqualTo(RecommendationKind.Suggested));
        Assert.That(suggest.SourceModules, Is.EquivalentTo(new[] { "ModB" }));
    }

    [Test]
    public void 用例3_过滤已安装的模组()
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
    }

    [Test]
    public void 用例4_过滤处于待安装列表中的模组()
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
    public void 用例6_默认全选且全选与全不选命令正常切换()
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

        // 全不选
        vm.UncheckAllCommand.Execute(null);
        Assert.That(vm.SelectedCount, Is.EqualTo(0));
        Assert.That(vm.CanApply, Is.False);
        Assert.That(vm.SelectedIdentifiers, Is.Empty);

        // 全选
        vm.CheckAllCommand.Execute(null);
        Assert.That(vm.SelectedCount, Is.EqualTo(2));
        Assert.That(vm.CanApply, Is.True);
        Assert.That(vm.SelectedIdentifiers, Is.EquivalentTo(new[] { "Rec1", "Rec2" }));
    }

    [Test]
    public void 用例7_未勾选时点击主按钮触发校验错误且不提交()
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
    public void 用例8_勾选后点击加入待安装将请求灌入Sink并关窗()
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
    public void 用例9_点击关闭或取消时不提交变更且AppliedRequests为空()
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

        vm.SecondaryCommand.Execute(null);

        Assert.That(vm.IsClosed, Is.True);
        Assert.That(vm.IsConfirmed, Is.False);
        Assert.That(vm.AppliedRequests, Is.Empty);
        Assert.That(sink.ReceivedRequests, Is.Null);
    }
}
