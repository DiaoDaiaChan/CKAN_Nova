using System.Collections.Generic;
using System.Runtime.CompilerServices;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace CkanUI.App.Animations;

/// <summary>
/// 缩略图/截图解码完成后淡入：图片从"没有"到"有"不再是一帧硬切。
/// </summary>
/// <remarks>
/// <para>缩略图走 <c>PathToThumbnailConverter</c> 同步解码，绑定一到位 <see cref="Image.SourceProperty"/> 就变成
/// 一张完整的位图——渲染出来是"啪"地出现，没有任何过渡。这里只做一件事：源变了就淡一次。</para>
/// <para>按控件去重（<see cref="ConditionalWeakTable{TKey,TValue}"/>），记住上一张源，同一张重复赋值不重播——
/// 虚拟化回收容器时会反复套同一个数据上下文，不去重就会看到缩略图闪。</para>
/// </remarks>
public static class ImageLoadMotion
{
    /// <summary>入场起始缩放（大图轻微顶一下，比纯淡入更容易被眼睛捕捉）。</summary>
    public const double StartScale = 0.985d;

    private const int MaxDepth = 24;

    private const int MaxVisited = 20000;

    private static readonly ConditionalWeakTable<Image, object> Tracked = new();

    /// <summary>接管 <paramref name="root"/> 下的图片；返回本次新增接管的数量。</summary>
    public static int Attach(Visual? root)
    {
        if (root is null)
        {
            return 0;
        }

        MotionScan.Subscribe(root, nameof(ImageLoadMotion), () => Attach(root));

        var count = 0;
        foreach (var image in Enumerate(root))
        {
            if (Tracked.TryGetValue(image, out _))
            {
                continue;
            }

            Tracked.Add(image, image.Source ?? (object)image);
            image.PropertyChanged += OnImagePropertyChanged;
            count++;

            if (image.Source is not null)
            {
                Play(image);
            }
        }

        return count;
    }

    /// <summary>让这张图淡入一次；关闭档直接返回。</summary>
    public static void Play(Image image)
    {
        if (MotionBudget.IsCollapsed)
        {
            return;
        }

        var spring = TransformSpring.For(image, SpringSpec.Glide);
        var scale = MotionBudget.Scaled(StartScale);
        spring.MoveTo(opacity: 0d, scaleX: scale, scaleY: scale, snap: true);
        spring.MoveTo(opacity: 1d, scaleX: 1d, scaleY: 1d);
    }

    private static void OnImagePropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (sender is not Image image || args.Property != Image.SourceProperty)
        {
            return;
        }

        // 同一张源重复赋值（虚拟化回收）不重播，否则滚动时缩略图会闪。
        if (Tracked.TryGetValue(image, out var last) && ReferenceEquals(last, image.Source))
        {
            return;
        }

        Tracked.Remove(image);
        Tracked.Add(image, image.Source ?? (object)image);
        if (image.Source is not null)
        {
            Play(image);
        }
    }

    private static IEnumerable<Image> Enumerate(Visual root)
    {
        var queue = new Queue<(Visual Node, int Depth)>();
        queue.Enqueue((root, 0));
        var visited = 0;

        while (queue.Count > 0 && visited < MaxVisited)
        {
            var (node, depth) = queue.Dequeue();
            visited++;

            if (node is Image image)
            {
                yield return image;
            }

            if (depth >= MaxDepth)
            {
                continue;
            }

            foreach (var child in node.GetVisualChildren())
            {
                queue.Enqueue((child, depth + 1));
            }
        }
    }
}
