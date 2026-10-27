using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Dialogs;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

/// <summary>
/// 兼容游戏版本管理对话框视图模型单元测试（F-14 / UI-11）。
/// 验证版本加载、状态管理、原生版本锁定、选择状态变化、保存交接与风险确认流程。
/// </summary>
[TestFixture]
public sealed class CompatibleVersionsDialogViewModelTests
{
    [Test]
    public void Constructor_WithoutService_UsesDesignerDefaults()
    {
        var vm = new CompatibleVersionsDialogViewModel();

        Assert.That(vm.Title, Is.EqualTo("兼容游戏版本管理"));
        Assert.That(vm.PrimaryButtonText, Is.EqualTo("保存"));
        Assert.That(vm.SecondaryButtonText, Is.EqualTo("取消"));
        Assert.That(vm.VersionItems, Is.Empty);
    }

    [Test]
    public async Task InitializeAsync_WithoutService_PopulatesMockItems()
    {
        var vm = new CompatibleVersionsDialogViewModel();

        await vm.InitializeAsync();

        Assert.That(vm.InstanceDisplayName, Is.EqualTo("DefaultInstance"));
        Assert.That(vm.RealVersion, Is.EqualTo("1.12.5"));
        Assert.That(vm.VersionItems, Has.Count.EqualTo(5));

        var realItem = vm.VersionItems[0];
        Assert.That(realItem.VersionString, Is.EqualTo("1.12.5"));
        Assert.That(realItem.IsRealVersion, Is.True);
        Assert.That(realItem.IsReadOnly, Is.True);
        Assert.That(realItem.IsSelected, Is.True);
    }

    [Test]
    public async Task InitializeAsync_WithService_LoadsSnapshotCorrectly()
    {
        var fakeService = new FakeCompatibleVersionsService
        {
            Snapshot = new CompatibleVersionsSnapshot(
                InstanceName: "KSP-Modded",
                RealVersion: "1.12.3",
                Items: new[]
                {
                    new CompatibleVersionItemDto("1.12.3", IsSelected: true, IsRealVersion: true, IsWildcard: false),
                    new CompatibleVersionItemDto("1.12", IsSelected: true, IsRealVersion: false, IsWildcard: true),
                    new CompatibleVersionItemDto("1.11", IsSelected: false, IsRealVersion: false, IsWildcard: true),
                })
        };

        var vm = new CompatibleVersionsDialogViewModel(fakeService, "KSP-Modded");

        await vm.InitializeAsync();

        Assert.That(vm.InstanceDisplayName, Is.EqualTo("KSP-Modded"));
        Assert.That(vm.RealVersion, Is.EqualTo("1.12.3"));
        Assert.That(vm.VersionItems, Has.Count.EqualTo(3));
        Assert.That(vm.VersionItems[0].IsRealVersion, Is.True);
        Assert.That(vm.VersionItems[0].IsReadOnly, Is.True);
        Assert.That(vm.VersionItems[1].IsWildcard, Is.True);
        Assert.That(vm.VersionItems[1].IsSelected, Is.True);
        Assert.That(vm.VersionItems[2].IsSelected, Is.False);
    }

    [Test]
    public async Task InitializeAsync_WhenServiceThrows_SetsValidationError()
    {
        var fakeService = new FakeCompatibleVersionsService
        {
            ThrowOnLoad = true
        };

        var vm = new CompatibleVersionsDialogViewModel(fakeService);

        await vm.InitializeAsync();

        Assert.That(vm.HasValidationError, Is.True);
        Assert.That(vm.ValidationError, Does.Contain("加载兼容游戏版本失败"));
        Assert.That(vm.IsLoading, Is.False);
    }

    [Test]
    public async Task SelectAll_SelectsAllExceptRealVersion()
    {
        var fakeService = new FakeCompatibleVersionsService
        {
            Snapshot = new CompatibleVersionsSnapshot(
                InstanceName: "Default",
                RealVersion: "1.12.5",
                Items: new[]
                {
                    new CompatibleVersionItemDto("1.12.5", true, true, false),
                    new CompatibleVersionItemDto("1.12", false, false, true),
                    new CompatibleVersionItemDto("1.11", false, false, true),
                })
        };

        var vm = new CompatibleVersionsDialogViewModel(fakeService);
        await vm.InitializeAsync();

        vm.SelectAllCommand.Execute(true);

        Assert.That(vm.VersionItems.All(i => i.IsSelected), Is.True);
        Assert.That(vm.VersionItems[0].IsRealVersion, Is.True);

        vm.SelectAllCommand.Execute(false);

        Assert.That(vm.VersionItems[0].IsSelected, Is.True, "真实版本即使取消全选也必须保持选中");
        Assert.That(vm.VersionItems.Skip(1).All(i => !i.IsSelected), Is.True);
    }

