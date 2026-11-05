using System.Collections.Specialized;
using System.Globalization;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
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

    /// <summary>把 270px 最外圈（含节点半径）完整装进当前可用区域的自适应缩放系数。</summary>
    private double FitScale => Math.Clamp(
        Math.Min(Bounds.Width, Bounds.Height) / (2.0 * (270.0 + 46.0)),
        0.28,
        1.0);

    /// <summary>用户缩放 × 自适应缩放后的实际绘制比例。</summary>
    private double Scale => zoom * FitScale;

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

        var nodes = Nodes?.ToList();
        var edges = Edges?.ToList();

        if (nodes is null || nodes.Count == 0)
        {
            // 1. 绘制太空深空背景
            DrawCelestialBackground(context, center, width, height, null);
            DrawEmptyHint(context, center);
            return;
        }

        // 1. 绘制太空深空背景与同心天体引力轨道（仅绘制实际有节点的轨道）
        DrawCelestialBackground(context, center, width, height, nodes);

        // 2. 先统一排布所有节点标签（含防重叠），再画连线与节点
        var labels = BuildLabels(nodes, center);
        var labelMap = new Dictionary<RelationGraphNode, NodeLabel>(labels.Count);
        foreach (var label in labels)
        {
            labelMap[label.Node] = label;
        }

        // 3. 绘制节点间的引力连线
        if (edges is not null && edges.Count > 0)
        {
            foreach (var edge in edges)
            {
                DrawEdge(context, edge, center);
            }
        }

        // 4. 绘制节点天体（先画非根节点，最后画根节点与高亮节点）
        foreach (var node in nodes.Where(n => !n.IsRoot && n != hoveredNode && n != SelectedNode))
        {
            DrawNode(context, node, center, labelMap.GetValueOrDefault(node));
        }

        var rootNode = nodes.FirstOrDefault(n => n.IsRoot);
        if (rootNode is not null && rootNode != hoveredNode && rootNode != SelectedNode)
        {
            DrawNode(context, rootNode, center, labelMap.GetValueOrDefault(rootNode));
        }

        // 优先将选中和悬停节点浮于顶层
        if (SelectedNode is not null && SelectedNode != hoveredNode)
        {
            DrawNode(context, SelectedNode, center, labelMap.GetValueOrDefault(SelectedNode));
        }

        if (hoveredNode is not null)
        {
            DrawNode(context, hoveredNode, center, labelMap.GetValueOrDefault(hoveredNode));
            DrawHoverCard(context, hoveredNode, center);
        }
    }

    private void DrawCelestialBackground(
        DrawingContext context,
        Point center,
        double width,
        double height,
        IReadOnlyList<RelationGraphNode>? nodes)
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

        // 同心引力轨道 (110px, 190px, 270px)，仅绘制实际承载节点的轨道。
        // 轨道不再画罗马数字说明：400px 宽的详情面板里那圈文字必然压到节点标签，
        // 分类语义已经由上方计数胶囊（依赖/推荐/冲突 + 颜色 + 数量）承担。
        var orbitRadii = new[] { 110.0, 190.0, 270.0 };
        var orbitBrush = ResolveThemeBrush("CkanBorderBrush");
        var orbitPen = new Pen(orbitBrush, 1.0, OrbitDash);

        for (var i = 0; i < orbitRadii.Length; i++)
        {
            var ringIndex = i + 1;
            var hasNodes = nodes is not null && nodes.Any(n => !n.IsRoot && n.OrbitRing == ringIndex);
            if (!hasNodes)
            {
                continue;
            }

            var r = orbitRadii[i] * Scale;
            context.DrawEllipse(null, orbitPen, center, r, r);
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

    private void DrawNode(DrawingContext context, RelationGraphNode node, Point center, NodeLabel? label)
    {
        var pt = GetScreenPos(node, center);
        var baseRadius = node.Radius * Scale;
        var isHover = node == hoveredNode;
        var isSelect = node == SelectedNode;
        var r = isHover ? baseRadius + 3.0 : baseRadius;

        var kindBrush = GetRelationBrush(node.Kind);
        var cardBg = ResolveThemeBrush("CkanCardBackgroundBrush");

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

            // 未满足的必需依赖：右上角危险标记
            if (!node.IsSatisfied && node.Kind == RelationKind.Depends)
            {
                var dangerColor = ResolveThemeBrush("CkanDangerBrush") is ISolidColorBrush db
                    ? Color.FromArgb(255, db.Color.R, db.Color.G, db.Color.B)
                    : Color.FromArgb(255, 179, 38, 30);
                var dotRadius = Math.Max(3.0, r * 0.3);
                context.DrawEllipse(new SolidColorBrush(dangerColor), null,
                    new Point(pt.X + r * 0.72, pt.Y - r * 0.72), dotRadius, dotRadius);
            }
        }

        // 3. 节点文字（位置由 BuildLabels 统一排布并做过防重叠处理）
        if (label is not null)
        {
            context.DrawText(label.Text, new Point(label.X, label.Y));
        }
    }

    /// <summary>
    /// 统一排布所有节点标签。详情面板只有 320–640px 宽，完整模组名经常比两条轨道半径还宽；
    /// 这里给每个节点在自己四周（右/左/上/下 × 近/远两档）找第一个不压字、不压圆的空位，
    /// 找不到就逐步缩短标签再试。实在挤不下才不画（悬停卡片仍显示完整名字），
    /// 而不是把标签甩到画布边缘——那样标签和节点对不上，比不画更糟。
    /// </summary>
    private List<NodeLabel> BuildLabels(IReadOnlyList<RelationGraphNode> nodes, Point center)
    {
        var textBrush = ResolveThemeBrush("CkanTextPrimaryBrush");
        var labels = new List<NodeLabel>(nodes.Count);
        var placed = new List<Rect>(nodes.Count);
        var circles = new List<(Point Center, double Radius)>(nodes.Count);

        foreach (var node in nodes)
        {
            circles.Add((GetScreenPos(node, center), node.Radius * Scale + 5.0));
        }

        // 根节点优先占位，其次由内向外：重要的标签先挑位置
        foreach (var node in nodes.OrderBy(n => n.IsRoot ? 0 : n.OrbitRing + 1))
        {
            var displayLabel = node.IsRoot ? $"★ {node.DisplayText}" : node.DisplayText;
            var fontSize = node.IsRoot ? 11.5 : (Scale < 0.6 ? 9.5 : 10.5);
            var available = Math.Max(48.0, Bounds.Width * (node.IsRoot ? 0.62 : 0.46));

            var pt = GetScreenPos(node, center);
            var r = node.Radius * Scale;
            Rect? chosen = null;

            // 由宽到窄四档：先试完整名字，挤不下再逐级截短，避免窄栏里一片「标签消失」
            foreach (var factor in new[] { 1.0, 0.78, 0.62, 0.45 })
            {
                var text = FitLabel(displayLabel, fontSize, available * factor, textBrush);
                var w = text.Width;
                var h = text.Height;

                foreach (var candidate in BuildLabelCandidates(node, pt, r, w, h))
                {
                    var rect = new Rect(
                        Math.Clamp(candidate.X, 4.0, Math.Max(4.0, Bounds.Width - w - 4.0)),
                        Math.Clamp(candidate.Y, 2.0, Math.Max(2.0, Bounds.Height - h - 2.0)),
                        w,
                        h);

                    if (Collides(rect, placed, circles))
                    {
                        continue;
                    }

                    chosen = rect;
                    break;
                }

                if (chosen is not null)
                {
                    labels.Add(new NodeLabel
                    {
                        Node = node,
                        Text = text,
                        X = chosen.Value.X,
                        Y = chosen.Value.Y
                    });
                    placed.Add(chosen.Value);
                    break;
                }
            }

            // 根节点必须始终可读：实在挤不下就压在圆的正下方，允许轻微重叠
            if (chosen is null && node.IsRoot)
            {
                var text = FitLabel(displayLabel, fontSize, available, textBrush);
                var rect = new Rect(
                    Math.Clamp(pt.X - text.Width / 2.0, 4.0, Math.Max(4.0, Bounds.Width - text.Width - 4.0)),
                    Math.Clamp(pt.Y + r + 6.0, 2.0, Math.Max(2.0, Bounds.Height - text.Height - 2.0)),
                    text.Width,
                    text.Height);

                labels.Add(new NodeLabel
                {
                    Node = node,
                    Text = text,
                    X = rect.X,
                    Y = rect.Y
                });
                placed.Add(rect);
            }
        }

        return labels;
    }

    /// <summary>
    /// 节点四周的候选槽位：朝外的方向优先，近档放不下还有远档（更远离自己的圆），
    /// 窄栏里多两个空位就少一次「标签画不出来」。
    /// </summary>
    private static IEnumerable<(double X, double Y)> BuildLabelCandidates(
        RelationGraphNode node, Point pt, double r, double w, double h)
    {
        static IEnumerable<(double X, double Y)> Slots(Point pt, double r, double w, double h, double gap)
        {
            yield return (pt.X + r + gap, pt.Y - h / 2.0);          // 右
            yield return (pt.X - r - gap - w, pt.Y - h / 2.0);      // 左
            yield return (pt.X - w / 2.0, pt.Y - r - gap - h);      // 上
            yield return (pt.X - w / 2.0, pt.Y + r + gap);          // 下
        }

        var near = Slots(pt, r, w, h, 6.0).ToList();
        var far = Slots(pt, r, w, h, 6.0 + h * 0.85).ToList();

        if (node.IsRoot)
        {
            foreach (var slot in near.Concat(far))
            {
                yield return slot;
            }

            yield break;
        }

        var dirX = node.X;
        var dirY = node.Y;
        var length = Math.Sqrt(dirX * dirX + dirY * dirY);
        if (length < 0.001)
        {
            length = 1;
        }

        dirX /= length;
        dirY /= length;

        int[] order;
        if (Math.Abs(dirX) >= Math.Abs(dirY))
        {
            order = dirX >= 0 ? new[] { 0, 2, 3, 1 } : new[] { 1, 2, 3, 0 };
        }
        else
        {
            order = dirY >= 0 ? new[] { 3, 0, 1, 2 } : new[] { 2, 0, 1, 3 };
        }

        // 先按方位把近档排一遍，再按同样顺序补远档
        foreach (var index in order)
        {
            yield return near[index];
        }

        foreach (var index in order)
        {
            yield return far[index];
        }
    }

    private static bool Collides(
        Rect rect,
        IReadOnlyList<Rect> placed,
        IReadOnlyList<(Point Center, double Radius)> circles)
    {
        // 视觉间隙：两段文字贴在一起会被读成一整串（如 "ScattererRealPlume"）
        var padded = new Rect(rect.X - 8.0, rect.Y - 5.0, rect.Width + 16.0, rect.Height + 10.0);
        foreach (var other in placed)
        {
            if (padded.Intersects(other))
            {
                return true;
            }
        }

        foreach (var circle in circles)
        {
            var closestX = Math.Clamp(circle.Center.X, padded.X, padded.Right);
            var closestY = Math.Clamp(circle.Center.Y, padded.Y, padded.Bottom);
            var dx = closestX - circle.Center.X;
            var dy = closestY - circle.Center.Y;
            if (dx * dx + dy * dy < circle.Radius * circle.Radius)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>生成一条不超过 maxWidth 的标签：超宽时逐字截断补省略号，保证标签不会互相压字。</summary>
    private static FormattedText FitLabel(string raw, double fontSize, double maxWidth, IBrush brush)
    {
        var text = CreateText(raw, fontSize, brush);
        if (text.Width <= maxWidth)
        {
            return text;
        }

        for (var length = raw.Length - 1; length > 2; length--)
        {
            // 不要从代理对中间截断（模组名里偶有 emoji）
            if (char.IsLowSurrogate(raw[length]))
            {
                continue;
            }

            var candidate = CreateText(string.Concat(raw.AsSpan(0, length), "…"), fontSize, brush);
            if (candidate.Width <= maxWidth)
            {
                return candidate;
            }
        }

        return CreateText(string.Concat(raw.AsSpan(0, 1), "…"), fontSize, brush);
    }

    private static FormattedText CreateText(string value, double fontSize, IBrush brush) =>
        new(value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, fontSize, brush);

    private sealed class NodeLabel
    {
        public required RelationGraphNode Node { get; init; }

        public required FormattedText Text { get; init; }

        public double X { get; set; }

        public double Y { get; set; }

        public Rect Bounds => new(X, Y, Text.Width, Text.Height);
    }

    private void DrawHoverCard(DrawingContext context, RelationGraphNode node, Point center)
    {
        var pt = GetScreenPos(node, center);
        var cardWidth = 220.0;
        var cardHeight = 74.0;
        var cardX = Math.Clamp(pt.X - cardWidth / 2.0, 10, Math.Max(10, Bounds.Width - cardWidth - 10));
        var cardY = pt.Y - node.Radius * Scale - cardHeight - 12;
        if (cardY < 10)
        {
            cardY = pt.Y + node.Radius * Scale + 32;
        }

        var cardRect = new Rect(cardX, cardY, cardWidth, cardHeight);
        var bg = ResolveThemeBrush("CkanCardBackgroundBrush");
        var borderPen = new Pen(ResolveThemeBrush("CkanBorderBrush"), 1.2);

        // 卡片圆角外框与阴影
        context.DrawRectangle(bg, borderPen, new RoundedRect(cardRect, 6));

        // 标题与分类
        var titleText = new FormattedText(
            node.DisplayText,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            Typeface.Default,
            12.0,
            ResolveThemeBrush("CkanTextPrimaryBrush"));
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
            node.IsKnown ? ResolveThemeBrush("CkanTextSecondaryBrush") : ResolveThemeBrush("CkanDangerBrush"));
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
        return new Point(center.X + node.X * Scale, center.Y + node.Y * Scale);
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
            var r = (node.Radius + 6.0) * Scale;
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
        // 必须显式带上 ActualThemeVariant：不带主题的 TryFindResource 会落到
        // ThemeDictionaries 里的第一个主题（暗色），浅色主题下文字/卡片会变成
        // 暗色主题的颜色 —— 白底白字，等于没画。
        if (this.TryFindResource(key, ActualThemeVariant, out var resource) && resource is IBrush brush)
        {
            return brush;
        }

        return ActualThemeVariant == ThemeVariant.Dark ? Brushes.White : Brushes.Black;
    }
}
