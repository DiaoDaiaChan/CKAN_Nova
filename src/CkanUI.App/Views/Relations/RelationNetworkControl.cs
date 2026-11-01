using System.Collections.Specialized;
using System.Globalization;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using CkanUI.ViewModels.Relations;

namespace CkanUI.App.Views.Relations;

/// <summary>
/// 可视化模组关系网交互自绘控件（F-28 / UI-18）。
/// 遵循太空星轨（Celestial Orbital）设计语言：
/// 1. 同心天体引力轨道：内圈依赖、中圈推荐/建议/支持、外圈冲突/不兼容；
/// 2. 交互手势：鼠标拖拽平移、滚轮平滑缩放 (0.4x~3.0x)、悬停高亮光晕、双击节点跳转模组；
/// 3. 动态视觉：贝塞尔引力流线、流向指示、语义分类图例过滤联动。
/// </summary>
public sealed class RelationNetworkControl : Control
{
    public static readonly StyledProperty<IEnumerable<RelationGraphNode>?> NodesProperty =
        AvaloniaProperty.Register<RelationNetworkControl, IEnumerable<RelationGraphNode>?>(nameof(Nodes));

    public static readonly StyledProperty<IEnumerable<RelationGraphEdge>?> EdgesProperty =
        AvaloniaProperty.Register<RelationNetworkControl, IEnumerable<RelationGraphEdge>?>(nameof(Edges));

    public static readonly StyledProperty<RelationGraphNode?> SelectedNodeProperty =
        AvaloniaProperty.Register<RelationNetworkControl, RelationGraphNode?>(nameof(SelectedNode), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<ICommand?> NavigateCommandProperty =
        AvaloniaProperty.Register<RelationNetworkControl, ICommand?>(nameof(NavigateCommand));

    public IEnumerable<RelationGraphNode>? Nodes
    {
        get => GetValue(NodesProperty);
        set => SetValue(NodesProperty, value);
    }

    public IEnumerable<RelationGraphEdge>? Edges
    {
        get => GetValue(EdgesProperty);
        set => SetValue(EdgesProperty, value);
    }

    public RelationGraphNode? SelectedNode
    {
        get => GetValue(SelectedNodeProperty);
        set => SetValue(SelectedNodeProperty, value);
    }

    public ICommand? NavigateCommand
    {
        get => GetValue(NavigateCommandProperty);
        set => SetValue(NavigateCommandProperty, value);
    }

    private double zoom = 1.0;
    private Point panOffset = new(0, 0);
    private bool isPanning;
    private Point panStart;
    private RelationGraphNode? hoveredNode;

    private static readonly DashStyle OrbitDash = new(new double[] { 3, 5 }, 0);

    static RelationNetworkControl()
    {
        AffectsRender<RelationNetworkControl>(NodesProperty, EdgesProperty, SelectedNodeProperty);
    }

    public RelationNetworkControl()
    {
        ClipToBounds = true;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == NodesProperty)
        {
            if (change.OldValue is INotifyCollectionChanged oldCol)
            {
                oldCol.CollectionChanged -= OnCollectionChanged;
            }
            if (change.NewValue is INotifyCollectionChanged newCol)
            {
                newCol.CollectionChanged += OnCollectionChanged;
            }
            ResetView();
        }
        else if (change.Property == EdgesProperty)
        {
            if (change.OldValue is INotifyCollectionChanged oldCol)
            {
                oldCol.CollectionChanged -= OnCollectionChanged;
            }
            if (change.NewValue is INotifyCollectionChanged newCol)
            {
                newCol.CollectionChanged += OnCollectionChanged;
            }
            InvalidateVisual();
        }
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        InvalidateVisual();
    }

    public void ZoomIn()
    {
        zoom = Math.Clamp(zoom * 1.2, 0.4, 3.0);
        InvalidateVisual();
    }

    public void ZoomOut()
    {
        zoom = Math.Clamp(zoom / 1.2, 0.4, 3.0);
        InvalidateVisual();
    }

