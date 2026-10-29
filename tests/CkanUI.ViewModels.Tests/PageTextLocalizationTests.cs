using System.Threading.Tasks;
using CkanUI.Localization;
using CkanUI.ViewModels.Navigation;
using CkanUI.ViewModels.Pages;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

/// <summary>
/// 页面 VM 文案层（缺口 ⑧）：<c>PageViewModel.Text(fallback, key)</c> 的兜底语义与解析器接管。
/// 这里断言的重点不是"某个键翻成什么"，而是：没有解析器时页面必须原样吐中文，
/// 有解析器/格式化器时同一处代码必须改走键，且 <see cref="NavigationService"/> 会替页面挂上它们。
/// </summary>
[TestFixture]
public sealed class PageTextLocalizationTests
{
    /// <summary>最小页面：只暴露 <c>Text(...)</c> 的两条路径，不依赖任何服务与语言包。</summary>
    private sealed class ProbePage : PageViewModel
    {
        /// <summary>无占位符的文案（走 <c>Text(fallback, key)</c>）。</summary>
        public override string Title => Text("模组", LocalizationKeys.NavRailManageMods);

        /// <summary>无占位符的另一条文案，用于区分"标题"与"正文"都被接管。</summary>
        public string EmptyTitle => Text("未找到 KSP 实例", LocalizationKeys.ManageModsEmptyNoInstance);

        /// <summary>带占位符的文案（走 <c>Text(fallback, key, args)</c>）。</summary>
        public string InstanceCount(int count) => Text("共 {0} 个实例", LocalizationKeys.InstancesCount, count);

        /// <summary>故意少给一个参数：string.Format 会抛 FormatException，页面必须回落中文原文。</summary>
        public string BrokenTemplate() => Text("{0} 个实例 {1}", LocalizationKeys.InstancesCount, 1);
    }

    [Test]
    public void ProbePage_WithoutResolver_ReturnsChineseFallback()
    {
        var page = new ProbePage();

        // 测试里没人挂解析器：页面必须吐源码里的中文原文（既有断言中文的用例靠这条保持绿）。
        Assert.That(page.TextResolver, Is.Null);
        Assert.That(page.TextFormatter, Is.Null);
        Assert.That(page.Title, Is.EqualTo("模组"));
        Assert.That(page.EmptyTitle, Is.EqualTo("未找到 KSP 实例"));
    }

    [Test]
    public void ProbePage_WithResolver_ReturnsMappedValue()
    {
        var page = new ProbePage { TextResolver = key => "EN:" + key };

        Assert.That(page.Title, Is.EqualTo("EN:Ui_NavRail_ManageMods"));
        Assert.That(page.EmptyTitle, Is.EqualTo("EN:Ui_ManageMods_EmptyNoInstance"));
    }

    [Test]
    public void ProbePage_WithFormatter_FormatsThroughKey()
    {
        var page = new ProbePage
        {
            TextFormatter = (key, args) => "FMT:" + key + ":" + string.Join(",", args),
        };

        Assert.That(page.InstanceCount(3), Is.EqualTo("FMT:Ui_Instances_Count:3"));

        // 无占位符的文案不走格式化器，所以仍然只有"没有解析器 → 中文原文"这一条路径。
        Assert.That(page.Title, Is.EqualTo("模组"));
    }

    [Test]
    public void ProbePage_WithoutFormatter_FormatsChineseTemplate()
    {
        var page = new ProbePage();

        Assert.That(page.InstanceCount(3), Is.EqualTo("共 3 个实例"));
    }

    [Test]
    public void ProbePage_ResolverOnly_StillFormatsChineseTemplate()
    {
        // 只挂 Get 不挂 Format 时，带占位符的文案没有键可查，只能拿中文模板自己格式化：
        // 这正是宿主必须同时注册 ILocalizer.Get 与 ILocalizer.Format 的原因。
        var page = new ProbePage { TextResolver = key => "EN:" + key };

        Assert.That(page.InstanceCount(2), Is.EqualTo("共 2 个实例"));
        Assert.That(page.Title, Is.EqualTo("EN:Ui_NavRail_ManageMods"));
    }

