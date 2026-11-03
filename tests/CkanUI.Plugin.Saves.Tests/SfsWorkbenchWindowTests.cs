using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using NUnit.Framework;

namespace CkanUI.Plugin.Saves.Tests;

/// <summary>
/// 工作台窗口的无头装载：XAML 能实例化、绑定能解析、页头四项常用内容按词表键名取值。
/// 这里不验像素，只验“窗口打开后用户真能看到资金/分区/键值/块操作”这条链路没断。
/// </summary>
[TestFixture]
public sealed class SfsWorkbenchWindowTests
{
    [Test]
    public async Task Window_RendersHeaderStatsAndSections()
    {
        HeadlessHost.EnsureInitialized();
        if (!HeadlessHost.CanCreateWindows)
        {
            Assert.Ignore("无头壳由全量套件里另一个测试程序集在别的线程建好，本线程没有窗口亲和性；" +
                "单跑 CkanUI.Plugin.Saves.Tests 即可验证本条。");
        }

        var viewModel = new SfsWorkbenchViewModel(@"C:\saves\persistent.sfs", new FakeSaveEditorService());
        await viewModel.InitializeAsync();
        viewModel.SelectedField = viewModel.Fields[0];

        var window = new SfsWorkbenchWindow { DataContext = viewModel };
        window.Show();
        HeadlessHost.PumpJobs();

        try
        {
            var texts = window.GetVisualDescendants().OfType<TextBlock>()
                .Select(block => block.Text ?? string.Empty)
                .ToList();

            Assert.That(window.Title, Is.EqualTo("我的生涯"));
            Assert.That(texts, Does.Contain("√1234.5"), "页头资金要显示出来");
            Assert.That(texts, Does.Contain("42"), "页头科技点要显示出来");
            Assert.That(texts, Does.Contain("—"), "沙盒/缺字段时显示破折号，不能是空框");
            Assert.That(texts.Any(text => text.Contains("存档信息", StringComparison.Ordinal)), Is.True);
            Assert.That(texts.Any(text => text.Contains("资金", StringComparison.Ordinal)), Is.True);
            Assert.That(texts, Does.Contain("展开块"));
            Assert.That(texts, Does.Contain("双击块也能展开"));
            Assert.That(texts, Does.Contain("复制块"));
            Assert.That(texts, Does.Contain("删除块"));
            Assert.That(texts, Does.Contain("还原"));

            var filters = window.GetVisualDescendants().OfType<TextBox>()
                .Select(box => box.PlaceholderText ?? string.Empty)
                .ToList();
            Assert.That(filters.Any(text => text.Contains("筛选分区", StringComparison.Ordinal)), Is.True);
            Assert.That(filters.Any(text => text.Contains("筛选键值", StringComparison.Ordinal)), Is.True);
            Assert.That(filters.Any(text => text.Contains("筛选块", StringComparison.Ordinal)), Is.True);
        }
        finally
        {
            window.Close();
        }
    }

    private sealed class FakeSaveEditorService : ISaveEditorService
    {
        public Task<OperationResult<SaveOutlineDto>> LoadOutlineAsync(string filePath, CancellationToken cancellationToken = default)
        {
            var funds = new SaveOutlineFieldDto
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
            var science = funds with { Key = "sci", Label = "科技点", Value = "42", DisplayValue = "42", Line = 12, Priority = 1 };

            var outline = new SaveOutlineDto
            {
                FilePath = filePath,
                SaveName = "我的生涯",
                Version = "1.12.5",
                Mode = "CAREER",
                Funds = "√1234.5",
                Science = "42",
                Highlights = [funds, science],
                LineCount = 24,
                PairCount = 4,
                NodeCount = 3,
                Sections =
                [
                    new SaveOutlineSectionDto
                    {
                        Id = "game",
                        Title = "存档信息",
                        Subtitle = "GAME",
                        Fields = [funds, science],
                        Nodes =
                        [
                            new SaveOutlineNodeDto
                            {
                                Path = "GAME/FLIGHTSTATE/VESSEL#0",
                                Name = "VESSEL",
                                Label = "飞行器",
                                Summary = "一号船",
                                Line = 13,
                                PairCount = 2,
                                NodeCount = 2,
                            },
                        ],
                    },
                ],
            };

            return Task.FromResult(OperationResult<SaveOutlineDto>.Ok(outline));
        }

        public Task<OperationResult<IReadOnlyList<SaveOutlineMatchDto>>> SearchAsync(
            string filePath,
            string query,
            int limit = 200,
            CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult<IReadOnlyList<SaveOutlineMatchDto>>.Ok([]));

        public Task<OperationResult<SaveNodeDetailDto>> DescribeNodeAsync(string filePath, int line, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult<SaveNodeDetailDto>.Fail("未实现"));

        public Task<OperationResult<SaveEditOutcomeDto>> EditAsync(SaveEditRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult<SaveEditOutcomeDto>.Fail("未实现"));
    }
}
