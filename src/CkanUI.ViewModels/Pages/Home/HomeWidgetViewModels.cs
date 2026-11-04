using System;
using System.Collections.ObjectModel;
using CkanUI.Services.Abstractions.Home;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CkanUI.ViewModels.Pages.Home;

/// <summary>主页上一块可拖动、可移除的部件。数据本身仍由所在页面或插件提供，这里只做排版身份。</summary>
/// <remarks>
/// <para>部件分成四种：实例概览、快捷操作、最近截图，以及插件提供的部件。前三种直接复用
/// <see cref="HomePageViewModel"/> 上已有的字段与命令（模板里通过 <see cref="Page"/> 取），
/// 避免把同一份实例/截图状态复制两份、再让两边慢慢漂移。</para>
/// <para>顺序与显隐只由 <see cref="Id"/> 在设置里的位置决定；部件对象可以被重建，拖拽不会依赖对象引用。</para>
/// </remarks>
public abstract partial class HomeWidgetViewModel : ObservableObject
{
    /// <summary>部件身份构造。</summary>
    protected HomeWidgetViewModel(string id, string title, string glyph, HomePageViewModel page)
    {
        Id = id;
        Title = title;
        Glyph = glyph;
        Page = page;
    }

    /// <summary>稳定身份：内置键或 <c>plugin:&lt;插件Id&gt;:&lt;部件键&gt;</c>。布局持久化只存它。</summary>
    public string Id { get; }

    /// <summary>部件标题（插件部件可被快照覆盖，因此可变）。</summary>
    [ObservableProperty]
    private string _title;

    /// <summary>标题旁的字形名（宿主字形表）。</summary>
    public string Glyph { get; }

    /// <summary>所属主页：模板用它取实例、快捷操作与截图。</summary>
    public HomePageViewModel Page { get; }

    /// <summary>拖动中的视觉状态（模板据此加边框/透明度）。</summary>
    [ObservableProperty]
    private bool _isDragging;

    /// <summary>是否由插件提供（模板据此画「插件」角标）。</summary>
    public virtual bool IsPlugin => false;
}

/// <summary>实例概览部件。</summary>
public sealed class InstanceHomeWidgetViewModel : HomeWidgetViewModel
{
    /// <summary>创建实例概览部件。</summary>
    public InstanceHomeWidgetViewModel(HomePageViewModel page, string title)
        : base(HomePageViewModel.InstanceWidgetId, title, "Folder", page)
    {
    }
}

/// <summary>快捷操作部件：三层次卡片仍是页面自己的集合。</summary>
public sealed class QuickActionsHomeWidgetViewModel : HomeWidgetViewModel
{
    /// <summary>创建快捷操作部件。</summary>
    public QuickActionsHomeWidgetViewModel(HomePageViewModel page, string title)
        : base(HomePageViewModel.QuickActionsWidgetId, title, "Apps", page)
    {
    }
}

/// <summary>最近截图部件。</summary>
public sealed class ScreenshotsHomeWidgetViewModel : HomeWidgetViewModel
{
    /// <summary>创建截图部件。</summary>
    public ScreenshotsHomeWidgetViewModel(HomePageViewModel page, string title)
        : base(HomePageViewModel.ScreenshotsWidgetId, title, "Image", page)
    {
    }
}

/// <summary>插件提供的主页部件：宿主只负责把插件算好的文本排出来。</summary>
public sealed partial class PluginHomeWidgetViewModel : HomeWidgetViewModel
{
    /// <summary>创建一个插件部件（数据随后由 <see cref="Apply"/> 填入）。</summary>
    public PluginHomeWidgetViewModel(
        HomePageViewModel page,
        string pluginId,
        string pluginName,
        PluginHomeWidgetDescriptor descriptor)
        : base("plugin:" + pluginId + ":" + descriptor.Key, descriptor.Title, descriptor.Glyph ?? "Puzzle", page)
    {
        PluginId = pluginId;
        PluginName = pluginName;
        Descriptor = descriptor;
        PageKey = descriptor.PageKey;
        StatusText = "正在读取…";
    }

    /// <summary>提供该部件的插件标识。</summary>
    public string PluginId { get; }

    /// <summary>插件显示名（用于角标提示）。</summary>
    public string PluginName { get; }

    /// <summary>插件声明的描述符。</summary>
    public PluginHomeWidgetDescriptor Descriptor { get; }

    /// <summary>点「打开」时跳转的页签键；空表示不提供。</summary>
    public string? PageKey { get; }

    /// <summary>统计值。</summary>
    public ObservableCollection<PluginHomeWidgetMetric> Metrics { get; } = new();

