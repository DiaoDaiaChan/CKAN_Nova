using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Pages.Settings;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

/// <summary>
/// 界面密度三档（20 篇 P-08 / THM-06）与诊断区（D-03）的 headless 契约。
/// </summary>
/// <remarks>
/// 密度只映射数值，真正写资源字典的是 App 层 <c>DensityHost</c>；
/// 这里锁住三档的数值与回退规则，App 层一旦改口径就会红。
/// </remarks>
[TestFixture]
public sealed class SettingsDensityTests
{
    [Test]
    public void Normalize_FallsBackToStandard()
    {
        Assert.That(DensityScale.Normalize(null), Is.EqualTo("standard"));
        Assert.That(DensityScale.Normalize(string.Empty), Is.EqualTo("standard"));
        Assert.That(DensityScale.Normalize("densely"), Is.EqualTo("standard"));
        Assert.That(DensityScale.Normalize("COMPACT"), Is.EqualTo("compact"));
        Assert.That(DensityScale.Normalize("comfortable"), Is.EqualTo("comfortable"));
    }

    [Test]
    public void RowHeight_MatchesThreeTiers()
    {
        Assert.That(DensityScale.RowHeight("compact"), Is.EqualTo(24));
        Assert.That(DensityScale.RowHeight("standard"), Is.EqualTo(28));
        Assert.That(DensityScale.RowHeight("comfortable"), Is.EqualTo(32));
        // 未知/缺省回落 standard，与 Tokens.axaml 里 CkanRowHeight 的默认值一致。
        Assert.That(DensityScale.RowHeight(null), Is.EqualTo(28));
    }

    [Test]
    public void Padding_GrowsWithDensity()
    {
        Assert.That(DensityScale.PagePadding("compact"), Is.EqualTo(12));
        Assert.That(DensityScale.PagePadding("standard"), Is.EqualTo(16));
        Assert.That(DensityScale.PagePadding("comfortable"), Is.EqualTo(20));

        Assert.That(DensityScale.ToolbarHorizontalPadding("compact"), Is.EqualTo(8));
        Assert.That(DensityScale.ToolbarHorizontalPadding("standard"), Is.EqualTo(12));
        Assert.That(DensityScale.ToolbarHorizontalPadding("comfortable"), Is.EqualTo(16));

        Assert.That(DensityScale.ToolbarVerticalPadding("compact"), Is.EqualTo(4));
        Assert.That(DensityScale.ToolbarVerticalPadding("standard"), Is.EqualTo(8));
        Assert.That(DensityScale.ToolbarVerticalPadding("comfortable"), Is.EqualTo(12));
    }

    [Test]
    public void Diagnostics_WithoutInstance_ShowsPlaceholders()
    {
        var diagnostics = new SettingsDiagnosticsViewModel();

        diagnostics.Update(instance: null, configDirectory: null, downloadCacheDirectory: null);

        Assert.That(diagnostics.HasRows, Is.True);
        Assert.That(RowValue(diagnostics, "当前实例"), Is.EqualTo("（未打开实例）"));
        Assert.That(RowValue(diagnostics, "配置目录"), Is.EqualTo(SettingsPaths.DefaultConfigDirectory));
        Assert.That(RowValue(diagnostics, "缓存目录"), Is.EqualTo("（Core 默认目录）"));
        Assert.That(diagnostics.Rows.Select(row => row.Name), Has.No.Member("实例目录"));
    }

    [Test]
    public void Diagnostics_WithInstanceAndCoreVersion_ListsEverything()
    {
        var diagnostics = new SettingsDiagnosticsViewModel
        {
            CoreVersionProvider = () => "1.35.2",
        };
        var instance = new InstanceInfo("主力", "D:/KSP", "1.12.5", IsDefault: true, IsLocked: false, ModuleCount: 42);

        diagnostics.Update(instance, "D:/KSP/CKAN", "D:/KSP/cache");

        Assert.That(RowValue(diagnostics, "当前实例"), Is.EqualTo("主力"));
        Assert.That(RowValue(diagnostics, "实例目录"), Is.EqualTo("D:/KSP"));
        Assert.That(RowValue(diagnostics, "游戏版本"), Is.EqualTo("1.12.5"));
        Assert.That(RowValue(diagnostics, "CKAN Core 版本"), Is.EqualTo("1.35.2"));
        Assert.That(RowValue(diagnostics, "日志文件"), Is.EqualTo(SettingsPaths.LogFile("D:/KSP/CKAN")));
    }

    [Test]
    public void Diagnostics_BuildText_IsPasteable()
    {
        var diagnostics = new SettingsDiagnosticsViewModel();
        diagnostics.Update(instance: null, configDirectory: "D:/KSP/CKAN", downloadCacheDirectory: null);

        var text = diagnostics.BuildText();

        Assert.That(text, Does.StartWith("诊断信息"));
        Assert.That(text, Does.Contain("应用版本: "));
        Assert.That(text, Does.Contain("配置目录: D:/KSP/CKAN"));
    }

    [Test]
    public async Task Diagnostics_CopyFailure_ReportsMessage()
    {
        var diagnostics = new SettingsDiagnosticsViewModel(_ => throw new NotSupportedException("没有可用的剪贴板通道"));
        diagnostics.Update(instance: null, configDirectory: null, downloadCacheDirectory: null);

        await diagnostics.CopyCommand.ExecuteAsync(null);

        Assert.That(diagnostics.CopyStatusText, Is.EqualTo("当前环境没有剪贴板通道，无法复制"));
    }

    private static string RowValue(SettingsDiagnosticsViewModel diagnostics, string name) =>
        diagnostics.Rows.Single(row => string.Equals(row.Name, name, StringComparison.Ordinal)).Value;
}
