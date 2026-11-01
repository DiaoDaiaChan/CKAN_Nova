using System;
using System.Collections.Generic;
using System.Linq;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CkanUI.ViewModels.Pages.Mods;

/// <summary>
/// 组合搜索面板（12 篇 F-20/F-21 / UI-14）：18 个字段逐项填写，实时生成搜索框里的语法文本。
/// </summary>
/// <remarks>
/// 面板与搜索框是同一份条件的两种表达：面板改字段 → <see cref="CombinedChanged"/> 给出语法串，
/// 页面写回 <c>SearchText</c>；用户直接在搜索框里敲字 → 页面解析成 <see cref="ModSearchSpec"/>
/// 后调 <see cref="LoadFrom"/> 回填字段（等价上游 <c>EditModSearchDetails.PopulateSearch</c>）。
/// 两边都不自己拼前缀，拼装只由服务层 <see cref="IModSearchFormatter"/> 负责。
/// 上游没有真正的 OR 组合（<c>FilterOrLabel</c> 只切换标题），18 个字段是 AND 关系，故不提供 OR 模式。
/// </remarks>
public sealed partial class ModsSearchPanelViewModel : ObservableObject
{
    private readonly IModSearchFormatter? _formatter;
    private bool _syncing;

    /// <summary>设计时构造（没有服务，面板只显示、不联动）。</summary>
    public ModsSearchPanelViewModel()
        : this(null)
    {
    }

    /// <summary>运行期构造。</summary>
    public ModsSearchPanelViewModel(IModSearchFormatter? formatter)
    {
        _formatter = formatter;
        Fields = CreateFields();
        foreach (var field in Fields)
        {
            field.PropertyChanged += OnFieldPropertyChanged;
        }
    }

    /// <summary>18 个字段，顺序对齐上游 <c>EditModSearchDetails</c>：12 个文本 + 6 个三态。</summary>
    public IReadOnlyList<ModsSearchFieldViewModel> Fields { get; }

    /// <summary>面板是否展开（工具栏「筛选」按钮切换）。</summary>
    [ObservableProperty]
    private bool _isExpanded;

    /// <summary>当前条件生成的查询文本，供用户对照学习语法。</summary>
    [ObservableProperty]
    private string _preview = string.Empty;

    /// <summary>已参与筛选的字段数。</summary>
    [ObservableProperty]
    private int _activeCount;

    /// <summary>是否有条件在用（决定「清空条件」是否可用、按钮是否高亮）。</summary>
    public bool HasActiveFilters => ActiveCount > 0;

    /// <summary>工具栏按钮上的状态文案（无障碍：不只靠颜色表达）。</summary>
    public string SummaryText => ActiveCount > 0 ? $"高级筛选（{ActiveCount}）" : "高级筛选";

    /// <summary>字段变化后抛出的查询文本（页面据此写回搜索框）。</summary>
    public event Action<string>? CombinedChanged;

    /// <summary>
    /// 用已解析的条件回填面板（用户直接编辑搜索框时）。
    /// 回填过程不抛 <see cref="CombinedChanged"/>，否则会把用户的输入改写成规范化形式。
    /// </summary>
    public void LoadFrom(ModSearchSpec? spec)
    {
        var source = spec ?? ModSearchSpec.Empty;
        _syncing = true;
        try
        {
            foreach (var field in Fields)
            {
                if (field.Kind == ModsSearchFieldKind.State)
                {
                    field.StateIndex = StateValueOf(source, field.Key) switch
                    {
                        true => 1,
                        false => 2,
                        _ => 0,
                    };
                }
                else
                {
                    field.Text = TextOf(source, field.Key);
                }
            }
        }
        finally
        {
            _syncing = false;
        }

        RefreshState(raiseCombinedChanged: false);
    }

    /// <summary>把面板上的字段收成结构化条件。</summary>
    public ModSearchSpec BuildSpec()
    {
        var spec = ModSearchSpec.Empty;
        foreach (var field in Fields)
        {
            if (field.Kind == ModsSearchFieldKind.State)
            {
                var value = field.StateValue;
                if (!value.HasValue)
                {
                    continue;
                }

                spec = field.Key switch
                {
                    "compatible" => spec with { Compatible = value },
                    "installed" => spec with { Installed = value },
                    "cached" => spec with { Cached = value },
                    "newly-compatible" => spec with { NewlyCompatible = value },
                    "upgradeable" => spec with { Upgradeable = value },
                    "replaceable" => spec with { Replaceable = value },
                    _ => spec,
                };
                continue;
            }

            if (string.IsNullOrWhiteSpace(field.Text))
            {
                continue;
            }

            var raw = field.Text.Trim();
            var terms = SplitTerms(raw);
            spec = field.Key switch
            {
                "name" => spec with { Name = raw },
                "desc" => spec with { Description = raw },
                "author" => spec with { Authors = terms },
                "lic" => spec with { Licenses = terms },
                "lang" => spec with { Localizations = terms },
                "dep" => spec with { DependsOn = terms },
                "rec" => spec with { Recommends = terms },
                "sug" => spec with { Suggests = terms },
                "conf" => spec with { ConflictsWith = terms },
                "sup" => spec with { Supports = terms },
                "tag" => spec with { Tags = terms },
                "label" => spec with { Labels = terms },
                _ => spec,
            };
        }

        return spec;
    }