    public void ResetView()
    {
        zoom = 1.0;
        panOffset = new Point(0, 0);
        hoveredNode = null;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var bounds = Bounds;
        var width = bounds.Width;
        var height = bounds.Height;
        if (width <= 0 || height <= 0) return;

        var center = new Point(width / 2.0 + panOffset.X, height / 2.0 + panOffset.Y);

        // 1. 绘制太空深空背景与同心天体引力轨道
        DrawCelestialBackground(context, center, width, height);

        var nodes = Nodes?.ToList();
        var edges = Edges?.ToList();

        if (nodes is null || nodes.Count == 0)
        {
            DrawEmptyHint(context, center);
            return;
        }

        // 2. 绘制节点间的引力连线
        if (edges is not null && edges.Count > 0)
        {
            foreach (var edge in edges)
            {
                DrawEdge(context, edge, center);
            }
        }

        // 3. 绘制节点天体（先画非根节点，最后画根节点与高亮节点）
        foreach (var node in nodes.Where(n => !n.IsRoot && n != hoveredNode && n != SelectedNode))
        {
            DrawNode(context, node, center);
        }

        var rootNode = nodes.FirstOrDefault(n => n.IsRoot);
        if (rootNode is not null && rootNode != hoveredNode && rootNode != SelectedNode)
        {
            DrawNode(context, rootNode, center);
        }

        // 优先将选中和悬停节点浮于顶层
        if (SelectedNode is not null && SelectedNode != hoveredNode)
        {
            DrawNode(context, SelectedNode, center);
        }

        if (hoveredNode is not null)
        {
            DrawNode(context, hoveredNode, center);
            DrawHoverCard(context, hoveredNode, center);
        }
    }

