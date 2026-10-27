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
    public string? Locale { get; init; }
    public IReadOnlyDictionary<string, double> ColumnWidths { get; init; } =
        new Dictionary<string, double>();
}

public interface IUiSettingsStore
{
    UiSettings Current { get; }

    /// <summary>从磁盘加载；文件缺失或损坏时回退默认值（不抛异常）。</summary>
    UiSettings Load();

    /// <summary>原子写回（临时文件 + 替换）。</summary>
    OperationResult<bool> Save(UiSettings settings);
}