    /// <summary>把面板字段格式化成查询文本（无格式化服务时返回空串）。</summary>
    public string BuildCombined()
        => _formatter is null ? string.Empty : _formatter.Format(BuildSpec());

    /// <summary>清空全部字段（「清空条件」）。</summary>
    public void Clear()
    {
        _syncing = true;
        try
        {
            foreach (var field in Fields)
            {
                field.Clear();
            }
        }
        finally
        {
            _syncing = false;
        }

        RefreshState(raiseCombinedChanged: true);
    }

    [RelayCommand]
    private void ToggleExpanded() => IsExpanded = !IsExpanded;

    [RelayCommand]
    private void ClearAll() => Clear();

    partial void OnActiveCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasActiveFilters));
        OnPropertyChanged(nameof(SummaryText));
    }

    private void OnFieldPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // 只认"用户改了值"这两条通知：HasValue/StateValue 是同一次编辑的派生通知，重算一次就够。
        if (_syncing
            || (e.PropertyName != nameof(ModsSearchFieldViewModel.Text)
                && e.PropertyName != nameof(ModsSearchFieldViewModel.StateIndex)))
        {
            return;
        }

        RefreshState(raiseCombinedChanged: true);
    }

    private void RefreshState(bool raiseCombinedChanged)
    {
        var combined = _formatter is null ? string.Empty : _formatter.Format(BuildSpec());
        Preview = combined;
        ActiveCount = Fields.Count(field => field.HasValue);
        if (raiseCombinedChanged && _formatter is not null)
        {
            CombinedChanged?.Invoke(combined);
        }
    }

    private static IReadOnlyList<string> SplitTerms(string raw) =>
        raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string TextOf(ModSearchSpec spec, string key) => key switch
    {
        "name" => spec.Name,
        "desc" => spec.Description,
        "author" => Join(spec.Authors),
        "lic" => Join(spec.Licenses),
        "lang" => Join(spec.Localizations),
        "dep" => Join(spec.DependsOn),
        "rec" => Join(spec.Recommends),
        "sug" => Join(spec.Suggests),
        "conf" => Join(spec.ConflictsWith),
        "sup" => Join(spec.Supports),
        "tag" => Join(spec.Tags),
        "label" => Join(spec.Labels),
        _ => string.Empty,
    };

    private static bool? StateValueOf(ModSearchSpec spec, string key) => key switch
    {
        "compatible" => spec.Compatible,
        "installed" => spec.Installed,
        "cached" => spec.Cached,
        "newly-compatible" => spec.NewlyCompatible,
        "upgradeable" => spec.Upgradeable,
        "replaceable" => spec.Replaceable,
        _ => null,
    };

    private static string Join(IReadOnlyList<string> values) =>
        values.Count == 0 ? string.Empty : string.Join(" ", values);

    private static IReadOnlyList<ModsSearchFieldViewModel> CreateFields() =>
    [
        ModsSearchFieldViewModel.CreateText("name", "名称", "名称 / 标识符 / 缩写"),
        ModsSearchFieldViewModel.CreateText("author", "作者", "@作者", "@"),
        ModsSearchFieldViewModel.CreateText("desc", "描述", "desc:关键词", "desc:"),
        ModsSearchFieldViewModel.CreateText("lic", "许可证", "lic:MIT", "lic:"),
        ModsSearchFieldViewModel.CreateText("lang", "语言", "lang:zh", "lang:"),
        ModsSearchFieldViewModel.CreateText("dep", "依赖", "dep:标识符", "dep:"),
        ModsSearchFieldViewModel.CreateText("rec", "推荐", "rec:标识符", "rec:"),
        ModsSearchFieldViewModel.CreateText("sug", "建议", "sug:标识符", "sug:"),
        ModsSearchFieldViewModel.CreateText("conf", "冲突", "conf:标识符", "conf:"),
        ModsSearchFieldViewModel.CreateText("sup", "支持", "sup:标识符", "sup:"),
        ModsSearchFieldViewModel.CreateText("tag", "标签", "tag:名称", "tag:"),
        ModsSearchFieldViewModel.CreateText("label", "标签组", "label:名称", "label:"),
        ModsSearchFieldViewModel.CreateState("compatible", "兼容"),
        ModsSearchFieldViewModel.CreateState("installed", "已安装"),
        ModsSearchFieldViewModel.CreateState("cached", "已缓存"),
        ModsSearchFieldViewModel.CreateState("newly-compatible", "新增兼容"),
        ModsSearchFieldViewModel.CreateState("upgradeable", "可更新"),
        ModsSearchFieldViewModel.CreateState("replaceable", "可替换"),
    ];
}
