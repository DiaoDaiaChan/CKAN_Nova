using System;
using System.Linq;

using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;

using CkanUI.App.Animations;

using NUnit.Framework;

namespace CkanUI.App.Tests;

/// <summary>
/// 接管前只摘 RenderTransform / Opacity 的过渡：和逐帧写入打架的必须摘，底色渐变必须留。
/// </summary>
[TestFixture]
public sealed class MotionTransitionsTests
{
    [Test]
    public void KeepsColourTransitionsAndDropsTheTransformOne()
    {
        var control = Build();
        control.Transitions!.Add(new BrushTransition { Property = Border.BackgroundProperty, Duration = TimeSpan.FromMilliseconds(120) });
        control.Transitions!.Add(new TransformOperationsTransition
        {
            Property = Visual.RenderTransformProperty,
            Duration = TimeSpan.FromMilliseconds(160),
        });

        MotionTransitions.Strip(control, includeOpacity: false);

        Assert.That(control.Transitions, Is.Not.Null);
        var properties = control.Transitions!.Select(PropertyOf).ToList();
        Assert.That(properties, Does.Contain(Border.BackgroundProperty), "底色渐变要留着");
        Assert.That(properties, Does.Not.Contain(Visual.RenderTransformProperty), "变换过渡会每帧盖住弹簧");
        Assert.That(properties, Does.Not.Contain(Visual.OpacityProperty));
    }

    [Test]
    public void OpacityIsOnlyDroppedWhenTheModuleDrivesIt()
    {
        var kept = Build();
        kept.Transitions!.Add(new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(90) });
        MotionTransitions.Strip(kept, includeOpacity: false);
        Assert.That(kept.Transitions!.Select(PropertyOf), Does.Contain(Visual.OpacityProperty));

        var stripped = Build();
        stripped.Transitions!.Add(new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(90) });
        MotionTransitions.Strip(stripped, includeOpacity: true);
        Assert.That(stripped.Transitions, Is.Null.Or.Empty);
    }

    [Test]
    public void OnlyTheTransformGoesWhenItIsTheOnlyOne()
    {
        var control = Build();
        control.Transitions!.Add(new TransformOperationsTransition
        {
            Property = Visual.RenderTransformProperty,
            Duration = TimeSpan.FromMilliseconds(160),
        });

        MotionTransitions.Strip(control, includeOpacity: true);

        Assert.That(control.Transitions, Is.Null.Or.Empty);
    }

    [Test]
    public void UntouchedControlKeepsItsList()
    {
        var control = Build();
        var list = new Transitions
        {
            new BrushTransition { Property = Border.BackgroundProperty, Duration = TimeSpan.FromMilliseconds(120) },
        };
        control.Transitions = list;

        MotionTransitions.Strip(control, includeOpacity: true);

        Assert.That(control.Transitions, Is.SameAs(list), "没有要摘的就不要无谓地换一个新列表");
    }

    private static Control Build() => new Border { Width = 100, Height = 40, Background = Brushes.Transparent, Transitions = new Transitions() };

    private static AvaloniaProperty? PropertyOf(ITransition transition) =>
        (AvaloniaProperty?)transition.GetType()
            .GetProperty("Property", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
            ?.GetValue(transition);
}
