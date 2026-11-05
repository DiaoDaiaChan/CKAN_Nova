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
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Controls.Shapes;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;

using CkanUI.Ui.Tables;

namespace CkanUI.App.Controls;

/// <summary>
/// 表格工具：给 <see cref="DataGrid"/> 一次性打开排序、列拖动重排、列宽调整，并给每列挂一个筛选入口。
/// </summary>
/// <remarks>
/// <para>用法：在 XAML 里写 <c>controls:TableTools.Enabled="True"</c>；窄列（如行首复选框列）用
/// <c>controls:TableTools.Filterable="False"</c> 关掉筛选入口。</para>
/// <para>筛选走 <see cref="IDataGridCollectionView.Filter"/>，多列条件按「与」叠加；单元格文本按当前列
/// 的绑定路径取值（界面上看到的是什么就按什么筛），没有绑定路径的模板列退回 <c>SortMemberPath</c>。</para>
/// <para>排序与重排直接交给 DataGrid 自带的交互，不自己实现拖动逻辑——自带实现已处理虚拟化、
/// 冻结列与列宽恢复，重写只会引入新的错位。</para>
/// </remarks>
public sealed class TableTools : AvaloniaObject
{
    private TableTools()
    {
    }

    /// <summary>打开表格工具（排序 + 列拖动 + 列宽 + 筛选入口）。</summary>
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<TableTools, DataGrid, bool>("Enabled");

    /// <summary>某一列是否显示筛选入口，默认显示。</summary>
    public static readonly AttachedProperty<bool> FilterableProperty =
        AvaloniaProperty.RegisterAttached<TableTools, DataGridColumn, bool>("Filterable", defaultValue: true);

    /// <summary>筛选取值路径；不设时先看列的绑定路径，再看 <see cref="DataGridColumn.SortMemberPath"/>。</summary>
    public static readonly AttachedProperty<string?> FilterPathProperty =
        AvaloniaProperty.RegisterAttached<TableTools, DataGridColumn, string?>("FilterPath");

    /// <summary>排序取值路径；给模板列与「显示文本经过格式化」的列一个原始值入口（模板列没有绑定，靠它才有排序）。</summary>
    public static readonly AttachedProperty<string?> SortPathProperty =
        AvaloniaProperty.RegisterAttached<TableTools, DataGridColumn, string?>("SortPath");

    private static readonly ConditionalWeakTable<DataGrid, TableState> States = new();

    /// <remarks>
    /// 几何必须按需构造：<see cref="StreamGeometry.Parse"/> 会向 <c>IPlatformRenderInterface</c> 取实例，
    /// 在应用宿主起来之前（单元测试首次碰到附加属性）就会抛 <c>Unable to locate 'IPlatformRenderInterface'</c>。
    /// </remarks>
    private const string FunnelPath = "M2,3 H12 L8,7.2 V11.4 L6,10.2 V7.2 Z";

    static TableTools()
    {
        EnabledProperty.Changed.AddClassHandler<DataGrid>(static (grid, args) => Toggle(grid, args.GetNewValue<bool>()));
        SortPathProperty.Changed.AddClassHandler<DataGridColumn>(static (column, args) => ApplySortPath(column, args.GetNewValue<string?>()));
    }

    /// <summary>读取排序取值路径。</summary>
    /// <param name="column">列。</param>
    public static string? GetSortPath(DataGridColumn column) => column.GetValue(SortPathProperty);

    /// <summary>设置排序取值路径。</summary>
    /// <param name="column">列。</param>
    /// <param name="value">属性路径。</param>
    public static void SetSortPath(DataGridColumn column, string? value) => column.SetValue(SortPathProperty, value);

    private static void ApplySortPath(DataGridColumn column, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        column.SortMemberPath = path;
        column.CustomSortComparer = new ValueComparer(path);
    }

    /// <summary>读取表格工具开关。</summary>
    /// <param name="grid">表格。</param>
    public static bool GetEnabled(DataGrid grid) => grid.GetValue(EnabledProperty);

    /// <summary>设置表格工具开关。</summary>
    /// <param name="grid">表格。</param>
    /// <param name="value">是否开启。</param>
    public static void SetEnabled(DataGrid grid, bool value) => grid.SetValue(EnabledProperty, value);