    private void DrawCelestialBackground(DrawingContext context, Point center, double width, double height)
    {
        // 微弱深空径向渐变底色
        var bgBrush = new RadialGradientBrush
        {
            Center = new RelativePoint(center.X / width, center.Y / height, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(center.X / width, center.Y / height, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(0.85, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(0.85, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(35, 14, 165, 233), 0.0),
                new GradientStop(Color.FromArgb(0, 10, 15, 25), 1.0)
            }
        };
        context.FillRectangle(bgBrush, new Rect(0, 0, width, height));

        // 同心引力轨道 (110px, 190px, 270px)
        var orbitRadii = new[] { 110.0, 190.0, 270.0 };
        var orbitLabels = new[] { "I · 必需依赖 (Depends)", "II · 推荐与扩展 (Recommends / Suggests)", "III · 冲突与警告 (Conflicts / Incompatible)" };
        var orbitBrush = ResolveThemeBrush("CkanBorderBrush");
        var orbitPen = new Pen(orbitBrush, 1.0, OrbitDash);
        var labelBrush = ResolveThemeBrush("CkanTextSecondaryBrush");

        for (var i = 0; i < orbitRadii.Length; i++)
        {
            var r = orbitRadii[i] * zoom;
            context.DrawEllipse(null, orbitPen, center, r, r);

            // 轨道标签
            if (zoom >= 0.75)
            {
                var labelText = new FormattedText(
                    orbitLabels[i],
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    Typeface.Default,
                    9.5,
                    labelBrush);
                context.DrawText(labelText, new Point(center.X + 8, center.Y - r - 12));
            }
        }
    }

    private void DrawEdge(DrawingContext context, RelationGraphEdge edge, Point center)
    {
        var sourcePt = GetScreenPos(edge.Source, center);
        var targetPt = GetScreenPos(edge.Target, center);

        var isHighlighted = (hoveredNode is not null && (edge.Source == hoveredNode || edge.Target == hoveredNode)) ||
                            (SelectedNode is not null && (edge.Source == SelectedNode || edge.Target == SelectedNode));

        var isDimmed = !isHighlighted && (edge.Target.IsFiltered || (hoveredNode is not null && hoveredNode != edge.Source && hoveredNode != edge.Target));

        var brush = GetRelationBrush(edge.Kind);
        var alpha = isHighlighted ? (byte)245 : (isDimmed ? (byte)35 : (byte)150);
        var edgeColor = (brush is ISolidColorBrush scb) ? Color.FromArgb(alpha, scb.Color.R, scb.Color.G, scb.Color.B) : Color.FromArgb(alpha, 120, 150, 190);
        var pen = new Pen(new SolidColorBrush(edgeColor), isHighlighted ? 2.5 : 1.2);

        // 绘制引力连线
        context.DrawLine(pen, sourcePt, targetPt);

        // 绘制沿线的方向粒子/小箭头
        var midX = sourcePt.X + (targetPt.X - sourcePt.X) * 0.65;
        var midY = sourcePt.Y + (targetPt.Y - sourcePt.Y) * 0.65;
        var dotRadius = isHighlighted ? 3.5 : 2.0;
        context.DrawEllipse(new SolidColorBrush(edgeColor), null, new Point(midX, midY), dotRadius, dotRadius);
    }

    private void DrawNode(DrawingContext context, RelationGraphNode node, Point center)
    {
        var pt = GetScreenPos(node, center);
        var baseRadius = node.Radius * zoom;
        var isHover = node == hoveredNode;
        var isSelect = node == SelectedNode;
        var r = isHover ? baseRadius + 3.0 : baseRadius;

        var kindBrush = GetRelationBrush(node.Kind);
        var cardBg = ResolveThemeBrush("CkanSurfaceCardBrush");

        // 如果节点被分类过滤掉了，以微弱半透明渲染
        var opacity = node.IsFiltered ? 0.25 : 1.0;

        // 1. 光晕与选中光环
        if (isSelect || isHover || node.IsRoot)
        {
            var haloBrush = node.IsRoot
                ? new SolidColorBrush(Color.FromArgb(80, 14, 165, 233))
                : (kindBrush is ISolidColorBrush kb ? new SolidColorBrush(Color.FromArgb(70, kb.Color.R, kb.Color.G, kb.Color.B)) : new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)));
            context.DrawEllipse(haloBrush, null, pt, r + 6.0, r + 6.0);
        }

        // 2. 节点主体
        if (node.IsRoot)
        {
            var rootFill = new RadialGradientBrush
            {
                Center = new RelativePoint(0.4, 0.4, RelativeUnit.Relative),
                GradientOrigin = new RelativePoint(0.3, 0.3, RelativeUnit.Relative),
                RadiusX = new RelativeScalar(0.8, RelativeUnit.Relative),
                RadiusY = new RelativeScalar(0.8, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromRgb(56, 189, 248), 0.0),
                    new GradientStop(Color.FromRgb(2, 132, 199), 1.0)
                }
            };
            var rootStroke = new Pen(new SolidColorBrush(Color.FromArgb((byte)(255 * opacity), 255, 255, 255)), 2.5);
            context.DrawEllipse(rootFill, rootStroke, pt, r, r);
        }
        else
        {
            var fillBrush = (cardBg is ISolidColorBrush cb)
                ? new SolidColorBrush(Color.FromArgb((byte)(cb.Color.A * opacity), cb.Color.R, cb.Color.G, cb.Color.B))
                : cardBg;
            var strokeColor = (kindBrush is ISolidColorBrush kb)
                ? Color.FromArgb((byte)(255 * opacity), kb.Color.R, kb.Color.G, kb.Color.B)
                : Color.FromArgb((byte)(255 * opacity), 140, 160, 180);
            var strokePen = new Pen(new SolidColorBrush(strokeColor), isHover || isSelect ? 2.5 : 1.8);

            context.DrawEllipse(fillBrush, strokePen, pt, r, r);

            // 节点内核心圆点
            context.DrawEllipse(new SolidColorBrush(strokeColor), null, pt, Math.Max(2.5, r * 0.35), Math.Max(2.5, r * 0.35));
        }

        // 3. 节点文字
        var textBrush = ResolveThemeBrush("CkanTextPrimaryBrush");
        var displayLabel = node.IsRoot ? $"★ {node.DisplayText}" : node.DisplayText;
        if (displayLabel.Length > 16 && !isHover)
        {
            displayLabel = displayLabel.Substring(0, 14) + "…";
        }

        var label = new FormattedText(
            displayLabel,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            Typeface.Default,
            node.IsRoot ? 11.5 : 10.5,
            textBrush);