    /// <summary>内容行。</summary>
    public ObservableCollection<PluginHomeWidgetLine> Lines { get; } = new();

    /// <summary>副标题。</summary>
    [ObservableProperty]
    private string _subtitle = string.Empty;

    /// <summary>页脚。</summary>
    [ObservableProperty]
    private string _footerText = string.Empty;

    /// <summary>错误文案（空串表示没有错误）。</summary>
    [ObservableProperty]
    private string _errorText = string.Empty;

    /// <summary>是否正在读取。</summary>
    [ObservableProperty]
    private bool _isLoading;

    /// <summary>读取状态（加载中/没有数据等），有数据后清空。</summary>
    [ObservableProperty]
    private string _statusText = string.Empty;

    /// <inheritdoc />
    public override bool IsPlugin => true;

    /// <summary>有统计值。</summary>
    public bool HasMetrics => Metrics.Count > 0;

    /// <summary>有内容行。</summary>
    public bool HasLines => Lines.Count > 0;

    /// <summary>有副标题。</summary>
    public bool HasSubtitle => !string.IsNullOrWhiteSpace(Subtitle);

    /// <summary>有页脚。</summary>
    public bool HasFooter => !string.IsNullOrWhiteSpace(FooterText);

    /// <summary>有错误。</summary>
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);

    /// <summary>有可跳转的页签。</summary>
    public bool CanOpen => !string.IsNullOrWhiteSpace(PageKey);

    /// <summary>加载中或状态文案非空时显示占位区。</summary>
    public bool ShowPlaceholder => IsLoading || (!HasMetrics && !HasLines && (!string.IsNullOrWhiteSpace(StatusText) || HasError));

    /// <summary>把一次快照写进可绑定字段。</summary>
    public void Apply(PluginHomeWidgetSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (!string.IsNullOrWhiteSpace(snapshot.Title))
        {
            Title = snapshot.Title;
        }

        Subtitle = snapshot.Subtitle ?? string.Empty;
        FooterText = snapshot.Footer ?? string.Empty;
        ErrorText = snapshot.Error ?? string.Empty;
        StatusText = string.Empty;
        IsLoading = false;

        Metrics.Clear();
        foreach (var metric in snapshot.Metrics ?? Array.Empty<PluginHomeWidgetMetric>())
        {
            if (metric is not null)
            {
                Metrics.Add(metric);
            }
        }

        Lines.Clear();
        foreach (var line in snapshot.Lines ?? Array.Empty<PluginHomeWidgetLine>())
        {
            if (line is not null)
            {
                Lines.Add(line);
            }
        }

        NotifyDerived();
    }

    /// <summary>一次读取失败：保留标题，显示可读错误（不回退成 0 或假数据）。</summary>
    public void Fail(string message)
    {
        ErrorText = string.IsNullOrWhiteSpace(message) ? "读取失败。" : message;
        StatusText = string.Empty;
        IsLoading = false;
        NotifyDerived();
    }

    /// <summary>标记开始读取。</summary>
    public void BeginLoad()
    {
        IsLoading = true;
        ErrorText = string.Empty;
        StatusText = "正在读取…";
        NotifyDerived();
    }

    /// <summary>插件正常返回但没有任何数据时的占位文案。</summary>
    public void Empty(string message)
    {
        Metrics.Clear();
        Lines.Clear();
        Subtitle = string.Empty;
        FooterText = string.Empty;
        ErrorText = string.Empty;
        StatusText = message;
        IsLoading = false;
        NotifyDerived();
    }

    partial void OnSubtitleChanged(string value) => OnPropertyChanged(nameof(HasSubtitle));

    partial void OnFooterTextChanged(string value) => OnPropertyChanged(nameof(HasFooter));

    partial void OnErrorTextChanged(string value) => OnPropertyChanged(nameof(HasError));

    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(ShowPlaceholder));

    partial void OnStatusTextChanged(string value) => OnPropertyChanged(nameof(ShowPlaceholder));

    private void NotifyDerived()
    {
        OnPropertyChanged(nameof(HasMetrics));
        OnPropertyChanged(nameof(HasLines));
        OnPropertyChanged(nameof(HasSubtitle));
        OnPropertyChanged(nameof(HasFooter));
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(ShowPlaceholder));
    }
}

/// <summary>「添加部件」菜单里的一项。</summary>
/// <param name="Id">布局身份。</param>
/// <param name="Title">显示名。</param>
/// <param name="Description">一句话说明。</param>
/// <param name="Glyph">字形名。</param>
/// <param name="IsPlugin">是否由插件提供。</param>
public sealed record HomeWidgetOptionViewModel(
    string Id,
    string Title,
    string Description,
    string Glyph,
    bool IsPlugin);
