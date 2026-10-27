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

[TestFixture]
public sealed class PreferredHostsDialogViewModelTests
{
    private FakePreferredHostsService _fakeService = null!;

    [SetUp]
    public void SetUp()
    {
        _fakeService = new FakePreferredHostsService();
    }

    private PreferredHostsDialogViewModel CreateViewModel(bool withService = true) =>
        new PreferredHostsDialogViewModel(withService ? _fakeService : null);

    [Test]
    public void Constructor_WithoutService_UsesDesignDefaults()
    {
        var vm = CreateViewModel(withService: false);

        Assert.That(vm.Title, Is.EqualTo("首选下载主机"));
        Assert.That(vm.PrimaryButtonText, Is.EqualTo("保存"));
        Assert.That(vm.SecondaryButtonText, Is.EqualTo("取消"));
    }

    [Test]
    public async Task InitializeAsync_WithoutService_PopulatesDemoData()
    {
        var vm = CreateViewModel(withService: false);

        await vm.InitializeAsync();

        Assert.That(vm.PreferredHosts, Is.Not.Empty);
        Assert.That(vm.PreferredHosts.Any(h => h.IsPlaceholder), Is.True);
        Assert.That(vm.KnownHosts, Does.Contain("github.com"));
    }

    [Test]
    public async Task InitializeAsync_WithService_LoadsHostsCorrectly()
    {
        _fakeService.Preferred = new List<HostPriorityItemDto>
        {
            new("github.com", 1),
            new("spacedock.info", 2)
        };
        _fakeService.Known = new List<string> { "github.com", "spacedock.info", "archive.org" };

        var vm = CreateViewModel();
        await vm.InitializeAsync();

        // 包含配置中的两项 + 自动追加的占位符（其余所有主机）
        Assert.That(vm.PreferredHosts, Has.Count.EqualTo(3));
        Assert.That(vm.PreferredHosts[0].Host, Is.EqualTo("github.com"));
        Assert.That(vm.PreferredHosts[0].Priority, Is.EqualTo(1));
        Assert.That(vm.PreferredHosts[1].Host, Is.EqualTo("spacedock.info"));
        Assert.That(vm.PreferredHosts[1].Priority, Is.EqualTo(2));
        Assert.That(vm.PreferredHosts[2].IsPlaceholder, Is.True);
        Assert.That(vm.PreferredHosts[2].Priority, Is.EqualTo(3));

        Assert.That(vm.KnownHosts, Has.Count.EqualTo(3));
    }

    [Test]
    public void MoveUp_FirstItem_CannotMoveUp()
    {
        var vm = CreateViewModel();
        vm.PopulateInitialData(
            new[] { new HostPriorityItemDto("github.com", 1), new HostPriorityItemDto("spacedock.info", 2) },
            Array.Empty<string>());

        vm.SelectedHost = vm.PreferredHosts[0];

        Assert.That(vm.CanMoveUp, Is.False);
        Assert.That(vm.MoveUpCommand.CanExecute(null), Is.False);
    }

    [Test]
    public void MoveUp_SecondItem_SwapsWithFirstAndReindexesPriorities()
    {
        var vm = CreateViewModel();
        vm.PopulateInitialData(
            new[] { new HostPriorityItemDto("github.com", 1), new HostPriorityItemDto("spacedock.info", 2) },
            Array.Empty<string>());

        vm.SelectedHost = vm.PreferredHosts[1];
        Assert.That(vm.CanMoveUp, Is.True);

        vm.MoveUpCommand.Execute(null);

        Assert.That(vm.PreferredHosts[0].Host, Is.EqualTo("spacedock.info"));
        Assert.That(vm.PreferredHosts[0].Priority, Is.EqualTo(1));
        Assert.That(vm.PreferredHosts[1].Host, Is.EqualTo("github.com"));
        Assert.That(vm.PreferredHosts[1].Priority, Is.EqualTo(2));
    }

    [Test]
    public void MoveDown_LastItem_CannotMoveDown()
    {
        var vm = CreateViewModel();
        vm.PopulateInitialData(
            new[] { new HostPriorityItemDto("github.com", 1) },
            Array.Empty<string>());

        // 列表中有 github.com 和自动补齐的占位符
        vm.SelectedHost = vm.PreferredHosts.Last();

        Assert.That(vm.CanMoveDown, Is.False);
        Assert.That(vm.MoveDownCommand.CanExecute(null), Is.False);
    }

    [Test]
    public void MoveDown_FirstItem_SwapsWithNextAndReindexesPriorities()
    {
        var vm = CreateViewModel();
        vm.PopulateInitialData(
            new[] { new HostPriorityItemDto("github.com", 1), new HostPriorityItemDto("spacedock.info", 2) },
            Array.Empty<string>());

        vm.SelectedHost = vm.PreferredHosts[0];
        Assert.That(vm.CanMoveDown, Is.True);

        vm.MoveDownCommand.Execute(null);

        Assert.That(vm.PreferredHosts[0].Host, Is.EqualTo("spacedock.info"));
        Assert.That(vm.PreferredHosts[0].Priority, Is.EqualTo(1));
        Assert.That(vm.PreferredHosts[1].Host, Is.EqualTo("github.com"));
        Assert.That(vm.PreferredHosts[1].Priority, Is.EqualTo(2));
    }

