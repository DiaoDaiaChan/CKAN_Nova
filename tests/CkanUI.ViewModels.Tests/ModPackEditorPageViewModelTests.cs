using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Pages;
using CkanUI.ViewModels.Pages.ModPacks;

using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

/// <summary>
/// <see cref="ModPackEditorPageViewModel"/> 的离线用例（06 篇 UI-24、01 篇 F-46）：
/// 表单↔DTO 投影、加载回填、保存前校验闸门、脏标记、关系行的增删与模式切换、服务异常落到错误栏。
/// 全部走 <see cref="ModPackEditorFakeService"/>，不碰真实文件系统。
/// </summary>
[TestFixture]
public sealed class ModPackEditorPageViewModelTests
{
    private ModPackEditorFakeService _service = null!;
    private ModPackEditorPageViewModel _viewModel = null!;

    [SetUp]
    public void SetUp()
    {
        _service = new ModPackEditorFakeService();
        _viewModel = new ModPackEditorPageViewModel(_service);
    }

    private static ModPackEditorDocument SampleDocument() => new()
    {
        FilePath = "C:/tmp/sample.ckan",
        Identifier = "sample-pack",
        Name = "示例整合包",
        Abstract = "摘要",
        Version = "1.2.3",
        Licenses = new[] { "MIT" },
        Authors = new[] { "Tester" },
        Kind = "metapackage",
        ReleaseStatus = "stable",
        Relations = new[]
        {
            new ModPackRelation
            {
                RelationType = ModPackRelationTypes.Depends,
                Mode = ModPackRelationModes.Single,
                Name = "modulemanager",
            },
        },
    };

    // ---------------- 构造 / 默认状态 ----------------

    [Test]
    public void Constructor_StartsBlankAndClean()
    {
        Assert.That(_viewModel.Title, Is.EqualTo(ModPackEditorText.PageTitle));
        Assert.That(_viewModel.Identifier, Is.Empty);
        Assert.That(_viewModel.Kind, Is.EqualTo("metapackage"));
        Assert.That(_viewModel.ReleaseStatus, Is.EqualTo("stable"));
        Assert.That(_viewModel.IsDirty, Is.False);
        Assert.That(_viewModel.HasError, Is.False);
        Assert.That(_viewModel.ShowEmptyRelations, Is.True);
    }

    [Test]
    public async Task ParameterlessConstructor_ServiceUnavailable_OperationsFailLoudly()
    {
        var viewModel = new ModPackEditorPageViewModel();

        Assert.That(viewModel.Title, Is.EqualTo(ModPackEditorText.PageTitle));
        viewModel.FilePath = "C:/tmp/x.ckan";
        await viewModel.SaveCommand.ExecuteAsync(null);
        Assert.That(viewModel.ErrorText, Does.Contain("不可用"));
    }

    // ---------------- 表单 → DTO ----------------

    [Test]
    public void ToDocument_ProjectsTrimmedFormValues()
    {
        _viewModel.FilePath = "  C:/tmp/x.ckan  ";
        _viewModel.Identifier = "  my-pack  ";
        _viewModel.Name = " 我的包 ";
        _viewModel.Version = " 1.0.0 ";
        _viewModel.NewLicense = "MIT";
        _viewModel.AddLicenseCommand.Execute(null);
        _viewModel.NewAuthor = "Tester";
        _viewModel.AddAuthorCommand.Execute(null);

        var document = _viewModel.ToDocument();

        Assert.That(document.FilePath, Is.EqualTo("C:/tmp/x.ckan"));
        Assert.That(document.Identifier, Is.EqualTo("my-pack"));
        Assert.That(document.Name, Is.EqualTo("我的包"));
        Assert.That(document.Version, Is.EqualTo("1.0.0"));
        Assert.That(document.Licenses, Is.EqualTo(new[] { "MIT" }));
        Assert.That(document.Authors, Is.EqualTo(new[] { "Tester" }));
    }

    [Test]
    public void ToDocument_MapsRelationRowsIntoRelations()
    {
        _viewModel.AddRelationCommand.Execute(null);
        var row = _viewModel.Relations.Single();
        row.RelationType = ModPackRelationTypes.Recommends;
        row.Mode = ModPackRelationModes.Single;
        row.Name = "scatterer";

        var relation = _viewModel.ToDocument().Relations.Single();

        Assert.That(relation.RelationType, Is.EqualTo(ModPackRelationTypes.Recommends));
        Assert.That(relation.Name, Is.EqualTo("scatterer"));
    }

