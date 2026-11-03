using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using NUnit.Framework;

namespace CkanUI.Plugin.Saves.Tests;

/// <summary>
/// SFS 工作台的命令编排：分区切换、搜索视图、改值/删除的“先上膛再确认”、改完必须重载（行号会位移）。
/// 服务用假实现，只盯 VM 的行为契约。
/// </summary>
[TestFixture]
public sealed class SfsWorkbenchViewModelTests
{
    [Test]
    public async Task Initialize_LoadsSectionsAndSelectsFirst()
    {
        var service = new FakeSaveEditorService();
        var vm = new SfsWorkbenchViewModel(@"C:\saves\persistent.sfs", service);

        await vm.InitializeAsync();

        Assert.That(vm.Title, Is.EqualTo("我的生涯"));
        Assert.That(vm.Sections, Is.Not.Empty);
        Assert.That(vm.SelectedSection, Is.Not.Null);
        Assert.That(vm.Fields.Count, Is.GreaterThan(0));
        Assert.That(vm.SummaryText, Does.Contain("CAREER"));
        Assert.That(vm.HasHighlights, Is.True);
        Assert.That(vm.HasError, Is.False);
    }

    [Test]
    public async Task SelectingSection_SwapsFieldsAndNodes()
    {
        var service = new FakeSaveEditorService();
        var vm = new SfsWorkbenchViewModel(@"C:\saves\persistent.sfs", service);
        await vm.InitializeAsync();

        var game = vm.Sections[0];
        var funding = vm.Sections[1];

        vm.SelectedSection = funding;
        Assert.Multiple(() =>
        {
            Assert.That(vm.Fields.Select(field => field.Key), Is.EquivalentTo(new[] { "name", "sci" }));
            Assert.That(vm.Nodes, Is.Empty);
            Assert.That(vm.StatusText, Does.Contain("资金"));
        });

        vm.SelectedSection = game;
        Assert.Multiple(() =>
        {
            Assert.That(vm.Fields.Select(field => field.Key), Is.EquivalentTo(new[] { "funds" }));
            Assert.That(vm.Nodes.Select(node => node.Name), Is.EquivalentTo(new[] { "VESSEL" }));
        });
    }

    [Test]
    public async Task ApplyValue_WritesAndReloads()
    {
        var service = new FakeSaveEditorService();
        var vm = new SfsWorkbenchViewModel(@"C:\saves\persistent.sfs", service);
        await vm.InitializeAsync();
        vm.SelectedField = vm.Fields.First(f => f.Key == "funds");
        vm.EditValue = "777";

        await vm.ApplyValueCommand.ExecuteAsync(null);

        Assert.That(service.Edits, Has.Count.EqualTo(1));
        Assert.That(service.Edits[0].Kind, Is.EqualTo(SaveEditKind.SetValue));
        Assert.That(service.Edits[0].Value, Is.EqualTo("777"));
        Assert.That(service.Edits[0].Write, Is.True);
        Assert.That(service.Loads, Is.EqualTo(2), "改完必须重新解析，否则后续行号全错");
        Assert.That(vm.ErrorText, Is.Empty);
    }

    [Test]
    public async Task ApplyValue_WithSameValue_DoesNotCallService()
    {
        var service = new FakeSaveEditorService();
        var vm = new SfsWorkbenchViewModel(@"C:\saves\persistent.sfs", service);
        await vm.InitializeAsync();
        vm.SelectedField = vm.Fields.First(f => f.Key == "funds");
        vm.EditValue = vm.SelectedField.Value;

        await vm.ApplyValueCommand.ExecuteAsync(null);

        Assert.That(service.Edits, Is.Empty);
        Assert.That(vm.StatusText, Does.Contain("没有变化"));
    }

    [Test]
    public async Task RenameKey_SendsNewKeyAndReloads()
    {
        var service = new FakeSaveEditorService();
        var vm = new SfsWorkbenchViewModel(@"C:\saves\persistent.sfs", service);
        await vm.InitializeAsync();
        vm.SelectedField = vm.Fields.First(f => f.Key == "funds");
        vm.KeyEdit = "fundsFixed";

        await vm.RenameKeyCommand.ExecuteAsync(null);

        Assert.That(service.Edits, Has.Count.EqualTo(1));
        Assert.That(service.Edits[0].Kind, Is.EqualTo(SaveEditKind.RenameKey));
        Assert.That(service.Edits[0].Key, Is.EqualTo("fundsFixed"));
        Assert.That(service.Loads, Is.EqualTo(2));
    }

