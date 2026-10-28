using System.Reflection;
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
        var assembly = typeof(AboutInfoFactory).Assembly;
        var version = assembly.GetName().Version is { } v ? $"v{v.Major}.{v.Minor}.{v.Build}" : "v0.0.0";

        // M6 / PKG-03：发布脚本把「提交数.短哈希」写进 InformationalVersion（0.1.0+37.g4a888a5），
        // 关于页把它附在语义版本后面 —— 用户报问题时能直接定位到具体提交，
        // 不必再问「你装的是哪个包」。手工构建没有这段信息时保持纯版本号。
        if (ReadCommitSuffix(assembly) is { } commit)
        {
            version = $"{version} ({commit})";
        }

        return new AboutInfo(
            AppName: "CKAN UI",
            AppVersion: version,
            CoreVersion: CoreVersionInfo.ReleaseVersion,
            RuntimeVersion: $".NET {Environment.Version}",
            OsDescription: RuntimeInformation.OSDescription);
    }

    /// <summary>从 InformationalVersion 的 "+" 之后取出「提交数.短哈希」；没有就返回 null。</summary>
    private static string? ReadCommitSuffix(Assembly assembly)
    {
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(informational))
        {
            return null;
        }

        var plus = informational.IndexOf('+', StringComparison.Ordinal);
        return plus > 0 && plus < informational.Length - 1 ? informational[(plus + 1)..] : null;
    }
}