    // ---------------- 加载 ----------------

    [Test]
    public async Task LoadAsync_FillsFormFromService()
    {
        _viewModel.FilePath = "C:/tmp/sample.ckan";
        _service.LoadResult = OperationResult<ModPackEditorDocument>.Ok(SampleDocument());

        await _viewModel.LoadCommand.ExecuteAsync(null);

        Assert.That(_viewModel.Identifier, Is.EqualTo("sample-pack"));
        Assert.That(_viewModel.Name, Is.EqualTo("示例整合包"));
        Assert.That(_viewModel.Version, Is.EqualTo("1.2.3"));
        Assert.That(_viewModel.Authors.Count, Is.EqualTo(1));
        Assert.That(_viewModel.Licenses.Count, Is.EqualTo(1));
        Assert.That(_viewModel.Relations.Count, Is.EqualTo(1));
        Assert.That(_viewModel.Relations[0].Name, Is.EqualTo("modulemanager"));
        Assert.That(_viewModel.IsDirty, Is.False, "加载不算未保存改动");
        Assert.That(_viewModel.StatusText, Does.Contain("已读取"));
        Assert.That(_service.LoadCalls, Is.EqualTo(1));
    }

    [Test]
    public async Task LoadAsync_Failure_ShowsErrorAndKeepsForm()
    {
        _viewModel.FilePath = "C:/tmp/sample.ckan";
        _viewModel.Identifier = "keep-me";
        _service.LoadResult = OperationResult<ModPackEditorDocument>.Fail("找不到文件。");

        await _viewModel.LoadCommand.ExecuteAsync(null);

        Assert.That(_viewModel.HasError, Is.True);
        Assert.That(_viewModel.ErrorText, Does.Contain("找不到文件"));
        Assert.That(_viewModel.Identifier, Is.EqualTo("keep-me"));
    }

    [Test]
    public async Task LoadAsync_AppliesValidationIssuesToIssueList()
    {
        _viewModel.FilePath = "C:/tmp/sample.ckan";
        _service.LoadResult = OperationResult<ModPackEditorDocument>.Ok(SampleDocument());
        _service.ValidateResult = new[]
        {
            new ModPackEditorIssue { Code = "name.empty", Field = "Name", Message = "显示名不能为空。" },
        };

        await _viewModel.LoadCommand.ExecuteAsync(null);

        Assert.That(_viewModel.Issues.Count, Is.EqualTo(1));
        Assert.That(_viewModel.HasIssues, Is.True);
        Assert.That(_viewModel.IssueSummary, Is.EqualTo("共 1 条问题"));
    }

    // ---------------- 保存闸门 ----------------

    [Test]
    public async Task SaveAsync_WithErrors_RefusesAndListsReasonsInChinese()
    {
        _viewModel.Identifier = "bad-ident";
        _service.ValidateResult = new[]
        {
            new ModPackEditorIssue
            {
                Code = "identifier.lowercase",
                Field = "Identifier",
                Message = "模组标识必须全部小写。",
                Severity = ModPackEditorIssueSeverity.Error,
            },
        };

        await _viewModel.SaveCommand.ExecuteAsync(null);

        Assert.That(_service.SaveCalls, Is.Zero, "有错误时不许调用写盘");
        Assert.That(_viewModel.ErrorText, Does.Contain("校验未通过"));
        Assert.That(_viewModel.Issues.Single(), Does.Contain("模组标识必须全部小写"));
        Assert.That(_viewModel.IsDirty, Is.True);
    }

    [Test]
    public async Task SaveAsync_Success_UpdatesPathAndClearsDirty()
    {
        _viewModel.Identifier = "my-pack";
        _viewModel.Version = "1.0.0";
        _service.SaveResult = OperationResult<string>.Ok("C:/tmp/my-pack.ckan");

        await _viewModel.SaveCommand.ExecuteAsync(null);

        Assert.That(_service.SaveCalls, Is.EqualTo(1));
        Assert.That(_viewModel.FilePath, Is.EqualTo("C:/tmp/my-pack.ckan"));
        Assert.That(_viewModel.IsDirty, Is.False);
        Assert.That(_viewModel.StatusText, Does.Contain("已保存"));
        Assert.That(_viewModel.HasError, Is.False);
    }

