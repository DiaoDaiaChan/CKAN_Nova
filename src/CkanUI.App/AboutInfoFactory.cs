using System.Runtime.InteropServices;
using CkanUI.Services.Diagnostics;
using CkanUI.ViewModels.Dialogs;

namespace CkanUI.App;

/// <summary>关于框的数据来源（12 篇 F-55）：版本号与平台事实在 App 层采集，ViewModels 只接收字符串。</summary>
public static class AboutInfoFactory
{
    /// <summary>采集当前运行的版本矩阵。</summary>
    public static AboutInfo Create()
    {
        var assemblyName = typeof(AboutInfoFactory).Assembly.GetName();
        var version = assemblyName.Version is { } v ? $"v{v.Major}.{v.Minor}.{v.Build}" : "v0.0.0";

        return new AboutInfo(
            AppName: "CKAN UI",
            AppVersion: version,
            CoreVersion: CoreVersionInfo.ReleaseVersion,
            RuntimeVersion: $".NET {Environment.Version}",
            OsDescription: RuntimeInformation.OSDescription);
    }
}