    [Test]
    public async Task RenameKey_WithUnchangedKey_DoesNotCallService()
    {
        var service = new FakeSaveEditorService();
        var vm = new SfsWorkbenchViewModel(@"C:\saves\persistent.sfs", service);
        await vm.InitializeAsync();
        vm.SelectedField = vm.Fields.First(f => f.Key == "funds");
        vm.KeyEdit = "funds";

        await vm.RenameKeyCommand.ExecuteAsync(null);

        Assert.That(service.Edits, Is.Empty);
        Assert.That(vm.StatusText, Does.Contain("键名没有变化"));
    }

    [Test]
    public async Task Delete_RequiresConfirmationBeforeTouchingService()
    {
        var service = new FakeSaveEditorService();
        var vm = new SfsWorkbenchViewModel(@"C:\saves\persistent.sfs", service);
        await vm.InitializeAsync();
        vm.SelectedSection = vm.Sections.First(s => s.Dto.Nodes.Count > 0);
        vm.SelectedNode = vm.SelectedSection.Dto.Nodes[0];

        vm.ArmDeleteCommand.Execute(null);
        Assert.That(vm.HasPendingDelete, Is.True);
        Assert.That(service.Edits, Is.Empty);

        await vm.ConfirmDeleteCommand.ExecuteAsync(null);
        Assert.That(service.Edits, Has.Count.EqualTo(1));
        Assert.That(service.Edits[0].Kind, Is.EqualTo(SaveEditKind.DeleteElement));
        Assert.That(vm.HasPendingDelete, Is.False);
    }

    [Test]
    public async Task LargeFileMode_DisablesWholeBlockOperations()
    {
        var service = new FakeSaveEditorService { LargeFile = true };
        var vm = new SfsWorkbenchViewModel(@"C:\saves\persistent.sfs", service);
        await vm.InitializeAsync();
        vm.SelectedNode = vm.Sections[0].Dto.Nodes[0];

        Assert.That(vm.IsLargeFileMode, Is.True);
        Assert.That(vm.HasLargeFileNote, Is.True);
        Assert.That(vm.LargeFileNote, Does.Contain("流式"));
        Assert.That(vm.CanTouchNode, Is.False, "大文件模式不能整块复制/删除");
        Assert.That(vm.CanExpandNode, Is.True, "大文件模式仍允许按需展开");
        Assert.That(vm.HasOversizedBlocks, Is.True);
        Assert.That(vm.OversizedBlocksNote, Does.Contain("MODDATA"));

        vm.ArmDeleteCommand.Execute(null);
        Assert.That(vm.HasPendingDelete, Is.False);
        Assert.That(vm.StatusText, Does.Contain("大文件"));

        await vm.DuplicateNodeCommand.ExecuteAsync(null);
        Assert.That(service.Edits, Is.Empty);
        Assert.That(vm.StatusText, Does.Contain("大文件"));
    }

    [Test]
    public async Task NormalFile_AllowsWholeBlockOperations()
    {
        var service = new FakeSaveEditorService();
        var vm = new SfsWorkbenchViewModel(@"C:\saves\persistent.sfs", service);
        await vm.InitializeAsync();
        vm.SelectedNode = vm.Sections[0].Dto.Nodes[0];

        Assert.That(vm.IsLargeFileMode, Is.False);
        Assert.That(vm.HasLargeFileNote, Is.False);
        Assert.That(vm.CanTouchNode, Is.True);
    }

    [Test]
    public async Task Search_SetsResultViewAndClearReturns()
    {
        var service = new FakeSaveEditorService();
        var vm = new SfsWorkbenchViewModel(@"C:\saves\persistent.sfs", service);
        await vm.InitializeAsync();
        vm.SearchText = "资金";

        await vm.SearchCommand.ExecuteAsync(null);

        Assert.That(vm.IsSearchResult, Is.True);
        Assert.That(vm.Matches, Has.Count.EqualTo(1));
        Assert.That(vm.Matches[0].Key, Is.EqualTo("funds"));

        vm.ClearSearchCommand.Execute(null);
        Assert.That(vm.IsSearchResult, Is.False);
        Assert.That(vm.Fields, Is.Not.Empty);
    }

