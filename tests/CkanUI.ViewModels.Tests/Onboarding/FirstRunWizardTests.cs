using CkanUI.ViewModels.Dialogs;

using NUnit.Framework;

namespace CkanUI.ViewModels.Tests.Onboarding;

/// <summary>
/// 装机必备清单是「可选推荐」的底线守卫：默认全不勾、标识唯一、条目有说明。
/// </summary>
/// <remarks>
/// 这条测试盯的是需求原话「做成可选勾选，别强制就行」——将来谁把默认值改成 true，
/// 用户会在首启时被动装上一堆模组，而这种回归在人工点测里几乎看不出来。
/// </remarks>
[TestFixture]
public sealed class FirstRunWizardCatalogTests
{
    [Test]
    public void Catalog_DefaultsToNothingChecked()
    {
        var items = EssentialModCatalog.CreateItems();

        Assert.That(items, Is.Not.Empty, "推荐清单不该是空的：首启这一步就没有意义了。");
        Assert.That(items.All(item => !item.IsChecked), Is.True, "推荐模组必须默认不勾选，不能被强制安装。");
    }

    [Test]
    public void Catalog_IdentifiersAreUniqueAndExplained()
    {
        var items = EssentialModCatalog.CreateItems();

        Assert.That(items.Select(item => item.Identifier).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            Is.EqualTo(items.Count), "推荐清单里出现了重复标识，装配时会互相覆盖。");
        Assert.That(items.All(item => !string.IsNullOrWhiteSpace(item.Summary)), Is.True, "每条推荐都要有一句话说明。");
    }

    [Test]
    public void StepItem_MarksProgressWithCheckmark()
    {
        var step = new FirstRunStepItemViewModel(FirstRunStepKind.Proxy, 2, "下载代理");

        Assert.That(step.Marker, Is.EqualTo("3"), "未完成的步骤显示序号（从 1 开始，便于对照「第几步」）。");
        Assert.That(step.IsPending, Is.True);

        step.IsCompleted = true;

        Assert.That(step.Marker, Is.EqualTo("✓"), "走完的步骤要变成对勾。");
        Assert.That(step.IsPending, Is.False);
    }

    [Test]
    public void VersionItem_PinsRealVersion()
    {
        var real = new FirstRunVersionItemViewModel("1.12.5", isSelected: false, isRealVersion: true, isWildcard: false, "真实版本");
        var other = new FirstRunVersionItemViewModel("1.11.2", isSelected: false, isRealVersion: false, isWildcard: false, "真实版本");

        Assert.That(real.IsSelected, Is.True, "真实版本永远兼容，构造时就该钉住。");
        Assert.That(real.IsEditable, Is.False, "真实版本不给取消的机会。");
        Assert.That(other.IsSelected, Is.False);
        Assert.That(other.IsEditable, Is.True);
    }
}