    /// <summary>读取某列是否显示筛选入口。</summary>
    /// <param name="column">列。</param>
    public static bool GetFilterable(DataGridColumn column) => column.GetValue(FilterableProperty);

    /// <summary>设置某列是否显示筛选入口。</summary>
    /// <param name="column">列。</param>
    /// <param name="value">是否显示。</param>
    public static void SetFilterable(DataGridColumn column, bool value) => column.SetValue(FilterableProperty, value);

    /// <summary>读取筛选取值路径。</summary>
    /// <param name="column">列。</param>
    public static string? GetFilterPath(DataGridColumn column) => column.GetValue(FilterPathProperty);

    /// <summary>设置筛选取值路径。</summary>
    /// <param name="column">列。</param>
    /// <param name="value">属性路径。</param>
    public static void SetFilterPath(DataGridColumn column, string? value) => column.SetValue(FilterPathProperty, value);

    /// <summary>给某列设筛选词（空白等于清除）。</summary>
    /// <param name="grid">表格。</param>
    /// <param name="column">列。</param>
    /// <param name="text">筛选词，空格分隔多词，<c>-词</c> 表示排除。</param>
    public static void SetFilter(DataGrid grid, DataGridColumn column, string? text)
    {
        if (!States.TryGetValue(grid, out var state))
        {
            return;
        }

        state.SetFilter(column, text);
    }

    /// <summary>读取某列的筛选词。</summary>
    /// <param name="grid">表格。</param>
    /// <param name="column">列。</param>
    public static string? GetFilter(DataGrid grid, DataGridColumn column) =>
        States.TryGetValue(grid, out var state) && state.Filters.TryGetValue(column, out var text) ? text : null;

    /// <summary>清掉所有列筛选。</summary>
    /// <param name="grid">表格。</param>
    public static void ClearFilters(DataGrid grid)
    {
        if (!States.TryGetValue(grid, out var state))
        {
            return;
        }

        state.ClearFilters();
    }

    /// <summary>某列是否已经挂上筛选入口（表头是否实例化由 DataGrid 虚拟化决定，这里只回答「挂了没有」）。</summary>
    /// <param name="grid">表格。</param>
    /// <param name="column">列。</param>
    public static bool HasFilterEntry(DataGrid grid, DataGridColumn column) =>
        States.TryGetValue(grid, out var state) && state.HasFilterEntry(column);

    private static void Toggle(DataGrid grid, bool enabled)
    {
        if (!enabled)
        {
            if (States.TryGetValue(grid, out var existing))
            {
                existing.Detach();
                States.Remove(grid);
            }

            return;
        }

        if (States.TryGetValue(grid, out _))
        {
            return;
        }

        var state = new TableState(grid);
        States.Add(grid, state);
        state.Attach();
    }

    private static object? ReadValue(object item, string path)
    {
        object? current = item;
        foreach (var part in path.Split('.'))
        {
            if (current is null)
            {
                return null;
            }

            var property = PropertyCache.GetOrAdd((current.GetType(), part), static key =>
            {
                var (type, name) = key;
                return type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            });

            if (property is null)
            {
                return null;
            }

            current = property.GetValue(current);
        }

        return current;
    }

    private static readonly ConcurrentDictionary<(Type Type, string Name), PropertyInfo?> PropertyCache = new();

    private static string PathOf(DataGridColumn column)
    {
        var explicitPath = GetFilterPath(column);
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return explicitPath;
        }

        if (column is DataGridBoundColumn { Binding: Binding binding } && binding.Path is { Length: > 0 } boundPath)
        {
            return boundPath;
        }

