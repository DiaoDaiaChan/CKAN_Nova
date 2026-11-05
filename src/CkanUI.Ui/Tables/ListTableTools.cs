using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace CkanUI.Ui.Tables;

/// <summary>
/// 手画表头的列表表格工具：给「Border+Grid 表头 + ListBox 行模板」这种手写表格补上筛选、排序、列宽拖动。
/// </summary>
/// <remarks>
/// <para>宿主与首方插件共用：<c>CkanUI.App</c> 的 <c>TableTools</c> 只服务 <c>DataGrid</c>，
/// 而仓里绝大多数表格是手画表头（表头 Grid 与行模板 Grid 各写一份相同的 <c>ColumnDefinitions</c>），
/// DataGrid 那套用不上。这个附加行为把同一份能力补到手写表格上，两边的交互保持一致。</para>
/// <para>用法（三步，表头 <c>Grid</c> 与 <c>ListBox</c> 必须是同一个父级下的兄弟，工具自己找表头）：</para>
/// <code>
/// &lt;ListBox ui:ListTableTools.Enabled="True"
///          ui:ListTableTools.Columns="-,Name,KindLabel,VersionText" ... /&gt;
/// </code>
/// <para><c>Columns</c> 按下标对齐表头的列：写属性路径就开这一列的排序与筛选，
/// 写 <c>-</c>（或留空）保留纯展示列（缩略图、复选框、状态条）。</para>
/// <para>筛选写进包装视图的 <see cref="TableView.Filter"/>，多列条件按「与」叠加；排序按取值比较，
/// 数字与版本号按语义比较而不是字典序（<c>1.10 &gt; 1.9</c>）。列宽拖动同时改表头与已实化的行 Grid，
/// 新实化的行在 <see cref="ItemsControl.ContainerPrepared"/> 里补同一份宽度。</para>
/// </remarks>
public sealed class ListTableTools : AvaloniaObject
{
    private ListTableTools()
    {
    }

    /// <summary>打开表格工具（表头筛选入口 + 点击表头排序 + 拖动列边界调宽）。</summary>
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<ListTableTools, ItemsControl, bool>("Enabled");

    /// <summary>列定义：逗号分隔的属性路径，按表头列下标对齐；<c>-</c> 表示这一列不排序不筛选。</summary>
    public static readonly AttachedProperty<string?> ColumnsProperty =
        AvaloniaProperty.RegisterAttached<ListTableTools, ItemsControl, string?>("Columns");

    private const string FunnelPath = "M2,3 H12 L8,7.2 V11.4 L6,10.2 V7.2 Z";

    private static readonly ConditionalWeakTable<ItemsControl, TableState> States = new();

    static ListTableTools()
    {
        EnabledProperty.Changed.AddClassHandler<ItemsControl>(static (control, args) =>
        {
            if (args.GetNewValue<bool>())
            {
                Attach(control);
            }
            else if (States.TryGetValue(control, out var state))
            {
                state.Detach();
                States.Remove(control);
            }
        });
    }

    /// <summary>读取开关。</summary>
    /// <param name="control">列表。</param>
    public static bool GetEnabled(ItemsControl control) => control.GetValue(EnabledProperty);

    /// <summary>设置开关。</summary>
    /// <param name="control">列表。</param>
    /// <param name="value">是否开启。</param>
    public static void SetEnabled(ItemsControl control, bool value) => control.SetValue(EnabledProperty, value);

    /// <summary>读取列定义。</summary>
    /// <param name="control">列表。</param>
    public static string? GetColumns(ItemsControl control) => control.GetValue(ColumnsProperty);

    /// <summary>设置列定义。</summary>
    /// <param name="control">列表。</param>
    /// <param name="value">逗号分隔的属性路径。</param>
    public static void SetColumns(ItemsControl control, string? value) => control.SetValue(ColumnsProperty, value);

    /// <summary>给某一列设筛选词（空白等于清除）。列下标按表头顺序。</summary>
    /// <param name="control">列表。</param>
    /// <param name="columnIndex">列下标。</param>
    /// <param name="text">筛选词；空格分隔多个条件，前缀 <c>-</c> 表示排除。</param>
    public static void SetFilter(ItemsControl control, int columnIndex, string? text)
    {
        if (States.TryGetValue(control, out var state))
        {
            state.SetFilter(columnIndex, text);
        }
    }

