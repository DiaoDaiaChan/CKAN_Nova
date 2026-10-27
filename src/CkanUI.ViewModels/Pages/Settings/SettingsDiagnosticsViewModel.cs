using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CkanUI.Core;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Dialogs;

namespace CkanUI.ViewModels.Pages.Settings;

/// <summary>
/// 设置页「高级」组里的诊断区（03 篇 §7「高级/诊断」、D-03「复制诊断信息」）。
/// </summary>
/// <remarks>
/// 只做「拼文本 + 调宿主剪贴板委托」，没有任何 Avalonia 依赖，因此可 headless 断言；
/// 剪贴板能力由 DI 注入的 <c>Func&lt;string, Task&gt;</c> 提供（与错误对话框同一套先例）。
/// 缺一条关键信息时宁可整行不显示，也不要显示「Core 版本: 未知」这种半截行。
/// </remarks>
public sealed partial class SettingsDiagnosticsViewModel : ObservableObject
{
    private readonly Func<string, Task>? _copyTextAsync;

    /// <summary>创建诊断区。</summary>
    /// <param name="copyTextAsync">写剪贴板的宿主委托；为空时复制按钮给出「没有剪贴板通道」提示。</param>
    public SettingsDiagnosticsViewModel(Func<string, Task>? copyTextAsync = null)
    {
        _copyTextAsync = copyTextAsync;
        CopyCommand = new AsyncRelayCommand(ExecuteCopyAsync);
    }

    /// <summary>复制诊断信息命令。</summary>
    public IAsyncRelayCommand CopyCommand { get; }

    /// <summary>
    /// Core（CKAN 库）版本提供者。Core 的版本号（<c>CKAN.Meta.ReleaseVersion</c>）只在 Services 层可达，
    /// 分层铁律不允许 ViewModels 引用 CKAN 命名空间，故由宿主注入；未注入时该行不显示。
    /// </summary>
    public Func<string?>? CoreVersionProvider { get; set; }

    /// <summary>诊断行（名称 + 值）。</summary>
    [ObservableProperty]
    private IReadOnlyList<DiagnosticItem> _rows = Array.Empty<DiagnosticItem>();

    /// <summary>复制结果提示（成功 / 无通道 / 失败）。</summary>
    [ObservableProperty]
    private string? _copyStatusText;

    /// <summary>是否有诊断行。</summary>
    public bool HasRows => Rows.Count > 0;

    /// <summary>是否显示复制结果提示。</summary>
    public bool HasCopyStatus => !string.IsNullOrEmpty(CopyStatusText);

    /// <summary>按当前环境重建诊断行。</summary>
    /// <param name="instance">当前实例；未打开实例时传 null。</param>
    /// <param name="configDirectory">配置目录；为空时用默认目录。</param>
    /// <param name="downloadCacheDirectory">下载缓存目录；为空表示 Core 用默认目录。</param>
    public void Update(InstanceInfo? instance, string? configDirectory, string? downloadCacheDirectory)
    {
        var config = string.IsNullOrWhiteSpace(configDirectory) ? SettingsPaths.DefaultConfigDirectory : configDirectory;

        var rows = new List<DiagnosticItem>
        {
            new("应用版本", BuildInfo.Version),
        };

        var coreVersion = CoreVersionProvider?.Invoke();
        if (!string.IsNullOrWhiteSpace(coreVersion))
        {
            rows.Add(new DiagnosticItem("CKAN Core 版本", coreVersion));
        }

        rows.Add(new DiagnosticItem("当前实例", instance?.Name ?? "（未打开实例）"));
        if (instance is not null)
        {
            rows.Add(new DiagnosticItem("实例目录", instance.GameDir));
            rows.Add(new DiagnosticItem(
                "游戏版本",
                string.IsNullOrWhiteSpace(instance.GameVersion) ? "未知" : instance.GameVersion));
        }

        rows.Add(new DiagnosticItem("配置目录", config));
        rows.Add(new DiagnosticItem(
            "缓存目录",
            string.IsNullOrWhiteSpace(downloadCacheDirectory) ? "（Core 默认目录）" : downloadCacheDirectory));
        rows.Add(new DiagnosticItem("日志文件", SettingsPaths.LogFile(config)));

        Rows = rows;
        CopyStatusText = null;
    }

    /// <summary>拼出可直接贴进 issue 的诊断文本。</summary>
    public string BuildText() => DiagnosticTextBuilder.FromItems(Rows);

    private async Task ExecuteCopyAsync()
    {
        if (_copyTextAsync is null)
        {
            CopyStatusText = "当前环境没有剪贴板通道，无法复制";
            return;
        }

        try
        {
            await _copyTextAsync(BuildText()).ConfigureAwait(true);
            CopyStatusText = "已复制到剪贴板";
        }
        catch (NotSupportedException)
        {
            CopyStatusText = "当前环境没有剪贴板通道，无法复制";
        }
        catch (Exception ex)
        {
            CopyStatusText = "复制失败: " + ex.Message;
        }
    }

    partial void OnRowsChanged(IReadOnlyList<DiagnosticItem> value) => OnPropertyChanged(nameof(HasRows));

    partial void OnCopyStatusTextChanged(string? value) => OnPropertyChanged(nameof(HasCopyStatus));
}