    [Test]
    public async Task SaveAsync_ServiceFailure_ShowsError()
    {
        _viewModel.Name = "改过的名字";
        _service.SaveResult = OperationResult<string>.Fail("写入失败。", "磁盘只读");

        await _viewModel.SaveCommand.ExecuteAsync(null);

        Assert.That(_viewModel.ErrorText, Does.Contain("写入失败"));
        Assert.That(_viewModel.ErrorText, Does.Contain("磁盘只读"));
        Assert.That(_viewModel.IsDirty, Is.True);
    }

    [Test]
    public async Task SaveAsync_ServiceThrows_LandsInErrorBar()
    {
        _service.SaveThrows = new InvalidOperationException("炸了");

        await _viewModel.SaveCommand.ExecuteAsync(null);

        Assert.That(_viewModel.HasError, Is.True);
        Assert.That(_viewModel.ErrorText, Does.Contain("炸了"));
        Assert.That(_viewModel.IsBusy, Is.False);
    }

    // ---------------- 校验 ----------------

    [Test]
    public void Validate_WithNoErrors_SaysCanSave()
    {
        _service.ValidateResult = System.Array.Empty<ModPackEditorIssue>();

        _viewModel.ValidateCommand.Execute(null);

        Assert.That(_viewModel.StatusText, Does.Contain("可以保存"));
        Assert.That(_viewModel.HasError, Is.False);
    }

    [Test]
    public void Validate_WithErrors_CountsThem()
    {
        _service.ValidateResult = new[]
        {
            new ModPackEditorIssue { Code = "name.empty", Field = "Name", Message = "显示名不能为空。" },
            new ModPackEditorIssue { Code = "version.empty", Field = "Version", Message = "版本号不能为空。" },
        };

        _viewModel.ValidateCommand.Execute(null);

        Assert.That(_viewModel.StatusText, Does.Contain("2 个错误"));
        Assert.That(_viewModel.Issues.Count, Is.EqualTo(2));
    }

    // ---------------- 脏标记 ----------------

    [Test]
    public void EditingFormField_MarksDirty()
    {
        Assert.That(_viewModel.IsDirty, Is.False);

        _viewModel.Name = "改了名字";

        Assert.That(_viewModel.IsDirty, Is.True);
    }

    [Test]
    public void ApplyDocument_DoesNotMarkDirty()
    {
        _viewModel.ApplyDocument(SampleDocument());

        Assert.That(_viewModel.Identifier, Is.EqualTo("sample-pack"));
        Assert.That(_viewModel.IsDirty, Is.False);
    }

    [Test]
    public void StatusAndErrorText_DoNotMarkDirty()
    {
        _viewModel.ApplyIssues(new[]
        {
            new ModPackEditorIssue { Code = "name.empty", Field = "Name", Message = "显示名不能为空。" },
        });

        Assert.That(_viewModel.IsDirty, Is.False);
    }

    [Test]
    public void NewDocument_ResetsFormAndDirtyFlag()
    {
        _viewModel.Identifier = "old-pack";
        _viewModel.Name = "旧名字";

        _viewModel.NewDocumentCommand.Execute(null);

        Assert.That(_viewModel.Identifier, Is.Empty);
        Assert.That(_viewModel.Name, Is.Empty);
        Assert.That(_viewModel.Version, Is.EqualTo("1.0.0"));
        Assert.That(_viewModel.Kind, Is.EqualTo("metapackage"));
        Assert.That(_viewModel.IsDirty, Is.False);
        Assert.That(_viewModel.Relations, Is.Empty);
    }

    // ---------------- 关系行 ----------------

    [Test]
    public void AddRelation_AppendsEmptyRowAndHidesEmptyState()
    {
        _viewModel.AddRelationCommand.Execute(null);

        Assert.That(_viewModel.Relations.Count, Is.EqualTo(1));
        Assert.That(_viewModel.ShowEmptyRelations, Is.False);
        Assert.That(_viewModel.Relations[0].RelationType, Is.EqualTo(ModPackRelationTypes.Depends));
        Assert.That(_viewModel.Relations[0].Mode, Is.EqualTo(ModPackRelationModes.Single));
    }