    /// <summary>读取某一列的筛选词。</summary>
    /// <param name="control">列表。</param>
    /// <param name="columnIndex">列下标。</param>
    public static string? GetFilter(ItemsControl control, int columnIndex)
        => States.TryGetValue(control, out var state) ? state.GetFilter(columnIndex) : null;

    /// <summary>清空所有列的筛选词。</summary>
    /// <param name="control">列表。</param>
    public static void ClearFilters(ItemsControl control)
    {
        if (States.TryGetValue(control, out var state))
        {
            state.ClearFilters();
        }
    }

    /// <summary>按某一列排序。<paramref name="descending"/> 为 <c>null</c> 表示取消排序（回到原始顺序）。</summary>
    /// <param name="control">列表。</param>
    /// <param name="columnIndex">列下标。</param>
    /// <param name="descending">排序方向。</param>
    public static void SortBy(ItemsControl control, int columnIndex, bool? descending)
    {
        if (States.TryGetValue(control, out var state))
        {
            state.SortBy(columnIndex, descending);
        }
    }

    /// <summary>读取当前排序的列下标（<c>-1</c> 表示未排序）。</summary>
    /// <param name="control">列表。</param>
    public static int GetSortColumn(ItemsControl control)
        => States.TryGetValue(control, out var state) ? state.SortColumn : -1;

    /// <summary>读取当前排序方向。</summary>
    /// <param name="control">列表。</param>
    public static bool GetSortDescending(ItemsControl control)
        => States.TryGetValue(control, out var state) && state.SortDescending;

    private static void Attach(ItemsControl control)
    {
        var state = States.GetValue(control, static c => new TableState(c));
        state.Initialize();
    }

    /// <summary>单个列表的运行时状态：包装视图、表头装饰、列宽。</summary>
    private sealed class TableState
    {
        private readonly ItemsControl _list;
        private readonly Dictionary<int, Button> _headerButtons = new();
        private readonly Dictionary<int, TextBlock> _arrows = new();
        private readonly Dictionary<int, string> _filters = new();
        private readonly Dictionary<int, double> _widths = new();
        private Grid? _header;
        private TableView? _view;
        private string[] _paths = [];
        private bool _wrapping;
#pragma warning disable CS0414
        private bool _applyingWidths;
#pragma warning restore CS0414
        private bool _syncingGeometry;

        public TableState(ItemsControl list)
        {
            _list = list;
        }

        public int SortColumn { get; private set; } = -1;

        public bool SortDescending { get; private set; }

        public void Initialize()
        {
            _list.PropertyChanged += OnListPropertyChanged;
            _list.ContainerPrepared += OnContainerPrepared;
            _list.AttachedToVisualTree += OnAttachedToVisualTree;
            // 排序/筛选之后行会换位置，交给重排动画滑过去（token 关到瞬时时它自己不演）。
            TableReorderMotion.SetIsEnabled(_list, true);
            if (TopLevel.GetTopLevel(_list) is not null)
            {
                HookHeader();
            }

            WrapSource();
        }

        public void Detach()
        {
            _list.PropertyChanged -= OnListPropertyChanged;
            _list.ContainerPrepared -= OnContainerPrepared;
            _list.AttachedToVisualTree -= OnAttachedToVisualTree;
        }

        public void SetFilter(int columnIndex, string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                _filters.Remove(columnIndex);
            }
            else
            {
                _filters[columnIndex] = text.Trim();
            }

            UpdateHeaderStates();
            // 先留旧位置快照，再刷新：行才知道自己该从哪儿滑到哪儿。
            TableReorderMotion.Play(_list);
            _view?.Refresh();
        }

        public string? GetFilter(int columnIndex) => _filters.TryGetValue(columnIndex, out var text) ? text : null;

        public void ClearFilters()
        {
            _filters.Clear();
            UpdateHeaderStates();
            TableReorderMotion.Play(_list);
            _view?.Refresh();
        }