        context.DrawText(label, new Point(pt.X - label.Width / 2.0, pt.Y + r + 4));
    }

    private void DrawHoverCard(DrawingContext context, RelationGraphNode node, Point center)
    {
        var pt = GetScreenPos(node, center);
        var cardWidth = 220.0;
        var cardHeight = 74.0;
        var cardX = Math.Clamp(pt.X - cardWidth / 2.0, 10, Bounds.Width - cardWidth - 10);
        var cardY = pt.Y - node.Radius * zoom - cardHeight - 12;
        if (cardY < 10)
        {
            cardY = pt.Y + node.Radius * zoom + 32;
        }

        var cardRect = new Rect(cardX, cardY, cardWidth, cardHeight);
        var bg = new SolidColorBrush(Color.FromArgb(235, 15, 23, 42));
        var borderPen = new Pen(new SolidColorBrush(Color.FromArgb(180, 56, 189, 248)), 1.2);

        // 卡片圆角外框与阴影
        context.DrawRectangle(bg, borderPen, new RoundedRect(cardRect, 6));

        // 标题与分类
        var titleText = new FormattedText(
            node.DisplayText,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            Typeface.Default,
            12.0,
            Brushes.White);
        context.DrawText(titleText, new Point(cardX + 10, cardY + 8));

        var kindBrush = GetRelationBrush(node.Kind);
        var subText = new FormattedText(
            $"{node.BadgeText} {node.Identifier}",
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            Typeface.Default,
            10.5,
            kindBrush);
        context.DrawText(subText, new Point(cardX + 10, cardY + 28));

        var tipText = new FormattedText(
            node.IsRoot ? "中心主星（当前查看模组）" : (node.IsKnown ? "双击直接导航到该模组 ↗" : "未知外部模组（无法跳转）"),
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            Typeface.Default,
            9.5,
            node.IsKnown ? new SolidColorBrush(Color.FromRgb(148, 163, 184)) : new SolidColorBrush(Color.FromRgb(239, 68, 68)));
        context.DrawText(tipText, new Point(cardX + 10, cardY + 48));
    }

    private void DrawEmptyHint(DrawingContext context, Point center)
    {
        var brush = ResolveThemeBrush("CkanTextSecondaryBrush");
        var text = new FormattedText(
            "当前模组暂无任何依赖或外部关联关系",
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            Typeface.Default,
            13,
            brush);
        context.DrawText(text, new Point(center.X - text.Width / 2.0, center.Y - text.Height / 2.0));
    }

    private Point GetScreenPos(RelationGraphNode node, Point center)
    {
        return new Point(center.X + node.X * zoom, center.Y + node.Y * zoom);
    }

    private RelationGraphNode? HitTest(Point pos)
    {
        var nodes = Nodes?.ToList();
        if (nodes is null || nodes.Count == 0) return null;

        var center = new Point(Bounds.Width / 2.0 + panOffset.X, Bounds.Height / 2.0 + panOffset.Y);

        // 逆序优先检查顶层节点
        for (var i = nodes.Count - 1; i >= 0; i--)
        {
            var node = nodes[i];
            var screenPos = GetScreenPos(node, center);
            var r = (node.Radius + 6.0) * zoom;
            var dx = pos.X - screenPos.X;
            var dy = pos.Y - screenPos.Y;
            if (dx * dx + dy * dy <= r * r)
            {
                return node;
            }
        }

        return null;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        var pos = e.GetPosition(this);
        var hit = HitTest(pos);

        if (hit is not null)
        {
            SelectedNode = hit;
            if (e.ClickCount >= 2 && !hit.IsRoot && hit.IsKnown)
            {
                NavigateCommand?.Execute(hit.Identifier);
            }
            InvalidateVisual();
        }
        else
        {
            isPanning = true;
            panStart = pos;
            SelectedNode = null;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        var pos = e.GetPosition(this);
        if (isPanning)
        {
            panOffset = new Point(panOffset.X + (pos.X - panStart.X), panOffset.Y + (pos.Y - panStart.Y));
            panStart = pos;
            InvalidateVisual();
            return;
        }

        var hit = HitTest(pos);
        if (hit != hoveredNode)
        {
            hoveredNode = hit;
            Cursor = hit is not null ? new Cursor(StandardCursorType.Hand) : new Cursor(StandardCursorType.Arrow);
            InvalidateVisual();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        isPanning = false;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        var delta = e.Delta.Y > 0 ? 1.15 : 0.85;
        zoom = Math.Clamp(zoom * delta, 0.4, 3.0);
        InvalidateVisual();
    }

    private IBrush GetRelationBrush(RelationKind kind)
    {
        return (IBrush)RelationKindToBrushConverter.Instance.Convert(kind, typeof(IBrush), null, CultureInfo.CurrentCulture)!;
    }

    private IBrush ResolveThemeBrush(string key)
    {
        if (this.TryFindResource(key, out var resource) && resource is IBrush brush)
        {
            return brush;
        }

        if (this.TryFindResource("CkanTextPrimaryBrush", out var fallback) && fallback is IBrush fb)
        {
            return fb;
        }

        return Brushes.White;
    }
}
