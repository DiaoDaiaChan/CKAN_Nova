using System.Collections.Generic;
using CkanUI.ViewModels.Navigation;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CkanUI.ViewModels;

/// <summary>导航栏项目记录（03 篇 §1）。</summary>
/// <param name="Key">页面唯一标识键值。</param>
/// <param name="Title">导航项目显示标题。</param>
/// <param name="Glyph">图标字形标识符。</param>
public sealed record NavItem(string Key, string Title, string Glyph);

/// <summary>主窗口外壳（UI-01 骨架 / ARC-03）。管理顶层导航、全局状态栏与内容承载。</summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly INavigationService _navigation;

    /// <summary>导航项列表（mods / instances / settings）。</summary>
    public IReadOnlyList<NavItem> NavItems { get; } = new List<NavItem>
    {
        new("mods", "模组", "Apps"),
        new("instances", "实例", "Folder"),
        new("settings", "设置", "Settings")
    };

    [ObservableProperty]
    private string _statusText = "就绪";

    [ObservableProperty]
    private string _title = "CKAN UI";

    [ObservableProperty]
    private object? _currentPage;

    /// <summary>无参构造函数，供设计时与 XAML 预览使用。</summary>
    public ShellViewModel()
        : this(new EmptyNavigationService())
    {
    }

    /// <summary>带导航服务注入的构造函数。</summary>
    /// <param name="navigation">导航服务实例。</param>
    public ShellViewModel(INavigationService navigation)
    {
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        _currentPage = _navigation.CurrentPage;
        _navigation.Navigated += OnNavigated;

        NavigateCommand = new AsyncRelayCommand<string>(ExecuteNavigateAsync);
    }

    /// <summary>页面导航异步命令。</summary>
    public IAsyncRelayCommand<string> NavigateCommand { get; }

    private void OnNavigated(object? newPage, string key)
    {
        CurrentPage = newPage;
    }

    private Task ExecuteNavigateAsync(string? key, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(key))
        {
            return Task.CompletedTask;
        }

        return _navigation.NavigateAsync(key, null, ct);
    }

    private sealed class EmptyNavigationService : INavigationService
    {
        public object? CurrentPage => null;
        public string CurrentKey => string.Empty;
        public event Action<object?, string>? Navigated
        {
            add { }
            remove { }
        }

        public Task<bool> NavigateAsync(string pageKey, object? parameter = null, CancellationToken ct = default)
        {
            return Task.FromResult(false);
        }
    }
}
