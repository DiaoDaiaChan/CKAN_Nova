using System.Globalization;
using System.Runtime.InteropServices;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Logs;

namespace CkanUI.App.Diagnostics;

/// <summary>
/// 诊断包里「非日志事实」的提供者（14 篇 §5.1：env.txt / summary.json / registry.summary.json / config.redacted.json）。
/// </summary>
/// <remarks>
/// <para>
/// 为什么在 App 层：这些事实横跨关于框数据源（版本矩阵）、UI 设置（主题/密度/语言）与实例/仓库服务，
/// 而服务层不许认识实例与注册表 —— 那会把日志域钉死在特定装配上。服务层只按契约取快照并统一脱敏。
/// </para>
/// <para>
/// 刻意只给「计数 + 名字 + 版本」，不给路径：用户目录与游戏目录属于敏感信息，
/// 虽然 <see cref="ILogRedactor"/> 会再抹一遍，但没有必要把原始路径带进诊断包。
/// </para>
/// </remarks>
public sealed class AppDiagnosticFactsProvider : IDiagnosticFactsProvider
{
    private readonly IInstanceService? _instances;
    private readonly IRepositoryService? _repositories;
    private readonly IUiSettingsStore? _uiSettings;

    /// <summary>创建提供者。所有依赖都可缺省：任一服务没接线时只是少几行事实，仍然能出包。</summary>
    public AppDiagnosticFactsProvider(
        IInstanceService? instances = null,
        IRepositoryService? repositories = null,
        IUiSettingsStore? uiSettings = null)
    {
        _instances = instances;
        _repositories = repositories;
        _uiSettings = uiSettings;
    }

    /// <inheritdoc />
    public Task<DiagnosticFacts?> CollectAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<DiagnosticFacts?>(Collect());
    }

    private DiagnosticFacts Collect()
    {
        var about = AboutInfoFactory.Create();
        var settings = TryLoadSettings();
        var instances = _instances?.Instances;
        var current = _instances?.Current;
        var repositories = _repositories?.Repositories;

        var summary = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["appVersion"] = about.AppVersion,
            ["coreVersion"] = about.CoreVersion,
            ["runtime"] = about.RuntimeVersion,
            ["os"] = about.OsDescription,
            ["architecture"] = RuntimeInformation.ProcessArchitecture.ToString(),
            ["culture"] = CultureInfo.CurrentCulture.Name,
            ["uiCulture"] = CultureInfo.CurrentUICulture.Name,
            ["theme"] = settings?.Theme,
            ["density"] = settings?.Density,
            ["fontScale"] = settings?.FontScale,
            ["locale"] = string.IsNullOrWhiteSpace(settings?.Locale) ? "(follow-system)" : settings.Locale,
            ["instanceCount"] = instances?.Count ?? 0,
            ["currentInstance"] = current?.Name ?? "(none)",
            ["repositoryCount"] = repositories?.Count ?? 0,
        };

        var environment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["CKAN_UI_VERSION"] = about.AppVersion,
            ["CKAN_CORE_VERSION"] = about.CoreVersion,
            ["RUNTIME"] = about.RuntimeVersion,
            ["OS"] = about.OsDescription,
            ["PROCESS_ARCH"] = RuntimeInformation.ProcessArchitecture.ToString(),
            ["CLR"] = RuntimeInformation.FrameworkDescription,
            ["CULTURE"] = CultureInfo.CurrentCulture.Name,
            ["UI_CULTURE"] = CultureInfo.CurrentUICulture.Name,
        };

        // 只报版本号与计数，别把 CKAN 的注册表条目原样倒出来（14 篇 §5.1 的 registry.summary.json 语义）。
        var manifest = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["coreVersion"] = about.CoreVersion,
            ["instanceCount"] = instances?.Count ?? 0,
            ["repositoryCount"] = repositories?.Count ?? 0,
        };

        var config = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["theme"] = settings?.Theme,
            ["density"] = settings?.Density,
            ["fontScale"] = settings?.FontScale,
            ["locale"] = settings?.Locale,
            ["accentColor"] = settings?.AccentColor,
            ["motionIntensity"] = settings?.MotionIntensity,
            ["radiusScale"] = settings?.RadiusScale,
            ["backdrop"] = settings?.BackdropKind,
        };

        return new DiagnosticFacts
        {
            Environment = environment,
            Summary = summary,
            Manifest = manifest,
            Config = config,
        };
    }

    /// <summary>读 UI 设置：读盘失败（半截文件、权限）时不让诊断包整体失败。</summary>
    private UiSettings? TryLoadSettings()
    {
        if (_uiSettings is null)
        {
            return null;
        }

        try
        {
            return _uiSettings.Load();
        }
        catch (Exception)
        {
            return null;
        }
    }
}