    [Test]
    public async Task SectionFilter_NarrowsTheListAndKeepsTheSelectionVisible()
    {
        var vm = new SfsWorkbenchViewModel(@"C:\saves\persistent.sfs", new FakeSaveEditorService());
        await vm.InitializeAsync();

        vm.SectionFilterText = "资金";

        Assert.Multiple(() =>
        {
            Assert.That(vm.VisibleSections.Select(section => section.Title), Is.EqualTo(new[] { "资金" }));
            Assert.That(vm.SelectedSection, Is.Not.Null);
            Assert.That(vm.SelectedSection!.Title, Is.EqualTo("资金"));
            Assert.That(vm.SectionCountText, Does.Contain("1 / 2"));
        });

        vm.SectionFilterText = string.Empty;
        Assert.That(vm.VisibleSections, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task FieldFilter_NarrowsRowsAndReportsHowManyAreShown()
    {
        var vm = new SfsWorkbenchViewModel(@"C:\saves\persistent.sfs", new FakeSaveEditorService());
        await vm.InitializeAsync();
        vm.SelectedSection = vm.Sections.First(section => section.Title == "资金");

        vm.FieldFilterText = "sci";

        Assert.Multiple(() =>
        {
            Assert.That(vm.VisibleFields.Select(field => field.Key), Is.EqualTo(new[] { "sci" }));
            Assert.That(vm.FieldCountText, Does.Contain("筛出 1 / 2"));
            Assert.That(vm.HasFieldFilter, Is.True);
        });

        vm.FieldFilterText = string.Empty;
        Assert.That(vm.VisibleFields, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task NodeFilter_NarrowsBlocksAndCanReachEmptyState()
    {
        var vm = new SfsWorkbenchViewModel(@"C:\saves\persistent.sfs", new FakeSaveEditorService());
        await vm.InitializeAsync();

        vm.NodeFilterText = "一号船";
        Assert.That(vm.VisibleNodes.Select(node => node.Name), Is.EqualTo(new[] { "VESSEL" }));

        vm.NodeFilterText = "不存在";
        Assert.Multiple(() =>
        {
            Assert.That(vm.VisibleNodes, Is.Empty);
            Assert.That(vm.NodeCountText, Does.Contain("筛出 0 / 1"));
        });
    }

    [Test]
    public async Task RevertEdit_RestoresTheFileValueWithoutTouchingTheService()
    {
        var service = new FakeSaveEditorService();
        var vm = new SfsWorkbenchViewModel(@"C:\saves\persistent.sfs", service);
        await vm.InitializeAsync();
        vm.SelectedField = vm.Fields.First(field => field.Key == "funds");

        vm.EditValue = "999";
        vm.KeyEdit = "fundsFixed";

        Assert.Multiple(() =>
        {
            Assert.That(vm.HasPendingEdit, Is.True);
            Assert.That(vm.CanApplyValue, Is.True);
            Assert.That(vm.CanRenameKey, Is.True);
        });

        vm.RevertEditCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(vm.EditValue, Is.EqualTo("1234.5"));
            Assert.That(vm.KeyEdit, Is.EqualTo("funds"));
            Assert.That(vm.HasPendingEdit, Is.False);
            Assert.That(vm.CanApplyValue, Is.False);
            Assert.That(service.Edits, Is.Empty);
        });
    }

    [Test]
    public void DuplicateTitles_GetOrdinalsAndScenarioSubtitleGetsAReadableLabel()
    {
        var first = new SfsSectionItem(new SaveOutlineSectionDto
        {
            Id = "scenario:3:AlarmClockScenario",
            Title = "模组数据",
            Subtitle = "SCENARIO",
        }, duplicateOrdinal: 1);
        var second = new SfsSectionItem(new SaveOutlineSectionDto
        {
            Id = "scenario:4:ResourceScenario",
            Title = "模组数据",
            Subtitle = "SCENARIO",
        }, duplicateOrdinal: 2);

        Assert.Multiple(() =>
        {
            Assert.That(first.DisplayTitle, Is.EqualTo("模组数据 #1"));
            Assert.That(second.DisplayTitle, Is.EqualTo("模组数据 #2"));
            Assert.That(first.DisplaySubtitle, Is.EqualTo("场景 #4"));
            Assert.That(second.DisplaySubtitle, Is.EqualTo("场景 #5"));
            Assert.That(first.HasSubtitle, Is.True);
        });
    }

    [Test]
    public async Task TruncatedSection_ShowsListedCountsAndPointsAtSearch()
    {
        var service = new FakeSaveEditorService { TruncateFunding = true };
        var vm = new SfsWorkbenchViewModel(@"C:\saves\persistent.sfs", service);
        await vm.InitializeAsync();
        vm.SelectedSection = vm.Sections.First(section => section.Title == "资金");

        Assert.Multiple(() =>
        {
            Assert.That(vm.IsCurrentSectionTruncated, Is.True);
            Assert.That(vm.SelectedSection!.BadgeText, Does.Contain("已列 2/9 键值"));
            Assert.That(vm.SectionRangeText, Does.Contain("2 / 9 键值"));
            Assert.That(vm.TruncationHint, Does.Contain("顶部搜索"));
        });
    }

    private sealed class FakeSaveEditorService : ISaveEditorService
    {
        private static readonly SaveOutlineFieldDto Funds = new()
        {
            Path = "GAME/SCENARIO",
            Key = "funds",
            Label = "资金",
            Value = "1234.5",
            DisplayValue = "√1234.5",
            Line = 9,
            IsKnown = true,
            IsHighlighted = true,
            Priority = 0,
        };

        public bool LargeFile { get; set; }

        public bool TruncateFunding { get; set; }

        public int Loads { get; private set; }

        public List<SaveEditRequest> Edits { get; } = [];

        public Task<OperationResult<SaveOutlineDto>> LoadOutlineAsync(string filePath, CancellationToken cancellationToken = default)
        {
            Loads++;
            var section = new SaveOutlineSectionDto
            {
                Id = "game",
                Title = "存档信息",
                Subtitle = "GAME",
                Fields = [Funds],
                Nodes =
                [
                    new SaveOutlineNodeDto
                    {
                        Path = "GAME/FLIGHTSTATE/VESSEL#0",
                        Name = "VESSEL",
                        Label = "飞行器",
                        Summary = "一号船",
                        Line = 14,
                        PairCount = 2,
                        NodeCount = 2,
                    },
                ],
            };

            var funding = new SaveOutlineSectionDto
            {
                Id = "scenario:0:Funding",
                Title = "资金",
                Subtitle = "Funding",
                Fields =
                [
                    new SaveOutlineFieldDto
                    {
                        Path = "GAME/SCENARIO",
                        Key = "name",
                        Label = "名称",
                        Value = "Funding",
                        DisplayValue = "Funding",
                        Line = 22,
                        IsKnown = true,
                    },
                    new SaveOutlineFieldDto
                    {
                        Path = "GAME/SCENARIO",
                        Key = "sci",
                        Label = "科技点",
                        Value = "42",
                        DisplayValue = "42",
                        Line = 23,
                        IsKnown = true,
                        IsHighlighted = true,
                        Priority = 1,
                    },
                ],
                Nodes = [],
                TotalPairCount = TruncateFunding ? 9 : 2,
                TotalNodeCount = 0,
                Truncated = TruncateFunding,
            };

            return Task.FromResult(OperationResult<SaveOutlineDto>.Ok(new SaveOutlineDto
            {
                FilePath = filePath,
                SaveName = "我的生涯",
                Version = "1.12.5",
                Mode = "CAREER",
                Funds = "√1234.5",
                Highlights = [Funds],
                Sections = [section, funding],
                LineCount = 24,
                PairCount = 4,
                NodeCount = 3,
                LargeFileMode = LargeFile,
                LargeFileNote = LargeFile ? "这份存档 120.0 MB，已改用流式模式。" : string.Empty,
                OversizedBlocks = LargeFile
                    ? [new SaveOutlineNodeDto { Name = "MODDATA", Line = 30, Path = "GAME/MODDATA" }]
                    : [],
            }));
        }

        public Task<OperationResult<IReadOnlyList<SaveOutlineMatchDto>>> SearchAsync(
            string filePath,
            string query,
            int limit = 200,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<SaveOutlineMatchDto> hits =
            [
                new SaveOutlineMatchDto
                {
                    Line = 9,
                    SectionTitle = "存档信息",
                    Path = "GAME/SCENARIO",
                    Key = "funds",
                    Label = "资金",
                    Value = "1234.5",
                    DisplayValue = "√1234.5",
                    Match = "键值",
                },
            ];
            return Task.FromResult(OperationResult<IReadOnlyList<SaveOutlineMatchDto>>.Ok(hits));
        }

        public Task<OperationResult<SaveNodeDetailDto>> DescribeNodeAsync(string filePath, int line, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult<SaveNodeDetailDto>.Fail("未实现"));

        public Task<OperationResult<SaveEditOutcomeDto>> EditAsync(SaveEditRequest request, CancellationToken cancellationToken = default)
        {
            Edits.Add(request);
            return Task.FromResult(OperationResult<SaveEditOutcomeDto>.Ok(new SaveEditOutcomeDto
            {
                Succeeded = true,
                Changed = true,
                Verified = true,
                Written = request.Write,
                Summary = "ok",
                BackupPath = @"C:\saves\persistent.pre-edit-20250101-000000.sfs",
            }));
        }
    }
}