    [Test]
    public void RemoveRelation_DropsTheRow()
    {
        _viewModel.AddRelationCommand.Execute(null);
        var row = _viewModel.Relations[0];

        _viewModel.RemoveRelationCommand.Execute(row);

        Assert.That(_viewModel.Relations, Is.Empty);
        Assert.That(_viewModel.ShowEmptyRelations, Is.True);
    }

    [Test]
    public void RelationRow_SwitchingToAnyOfRevealsCandidateInput()
    {
        var row = new ModPackRelationRowViewModel(new ModPackRelation
        {
            RelationType = ModPackRelationTypes.Depends,
            Mode = ModPackRelationModes.Single,
            Name = "modulemanager",
        });

        Assert.That(row.IsSingleMode, Is.True);
        Assert.That(row.IsCandidateMode, Is.False);

        row.Mode = ModPackRelationModes.AnyOf;

        Assert.That(row.IsSingleMode, Is.False);
        Assert.That(row.IsCandidateMode, Is.True);
        Assert.That(row.TargetText, Is.EqualTo("（未填写）"));
        Assert.That(row.VersionText, Is.EqualTo("—"));
    }

    [Test]
    public void RelationRow_ToRelation_BlankCandidatesInSingleMode()
    {
        var row = new ModPackRelationRowViewModel(new ModPackRelation
        {
            RelationType = ModPackRelationTypes.Depends,
            Mode = ModPackRelationModes.Single,
            Name = " modulemanager ",
            MinVersion = "5.0",
            Candidates = new[] { "ignored" },
        });

        var relation = row.ToRelation();

        Assert.That(relation.Name, Is.EqualTo("modulemanager"));
        Assert.That(relation.MinVersion, Is.EqualTo("5.0"));
        Assert.That(relation.Candidates, Is.Empty);
    }

    [Test]
    public void RelationRow_CandidatesTextSplitsOnSeparators()
    {
        var row = ModPackRelationRowViewModel.CreateEmpty();
        row.Mode = ModPackRelationModes.AnyOf;
        row.CandidatesText = "scatterer, ksp-community-fixes；another-one";

        var relation = row.ToRelation();

        Assert.That(relation.Candidates, Is.EqualTo(new[] { "scatterer", "ksp-community-fixes", "another-one" }));
        Assert.That(relation.Name, Is.Empty);
    }

    [Test]
    public void RelationRow_VersionText_DescribesRange()
    {
        var row = ModPackRelationRowViewModel.CreateEmpty();
        Assert.That(row.VersionText, Is.EqualTo("任意"));

        row.MinVersion = "5.0";
        Assert.That(row.VersionText, Does.Contain("5.0"));

        row.MaxVersion = "6.0";
        Assert.That(row.VersionText, Does.Contain("5.0").And.Contain("6.0"));
    }

    // ---------------- 问题描述 ----------------

    // ---------------- 出错字段（第三阶段 MAT-04：供视图标红与冲量轻颤） ----------------

    [Test]
    public void ApplyIssues_PublishesErrorFieldsOnly()
    {
        _viewModel.ApplyIssues(new[]
        {
            new ModPackEditorIssue
            {
                Code = "identifier.empty",
                Field = "Identifier",
                Message = "模组标识不能为空。",
                Severity = ModPackEditorIssueSeverity.Error,
            },
            new ModPackEditorIssue
            {
                Code = "identifier.charset",
                Field = "Identifier",
                Message = "模组标识只能包含小写字母。",
                Severity = ModPackEditorIssueSeverity.Error,
            },
            new ModPackEditorIssue
            {
                Code = "author.empty",
                Field = "Authors",
                Message = "没有填写作者。",
                Severity = ModPackEditorIssueSeverity.Warning,
            },
            new ModPackEditorIssue
            {
                Code = "document.invalid",
                Field = string.Empty,
                Message = "整文档级问题。",
                Severity = ModPackEditorIssueSeverity.Error,
            },
        });

        Assert.That(
            _viewModel.InvalidFields,
            Is.EqualTo(new[] { "Identifier" }),
            "只收错误级、同字段去重、整文档级（空字段）剔掉");
        Assert.That(_viewModel.Issues.Count, Is.EqualTo(4), "问题列表仍然逐条展示，不受过滤影响");
    }