    [Test]
    public void ProbePage_BrokenTemplate_FallsBackToRawText()
    {
        var page = new ProbePage();

        // 占位符写坏（少一个实参）时只能少一句译文，不能让页面命令抛出去。
        Assert.That(page.BrokenTemplate(), Is.EqualTo("{0} 个实例 {1}"));
    }

    [Test]
    public void ModsPageViewModel_UsesChineseFallbackWithoutResolver()
    {
        var page = new ModsPageViewModel();

        Assert.That(page.Title, Is.EqualTo("模组"));
        Assert.That(page.EmptyStateTitle, Is.EqualTo("未找到 KSP 实例"));
        Assert.That(page.EmptyStateHint, Is.EqualTo("请先在「实例」页添加或选择一个 KSP 安装目录。"));
        Assert.That(page.CountText, Is.EqualTo("显示 0 / 0 个模组"));
        Assert.That(page.SelectionSummary, Is.EqualTo("已选 0 · 变更 +0/-0"));
    }

    [Test]
    public void ModsPageViewModel_RoutesTextThroughResolver()
    {
        var page = new ModsPageViewModel { TextResolver = key => "EN:" + key };

        Assert.That(page.Title, Is.EqualTo("EN:Ui_NavRail_ManageMods"));
        Assert.That(page.EmptyStateTitle, Is.EqualTo("EN:Ui_ManageMods_EmptyNoInstance"));
        Assert.That(page.EmptyStateHint, Is.EqualTo("EN:Ui_ManageMods_EmptyNoInstanceHint"));
    }

    [Test]
    public void InstancesPageViewModel_LocalizesPlaceholderText()
    {
        var page = new InstancesPageViewModel();

        Assert.That(page.Title, Is.EqualTo("实例"));
        Assert.That(page.CountText, Is.EqualTo("共 0 个实例"));
        Assert.That(page.EmptyStateTitle, Is.EqualTo("还没有游戏实例"));

        page.TextFormatter = (key, args) => "FMT:" + key + ":" + string.Join(",", args);
        Assert.That(page.CountText, Is.EqualTo("FMT:Ui_Instances_Count:0"));
    }

    [Test]
    public async Task NavigationService_AttachesResolversToResolvedPage()
    {
        var page = new ProbePage();
        var nav = new NavigationService(
            _ => page,
            key => "EN:" + key,
            (key, args) => "FMT:" + key + ":" + string.Join(",", args));

        var ok = await nav.NavigateAsync("probe");

        Assert.That(ok, Is.True);
        Assert.That(nav.CurrentPage, Is.SameAs(page));
        Assert.That(page.TextResolver, Is.Not.Null);
        Assert.That(page.TextFormatter, Is.Not.Null);
        Assert.That(page.Title, Is.EqualTo("EN:Ui_NavRail_ManageMods"));
        Assert.That(page.InstanceCount(1), Is.EqualTo("FMT:Ui_Instances_Count:1"));
    }

    [Test]
    public async Task NavigationService_WithoutLocalizer_KeepsPageOnChinese()
    {
        var page = new ProbePage();
        var nav = new NavigationService(_ => page);

        await nav.NavigateAsync("probe");

        // 宿主导航时没给解析器（例如 headless 测试）：页面继续吐中文原文。
        Assert.That(page.TextResolver, Is.Null);
        Assert.That(page.TextFormatter, Is.Null);
        Assert.That(page.Title, Is.EqualTo("模组"));
        Assert.That(page.InstanceCount(4), Is.EqualTo("共 4 个实例"));
    }

    [Test]
    public async Task NavigationService_KeepsResolverAlreadyOwnedByPage()
    {
        var page = new ProbePage { TextResolver = _ => "PRESET" };
        var nav = new NavigationService(_ => page, key => "EN:" + key);

        await nav.NavigateAsync("probe");

        // 用 ??= 挂：页面自己设过的解析器不被导航覆盖（测试与预览页要能钉住文案）。
        Assert.That(page.Title, Is.EqualTo("PRESET"));
    }
}
