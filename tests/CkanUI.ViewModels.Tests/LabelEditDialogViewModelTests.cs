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
public class LabelEditDialogViewModelTests
{
    private FakeLabelService _labelService = null!;
    private LabelEditDialogViewModel _viewModel = null!;

    [SetUp]
    public void SetUp()
    {
        _labelService = new FakeLabelService();
        _viewModel = new LabelEditDialogViewModel(_labelService);
    }

    private static LabelInfo CreateLabelInfo(
        string name,
        string? colorHex = null,
        bool isGlobal = true,
        bool hideFromOtherFilters = false,
        bool notifyOnChanges = false,
        bool removeOnChanges = false,
        bool alertOnInstall = false,
        bool removeOnInstall = false,
        bool holdVersion = false,
        bool ignoreMissingFiles = false,
        int moduleCount = 0)
    {
        return new LabelInfo(
            name,
            colorHex,
            isGlobal,
            hideFromOtherFilters,
            notifyOnChanges,
            removeOnChanges,
            alertOnInstall,
            removeOnInstall,
            holdVersion,
            ignoreMissingFiles,
            moduleCount);
    }

    [Test]
    public async Task InitializeAsync_LoadsLabelsAndSelectsFirst()
    {
        _labelService.LabelsList.Add(CreateLabelInfo("Favourites", colorHex: "#90EE90", moduleCount: 5));
        _labelService.LabelsList.Add(CreateLabelInfo("CustomTag", colorHex: "#87CEFA", isGlobal: false, moduleCount: 2));

        await _viewModel.InitializeAsync();

        Assert.That(_viewModel.Items.Count, Is.EqualTo(2));
        Assert.That(_viewModel.SelectedItem, Is.Not.Null);
        Assert.That(_viewModel.SelectedItem!.Name, Is.EqualTo("Favourites"));
        Assert.That(_viewModel.HasSelectedItem, Is.True);
    }

    [Test]
    public async Task InitializeAsync_SetsReservedLabelStatus()
    {
        _labelService.LabelsList.Add(CreateLabelInfo("Favourites"));
        _labelService.LabelsList.Add(CreateLabelInfo("Custom"));

        await _viewModel.InitializeAsync();

        _viewModel.SelectedItem = _viewModel.Items.First(i => i.Name == "Favourites");
        Assert.That(_viewModel.IsReservedLabel, Is.True);
        Assert.That(_viewModel.CanDeleteSelected, Is.False);

        _viewModel.SelectedItem = _viewModel.Items.First(i => i.Name == "Custom");
        Assert.That(_viewModel.IsReservedLabel, Is.False);
        Assert.That(_viewModel.CanDeleteSelected, Is.True);
    }

    [Test]
    public void AddLabel_CreatesNewItemWithDefaultNameAndSelectsIt()
    {
        _viewModel.AddLabelCommand.Execute(null);

        Assert.That(_viewModel.Items.Count, Is.EqualTo(1));
        var added = _viewModel.Items[0];
        Assert.That(added.IsNew, Is.True);
        Assert.That(added.Name, Is.EqualTo("新标签"));
        Assert.That(_viewModel.SelectedItem, Is.EqualTo(added));
        Assert.That(_viewModel.IsReservedLabel, Is.False);
        Assert.That(_viewModel.CanDeleteSelected, Is.True);
    }

    [Test]
    public void AddLabel_MultipleTimes_GeneratesUniqueNames()
    {
        _viewModel.AddLabelCommand.Execute(null);
        _viewModel.AddLabelCommand.Execute(null);

        Assert.That(_viewModel.Items.Count, Is.EqualTo(2));
        Assert.That(_viewModel.Items[0].Name, Is.EqualTo("新标签"));
        Assert.That(_viewModel.Items[1].Name, Is.EqualTo("新标签1"));
    }

