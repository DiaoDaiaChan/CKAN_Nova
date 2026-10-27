using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CkanUI.Services.Abstractions;
using CkanUI.ViewModels.Dialogs;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

[TestFixture]
public class InstallFiltersDialogViewModelTests
{
    private FakeInstallFiltersService _filtersService = null!;
    private readonly string[] _defaultRules = [".pdb", ".git", "Thumbs.db"];

    [SetUp]
    public void Setup()
    {
        _filtersService = new FakeInstallFiltersService
        {
            DefaultFilters = _defaultRules,
            LoadedFilters = new List<string> { ".pdb", ".git" }
        };
    }

    [Test]
    public void Constructor_NullService_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new InstallFiltersDialogViewModel(null!));
    }

    [Test]
    public void Constructor_SetsDefaultTitleAndButtonTexts()
    {
        var vm = new InstallFiltersDialogViewModel(_filtersService);
        Assert.That(vm.Title, Is.EqualTo("安装文件过滤器"));
        Assert.That(vm.PrimaryButtonText, Is.EqualTo("保存"));
        Assert.That(vm.SecondaryButtonText, Is.EqualTo("取消"));
    }

    [Test]
    public async Task InitializeAsync_LoadsFiltersIntoCollection()
    {
        var vm = new InstallFiltersDialogViewModel(_filtersService);
        await vm.InitializeAsync();

        Assert.That(vm.Filters, Has.Count.EqualTo(2));
        Assert.That(vm.Filters[0].Pattern, Is.EqualTo(".pdb"));
        Assert.That(vm.Filters[1].Pattern, Is.EqualTo(".git"));
        Assert.That(vm.ValidationError, Is.Null);
    }

    [Test]
    public async Task InitializeAsync_WhenServiceThrows_SetsValidationError()
    {
        _filtersService.ThrowOnLoad = true;

        var vm = new InstallFiltersDialogViewModel(_filtersService);
        await vm.InitializeAsync();

        Assert.That(vm.Filters, Is.Empty);
        Assert.That(vm.ValidationError, Does.Contain("加载失败测试"));
    }

    [Test]
    public void AddFilter_ValidPattern_AddsToCollectionAndClearsInput()
    {
        var vm = new InstallFiltersDialogViewModel(_filtersService);
        vm.NewFilterText = "  *.craft  ";

        vm.AddFilterCommand.Execute(null);

        Assert.That(vm.Filters, Has.Count.EqualTo(1));
        Assert.That(vm.Filters[0].Pattern, Is.EqualTo("*.craft"));
        Assert.That(vm.NewFilterText, Is.Empty);
        Assert.That(vm.ValidationError, Is.Null);
    }

    [Test]
    public void AddFilter_EmptyOrWhitespace_SetsValidationError()
    {
        var vm = new InstallFiltersDialogViewModel(_filtersService);
        vm.NewFilterText = "   ";

        vm.AddFilterCommand.Execute(null);

        Assert.That(vm.Filters, Is.Empty);
        Assert.That(vm.ValidationError, Does.Contain("不能为空"));
    }

    [Test]
    public void AddFilter_DuplicatePatternCaseInsensitive_SetsValidationError()
    {
        var vm = new InstallFiltersDialogViewModel(_filtersService);
        vm.NewFilterText = ".pdb";
        vm.AddFilterCommand.Execute(null);

        vm.NewFilterText = ".PDB";
        vm.AddFilterCommand.Execute(null);

        Assert.That(vm.Filters, Has.Count.EqualTo(1));
        Assert.That(vm.ValidationError, Does.Contain("已存在"));
    }

    [Test]
    public void RemoveFilter_RemovesItemFromCollection()
    {
        var vm = new InstallFiltersDialogViewModel(_filtersService);
        vm.NewFilterText = ".pdb";
        vm.AddFilterCommand.Execute(null);
        vm.NewFilterText = ".git";
        vm.AddFilterCommand.Execute(null);

        var toRemove = vm.Filters[0];
        toRemove.RemoveCommand.Execute(null);

        Assert.That(vm.Filters, Has.Count.EqualTo(1));
        Assert.That(vm.Filters[0].Pattern, Is.EqualTo(".git"));
    }

    [Test]
    public void ResetDefaults_ReplacesFiltersWithDefaultList()
    {
        var vm = new InstallFiltersDialogViewModel(_filtersService);
        vm.NewFilterText = "custom_rule";
        vm.AddFilterCommand.Execute(null);

        vm.ResetDefaultsCommand.Execute(null);

        Assert.That(vm.Filters, Has.Count.EqualTo(3));
        Assert.That(vm.Filters.Select(f => f.Pattern), Is.EquivalentTo(_defaultRules));
        Assert.That(vm.ValidationError, Is.Null);
    }

    [Test]
    public async Task SaveAsync_SavesFiltersAndCloses()
    {
        var vm = new InstallFiltersDialogViewModel(_filtersService);
        vm.NewFilterText = "*.pdb";
        vm.AddFilterCommand.Execute(null);
        vm.NewFilterText = "*.txt";
        vm.AddFilterCommand.Execute(null);

        await vm.SaveAsync();

        Assert.That(_filtersService.SavedFilters, Has.Count.EqualTo(2));
        Assert.That(_filtersService.SavedFilters, Contains.Item("*.pdb"));
        Assert.That(_filtersService.SavedFilters, Contains.Item("*.txt"));

        Assert.That(vm.IsClosed, Is.True);
        Assert.That(vm.IsConfirmed, Is.True);
        Assert.That(vm.ValidationError, Is.Null);
    }

    [Test]
    public async Task SaveAsync_WhenSaveThrows_SetsValidationError()
    {
        _filtersService.ThrowOnSave = true;

        var vm = new InstallFiltersDialogViewModel(_filtersService);
        vm.NewFilterText = "*.pdb";
        vm.AddFilterCommand.Execute(null);

        await vm.SaveAsync();

        Assert.That(vm.IsClosed, Is.False);
        Assert.That(vm.IsConfirmed, Is.False);
        Assert.That(vm.ValidationError, Does.Contain("保存失败测试"));
    }

    [Test]
    public async Task PrimaryCommand_TriggersSaveAsync()
    {
        var vm = new InstallFiltersDialogViewModel(_filtersService);
        vm.NewFilterText = "*.pdb";
        vm.AddFilterCommand.Execute(null);

        vm.PrimaryCommand.Execute(null);
        await Task.Delay(50);

        Assert.That(_filtersService.SavedFilters, Has.Count.EqualTo(1));
        Assert.That(_filtersService.SavedFilters[0], Is.EqualTo("*.pdb"));
        Assert.That(vm.IsClosed, Is.True);
        Assert.That(vm.IsConfirmed, Is.True);
    }

    private sealed class FakeInstallFiltersService : IInstallFiltersService
    {
        public IReadOnlyList<string> DefaultFilters { get; set; } = Array.Empty<string>();
        public List<string> LoadedFilters { get; set; } = new();
        public List<string> SavedFilters { get; } = new();
        public bool ThrowOnLoad { get; set; }
        public bool ThrowOnSave { get; set; }

        public IReadOnlyList<string> GetDefaultFilters() => DefaultFilters;

        public Task<IReadOnlyList<string>> LoadAsync(CancellationToken cancellationToken = default)
        {
            if (ThrowOnLoad)
            {
                throw new InvalidOperationException("加载失败测试");
            }
            return Task.FromResult<IReadOnlyList<string>>(LoadedFilters);
        }

        public Task SaveAsync(IReadOnlyCollection<string> filters, CancellationToken cancellationToken = default)
        {
            if (ThrowOnSave)
            {
                throw new InvalidOperationException("保存失败测试");
            }
            SavedFilters.Clear();
            SavedFilters.AddRange(filters);
            return Task.CompletedTask;
        }
    }
}
