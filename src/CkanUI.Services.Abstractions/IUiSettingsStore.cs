namespace CkanUI.Services.Abstractions;

/// <summary>UI 自有设置（%LocalAppData%/CKAN/ckan-ui.json，01 篇 §7）。窗口几何、列宽、上次页面、主题。</summary>
public sealed record UiSettings
{
    public double WindowWidth { get; init; } = 1280;
    public double WindowHeight { get; init; } = 800;
    public double? WindowX { get; init; }
    public double? WindowY { get; init; }
    public bool IsMaximized { get; init; }
    public string LastPage { get; init; } = "mods";
    public string Theme { get; init; } = "dark";

    /// <summary>界面密度：compact / standard / comfortable（03 篇 §21、20 篇 P-08）。</summary>
    public string? Density { get; init; } = "standard";

    /// <summary>界面语言（BCP-47）。UI 语言的唯一真源，不回写 Core 的 config.json（见工程约定 §12）。</summary>
    public string? Locale { get; init; }

    public IReadOnlyDictionary<string, double> ColumnWidths { get; init; } =
        new Dictionary<string, double>();

    // record 合成的 Equals 对 ColumnWidths 走引用比较（IReadOnlyDictionary 不是 record 友好的值类型），
    // 会让「内容完全相同的两份设置」判为不等。这里改成按列名序数比较的结构相等：
    // 一是让测试与 UI 层能可靠断言默认值，二是给后续「设置未变更就跳过落盘」的脏检查用。
    public bool Equals(UiSettings? other) =>
        other is not null
        && WindowWidth.Equals(other.WindowWidth)
        && WindowHeight.Equals(other.WindowHeight)
        && Nullable.Equals(WindowX, other.WindowX)
        && Nullable.Equals(WindowY, other.WindowY)
        && IsMaximized == other.IsMaximized
        && string.Equals(LastPage, other.LastPage, StringComparison.Ordinal)
        && string.Equals(Theme, other.Theme, StringComparison.Ordinal)
        && string.Equals(Density, other.Density, StringComparison.Ordinal)
        && string.Equals(Locale, other.Locale, StringComparison.Ordinal)
        && ColumnWidthsEqual(ColumnWidths, other.ColumnWidths);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(WindowWidth);
        hash.Add(WindowHeight);
        hash.Add(WindowX);
        hash.Add(WindowY);
        hash.Add(IsMaximized);
        hash.Add(LastPage, StringComparer.Ordinal);
        hash.Add(Theme, StringComparer.Ordinal);
        hash.Add(Density, StringComparer.Ordinal);
        hash.Add(Locale, StringComparer.Ordinal);

        // 字典无序：逐项哈希异或，保证顺序无关。
        var columns = 0;
        foreach (var pair in ColumnWidths)
        {
            columns ^= HashCode.Combine(pair.Key, pair.Value);
        }

        hash.Add(columns);
        return hash.ToHashCode();
    }

    private static bool ColumnWidthsEqual(
        IReadOnlyDictionary<string, double> left,
        IReadOnlyDictionary<string, double> right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null || left.Count != right.Count)
        {
            return false;
        }

        foreach (var pair in left)
        {
            if (!right.TryGetValue(pair.Key, out var value) || !value.Equals(pair.Value))
            {
                return false;
            }
        }

        return true;
    }
}

public interface IUiSettingsStore
{
    UiSettings Current { get; }

    /// <summary>从磁盘加载；文件缺失或损坏时回退默认值（不抛异常）。</summary>
    UiSettings Load();

    /// <summary>保存成功后触发，携带新值。供主题/密度/语言这类「写完就要生效」的订阅方使用（03 篇 §21）。</summary>
    event Action<UiSettings>? Changed;

    /// <summary>原子写回（临时文件 + 替换）。</summary>
    OperationResult<bool> Save(UiSettings settings);
}