    [Test]
    public async Task ResetToInitial_RestoresOriginalSelection()
    {
        var fakeService = new FakeCompatibleVersionsService
        {
            Snapshot = new CompatibleVersionsSnapshot(
                InstanceName: "Default",
                RealVersion: "1.12.5",
                Items: new[]
                {
                    new CompatibleVersionItemDto("1.12.5", true, true, false),
                    new CompatibleVersionItemDto("1.12", true, false, true),
                    new CompatibleVersionItemDto("1.11", false, false, true),
                })
        };

        var vm = new CompatibleVersionsDialogViewModel(fakeService);
        await vm.InitializeAsync();

        // 翻转选择
        vm.VersionItems[1].IsSelected = false;
        vm.VersionItems[2].IsSelected = true;

        vm.ResetToInitialCommand.Execute(null);

        Assert.That(vm.VersionItems[0].IsSelected, Is.True);
        Assert.That(vm.VersionItems[1].IsSelected, Is.True);
        Assert.That(vm.VersionItems[2].IsSelected, Is.False);
        Assert.That(vm.IsWarningPromptVisible, Is.False);
    }

    [Test]
    public async Task AddCustomVersion_ValidNewVersion_InsertsItemAndSelectsIt()
    {
        var fakeService = new FakeCompatibleVersionsService
        {
            Snapshot = new CompatibleVersionsSnapshot(
                InstanceName: "Default",
                RealVersion: "1.12.5",
                Items: new[]
                {
                    new CompatibleVersionItemDto("1.12.5", true, true, false)
                })
        };

        var vm = new CompatibleVersionsDialogViewModel(fakeService);
        await vm.InitializeAsync();

        vm.CustomVersionInput = "1.10.1";
        vm.AddCustomVersionCommand.Execute(null);

        Assert.That(vm.VersionItems, Has.Count.EqualTo(2));
        var added = vm.VersionItems.FirstOrDefault(i => i.VersionString == "1.10.1");
        Assert.That(added, Is.Not.Null);
        Assert.That(added!.IsSelected, Is.True);
        Assert.That(added.IsRealVersion, Is.False);
        Assert.That(added.IsWildcard, Is.False);
        Assert.That(vm.CustomVersionInput, Is.Empty);
        Assert.That(vm.HasValidationError, Is.False);
    }

    [Test]
    public async Task AddCustomVersion_ExistingVersion_MarksItSelectedWithoutDuplicate()
    {
        var fakeService = new FakeCompatibleVersionsService
        {
            Snapshot = new CompatibleVersionsSnapshot(
                InstanceName: "Default",
                RealVersion: "1.12.5",
                Items: new[]
                {
                    new CompatibleVersionItemDto("1.12.5", true, true, false),
                    new CompatibleVersionItemDto("1.12", false, false, true)
                })
        };

        var vm = new CompatibleVersionsDialogViewModel(fakeService);
        await vm.InitializeAsync();

        vm.CustomVersionInput = "1.12";
        vm.AddCustomVersionCommand.Execute(null);

        Assert.That(vm.VersionItems, Has.Count.EqualTo(2));
        Assert.That(vm.VersionItems[1].IsSelected, Is.True);
        Assert.That(vm.CustomVersionInput, Is.Empty);
    }

    [Test]
    public async Task AddCustomVersion_WhitespaceOrEmpty_SetsValidationError()
    {
        var vm = new CompatibleVersionsDialogViewModel();
        await vm.InitializeAsync();

        vm.CustomVersionInput = "   ";
        vm.AddCustomVersionCommand.Execute(null);

        Assert.That(vm.HasValidationError, Is.True);
        Assert.That(vm.ValidationError, Does.Contain("请输入有效的版本号"));
    }