    [Test]
    public void SelectPresetColor_UpdatesSelectedItemColor()
    {
        _viewModel.AddLabelCommand.Execute(null);
        Assert.That(_viewModel.SelectedItem, Is.Not.Null);

        _viewModel.SelectPresetColorCommand.Execute("#FFD700");

        Assert.That(_viewModel.SelectedItem!.ColorHex, Is.EqualTo("#FFD700"));
        Assert.That(_viewModel.SelectedItem.HasColor, Is.True);
    }

    [Test]
    public void ClearColor_SetsEmptyColorOnSelectedItem()
    {
        _viewModel.AddLabelCommand.Execute(null);
        _viewModel.SelectedItem!.ColorHex = "#FF0000";

        _viewModel.ClearColorCommand.Execute(null);

        Assert.That(_viewModel.SelectedItem.ColorHex, Is.Empty);
        Assert.That(_viewModel.SelectedItem.HasColor, Is.False);
    }

    [Test]
    public async Task DeleteLabel_PreventsDeletingReservedLabels()
    {
        _labelService.LabelsList.Add(CreateLabelInfo("Favourites"));
        await _viewModel.InitializeAsync();
        _viewModel.SelectedItem = _viewModel.Items[0];

        await _viewModel.DeleteLabelCommand.ExecuteAsync(null);

        Assert.That(_viewModel.Items.Count, Is.EqualTo(1));
        Assert.That(_viewModel.ValidationError, Does.Contain("系统内置标签"));
    }

    [Test]
    public async Task DeleteLabel_ExistingCustomLabel_CallsServiceAndRemovesItem()
    {
        _labelService.LabelsList.Add(CreateLabelInfo("OldCustom"));
        await _viewModel.InitializeAsync();
        _viewModel.SelectedItem = _viewModel.Items[0];

        await _viewModel.DeleteLabelCommand.ExecuteAsync(null);

        Assert.That(_viewModel.Items.Count, Is.EqualTo(0));
        Assert.That(_labelService.DeletedLabels, Contains.Item("OldCustom"));
        Assert.That(_viewModel.StatusMessage, Does.Contain("已删除"));
        Assert.That(_viewModel.SelectedItem, Is.Null);
    }

    [Test]
    public async Task SaveAsync_EmptyName_FailsValidation()
    {
        _viewModel.AddLabelCommand.Execute(null);
        _viewModel.SelectedItem!.Name = "   ";

        await _viewModel.SaveAsync();

        Assert.That(_viewModel.ValidationError, Does.Contain("名称不能为空"));
        Assert.That(_viewModel.IsConfirmed, Is.False);
        Assert.That(_labelService.CreatedRequests.Count, Is.EqualTo(0));
    }

    [Test]
    public async Task SaveAsync_InvalidHexColor_FailsValidation()
    {
        _viewModel.AddLabelCommand.Execute(null);
        _viewModel.SelectedItem!.Name = "MyTag";
        _viewModel.SelectedItem.ColorHex = "invalid-color";

        await _viewModel.SaveAsync();

        Assert.That(_viewModel.ValidationError, Does.Contain("十六进制色值"));
        Assert.That(_viewModel.IsConfirmed, Is.False);
    }

    [Test]
    public async Task SaveAsync_DuplicateNames_FailsValidation()
    {
        _viewModel.AddLabelCommand.Execute(null);
        _viewModel.SelectedItem!.Name = "TagA";

        _viewModel.AddLabelCommand.Execute(null);
        _viewModel.SelectedItem!.Name = "taga"; // case-insensitive duplicate

        await _viewModel.SaveAsync();

        Assert.That(_viewModel.ValidationError, Does.Contain("存在重复的标签名称"));
        Assert.That(_viewModel.IsConfirmed, Is.False);
    }

