using System;
using System.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;

using CkanUI.App.Animations;
using CkanUI.App.Tests.Automation;

using NUnit.Framework;

namespace CkanUI.App.Tests;

/// <summary>
/// 弹簧收敛后必须把 RenderTransform 摘掉：挂着恒等变换的控件会被合成器单独开一层，
/// 快速拖动滚动条时这些层来不及失效，屏幕上就留下一片空白（CHANGELOG「残影行」同类）。
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class TransformSpringLayerTests
{
    [Test]
    public void Spring_ReleasesItsRenderTransform_OnceItSettlesAtIdentity()
    {
        var (window, border) = CreateTarget();
        var spring = TransformSpring.For(border, SpringSpec.Glide);

        spring.MoveTo(y: 24d, opacity: 0d, snap: true);
        Assert.Multiple(() =>
        {
            Assert.That(border.RenderTransform, Is.Not.Null, "起跑姿态要靠 RenderTransform 表达，不能提前摘掉。");
            Assert.That(border.Opacity, Is.EqualTo(0d).Within(0.001d));
        });

        spring.MoveTo(y: 0d, opacity: 1d);
        Assert.That(border.RenderTransform, Is.Not.Null, "飞行途中必须保留变换。");

        PumpUntilSettled(spring);

        Assert.Multiple(() =>
        {
            Assert.That(spring.IsSettled, Is.True, "弹簧应该已经收敛。");
            Assert.That(border.Opacity, Is.EqualTo(1d).Within(0.001d));
            Assert.That(border.RenderTransform, Is.Null,
                "停在恒等姿态还挂着 RenderTransform，这一行就白背一层合成层，拖动滚动条时会留空白。");
        });

        window.Close();
    }

    [Test]
    public void Spring_TakesTheTransformBack_WhenItMovesAgain()
    {
        var (window, border) = CreateTarget();
        var spring = TransformSpring.For(border, SpringSpec.Glide);

        spring.MoveTo(y: 12d, opacity: 1d, snap: true);
        spring.MoveTo(y: 0d, opacity: 1d);
        PumpUntilSettled(spring);
        Assert.That(border.RenderTransform, Is.Null);

        spring.MoveTo(y: 8d, opacity: 1d);
        Assert.That(border.RenderTransform, Is.Not.Null, "再次起跑必须把变换收回来，否则动画写在不存在的组上。");

        spring.MoveTo(y: 0d, opacity: 1d);
        PumpUntilSettled(spring);
        Assert.That(border.RenderTransform, Is.Null, "回到恒等姿态后依旧要摘掉。");

        window.Close();
    }

    private static (Window Window, Border Border) CreateTarget()
    {
        HeadlessAppHost.EnsureInitialized();
        var border = new Border { Width = 120, Height = 40, Background = Brushes.Gray };
        var window = new Window { Width = 320, Height = 240, Content = border };
        window.Show();
        Pump(4);
        return (window, border);
    }

    private static void PumpUntilSettled(TransformSpring spring)
    {
        for (var i = 0; i < 400 && !spring.IsSettled; i++)
        {
            Pump(1);
        }
    }

    private static void Pump(int frames)
    {
        for (var i = 0; i < frames; i++)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            HeadlessAppHost.PumpJobs();
            System.Threading.Thread.Sleep(6);
        }
    }
}
