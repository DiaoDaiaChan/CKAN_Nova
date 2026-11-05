using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;

using NUnit.Framework;

namespace CkanUI.App.Tests;

/// <summary>
/// 悬停效果的落点契约：会参与命中测试的控件（列表行、表格行、卡片）自己身上不能挂 RenderTransform，
/// 否则指针压住边缘时控件会「移出」自己的包围盒 —— 悬停态每帧进出，界面上就是选中/未选中闪烁。
/// 位移只允许下沉到模板内的 ContentPresenter，或者干脆不做位移。
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class HoverStyleContractTests
{
    private static readonly string[] GeometrySensitiveHoverSelectors =
    {
        "ListBoxItem:pointerover",
        "DataGridRow:pointerover",
        "Border.CkanCardInteractive:pointerover",
        "Border.CkanGuideCard:pointerover",
        "Button.CkanGuide:pointerover",
        "TreeViewItem:pointerover",
    };

    [Test]
    public void HoverEffects_DoNotMoveTheControlThatOwnsThePointer()
    {
        var document = LoadStyles();

        var offenders = GeometrySensitiveHoverSelectors
            .SelectMany(selector => document
                .Descendants()
                .Where(element => element.Name.LocalName == "Style"
                                  && (string?)element.Attribute("Selector") == selector)
                .SelectMany(style => style.Descendants()
                    .Where(setter => setter.Name.LocalName == "Setter"
                                     && (string?)setter.Attribute("Property") == "RenderTransform")
                    .Select(_ => selector)))
            .Distinct()
            .ToList();

        Assert.That(offenders, Is.Empty,
            "悬停位移会让控件移出自己的包围盒，指针压边时悬停态自激闪烁：" + string.Join("；", offenders));
    }

    [Test]
    public void RowHoverSlide_LivesOnTheTemplatePresenter_SoTheRowKeepsItsGeometry()
    {
        var document = LoadStyles();

        var presenterSlides = document
            .Descendants()
            .Where(element => element.Name.LocalName == "Style"
                              && ((string?)element.Attribute("Selector"))?.EndsWith("/template/ ContentPresenter", StringComparison.Ordinal) == true
                              && (((string?)element.Attribute("Selector"))!.StartsWith("ListBoxItem:pointerover", StringComparison.Ordinal)
                                  || ((string?)element.Attribute("Selector"))!.StartsWith("DataGridRow:pointerover", StringComparison.Ordinal)))
            .SelectMany(style => style.Descendants()
                .Where(setter => setter.Name.LocalName == "Setter"
                                 && (string?)setter.Attribute("Property") == "RenderTransform"
                                 && ((string?)setter.Attribute("Value"))?.Contains("translate", StringComparison.Ordinal) == true))
            .ToList();

        Assert.That(presenterSlides, Has.Count.EqualTo(2),
            "列表行与表格行的悬停位移都要落在模板内的 ContentPresenter 上，行的几何才不跟着动");
    }

    [Test]
    public void RowSelection_SwapsInstantly_WithoutAFadingBackground()
    {
        var document = LoadStyles();

        var offenders = new[] { "ListBoxItem", "DataGridRow" }
            .SelectMany(selector => document
                .Descendants()
                .Where(element => element.Name.LocalName == "Style"
                                  && (string?)element.Attribute("Selector") == selector)
                .SelectMany(style => style.Descendants()
                    .Where(element => element.Name.LocalName == "BrushTransition"
                                      && (string?)element.Attribute("Property") == "Background")
                    .Select(_ => selector)))
            .ToList();

        Assert.That(offenders, Is.Empty,
            "行的底色不能做过渡：选中态切走时旧行会继续亮一段，" +
            "用户看到的就是「A 又高亮了一下」：" + string.Join("；", offenders));
    }

    [Test]
    public void KeyboardFocus_DoesNotRepaintTheRowBackground()
    {
        var document = LoadStyles();

        var offenders = document
            .Descendants()
            .Where(element => element.Name.LocalName == "Style"
                              && (string?)element.Attribute("Selector") == "ListBoxItem:focus-visible")
            .SelectMany(style => style.Descendants()
                .Where(setter => setter.Name.LocalName == "Setter"
                                 && (string?)setter.Attribute("Property") == "Background"))
            .ToList();

        Assert.That(offenders, Is.Empty,
            "键盘焦点只画焦点环；改底色会让焦点迁移看起来像一次选中闪烁");
    }

    private static XDocument LoadStyles()
    {
        var root = ResolveRepositoryRoot();
        var path = Path.Combine(root, "src", "CkanUI.App", "Resources", "FluentStyles.axaml");
        return XDocument.Load(path);
    }

    /// <summary>从测试输出目录向上找到仓库根（以 <c>src/CkanUI.App/CkanUI.App.csproj</c> 为锚点，与 sln 文件名无关）。</summary>
    private static string ResolveRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "src", "CkanUI.App", "CkanUI.App.csproj")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("未能从测试输出目录向上找到含 src/CkanUI.App/CkanUI.App.csproj 的仓库根。");
    }
}