    [Test]
    public async Task SaveAsync_CreatesNewAndUpdatesExistingAndCloses()
    {
        _labelService.LabelsList.Add(CreateLabelInfo(
            "ExistingTag",
            colorHex: "#112233",
            hideFromOtherFilters: false));
        await _viewModel.InitializeAsync();

        // 修改已存在的标签
        _viewModel.Items[0].HideFromOtherFilters = true;
        _viewModel.Items[0].ColorHex = "#223344";

        // 新增标签
        _viewModel.AddLabelCommand.Execute(null);
        var newItem = _viewModel.SelectedItem!;
        newItem.Name = "BrandNew";
        newItem.HoldVersion = true;
        newItem.ColorHex = "#90EE90";

        await _viewModel.SaveAsync();

        Assert.That(_viewModel.ValidationError, Is.Null);
        Assert.That(_viewModel.IsConfirmed, Is.True);
        Assert.That(_viewModel.IsClosed, Is.True);

        // 验证 UpdateLabelAsync 调用
        Assert.That(_labelService.UpdatedRequests.Count, Is.EqualTo(1));
        var updateReq = _labelService.UpdatedRequests[0];
        Assert.That(updateReq.Name, Is.EqualTo("ExistingTag"));
        Assert.That(updateReq.HideFromOtherFilters, Is.True);
        Assert.That(updateReq.ColorHex, Is.EqualTo("#223344"));

        // 验证 CreateLabelAsync 调用
        Assert.That(_labelService.CreatedRequests.Count, Is.EqualTo(1));
        var createReq = _labelService.CreatedRequests[0];
        Assert.That(createReq.Name, Is.EqualTo("BrandNew"));
        Assert.That(createReq.HoldVersion, Is.True);
        Assert.That(createReq.ColorHex, Is.EqualTo("#90EE90"));
    }

    private sealed class FakeLabelService : ILabelService
    {
        public List<LabelInfo> LabelsList { get; } = new();
        public List<LabelEditRequest> CreatedRequests { get; } = new();
        public List<LabelEditRequest> UpdatedRequests { get; } = new();
        public List<string> DeletedLabels { get; } = new();

        public IReadOnlyList<LabelInfo> Labels => LabelsList;

        public bool HasActiveInstance => true;

        public string? InstanceName => "KSP-Test";

        public Task<OperationResult<IReadOnlyList<LabelInfo>>> RefreshAsync(CancellationToken ct = default)
        {
            return Task.FromResult(OperationResult<IReadOnlyList<LabelInfo>>.Ok(LabelsList.AsReadOnly()));
        }

        public Task<OperationResult<LabelInfo>> CreateLabelAsync(LabelEditRequest request, CancellationToken ct = default)
        {
            CreatedRequests.Add(request);
            var info = new LabelInfo(
                request.Name,
                request.ColorHex,
                request.IsGlobal,
                request.HideFromOtherFilters,
                request.NotifyOnChanges,
                request.RemoveOnChanges,
                request.AlertOnInstall,
                request.RemoveOnInstall,
                request.HoldVersion,
                request.IgnoreMissingFiles,
                0);
            LabelsList.Add(info);
            return Task.FromResult(OperationResult<LabelInfo>.Ok(info));
        }

        public Task<OperationResult<LabelInfo>> UpdateLabelAsync(LabelEditRequest request, CancellationToken ct = default)
        {
            UpdatedRequests.Add(request);
            var index = LabelsList.FindIndex(l => l.Name.Equals(request.Name, StringComparison.OrdinalIgnoreCase));
            var info = new LabelInfo(
                request.Name,
                request.ColorHex,
                request.IsGlobal,
                request.HideFromOtherFilters,
                request.NotifyOnChanges,
                request.RemoveOnChanges,
                request.AlertOnInstall,
                request.RemoveOnInstall,
                request.HoldVersion,
                request.IgnoreMissingFiles,
                index >= 0 ? LabelsList[index].ModuleCount : 0);
            if (index >= 0)
            {
                LabelsList[index] = info;
            }
            return Task.FromResult(OperationResult<LabelInfo>.Ok(info));
        }

        public Task<OperationResult<bool>> DeleteLabelAsync(string labelName, CancellationToken ct = default)
        {
            DeletedLabels.Add(labelName);
            LabelsList.RemoveAll(l => l.Name.Equals(labelName, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(OperationResult<bool>.Ok(true));
        }
    }
}
