using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;

namespace CkanUI.Ui.Tables;

/// <summary>
/// 手画表头表格用的只读视图：把行集合包一层，支持筛选与排序，变化通过 Reset 通知列表重建。
/// </summary>
/// <remarks>
/// <see cref="Avalonia.Collections"/> 里没有通用 CollectionView（<c>DataGridCollectionView</c> 只服务 DataGrid），
/// 手画表头 + ListBox 的表格拿不到现成的排序/筛选能力，所以自带一层：
/// <see cref="ListTableTools"/> 把列表的 <c>ItemsSource</c> 换成这个视图，改筛选/排序后调用 <see cref="Refresh"/>。
/// 必须实现 <see cref="IList"/>（非泛型）：ListBox 的默认面板按 <c>IList</c> 取下标访问。
/// </remarks>
internal sealed class TableView : IList, INotifyCollectionChanged
{
    private readonly IList _source;
    private readonly List<object?> _items = [];
    private Func<object, bool>? _filter;
    private Comparison<object?>? _comparison;

    /// <summary>包住一个行集合。</summary>
    /// <param name="source">原始行集合。</param>
    public TableView(IEnumerable source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source as IList ?? new List<object?>(source.Cast<object?>());
        if (source is INotifyCollectionChanged notify)
        {
            notify.CollectionChanged += OnSourceChanged;
        }

        Rebuild();
    }

    /// <inheritdoc />
    public event NotifyCollectionChangedEventHandler? CollectionChanged;

    /// <inheritdoc />
    public int Count => _items.Count;

    /// <inheritdoc />
    public bool IsReadOnly => true;

    /// <inheritdoc />
    public bool IsFixedSize => true;

    /// <inheritdoc />
    public bool IsSynchronized => false;

    /// <inheritdoc />
    public object SyncRoot => this;

    /// <inheritdoc />
    public object? this[int index]
    {
        get => _items[index];
        set => throw new NotSupportedException();
    }

    /// <summary>设置筛选谓词（<c>null</c> 表示不过滤）。不会自动刷新。</summary>
    /// <param name="predicate">行谓词。</param>
    public void SetFilterPredicate(Func<object, bool>? predicate) => _filter = predicate;

    /// <summary>设置排序比较（<c>null</c> 表示保持源顺序）。不会自动刷新。</summary>
    /// <param name="comparison">行比较。</param>
    public void SetComparison(Comparison<object?>? comparison) => _comparison = comparison;

    /// <summary>按当前筛选与排序重建并通知列表。</summary>
    public void Refresh() => Rebuild();

    private void Rebuild()
    {
        var rows = new List<object?>(_source.Count);
        foreach (var item in _source)
        {
            if (_filter is null || item is null || _filter(item))
            {
                rows.Add(item);
            }
        }

        if (_comparison is not null)
        {
            rows.Sort(_comparison);
        }

        _items.Clear();
        _items.AddRange(rows);
        CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    private void OnSourceChanged(object? sender, NotifyCollectionChangedEventArgs e) => Rebuild();

    /// <inheritdoc />
    public IEnumerator GetEnumerator() => _items.GetEnumerator();

    /// <inheritdoc />
    public bool Contains(object? value) => _items.Contains(value);

    /// <inheritdoc />
    public int IndexOf(object? value) => _items.IndexOf(value);

    /// <inheritdoc />
    public void CopyTo(Array array, int index) => ((ICollection)_items).CopyTo(array, index);

    /// <inheritdoc />
    public int Add(object? value) => throw new NotSupportedException();

    /// <inheritdoc />
    public void Clear() => throw new NotSupportedException();

    /// <inheritdoc />
    public void Insert(int index, object? value) => throw new NotSupportedException();

    /// <inheritdoc />
    public void Remove(object? value) => throw new NotSupportedException();

    /// <inheritdoc />
    public void RemoveAt(int index) => throw new NotSupportedException();
}

/// <summary>按属性路径取行里的值并比较：数字与版本号按语义比较，不按字典序。</summary>
internal static class TableValues
{
    /// <summary>把行对象上某个路径的取值转成展示文本。</summary>
    /// <param name="item">行。</param>
    /// <param name="path">属性路径，支持点号（<c>Data.Name</c>）。</param>
    public static string ToText(object? item, string path)
    {
        var value = Read(item, path);
        return value switch
        {
            null => string.Empty,
            IFormattable formattable => formattable.ToString(null, CultureInfo.CurrentCulture),
            _ => value.ToString() ?? string.Empty,
        };
    }

    /// <summary>比较两个行对象上同一路径的取值。</summary>
    /// <param name="left">左行。</param>
    /// <param name="right">右行。</param>
    /// <param name="path">属性路径。</param>
    public static int Compare(object? left, object? right, string path)
    {
        var a = Read(left, path);
        var b = Read(right, path);
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

        if (ToInstant(a) is { } ia && ToInstant(b) is { } ib)
        {
            return ia.CompareTo(ib);
        }

        if (TryNumber(a, out var na) && TryNumber(b, out var nb))
        {
            return na.CompareTo(nb);
        }

        var ta = ToText(a, string.Empty);
        var tb = ToText(b, string.Empty);

        // 版本号先于数字："1.10" 当版本读是 1.10 > 1.9，当小数读却是 1.1 < 1.9。
        if (Version.TryParse(ta, out var va) && Version.TryParse(tb, out var vb))
        {
            return va.CompareTo(vb);
        }

        // 展示文本里的纯数字（"12"、"4.5"）按数值比，别让 "9" 排在 "12" 后面。
        if (double.TryParse(ta, NumberStyles.Float, CultureInfo.CurrentCulture, out var da) &&
            double.TryParse(tb, NumberStyles.Float, CultureInfo.CurrentCulture, out var db))
        {
            return da.CompareTo(db);
        }

        return string.Compare(ta, tb, StringComparison.CurrentCultureIgnoreCase);
    }

    private static object? Read(object? item, string path)
    {
        if (item is null || string.IsNullOrEmpty(path))
        {
            return item;
        }

        var current = item;
        foreach (var segment in path.Split('.'))
        {
            if (current is null)
            {
                return null;
            }

            var property = current.GetType().GetProperty(
                segment,
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (property is null)
            {
                return null;
            }

            current = property.GetValue(current);
        }

        return current;
    }

    private static DateTimeOffset? ToInstant(object value) => value switch
    {
        DateTimeOffset offset => offset,
        DateTime time => new DateTimeOffset(time.ToUniversalTime()),
        _ => null,
    };

    private static bool TryNumber(object value, out double number)
    {
        switch (value)
        {
            case sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal:
                number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                return true;
            default:
                number = 0;
                return false;
        }
    }
}

/// <summary>筛选词匹配：空格分隔多个条件按「与」叠加，前缀 <c>-</c> 表示排除。</summary>
internal static class FilterText
{
    /// <summary>判断一段文本是否命中筛选词。</summary>
    /// <param name="text">被检查的文本。</param>
    /// <param name="query">筛选词；空白表示不筛选。</param>
    public static bool Matches(string text, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        foreach (var token in query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (token.StartsWith('-') && token.Length > 1)
            {
                if (text.Contains(token[1..], StringComparison.CurrentCultureIgnoreCase))
                {
                    return false;
                }
            }
            else if (!text.Contains(token, StringComparison.CurrentCultureIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }
}
