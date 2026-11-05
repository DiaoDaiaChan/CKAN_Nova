using System.Linq;
using Avalonia;
using CkanUI.App.Controls;
using CkanUI.App.Tests.Automation;
using NUnit.Framework;

namespace CkanUI.App.Tests;

[TestFixture]
[NonParallelizable]
public class CursorPointerLayerWindowDragTests
{
    [SetUp]
    public void SetUp() => HeadlessAppHost.EnsureInitialized();

    [Test]
    public void OnWindowMoved_EmitsStarsAtAnchorInsteadOfAheadOfCursor()
    {
        var layer = new CursorPointerLayer();
        try
        {
            layer.StartWindowDrag(new Point(100, 100));
            int initialCount = layer.DragStarlight.ActiveCount;

            // 窗口实际向右移动 30px；光标相对窗口客户区仍在 (100,100)。
            layer.OnWindowMoved(30, 0);

            var particles = layer.DragStarlight.Particles;
            Assert.Multiple(() =>
            {
                Assert.That(layer.DragStarlight.IsDragging, Is.True);
                Assert.That(layer.DragStarlight.ActiveCount, Is.GreaterThan(initialCount));
                Assert.That(particles.Max(p => p.X), Is.LessThanOrEqualTo(104.5),
                    "窗口右移后新星光应出现在光标锚点附近，而不是被推到光标前方");
                Assert.That(particles.Min(p => p.X), Is.LessThan(100.0),
                    "已经生成的星光应留在屏幕原位，形成光标后方的拖尾");
                Assert.That(layer.DragStarlight.LastPosition.X, Is.EqualTo(100.0).Within(1e-9),
                    "下一次撒点必须从真实光标锚点继续，而不是从被推远的旧位置继续");
            });
        }
        finally
        {
            layer.Clear();
        }
    }

    [Test]
    public void OnWindowMoved_RepeatedMovesKeepStarsAnchoredToCursor()
    {
        var layer = new CursorPointerLayer();
        try
        {
            layer.StartWindowDrag(new Point(100, 100));
            layer.OnWindowMoved(30, 0);
            layer.OnWindowMoved(30, 0);
            layer.OnWindowMoved(30, 0);

            var particles = layer.DragStarlight.Particles;
            Assert.That(particles.Max(p => p.X), Is.LessThanOrEqualTo(104.5));
            Assert.That(particles.Min(p => p.X), Is.LessThan(60.0));
            Assert.That(layer.DragStarlight.LastPosition.X, Is.EqualTo(100.0).Within(1e-9));
        }
        finally
        {
            layer.Clear();
        }
    }

    [Test]
    public void OnWindowMoved_WithoutActiveDrag_DoesNotStartTrail()
    {
        var layer = new CursorPointerLayer();
        try
        {
            layer.OnWindowMoved(30, 0);

            Assert.That(layer.DragStarlight.IsDragging, Is.False);
            Assert.That(layer.DragStarlight.ActiveCount, Is.EqualTo(0));
        }
        finally
        {
            layer.Clear();
        }
    }
}