    [Test]
    public void ApplyIssues_RaisesInvalidFieldsNotification()
    {
        var changed = new List<string?>();
        _viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        _viewModel.ApplyIssues(new[]
        {
            new ModPackEditorIssue { Field = "Name", Message = "显示名不能为空。", Severity = ModPackEditorIssueSeverity.Error },
        });

        Assert.That(
            changed,
            Does.Contain(nameof(ModPackEditorPageViewModel.InvalidFields)),
            "视图靠这条通知触发标红与轻颤");
    }

    [Test]
    public void ApplyIssues_DoesNotDirtyTheForm()
    {
        _viewModel.ApplyIssues(new[]
        {
            new ModPackEditorIssue { Field = "Name", Message = "显示名不能为空。", Severity = ModPackEditorIssueSeverity.Error },
        });

        Assert.That(_viewModel.IsDirty, Is.False, "校验反馈是只读副作用，不能把表单标脏");
    }

    [Test]
    public void ApplyIssues_EmptyList_ClearsInvalidFields()
    {
        _viewModel.ApplyIssues(new[]
        {
            new ModPackEditorIssue { Field = "Name", Message = "显示名不能为空。", Severity = ModPackEditorIssueSeverity.Error },
        });

        _viewModel.ApplyIssues(Array.Empty<ModPackEditorIssue>());

        Assert.That(_viewModel.InvalidFields, Is.Empty, "校验通过后必须把红色描边清掉");
    }

    [Test]
    public void ValidateCommand_PublishesInvalidFieldsFromService()
    {
        _service.ValidateResult = new[]
        {
            new ModPackEditorIssue { Field = "Version", Message = "版本号不能为空。", Severity = ModPackEditorIssueSeverity.Error },
        };

        _viewModel.ValidateCommand.Execute(null);

        Assert.That(_viewModel.InvalidFields, Is.EqualTo(new[] { "Version" }));
    }

    [Test]
    public void DescribeIssue_MentionsRowNumberForRelationIssues()
    {
        var text = ModPackEditorText.DescribeIssue(new ModPackEditorIssue
        {
            Code = "relation.name.empty",
            Field = "Name",
            Message = "没有填写模组标识。",
            RowIndex = 2,
        });

        Assert.That(text, Does.Contain("第 3 行"));
        Assert.That(text, Does.Contain("错误"));
    }
}

/// <summary>编辑器页面的服务替身：只记调用次数并回放预设结果。</summary>
internal sealed class ModPackEditorFakeService : IModPackService
{
    public OperationResult<ModPackEditorDocument> LoadResult { get; set; }
        = OperationResult<ModPackEditorDocument>.Fail("没有预设结果。");

    public OperationResult<string> SaveResult { get; set; }
        = OperationResult<string>.Ok("C:/tmp/out.ckan");

    public IReadOnlyList<ModPackEditorIssue> ValidateResult { get; set; } = Array.Empty<ModPackEditorIssue>();

    public Exception? SaveThrows { get; set; }

    public Exception? LoadThrows { get; set; }

    public int LoadCalls { get; private set; }

    public int SaveCalls { get; private set; }

    public int ValidateCalls { get; private set; }

    public ModPackEditorDocument? LastSaved { get; private set; }

    public Task<OperationResult<ModPackEditorDocument>> LoadEditorAsync(string path, CancellationToken ct = default)
    {
        LoadCalls++;
        if (LoadThrows is not null)
        {
            return Task.FromException<OperationResult<ModPackEditorDocument>>(LoadThrows);
        }

        return Task.FromResult(LoadResult);
    }

    public Task<OperationResult<string>> SaveEditorAsync(ModPackEditorDocument document, CancellationToken ct = default)
    {
        SaveCalls++;
        LastSaved = document;
        if (SaveThrows is not null)
        {
            return Task.FromException<OperationResult<string>>(SaveThrows);
        }

        return Task.FromResult(SaveResult);
    }

    public IReadOnlyList<ModPackEditorIssue> ValidateEditor(ModPackEditorDocument document)
    {
        ValidateCalls++;
        return ValidateResult;
    }
}
