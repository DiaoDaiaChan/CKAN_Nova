using CkanUI.Core;

namespace CkanUI.ViewModels.Pages.Settings;

/// <summary>设置页诊断区要显示的路径（03 篇 §7 的「高级/诊断」组）。</summary>
public static class SettingsPaths
{
    /// <summary>日志文件名。</summary>
    public const string LogFileName = "CKAN.log";

    /// <summary>默认配置目录（<c>%LocalAppData%/CKAN</c>）；与 Services 层实际使用的目录同源。</summary>
    public static string DefaultConfigDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), BuildInfo.DataDirectoryName);

    /// <summary>日志文件路径。</summary>
    /// <param name="configDirectory">配置目录。</param>
    public static string LogFile(string configDirectory) => Path.Combine(configDirectory, LogFileName);
}
