using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Transformation;

using CkanUI.App.Animations;

using NUnit.Framework;

namespace CkanUI.App.Tests;

/// <summary>
/// 一个控件一根弹簧：位移与缩放共用一组 Transform，子属性逐帧改写，组本身不换。
/// </summary>
[TestFixture]
public sealed class TransformSpringTests
{
    [Test]
    public void SnapMove_WritesChildTransformsAndKeepsTheGroup()
    {
        var border = new Border();
        var spring = new TransformSpring(border, SpringSpec.Glide, SpringSpec.Snappy);
        var group = border.RenderTransform as TransformGroup;

        spring.MoveTo(x: 42, scaleX: 0.7, snap: true);

        Assert.That(group, Is.Not.Null);
        Assert.That(group!.Children.OfType<TranslateTransform>().First().X, Is.EqualTo(42).Within(1e-9));
        Assert.That(group.Children.OfType<ScaleTransform>().First().ScaleX, Is.EqualTo(0.7).Within(1e-9));
        Assert.That(border.RenderTransform, Is.SameAs(group), "组实例不能被替换");
    }

    [Test]
    public void MoveTo_OnlyTouchesTheChannelsItWasGiven()
    {
        var border = new Border();
        var spring = new TransformSpring(border, SpringSpec.Glide, SpringSpec.Snappy);
        var group = (TransformGroup)border.RenderTransform!;

        spring.MoveTo(x: 30, scaleX: 0.5, snap: true);
        spring.MoveTo(scaleX: 0.9, snap: true);

        Assert.That(group.Children.OfType<TranslateTransform>().First().X, Is.EqualTo(30).Within(1e-9));
        Assert.That(group.Children.OfType<ScaleTransform>().First().ScaleX, Is.EqualTo(0.9).Within(1e-9));
        Assert.That(group.Children.OfType<ScaleTransform>().First().ScaleY, Is.EqualTo(1).Within(1e-9));
    }

    [Test]
    public void For_ReusesOneSpringPerControl()
    {
        var border = new Border();
        Assert.That(TransformSpring.For(border, SpringSpec.Glide), Is.SameAs(TransformSpring.For(border, SpringSpec.Glide)));
    }

    [Test]
    public void OpacityChannel_WritesTheTargetAndClampsItsRange()
    {
        var border = new Border { Opacity = 0.8 };
        var spring = TransformSpring.For(border, SpringSpec.Glide);

        spring.MoveTo(opacity: 0.2, snap: true);
        Assert.That(border.Opacity, Is.EqualTo(0.2).Within(1e-9));

        spring.MoveTo(opacity: 5, snap: true);
        Assert.That(border.Opacity, Is.EqualTo(1).Within(1e-9));
    }

    [Test]
    public void OpacityChannel_IsOnlyWrittenWhenItWasAskedFor()
    {
        var border = new Border { Opacity = 0.6 };
        TransformSpring.For(border, SpringSpec.Glide).MoveTo(x: 10, snap: true);

        Assert.That(border.Opacity, Is.EqualTo(0.6).Within(1e-9), "没点名的通道不能被动");
    }

    [Test]
    public void Scaling_KeepsTheVisualCentre()
    {
        var border = new Border();
        TransformSpring.For(border, SpringSpec.Snappy).MoveTo(scaleX: 0.95, snap: true);

        Assert.That(border.RenderTransformOrigin.Point.X, Is.EqualTo(0.5).Within(1e-9));
        Assert.That(border.RenderTransformOrigin.Point.Y, Is.EqualTo(0.5).Within(1e-9));
    }

    [Test]
    public void MoveTo_ReclaimsTheRenderTransformIfAStyleTookItOver()
    {
        var border = new Border();
        var spring = TransformSpring.For(border, SpringSpec.Glide);
        var group = border.RenderTransform;
        border.RenderTransform = TransformOperations.Parse("translate(5px,0px)");

        spring.MoveTo(x: 0, snap: true);

        Assert.That(border.RenderTransform, Is.SameAs(group), "样式抢走的 RenderTransform 要收回");
        Assert.That(((TransformGroup)group!).Children.OfType<TranslateTransform>().First().X, Is.EqualTo(0).Within(1e-9));
    }

    [Test]
    public void Order_ScalePrecedesTranslate_SoTranslateIsNotScaledByScaleFactor()
    {
        var border = new Border
        {
            Width = 100,
            Height = 26,
            RenderTransformOrigin = new RelativePoint(0, 0.5, RelativeUnit.Relative)
        };
        var spring = new TransformSpring(border, SpringSpec.Glide, SpringSpec.Snappy);
        var group = (TransformGroup)border.RenderTransform!;

        Assert.That(group.Children[0], Is.InstanceOf<ScaleTransform>(), "缩放必须先于位移，否则位移量会被 scale 乘小导致页签指示条与胶囊错位");
        Assert.That(group.Children[1], Is.InstanceOf<TranslateTransform>());

        spring.MoveTo(x: 56, scaleX: 0.72, snap: true);
        var matrix = group.Value;
        var left = matrix.Transform(new Point(0, 0));
        var right = matrix.Transform(new Point(100, 0));

        Assert.That(left.X, Is.EqualTo(56).Within(1e-6), "左边界必须精确落在 X=56，不能被 0.72 缩成 40.32");
        Assert.That(right.X, Is.EqualTo(56 + 72).Within(1e-6), "右边界必须精确落在 X=56+72=128");
    }

    [Test]
    public void TiltChannels_UpdateSkewAndRotateTransforms()
    {
        var border = new Border { Width = 100, Height = 40 };
        var spring = new TransformSpring(border, SpringSpec.Glide, SpringSpec.Snappy);
        var group = (TransformGroup)border.RenderTransform!;

        spring.MoveTo(skewX: 1.2, skewY: -0.8, rotateAngle: 0.5, snap: true);

        var skew = group.Children.OfType<SkewTransform>().FirstOrDefault();
        var rotate = group.Children.OfType<RotateTransform>().FirstOrDefault();

        Assert.That(skew, Is.Not.Null);
        Assert.That(rotate, Is.Not.Null);
        Assert.That(skew!.AngleX, Is.EqualTo(1.2).Within(1e-9));
        Assert.That(skew.AngleY, Is.EqualTo(-0.8).Within(1e-9));
        Assert.That(rotate!.Angle, Is.EqualTo(0.5).Within(1e-9));
    }
}