    [Test]
    public void Remove_NonPlaceholderItem_RemovesAndUpdatesPriorities()
    {
        var vm = CreateViewModel();
        vm.PopulateInitialData(
            new[] { new HostPriorityItemDto("github.com", 1), new HostPriorityItemDto("spacedock.info", 2) },
            Array.Empty<string>());

        vm.SelectedHost = vm.PreferredHosts[0];
        Assert.That(vm.CanRemove, Is.True);

        vm.RemoveCommand.Execute(null);

        Assert.That(vm.PreferredHosts.Any(h => h.Host == "github.com"), Is.False);
        Assert.That(vm.PreferredHosts[0].Host, Is.EqualTo("spacedock.info"));
        Assert.That(vm.PreferredHosts[0].Priority, Is.EqualTo(1));
    }

    [Test]
    public void Remove_PlaceholderItem_CannotRemove()
    {
        var vm = CreateViewModel();
        vm.PopulateInitialData(
            new[] { new HostPriorityItemDto("github.com", 1) },
            Array.Empty<string>());

        var placeholder = vm.PreferredHosts.First(h => h.IsPlaceholder);
        vm.SelectedHost = placeholder;

        Assert.That(vm.CanRemove, Is.False);
        Assert.That(vm.RemoveCommand.CanExecute(null), Is.False);
    }

    [Test]
    public void AddCustomHost_ValidHost_InsertsBeforePlaceholder()
    {
        var vm = CreateViewModel();
        vm.PopulateInitialData(
            new[] { new HostPriorityItemDto("github.com", 1) },
            Array.Empty<string>());

        vm.CustomHostInput = "https://gitlab.com/repo";
        vm.AddCustomHostCommand.Execute(null);

        Assert.That(vm.CustomHostInput, Is.Empty);
        Assert.That(vm.PreferredHosts.Any(h => h.Host == "gitlab.com"), Is.True);

        var gitlabItem = vm.PreferredHosts.First(h => h.Host == "gitlab.com");
        var placeholderItem = vm.PreferredHosts.First(h => h.IsPlaceholder);
        Assert.That(gitlabItem.Priority, Is.LessThan(placeholderItem.Priority));
    }

    [Test]
    public void AddCustomHost_DuplicateHost_SetsErrorMessageAndDoesNotAdd()
    {
        var vm = CreateViewModel();
        vm.PopulateInitialData(
            new[] { new HostPriorityItemDto("github.com", 1) },
            Array.Empty<string>());

        vm.CustomHostInput = "GITHUB.COM";
        vm.AddCustomHostCommand.Execute(null);

        Assert.That(vm.ValidationError, Does.Contain("已经在首选列表中"));
        Assert.That(vm.PreferredHosts.Count(h => string.Equals(h.Host, "github.com", StringComparison.OrdinalIgnoreCase)), Is.EqualTo(1));
    }

    [Test]
    public void AddSelectedKnownHost_AddsHostSuccessfully()
    {
        var vm = CreateViewModel();
        vm.PopulateInitialData(
            new[] { new HostPriorityItemDto("github.com", 1) },
            new[] { "archive.org", "spacedock.info" });

        vm.SelectedKnownHost = "spacedock.info";
        vm.AddSelectedKnownHostCommand.Execute(null);

        Assert.That(vm.PreferredHosts.Any(h => h.Host == "spacedock.info"), Is.True);
    }

    [Test]
    public async Task PrimaryCommand_SavesOrderedHostsToService()
    {
        var vm = CreateViewModel();
        vm.PopulateInitialData(
            new[] { new HostPriorityItemDto("github.com", 1), new HostPriorityItemDto("spacedock.info", 2) },
            Array.Empty<string>());

        await vm.SaveAsync();

        Assert.That(_fakeService.SavedHosts, Is.Not.Null);
        var savedList = _fakeService.SavedHosts!.ToList();
        Assert.That(savedList, Has.Count.EqualTo(3)); // github.com, spacedock.info, null
        Assert.That(savedList[0], Is.EqualTo("github.com"));
        Assert.That(savedList[1], Is.EqualTo("spacedock.info"));
        Assert.That(savedList[2], Is.Null);
    }

    [Test]
    public async Task PrimaryCommand_WhenServiceSaveFails_SetsErrorMessage()
    {
        _fakeService.SaveResult = OperationResult<bool>.Fail("权限不足，无法保存");
        var vm = CreateViewModel();
        vm.PopulateInitialData(
            new[] { new HostPriorityItemDto("github.com", 1) },
            Array.Empty<string>());

        await vm.SaveAsync();

        Assert.That(vm.ValidationError, Does.Contain("权限不足，无法保存"));
    }

    private sealed class FakePreferredHostsService : IPreferredHostsService
    {
        public List<HostPriorityItemDto> Preferred { get; set; } = new();
        public List<string> Known { get; set; } = new();
        public IEnumerable<string?>? SavedHosts { get; private set; }
        public OperationResult<bool> SaveResult { get; set; } = OperationResult<bool>.Ok(true);

        public Task<IReadOnlyList<HostPriorityItemDto>> GetPreferredHostsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<HostPriorityItemDto>>(Preferred);

        public Task<IReadOnlyList<string>> GetKnownHostsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>(Known);

        public Task<OperationResult<bool>> SavePreferredHostsAsync(IEnumerable<string?> hosts, CancellationToken cancellationToken = default)
        {
            SavedHosts = hosts.ToList();
            return Task.FromResult(SaveResult);
        }
    }
}
