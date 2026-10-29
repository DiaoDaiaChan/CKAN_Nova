using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

using Avalonia.Animation.Easings;

using NUnit.Framework;

namespace CkanUI.App.Tests;

/// <summary>
/// 弹簧参数收敛门禁（用户 m02777 的工程铁律）。
/// </summary>
/// <remarks>
/// <para>Avalonia 的 <see cref="SpringEasing"/> 把 <c>Transition.Duration</c> 归一化成 t∈[0,1] 再解微分方程：
/// 轨道结束时若 x(1) 还没跑到 1，动画最后一帧就会「啪」地跳到目标位置（Snap）。</para>
/// <para>所以这里不信任注释里的说法：把 FluentStyles.axaml 里每一处 <c>&lt;SpringEasing /&gt;</c> 抓出来，
/// 用真实的 <see cref="SpringEasing"/> 求值，验证 |x(1) − 1| &lt; 0.002 且末端速度 ≈ 0。</para>
/// </remarks>
[TestFixture]
public sealed class SpringConvergenceTests
{
    /// <summary>终点位置允许误差（用户在指令里给的数）。</summary>
    private const double PositionTolerance = 0.002;

    /// <summary>末端 1% 时间窗内允许的位置变化量：用来代替「瞬时速度≈0」。</summary>
    private const double TailVelocityTolerance = 0.01;

    private static readonly Regex SpringTag = new(
        "<SpringEasing\\s+Mass=\"(?<mass>[^\"]+)\"\\s+Stiffness=\"(?<stiffness>[^\"]+)\"\\s+Damping=\"(?<damping>[^\"]+)\"\\s+InitialVelocity=\"(?<velocity>[^\"]+)\"\\s*/>",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>带弹簧过渡的状态类：每个都必须自己带一份 Transitions，否则属性切换不会走弹簧。</summary>
    private static readonly string[] SpringSelectors =
    [
        "Border.overlay-drawer",
        "Border.floating-pill-bar",
        "Control.view-layer",
        "Control.stagger-card",
        "Control.is-invalid",
        "ui|FANavigationViewItem",
    ];

    [Test]
    public void EverySpringInStylesConvergesAtTheEndOfTheNormalisedTimeline()
    {
        var matches = SpringTag.Matches(ReadStyles()).Cast<Match>().ToList();
        Assert.That(matches, Is.Not.Empty, "FluentStyles 里一处 SpringEasing 都没有：第三阶段没生效");

        foreach (var match in matches)
        {
            var easing = Create(match);
            var end = easing.Ease(1.0);
            var almost = easing.Ease(0.99);
            var label = Describe(match);

            Assert.That(
                System.Math.Abs(end - 1.0),
                Is.LessThan(PositionTolerance),
                label + " 在 t=1 处没收敛：" + end.ToString("F5", CultureInfo.InvariantCulture)
                + "（会看到尾帧跳变）");

            Assert.That(
                System.Math.Abs(end - almost),
                Is.LessThan(TailVelocityTolerance),
                label + " 末端速度不为零：x(0.99)=" + almost.ToString("F5", CultureInfo.InvariantCulture)
                + " x(1)=" + end.ToString("F5", CultureInfo.InvariantCulture));
        }
    }

    [Test]
    public void GoldenSpringGroupsMatchTheSpec()
    {
        var matches = SpringTag.Matches(ReadStyles()).Cast<Match>().ToList();
        var groups = matches
            .Select(match => (
                Mass: Num(match, "mass"),
                Stiffness: Num(match, "stiffness"),
                Damping: Num(match, "damping"),
                Velocity: Num(match, "velocity")))
            .Distinct()
            .ToList();

        // 用户给的配平表：临界阻尼 / 微欠阻尼 / 微欠阻尼带初速 / 高刚度微反馈 / 强欠阻尼冲量。
        var expected = new (double Mass, double Stiffness, double Damping, double Velocity)[]
        {
            (1, 180, 26, 0),
            (1, 220, 22, 0),
            (1, 220, 20, -2),
            (1, 320, 24, 0),
            (1, 650, 14, 30),
        };

        foreach (var group in groups)
        {
            Assert.That(group.Mass, Is.EqualTo(1), "弹簧质量统一取 1（归一化后质量只影响手感量级，不参与收敛）");
            Assert.That(expected, Does.Contain(group), "出现了未配平过的弹簧参数：" + group);
        }

        Assert.That(groups.Count, Is.EqualTo(expected.Length), "四档弹簧外加浮条的带初速变体，恰好五种参数组合");
    }

    [Test]
    public void EveryStateClassOwnsTransitions()
    {
        var styles = ReadStyles();

        foreach (var selector in SpringSelectors)
        {
            var block = ExtractStyleBlock(styles, selector);
            Assert.That(block, Does.Contain("<Transitions>"), selector + " 没有 Transitions：状态切换会瞬间跳变");
            Assert.That(block, Does.Contain("SpringEasing"), selector + " 的过渡没用弹簧缓动");
        }
    }

    [Test]
    public void PrepareAndImpulseStatesDisableTransitions()
    {
        var styles = ReadStyles();

        // 这两个类的语义就是「瞬间到位」：一旦它们也带过渡，错峰入场与冲量抖动都会变成慢动作。
        foreach (var selector in new[] { "Control.stagger-card.stagger-prepare", "Control.shake-impulse" })
        {
            var block = ExtractStyleBlock(styles, selector);
            Assert.That(block, Does.Contain("Value=\"{x:Null}\""), selector + " 必须把 Transitions 置空");
        }
    }

    private static SpringEasing Create(Match match) => new()
    {
        Mass = Num(match, "mass"),
        Stiffness = Num(match, "stiffness"),
        Damping = Num(match, "damping"),
        InitialVelocity = Num(match, "velocity"),
    };

    private static double Num(Match match, string group)
        => double.Parse(match.Groups[group].Value, CultureInfo.InvariantCulture);

    private static string Describe(Match match)
        => "SpringEasing(m=" + match.Groups["mass"].Value
           + " k=" + match.Groups["stiffness"].Value
           + " c=" + match.Groups["damping"].Value
           + " v0=" + match.Groups["velocity"].Value + ")";

    /// <summary>切出一个 <c>&lt;Style Selector="X"&gt;</c> 到它自己的 <c>&lt;/Style&gt;</c> 之间的文本。</summary>
    private static string ExtractStyleBlock(string styles, string selector)
    {
        var marker = "Selector=\"" + selector + "\"";
        var at = styles.IndexOf(marker, System.StringComparison.Ordinal);
        Assert.That(at, Is.GreaterThanOrEqualTo(0), "FluentStyles 里找不到选择器：" + selector);

        var open = styles.LastIndexOf("<Style", at, System.StringComparison.Ordinal);
        var close = styles.IndexOf("</Style>", at, System.StringComparison.Ordinal);
        Assert.That(open, Is.GreaterThanOrEqualTo(0));
        Assert.That(close, Is.GreaterThan(open));
        return styles.Substring(open, close - open);
    }

    private static string ReadStyles()
        => System.IO.File.ReadAllText(System.IO.Path.Combine(
            RepoRoot(),
            "src",
            "CkanUI.App",
            "Resources",
            "FluentStyles.axaml"));

    /// <summary>向上找到 ckan-ui.sln 所在目录（测试进程在 bin/Debug/net10.0 下运行）。</summary>
    private static string RepoRoot()
    {
        var dir = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "ckan-ui.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new System.InvalidOperationException("向上找不到 ckan-ui.sln");
    }
}
