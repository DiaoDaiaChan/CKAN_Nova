using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CkanUI.Services;
using CkanUI.ViewModels.Dialogs;
using NSubstitute;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

[TestFixture]
public class InstallFiltersDialogViewModelTests
{
    private IInstallFiltersService _filtersService = null!;
    private readonly string[] _defaultRules = [".pdb", ".git", "Thumbs.db"];

    [SetUp]
    public void Setup()
    {
        _filtersService = Substitute.For<IInstallFiltersService>();
        _filtersService.GetDefaultFilters().Returns(_defaultRules);
        _filtersService.LoadAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>(new List<string> { ".pdb", ".git" }));
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
        _filtersService.LoadAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyList<string>>(new InvalidOperationException("网络或磁盘错误")));

        var vm = new InstallFiltersDialogViewModel(_filtersService);
        await vm.InitializeAsync();

        Assert.That(vm.Filters, Is.Empty);
        Assert.That(vm.ValidationError, Does.Contain("网络或磁盘错误"));
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
    public async Task PrimaryCommand_SavesFiltersAndCloses()
    {
        var vm = new InstallFiltersDialogViewModel(_filtersService);
        vm.NewFilterText = "*.pdb";
        vm.AddFilterCommand.Execute(null);
        vm.NewFilterText = "*.txt";
        vm.AddFilterCommand.Execute(null);

        bool? closeResult = null;
        vm.CloseRequested += (_, r) => closeResult = r;

        await vm.PrimaryCommand.ExecuteAsync(null);

        await _filtersService.Received(1).SaveAsync(
            Arg.Is<IReadOnlyCollection<string>>(c => c.Count == 2 && c.Contains("*.pdb") && c.Contains("*.txt")),
            Arg.Any<CancellationToken>());

        Assert.That(closeResult, Is.True);
        Assert.That(vm.ValidationError, Is.Null);
    }

    [Test]
    public async Task PrimaryCommand_WhenSaveThrows_SetsValidationError()
    {
        _filtersService.SaveAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new Exception("IO 写入失败")));

        var vm = new InstallFiltersDialogViewModel(_filtersService);
        vm.NewFilterText = "*.pdb";
        vm.AddFilterCommand.Execute(null);

        bool? closeResult = null;
        vm.CloseRequested += (_, r) => closeResult = r;

        await vm.PrimaryCommand.ExecuteAsync(null);

        Assert.That(closeResult, Is.Null);
        Assert.That(vm.ValidationError, Does.Contain("IO 写入失败"));
    }
}