    [Test]
    public async Task SaveAsync_NoChanges_SavesAndClosesDirectlyWithoutWarning()
    {
        var fakeService = new FakeCompatibleVersionsService
        {
            Snapshot = new CompatibleVersionsSnapshot(
                InstanceName: "Default",
                RealVersion: "1.12.5",
                Items: new[]
                {
                    new CompatibleVersionItemDto("1.12.5", true, true, false),
                    new CompatibleVersionItemDto("1.12", true, false, true)
                })
        };

        var vm = new CompatibleVersionsDialogViewModel(fakeService);
        await vm.InitializeAsync();

        await vm.SaveAsync();

        Assert.That(fakeService.SavedVersions, Is.Not.Null);
        Assert.That(fakeService.SavedVersions, Is.EquivalentTo(new[] { "1.12.5", "1.12" }));
        Assert.That(vm.IsConfirmed, Is.True);
        Assert.That(vm.IsClosed, Is.True);
        Assert.That(vm.IsWarningPromptVisible, Is.False);
    }

    [Test]
    public async Task SaveAsync_WithChangesAndNoExternalHandler_ShowsInlineWarningPrompt()
    {
        var fakeService = new FakeCompatibleVersionsService
        {
            Snapshot = new CompatibleVersionsSnapshot(
                InstanceName: "Default",
                RealVersion: "1.12.5",
                Items: new[]
                {
                    new CompatibleVersionItemDto("1.12.5", true, true, false),
                    new CompatibleVersionItemDto("1.12", false, false, true)
                })
        };

        var vm = new CompatibleVersionsDialogViewModel(fakeService);
        await vm.InitializeAsync();

        // 勾选修改
        vm.VersionItems[1].IsSelected = true;

        await vm.SaveAsync();

        // 应弹出内联警示横幅且尚未保存
        Assert.That(vm.IsWarningPromptVisible, Is.True);
        Assert.That(fakeService.SavedVersions, Is.Null);
        Assert.That(vm.IsClosed, Is.False);

        // 再次调用 SaveCommand (模拟点击"已知悉风险，继续保存")
        await vm.SaveAsync();

        Assert.That(fakeService.SavedVersions, Is.Not.Null);
        Assert.That(fakeService.SavedVersions, Is.EquivalentTo(new[] { "1.12.5", "1.12" }));
        Assert.That(vm.IsConfirmed, Is.True);
        Assert.That(vm.IsClosed, Is.True);
    }

    [Test]
    public async Task SaveAsync_WithChangesAndExternalHandlerDeclined_DoesNotSave()
    {
        var fakeService = new FakeCompatibleVersionsService
        {
            Snapshot = new CompatibleVersionsSnapshot(
                InstanceName: "Default",
                RealVersion: "1.12.5",
                Items: new[]
                {
                    new CompatibleVersionItemDto("1.12.5", true, true, false),
                    new CompatibleVersionItemDto("1.12", false, false, true)
                })
        };

        var vm = new CompatibleVersionsDialogViewModel(fakeService)
        {
            WarningConfirmationHandler = _ => Task.FromResult(false)
        };
        await vm.InitializeAsync();

        vm.VersionItems[1].IsSelected = true;

        await vm.SaveAsync();

        Assert.That(fakeService.SavedVersions, Is.Null);
        Assert.That(vm.IsConfirmed, Is.False);
        Assert.That(vm.IsClosed, Is.False);
    }

    [Test]
    public async Task DismissWarning_HidesWarningPrompt()
    {
        var vm = new CompatibleVersionsDialogViewModel();
        await vm.InitializeAsync();
        vm.IsWarningPromptVisible = true;

        vm.DismissWarningCommand.Execute(null);

        Assert.That(vm.IsWarningPromptVisible, Is.False);
    }

    private sealed class FakeCompatibleVersionsService : ICompatibleVersionsService
    {
        public CompatibleVersionsSnapshot Snapshot { get; set; } = new(
            "FakeInstance",
            "1.12.5",
            Array.Empty<CompatibleVersionItemDto>());

        public bool ThrowOnLoad { get; set; }

        public IReadOnlyCollection<string>? SavedVersions { get; private set; }

        public Task<CompatibleVersionsSnapshot> LoadAsync(string? instanceName = null, CancellationToken cancellationToken = default)
        {
            if (ThrowOnLoad)
            {
                throw new InvalidOperationException("模拟服务错误");
            }
            return Task.FromResult(Snapshot);
        }

        public Task SaveAsync(
            IReadOnlyCollection<string> selectedVersions,
            string? instanceName = null,
            bool triggerRefresh = true,
            CancellationToken cancellationToken = default)
        {
            SavedVersions = selectedVersions;
            return Task.CompletedTask;
        }
    }
}
