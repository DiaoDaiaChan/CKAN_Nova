using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CkanUI.Services.Saves;
using CkanUI.TestSupport;
using CkanUI.ViewModels.Pages.Saves;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

/// <summary>
/// 工作台端到端：不接 Fake，而是让 ViewModel 直接驱动真实的 SaveEditorService 改一份临时存档。
/// 覆盖用户实际路径——改生涯资金、展开飞行器、展开部件、改部件字段，并验证写回只动目标行。
/// </summary>
[TestFixture]
public sealed class SfsWorkbenchIntegrationTests
{
    [Test]
    public async Task Workbench_EditsCareerFundsAndVesselPart()
    {
        using var workspace = new TempWorkspace("sfs-workbench-e2e");
        var path = workspace.Combine("persistent.sfs");
        await File.WriteAllTextAsync(path, SaveText);
        var originalLines = File.ReadAllLines(path);

        var viewModel = new SfsWorkbenchViewModel(path, new SaveEditorService());
        await viewModel.InitializeAsync();

        Assert.That(viewModel.Highlights.Any(field => field.Key == "funds"), Is.True, "生涯资金要进常用内容");
        var funding = viewModel.Sections.First(section => section.Dto.Fields.Any(field => field.Key == "funds"));
        viewModel.SelectedSection = funding;
        var funds = viewModel.Fields.First(field => field.Key == "funds");
        viewModel.SelectedField = funds;
        viewModel.EditValue = "16000.5";
        await viewModel.ApplyValueCommand.ExecuteAsync(null);

        Assert.That(viewModel.ErrorText, Is.Empty, viewModel.ErrorText);
        Assert.That(File.ReadAllText(path), Does.Contain("funds = 16000.5"));
        Assert.That(File.ReadAllLines(path).Length, Is.EqualTo(originalLines.Length), "改值不能改变行数");

        await viewModel.InitializeAsync();
        var flight = viewModel.Sections.First(section =>
            section.Dto.Subtitle == "FLIGHTSTATE" || section.Dto.Title.Contains("飞行器", StringComparison.Ordinal));
        viewModel.SelectedSection = flight;
        var vessel = viewModel.Nodes.First(node => node.Name == "VESSEL");
        viewModel.SelectedNode = vessel;
        await viewModel.ExpandNodeCommand.ExecuteAsync(null);

        var part = viewModel.Nodes.First(node => node.Name == "PART");
        viewModel.SelectedNode = part;
        await viewModel.ExpandNodeCommand.ExecuteAsync(null);

        var partName = viewModel.Fields.First(field => field.Key == "name");
        viewModel.SelectedField = partName;
        viewModel.EditValue = "mk1pod.v2";
        await viewModel.ApplyValueCommand.ExecuteAsync(null);

        Assert.That(viewModel.ErrorText, Is.Empty, viewModel.ErrorText);
        Assert.That(File.ReadAllText(path), Does.Contain("name = mk1pod.v2"), "部件字段要能真正写回");
        Assert.That(Directory.GetFiles(workspace.Path, "*.pre-edit-*"), Is.Not.Empty, "每次写回都要留快照");
        Assert.That(Directory.GetFiles(workspace.Path, "*.patch-*"), Is.Empty, "临时补丁文件不能残留");

        viewModel.SearchText = "mk1pod.v2";
        await viewModel.SearchCommand.ExecuteAsync(null);
        var partMatch = viewModel.Matches.First(match => match.Line == partName.Line);
        await viewModel.OpenMatchCommand.ExecuteAsync(partMatch);
        Assert.That(viewModel.SelectedField?.Key, Is.EqualTo("name"), "搜索命中要能自动展开到部件字段");
        Assert.That(viewModel.EditValue, Is.EqualTo("mk1pod.v2"));
    }

    [Test]
    public async Task LargeFileSearch_RevealsFundsAndNestedPart()
    {
        using var workspace = new TempWorkspace("sfs-workbench-large-e2e");
        var path = workspace.Combine("persistent.sfs");
        await File.WriteAllTextAsync(path, SaveText);

        var viewModel = new SfsWorkbenchViewModel(path, new SaveEditorService(fullParseLimitBytes: 100));
        await viewModel.InitializeAsync();
        Assert.That(viewModel.IsLargeFileMode, Is.True);

        viewModel.SearchText = "资金";
        await viewModel.SearchCommand.ExecuteAsync(null);
        var fundsMatch = viewModel.Matches.First(match => match.Key == "funds");
        await viewModel.OpenMatchCommand.ExecuteAsync(fundsMatch);
        Assert.That(viewModel.SelectedField?.Key, Is.EqualTo("funds"), "中文标签命中要跳到资金字段");
        Assert.That(viewModel.EditValue, Is.EqualTo("15000"));

        viewModel.SearchText = "mk1pod";
        await viewModel.SearchCommand.ExecuteAsync(null);
        var partMatch = viewModel.Matches.FirstOrDefault(match =>
            match.DisplayValue.Contains("mk1pod", StringComparison.Ordinal)
            || match.HighlightHit.Contains("mk1pod", StringComparison.Ordinal));
        Assert.That(partMatch, Is.Not.Null, string.Join(" | ", viewModel.Matches.Select(match => $"{match.Line}:{match.Key}:{match.DisplayValue}:{match.HighlightHit}")));
        await viewModel.OpenMatchCommand.ExecuteAsync(partMatch!);
        Assert.That(viewModel.SelectedField?.Key, Is.EqualTo("name"), "大文件模式也要能展开到 PART 字段");
        Assert.That(viewModel.EditValue, Is.EqualTo("mk1pod"));
    }

    [Test]
    public async Task Search_RevealsNestedFieldInLaterSection()
    {
        using var workspace = new TempWorkspace("sfs-workbench-nested-e2e");
        var path = workspace.Combine("persistent.sfs");
        await File.WriteAllTextAsync(path, NestedSaveText);

        var viewModel = new SfsWorkbenchViewModel(path, new SaveEditorService());
        await viewModel.InitializeAsync();
        viewModel.SearchText = "nestedFoo";
        await viewModel.SearchCommand.ExecuteAsync(null);

        var match = viewModel.Matches.First(candidate => candidate.Match == "原文" && candidate.DisplayValue.Contains("nestedFoo", StringComparison.Ordinal));
        await viewModel.OpenMatchCommand.ExecuteAsync(match);

        Assert.That(viewModel.SelectedField?.Key, Is.EqualTo("name"), "后面的场景分区不能被前面的飞行器分区截胡");
        Assert.That(viewModel.EditValue, Is.EqualTo("nestedFoo"));
        Assert.That(viewModel.BreadcrumbText, Does.Contain("MODULE"));
    }

    private const string NestedSaveText = """
GAME
{
	version = 1.12.5
	mode = CAREER
	FLIGHTSTATE
	{
		VESSEL
		{
			name = 一号船
			PART
			{
				name = mk1pod
			}
		}
	}
	SCENARIO
	{
		name = Other
		MODULE
		{
			name = nestedFoo
			value = 3
		}
	}
}
""";

    private const string SaveText = """
GAME
{
	version = 1.12.5
	mode = CAREER
	SCENARIO
	{
		name = Funding
		funds = 15000
	}
	FLIGHTSTATE
	{
		VESSEL
		{
			name = 一号船
			PART
			{
				name = mk1pod
				amount = 1
			}
		}
	}
}
""";
}