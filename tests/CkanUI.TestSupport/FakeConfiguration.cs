using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace CkanUI.TestSupport;

/// <summary>无 I/O 的 IConfiguration 假实现（02 篇 §3：设置服务测试不得碰磁盘）。</summary>
public sealed class FakeConfiguration : CKAN.Configuration.IConfiguration
{
    public string? AutoStartInstance { get; set; }
    public string? DownloadCacheDir { get; set; }
    public long? CacheSizeLimit { get; set; }
    public int RefreshRate { get; set; }
    public string? Language { get; set; }
    public string?[] PreferredHosts { get; set; } = Array.Empty<string?>();
    public bool? DevBuilds { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>测试里手动触发变更通知，同时避免 CS0067（事件从未被引发）。</summary>
    public void RaisePropertyChanged(string propertyName)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public IEnumerable<string> GetAuthTokenHosts() => Array.Empty<string>();

    public bool TryGetAuthToken(string host, [NotNullWhen(true)] out string? token)
    {
        token = null;
        return false;
    }

    public void SetAuthToken(string host, string? token) { }

    public void SetRegistryToInstances(SortedList<string, CKAN.GameInstance> instances) { }

    public IEnumerable<Tuple<string, string, string>> GetInstances()
        => Array.Empty<Tuple<string, string, string>>();

    public string[] GetGlobalInstallFilters(CKAN.Games.IGame game) => Array.Empty<string>();

    public void SetGlobalInstallFilters(CKAN.Games.IGame game, string[] value) { }
}