        public void SortBy(int columnIndex, bool? descending)
        {
            if (descending is null || columnIndex < 0 || columnIndex >= _paths.Length || IsSkipped(_paths[columnIndex]))
            {
                SortColumn = -1;
                SortDescending = false;
            }
            else
            {
                SortColumn = columnIndex;
                SortDescending = descending.Value;
            }

            UpdateHeaderStates();
            TableReorderMotion.Play(_list);
            _view?.Refresh();
        }

        private void OnAttachedToVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e) => HookHeader();

        private void OnListPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == ItemsSourceProperty)
            {
                if (_wrapping)
                {
                    return;
                }

                WrapSource();
            }
        }

        private static AvaloniaProperty ItemsSourceProperty => ItemsControl.ItemsSourceProperty;

        private void WrapSource()
        {
            var source = _list.ItemsSource;
            if (source is null || source is TableView)
            {
                return;
            }

            _wrapping = true;
            try
            {
                var previous = (_list as SelectingItemsControl)?.SelectedItem;
                _view = new TableView(source);
                _view.SetFilterPredicate(Matches);
                _view.SetComparison(CompareRows);
                _view.Refresh();
                // 绑定场景下用 SetCurrentValue 保住绑定语义；若列表本身是直接赋值的局部值，
                // SetCurrentValue 会被局部优先值盖掉，退回到 SetValue 硬替换，否则筛选/排序整个不生效。
                _list.SetCurrentValue(ItemsControl.ItemsSourceProperty, _view);
                if (!ReferenceEquals(_list.ItemsSource, _view))
                {
                    _list.SetValue(ItemsControl.ItemsSourceProperty, _view);
                }

                // 换 ItemsSource 会让 ListBox 清掉选中项，选中行在详情面板里还要用，装回去。
                if (_list is SelectingItemsControl selecting && previous is not null && _view.Contains(previous))
                {
                    selecting.SelectedItem = previous;
                }
            }
            finally
            {
                _wrapping = false;
            }
        }

        private void OnContainerPrepared(object? sender, ContainerPreparedEventArgs e)
        {
            if (_widths.Count == 0 || e.Container is not Control container)
            {
                return;
            }

            ApplyWidthsTo(container);
        }

        private void HookHeader()
        {
            if (_header is not null)
            {
                return;
            }

            var columns = SplitColumns(GetColumns(_list));
            _paths = columns;
            if (columns.Length == 0 || _list.Parent is not Control parent)
            {
                return;
            }

            // 表头不一定是列表的直接兄弟：KspConfig 这类页面把列表再包了一层 Grid，
            // 表头在祖父级。向上找最多三层，避免为每个页面写死查找路径。
            var header = FindHeader(parent, columns.Length);
            for (var level = 0; header is null && level < 3 && parent.Parent is Control up; level++)
            {
                parent = up;
                header = FindHeader(parent, columns.Length);
            }

            if (header is null)
            {
                return;
            }

            _header = header;
            for (var i = 0; i < columns.Length; i++)
            {
                if (!IsSkipped(columns[i]))
                {
                    Decorate(header, i, columns[i]);
                }
            }

            // 表头和行不是一个容器：行可能被 ListBoxItem 的内边距推进来，右侧还可能被
            // 竖直滚动条吃掉十几像素。列宽一致也没用，起点/终点不齐一样看着错位。
            // 每次布局把表头内缩到「行的实际内容区」，两侧对齐就总是成立的。
            _list.LayoutUpdated += OnListLayoutUpdated;
            SyncHeaderGeometry();
        }

        private void OnListLayoutUpdated(object? sender, EventArgs e) => SyncHeaderGeometry();

        /// <summary>把表头左右内缩到行模板的实际内容区（抵消列表内边距与滚动条宽度）。</summary>
        private void SyncHeaderGeometry()
        {
            if (_header is null || _paths.Length == 0 || _syncingGeometry)
            {
                return;
            }

            var container = _list.GetRealizedContainers().FirstOrDefault();
            if (container is null)
            {
                return;
            }

            var row = DescendantGrids(container).FirstOrDefault(g => g.ColumnDefinitions.Count == _paths.Length);
            if (row is null || TopLevel.GetTopLevel(_list) is not Visual root)
            {
                return;
            }

            var headerLeft = _header.TranslatePoint(default, root)?.X;
            var headerRight = _header.TranslatePoint(new Point(_header.Bounds.Width, 0), root)?.X;
            var rowLeft = row.TranslatePoint(default, root)?.X;
            var rowRight = row.TranslatePoint(new Point(row.Bounds.Width, 0), root)?.X;
            if (headerLeft is null || headerRight is null || rowLeft is null || rowRight is null)
            {
                return;
            }

            var deltaLeft = rowLeft.Value - headerLeft.Value;
            var deltaRight = headerRight.Value - rowRight.Value;
            if (Math.Abs(deltaLeft) < 0.5 && Math.Abs(deltaRight) < 0.5)
            {
                return;
            }

            var margin = _header.Margin;
            _syncingGeometry = true;
            try
            {
                _header.Margin = new Thickness(
                    Math.Max(0, margin.Left + deltaLeft),
                    margin.Top,
                    Math.Max(0, margin.Right + deltaRight),
                    margin.Bottom);
            }
            finally
            {
                _syncingGeometry = false;
            }
        }

        private static string[] SplitColumns(string? spec)
        {
            if (string.IsNullOrWhiteSpace(spec))
            {
                return [];
            }

            return spec.Split(',').Select(static part => part.Trim()).ToArray();
        }

        private static bool IsSkipped(string path) => string.IsNullOrEmpty(path) || path == "-";

        /// <summary>
        /// 列定义支持「展示路径~排序路径」：前半段用来筛选（通常是用户看到的文本），
        /// 后半段用来排序（通常是原始值，避免 "1.2 MB"、"3 天前" 这类文本排序）。
        /// 没有 <c>~</c> 时两者相同。
        /// </summary>
        private static string FilterPathOf(string path)
        {
            var index = path.IndexOf('~');
            return index < 0 ? path : path[..index];
        }

        private static string SortPathOf(string path)
        {
            var index = path.IndexOf('~');
            return index < 0 ? path : path[(index + 1)..];
        }

        /// <summary>在列表所在父级里找表头：兄弟节点中列数对得上、且含 <c>CkanCaption</c> 的 Grid。</summary>
        private static Grid? FindHeader(Control parent, int columnCount)
        {
            foreach (var sibling in parent.GetLogicalChildren().OfType<Control>())
            {
                foreach (var grid in DescendantGrids(sibling))
                {
                    if (grid.ColumnDefinitions.Count == columnCount &&
                        grid.GetLogicalChildren().OfType<TextBlock>().Any(t => t.Classes.Contains("CkanCaption")))
                    {
                        return grid;
                    }
                }
            }

            return null;
        }

        private static IEnumerable<Grid> DescendantGrids(Control root)
        {
            if (root is Grid self)
            {
                yield return self;
            }

            foreach (var child in root.GetLogicalChildren().OfType<Control>())
            {
                foreach (var grid in DescendantGrids(child))
                {
                    yield return grid;
                }
            }
        }

        /// <summary>把一列的表头标题换成「标题 + 排序箭头 + 筛选按钮」，并挂上拖动调宽的把手。</summary>
        private void Decorate(Grid header, int index, string path)
        {
            var caption = header.GetLogicalChildren().OfType<TextBlock>()
                .FirstOrDefault(t => Grid.GetColumn(t) == index && t.Classes.Contains("CkanCaption"));
            if (caption is null)
            {
                return;
            }

            // 漏斗紧跟标题文字：权重列放在最后，否则宽列里的漏斗会被推到列右缘，
            // 视觉上跟隔壁列的标题粘在一起（cache 页「文件名」777px 宽时最明显）。
            var holder = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,*") };
            Grid.SetColumn(holder, index);
            holder.VerticalAlignment = VerticalAlignment.Center;
            holder.Cursor = new Cursor(StandardCursorType.Hand);

            var arrow = new TextBlock
            {
                FontSize = 9,
                Margin = new Thickness(4, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = 0,
            };
            Grid.SetColumn(arrow, 1);
            _arrows[index] = arrow;

            var button = BuildFilterButton(index);
            Grid.SetColumn(button, 2);
            _headerButtons[index] = button;

            var parent = caption.Parent as Panel;
            parent?.Children.Remove(caption);
            // 标题原是表头 Grid 的第 index 列，搬进 holder 后必须回到第 0 列，
            // 否则会带着旧列号跑到 holder 之外（表头只剩漏斗、标题消失）。
            Grid.SetColumn(caption, 0);
            caption.VerticalAlignment = VerticalAlignment.Center;
            holder.Children.Add(caption);
            holder.Children.Add(arrow);
            holder.Children.Add(button);
            header.Children.Add(holder);

            holder.PointerPressed += (_, e) =>
            {
                if (e.Source is Visual source && FindAncestor<Button>(source) is not null)
                {
                    return;
                }

                ToggleSort(index);
                e.Handled = true;
            };

            AddResizeHandle(header, index);
        }

        /// <summary>
        /// 列宽拖拽把手：命中区 6px，视觉是一条 1px 的强调色细线。平时完全透明，
        /// 指针移上来才点亮、拖动时全亮 —— 用户先看见边界，才抓得住边界。
        /// </summary>
        private void AddResizeHandle(Grid header, int index)
        {
            var guide = new Border
            {
                Width = 1,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Stretch,
                Background = AccentBrush() ?? Brushes.Gray,
                Opacity = 0,
                IsHitTestVisible = false,
            };

            var handle = new Border
            {
                Width = 6,
                Background = Brushes.Transparent,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Stretch,
                Cursor = new Cursor(StandardCursorType.SizeWestEast),
                Child = guide,
            };
            Grid.SetColumn(handle, index);
            handle.ZIndex = 5;

            var startX = 0d;
            var startWidth = 0d;
            var dragging = false;

            handle.PointerEntered += (_, _) => guide.Opacity = 0.5;
            handle.PointerExited += (_, _) =>
            {
                if (!dragging)
                {
                    guide.Opacity = 0;
                }
            };

            handle.PointerPressed += (_, e) =>
            {
                var point = e.GetCurrentPoint(header);
                startX = point.Position.X;
                startWidth = CurrentWidth(header, index);
                e.Pointer.Capture(handle);
                dragging = true;
                guide.Opacity = 1;
                e.Handled = true;
            };

            handle.PointerMoved += (_, e) =>
            {
                if (!dragging)
                {
                    return;
                }

                var delta = e.GetCurrentPoint(header).Position.X - startX;
                SetWidth(header, index, Math.Max(24, startWidth + delta));
                e.Handled = true;
            };

            handle.PointerReleased += (_, e) =>
            {
                if (!dragging)
                {
                    return;
                }

                dragging = false;
                e.Pointer.Capture(null);
                guide.Opacity = handle.IsPointerOver ? 0.5 : 0;
                e.Handled = true;
            };

            header.Children.Add(handle);
        }

        private static double CurrentWidth(Grid header, int index)
            => header.ColumnDefinitions[index].ActualWidth > 0
                ? header.ColumnDefinitions[index].ActualWidth
                : header.ColumnDefinitions[index].Width.Value;

        private void SetWidth(Grid header, int index, double width)
        {
            _widths[index] = width;
            var length = new GridLength(width, GridUnitType.Pixel);
            if (index < header.ColumnDefinitions.Count)
            {
                header.ColumnDefinitions[index] = new ColumnDefinition(length);
            }

            _applyingWidths = true;
            try
            {
                foreach (var container in _list.GetRealizedContainers())
                {
                    ApplyWidthsTo(container);
                }
            }
            finally
            {
                _applyingWidths = false;
            }
        }

        private void ApplyWidthsTo(Control container)
        {
            // 注意：这里不能再判 _applyingWidths —— SetWidth 自己在同步循环里就是带着这个标志进来的，
            // 一判就把「拖动时行跟着变宽」整个跳过（表头拉宽了、行还是旧宽度，视觉上就是错位）。
            if (_widths.Count == 0)
            {
                return;
            }

            var target = DescendantGrids(container).FirstOrDefault(g => g.ColumnDefinitions.Count == _paths.Length);
            if (target is null)
            {
                return;
            }

            foreach (var pair in _widths)
            {
                if (pair.Key < target.ColumnDefinitions.Count)
                {
                    target.ColumnDefinitions[pair.Key] = new ColumnDefinition(new GridLength(pair.Value, GridUnitType.Pixel));
                }
            }
        }

        private static T? FindAncestor<T>(Visual visual) where T : Visual
        {
            for (Visual? current = visual; current is not null; current = current.GetVisualParent())
            {
                if (current is T match)
                {
                    return match;
                }
            }

            return null;
        }

        private Button BuildFilterButton(int index)
        {
            var button = new Button
            {
                Width = 18,
                Height = 18,
                Padding = new Thickness(0),
                Margin = new Thickness(2, 0, 0, 0),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = 0.45,
                Cursor = new Cursor(StandardCursorType.Hand),
            };
            button.Classes.Add("table-filter");
            var funnel = new Avalonia.Controls.Shapes.Path
            {
                Data = StreamGeometry.Parse(FunnelPath),
                Fill = Brushes.Gray,
                Width = 11,
                Height = 11,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            funnel.Bind(Avalonia.Controls.Shapes.Shape.FillProperty, new Avalonia.Data.Binding(nameof(Button.Foreground)) { Source = button });
            button.Content = funnel;

            var text = new TextBox
            {
                PlaceholderText = Resolve("Ui_Common_FilterHint", "输入关键字，空格分隔多个条件") ?? string.Empty,
                MinWidth = 180,
            };
            var reset = new Button { Content = Resolve("Ui_Common_ResetFilter", "清除") };
            var panel = new StackPanel { Spacing = 6 };
            panel.Children.Add(text);
            panel.Children.Add(reset);

            var flyout = new Flyout { Placement = PlacementMode.BottomEdgeAlignedRight, Content = panel };
            button.Flyout = flyout;

            text.TextChanged += (_, _) => SetFilter(index, text.Text);
            text.KeyDown += (_, e) =>
            {
                if (e.Key is Key.Enter or Key.Escape)
                {
                    flyout.Hide();
                    e.Handled = true;
                }
            };
            reset.Click += (_, _) =>
            {
                text.Text = string.Empty;
                SetFilter(index, null);
            };
            button.Click += (_, _) =>
            {
                text.Text = GetFilter(index) ?? string.Empty;
                text.Focus();
            };

            return button;
        }

        private string? Resolve(string key, string fallback)
            => _list.TryFindResource(key, _list.ActualThemeVariant, out var value) && value is string text ? text : fallback;

        private void ToggleSort(int index)
        {
            if (SortColumn != index)
            {
                SortBy(index, false);
            }
            else if (!SortDescending)
            {
                SortBy(index, true);
            }
            else
            {
                SortBy(index, null);
            }
        }

        private void UpdateHeaderStates()
        {
            foreach (var pair in _arrows)
            {
                var active = SortColumn == pair.Key;
                pair.Value.Text = active && SortDescending ? "▼" : "▲";
                pair.Value.Opacity = active ? 1 : 0;
                pair.Value.Foreground = active ? AccentBrush() : null;
            }

            foreach (var pair in _headerButtons)
            {
                var active = _filters.ContainsKey(pair.Key);
                pair.Value.Opacity = active ? 1 : 0.45;
                pair.Value.Foreground = active ? AccentBrush() : null;
            }
        }

        private IBrush? AccentBrush()
            => _list.TryFindResource("CkanAccentBrush", _list.ActualThemeVariant, out var value) && value is IBrush brush
                ? brush
                : null;

        private bool Matches(object item)
        {
            foreach (var pair in _filters)
            {
                if (pair.Key >= _paths.Length)
                {
                    continue;
                }

                var text = TableValues.ToText(item, FilterPathOf(_paths[pair.Key]));
                if (!FilterText.Matches(text, pair.Value))
                {
                    return false;
                }
            }

            return true;
        }

        private int CompareRows(object? left, object? right)
        {
            if (SortColumn < 0 || SortColumn >= _paths.Length)
            {
                return 0;
            }

            var comparison = TableValues.Compare(left, right, SortPathOf(_paths[SortColumn]));
            return SortDescending ? -comparison : comparison;
        }
    }
}