        return column.SortMemberPath ?? string.Empty;
    }

    /// <summary>
    /// 按值比较：数字按数值、版本号按 <see cref="Version"/>、其余按字符串。默认比较器对 "1.9"/"1.10"
    /// 与 "900 KB"/"2.3 MB" 会排反，这两类列必须换比较器。
    /// </summary>
    /// <remarks>DataGrid 传进来的是行对象（不是 <see cref="DataGridColumn.SortMemberPath"/> 取到的值），必须自己取路径。</remarks>
    private sealed class ValueComparer : IComparer
    {
        private readonly string _path;

        public ValueComparer(string path) => _path = path;

        public int Compare(object? left, object? right)
        {
            var a = left is null ? null : ReadValue(left, _path);
            var b = right is null ? null : ReadValue(right, _path);

            if (ReferenceEquals(a, b))
            {
                return 0;
            }

            if (a is null)
            {
                return -1;
            }

            if (b is null)
            {
                return 1;
            }

            if (IsNumeric(a) && IsNumeric(b))
            {
                return Convert.ToDouble(a, CultureInfo.InvariantCulture).CompareTo(Convert.ToDouble(b, CultureInfo.InvariantCulture));
            }

            var leftText = a as string ?? Convert.ToString(a, CultureInfo.CurrentCulture) ?? string.Empty;
            var rightText = b as string ?? Convert.ToString(b, CultureInfo.CurrentCulture) ?? string.Empty;

            if (Version.TryParse(leftText.Trim(), out var leftVersion) && Version.TryParse(rightText.Trim(), out var rightVersion))
            {
                return leftVersion.CompareTo(rightVersion);
            }

            return string.Compare(leftText, rightText, StringComparison.CurrentCultureIgnoreCase);
        }

        private static bool IsNumeric(object value) =>
            value is sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal;
    }

    private sealed class TableState
    {
        private const double GripHitWidth = 6d;

        private readonly DataGrid _grid;
        private readonly HashSet<DataGridColumn> _decorated = new();
        private readonly Dictionary<DataGridColumn, Button> _buttons = new();
        private Style? _gripBase;
        private Style? _gripHover;

        public TableState(DataGrid grid) => _grid = grid;

        public Dictionary<DataGridColumn, string> Filters { get; } = new();

        public bool HasFilterEntry(DataGridColumn column) => _buttons.ContainsKey(column);

        public void Attach()
        {
            _grid.CanUserSortColumns = true;
            _grid.CanUserReorderColumns = true;
            _grid.CanUserResizeColumns = true;

            _grid.Columns.CollectionChanged += OnColumnsChanged;
            _grid.PropertyChanged += OnGridPropertyChanged;
            // 排序、筛选之后行会换位置：交给重排动画滑过去。DataGrid 的排序不走集合 Reset，得自己抓。
            _grid.Sorting += (_, _) => PlayReorder();
            TableReorderMotion.SetIsEnabled(_grid, true);

            foreach (var column in _grid.Columns)
            {
                Decorate(column);
            }

            AttachResizeGuide();
            ApplyFilters();
        }

        /// <summary>
        /// 列宽拖拽的视觉指引。DataGrid 的可拖拽区域是表头模板里的 1px 透明矩形
        /// <c>Rectangle#VerticalSeparator</c>：既看不见边界，也几乎摸不到。这里把它的命中宽度
        /// 扩到 6px（左右各外扩 3px，用负外边距抵掉，列的实际布局不变），并在悬停表头时点亮成
        /// 强调色 —— 边界看得见、抓得住，拖起来才有反馈。
        /// </summary>
        private void AttachResizeGuide()
        {
            _grid.ActualThemeVariantChanged += OnGridThemeVariantChanged;
            RefreshResizeGuide();
        }

        private void OnGridThemeVariantChanged(object? sender, EventArgs e) => RefreshResizeGuide();

        private void RefreshResizeGuide()
        {
            DetachResizeGuide();

            _gripBase = new Style(selector => selector.OfType<DataGridColumnHeader>().Template().Name("VerticalSeparator"));
            _gripBase.Setters.Add(new Setter(InputElement.CursorProperty, new Cursor(StandardCursorType.SizeWestEast)));
            _gripBase.Setters.Add(new Setter(Layoutable.MinWidthProperty, GripHitWidth));
            _gripBase.Setters.Add(new Setter(Layoutable.MarginProperty, new Thickness(-GripHitWidth / 2, 0, 0, 0)));
            _gripBase.Setters.Add(new Setter(Shape.FillProperty, Brushes.Transparent));
            _grid.Styles.Add(_gripBase);

            var accent = FindBrush("CkanAccentBrush") ?? FindBrush("CkanBorderBrush");
            if (accent is null)
            {
                return;
            }

            _gripHover = new Style(selector => selector.OfType<DataGridColumnHeader>().Class(":pointerover").Template().Name("VerticalSeparator"));
            _gripHover.Setters.Add(new Setter(Shape.FillProperty, NarrowBand(accent)));
            _grid.Styles.Add(_gripHover);
        }

        private void DetachResizeGuide()
        {
            if (_gripBase is not null)
            {
                _grid.Styles.Remove(_gripBase);
                _gripBase = null;
            }

            if (_gripHover is not null)
            {
                _grid.Styles.Remove(_gripHover);
                _gripHover = null;
            }
        }

        /// <summary>
        /// 命中区被撑到 6 px，直接整块上色会变成一条 6 px 粗的色带；这里用硬边渐变只在正中间留 1 px，
        /// 看起来还是一条细线，抓取范围却仍是 6 px。
        /// </summary>
        private static IBrush NarrowBand(IBrush brush)
        {
            if (brush is not ISolidColorBrush solid)
            {
                return brush;
            }

            var color = solid.Color;
            var clear = Color.FromArgb(0, color.R, color.G, color.B);
            return new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(clear, 0d),
                    new GradientStop(color, 0.4d),
                    new GradientStop(color, 0.6d),
                    new GradientStop(clear, 1d),
                },
            };
        }

        private IBrush? FindBrush(string key)
            => _grid.TryFindResource(key, _grid.ActualThemeVariant, out var value) && value is IBrush brush ? brush : null;

        public void Detach()
        {
            _grid.Columns.CollectionChanged -= OnColumnsChanged;
            _grid.PropertyChanged -= OnGridPropertyChanged;
            _grid.ActualThemeVariantChanged -= OnGridThemeVariantChanged;
            DetachResizeGuide();
            _buttons.Clear();
            _decorated.Clear();
        }

        public void SetFilter(DataGridColumn column, string? text)
        {
            var normalized = text?.Trim() ?? string.Empty;
            if (normalized.Length == 0)
            {
                Filters.Remove(column);
            }
            else
            {
                Filters[column] = normalized;
            }

            UpdateButtonState(column);
            ApplyFilters();
        }

        public void ClearFilters()
        {
            var columns = Filters.Keys.ToList();
            Filters.Clear();
            foreach (var column in columns)
            {
                UpdateButtonState(column);
            }

            ApplyFilters();
        }

        private void OnColumnsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems is not null)
            {
                foreach (var column in e.NewItems.OfType<DataGridColumn>())
                {
                    Decorate(column);
                }
            }

            ApplyFilters();
        }

        private void OnGridPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == ItemsControl.ItemsSourceProperty || e.Property == DataGrid.ItemsSourceProperty)
            {
                // ItemsSource 换集合时 DataGrid 会重建 CollectionView，旧 Filter 一并丢失，必须重挂。
                ApplyFilters();
            }
        }

        private void Decorate(DataGridColumn column)
        {
            if (!_decorated.Add(column))
            {
                return;
            }

            if (!GetFilterable(column))
            {
                if (string.IsNullOrEmpty(column.SortMemberPath) && column is DataGridTemplateColumn)
                {
                    column.CanUserSort = false;
                }

                return;
            }

            var path = PathOf(column);
            if (path.Length == 0)
            {
                // 既没有绑定也没有排序路径（如行首复选框模板列）：没有可筛的文本，不给入口也不给假排序箭头。
                column.CanUserSort = false;
                return;
            }

            var originalTemplate = column.HeaderTemplate;
            column.HeaderTemplate = new FuncDataTemplate<object>((value, _) => BuildHeader(column, value, originalTemplate));
        }

        private Control BuildHeader(DataGridColumn column, object? value, IDataTemplate? originalTemplate)
        {
            var content = new ContentPresenter
            {
                Content = value,
                ContentTemplate = originalTemplate,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left,
            };

            var button = BuildFilterButton(column);
            _buttons[column] = button;
            UpdateButtonState(column);

            var panel = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(content, 0);
            Grid.SetColumn(button, 1);
            panel.Children.Add(content);
            panel.Children.Add(button);
            return panel;
        }

        private Button BuildFilterButton(DataGridColumn column)
        {
            var icon = new PathIcon { Data = StreamGeometry.Parse(FunnelPath), Width = 12, Height = 12, Opacity = 1d };

            var button = new Button
            {
                Content = icon,
                Width = 18,
                Height = 18,
                Padding = default,
                Margin = new Thickness(2, 0, 2, 0),
                Background = Brushes.Transparent,
                BorderThickness = default,
                CornerRadius = new CornerRadius(3),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Focusable = false,
                Cursor = new Cursor(StandardCursorType.Hand),
            };

            button.Classes.Add("table-filter");

            icon.Bind(PathIcon.ForegroundProperty, new Binding(nameof(Button.Foreground)) { Source = button });

            ToolTip.SetTip(button, Resolve("Ui_Common_Filter", "筛选"));
            ToolTip.SetShowDelay(button, 300);

            button.Click += (_, _) => ShowFilterFlyout(column, button);
            return button;
        }

        private void ShowFilterFlyout(DataGridColumn column, Button button)
        {
            var box = new TextBox
            {
                Text = Filters.TryGetValue(column, out var current) ? current : string.Empty,
                PlaceholderText = Resolve("Ui_Common_Filter", "筛选"),
                MinWidth = 180,
                Margin = new Thickness(0, 0, 0, 8),
            };

            var flyout = new Flyout
            {
                Placement = PlacementMode.BottomEdgeAlignedRight,
                Content = BuildFilterPanel(column, box, out var clear),
            };

            box.TextChanged += (_, _) => SetFilter(column, box.Text);
            box.KeyDown += (_, e) =>
            {
                if (e.Key is Key.Enter or Key.Escape)
                {
                    e.Handled = true;
                    flyout.Hide();
                }
            };

            clear.Click += (_, _) =>
            {
                box.Text = string.Empty;
                flyout.Hide();
            };

            flyout.ShowAt(button);
            box.Focus();
        }

        private Control BuildFilterPanel(DataGridColumn column, TextBox box, out Button clear)
        {
            clear = new Button
            {
                Content = Resolve("Ui_Common_ResetFilter", "清除"),
                Classes = { "CkanSmall" },
                HorizontalAlignment = HorizontalAlignment.Right,
            };

            var hint = new TextBlock
            {
                Text = Resolve("Ui_Common_FilterHint", "输入关键字，空格分隔多个条件"),
                Opacity = 0.65,
                FontSize = 11,
                Margin = new Thickness(0, 8, 0, 0),
            };

            var panel = new StackPanel { Spacing = 0, MinWidth = 200 };
            panel.Children.Add(box);
            panel.Children.Add(clear);
            panel.Children.Add(hint);
            return panel;
        }

        private string Resolve(string key, string fallback) =>
            _grid.TryFindResource(key, out var value) && value is string text && text.Length > 0 ? text : fallback;

        private void UpdateButtonState(DataGridColumn column)
        {
            if (!_buttons.TryGetValue(column, out var button))
            {
                return;
            }

            var active = Filters.ContainsKey(column);
            button.Opacity = active ? 1d : 0.45d;
            if (active && _grid.TryFindResource("CkanAccentBrush", out var resource) && resource is IBrush brush)
            {
                button.Foreground = brush;
            }
            else if (button.TryFindResource("CkanTextTertiaryBrush", out var muted) && muted is IBrush mutedBrush)
            {
                button.Foreground = mutedBrush;
            }
            else
            {
                button.ClearValue(Button.ForegroundProperty);
            }
        }

        /// <summary>DataGrid 的行容器是 <c>DataGridRow</c>，共享层不认识它，容器与数据项由这里配好。</summary>
        private void PlayReorder()
            => TableReorderMotion.Play(_grid, _grid.GetVisualDescendants().OfType<DataGridRow>(), static row => row.DataContext);

        private void ApplyFilters()
        {
            if (_grid.CollectionView is not { } view)
            {
                return;
            }

            // 先在旧位置上留快照，Refresh 之后行才知道自己该从哪儿滑回来。
            PlayReorder();

            var active = Filters.Where(pair => pair.Value.Length > 0).ToList();
            view.Filter = active.Count == 0
                ? null!
                : item => active.All(pair => Matches(item, pair.Key, pair.Value));

            if (view is DataGridCollectionView concrete)
            {
                concrete.Refresh();
            }
        }

        private static bool Matches(object item, DataGridColumn column, string filterText)
        {
            var path = PathOf(column);
            if (path.Length == 0)
            {
                return true;
            }

            var text = ReadValue(item, path)?.ToString() ?? string.Empty;
            foreach (var token in filterText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                var negate = token.Length > 1 && token[0] == '-';
                var needle = negate ? token[1..] : token;
                var hit = text.Contains(needle, StringComparison.CurrentCultureIgnoreCase);
                if (negate ? hit : !hit)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
