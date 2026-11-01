using System;
using System.IO;

using Avalonia.Controls;

namespace CkanUI.App.Branding;

/// <summary>
/// 应用图标的唯一加载入口（M6 / PKG-02）：窗口、托盘共用同一份随包复制的
/// <c>Assets/ckan.ico</c>（7 档 PNG-entry ICO，由 <c>tools/icon-gen</c> 生成）。
/// </summary>
/// <remarks>
/// <para>
/// 图标文件是 <c>Content</c> 而不是 <c>AvaloniaResource</c>（csproj 里按便携包布局复制到
/// <c>Assets/</c>），所以这里走磁盘路径而不是 <c>avares://</c>；两者都用同一个路径口径，
/// 打包漏文件时两边一起降级，不会出现「窗口有图标、托盘没图标」的半残状态。
/// </para>
/// <para>
/// 加载失败（文件缺失、ICO 损坏、渲染后端缺失）一律返回 <c>null</c>，由调用方各自决定降级：
/// 窗口保持平台默认外观，托盘退回 <see cref="Tray.TrayHost"/> 自绘的方块。
/// </para>
/// </remarks>
public static class AppIcon
{
    /// <summary>图标文件名（相对 <c>Assets/</c>）。</summary>
    public const string FileName = "ckan.ico";

    /// <summary>图标所在的子目录名。</summary>
    private const string DirectoryName = "Assets";

    /// <summary>图标在输出目录里的绝对路径。</summary>
    /// <returns>形如 <c>.../Assets/ckan.ico</c> 的路径；两处候选根目录都没有时返回第一处的路径（不保证存在）。</returns>
    public static string ResolvePath()
    {
        foreach (var root in CandidateRoots())
        {
            var path = Path.Combine(root, DirectoryName, FileName);
            if (File.Exists(path))
            {
                return path;
            }
        }

        return Path.Combine(AppContext.BaseDirectory, DirectoryName, FileName);
    }

    /// <summary>加载图标。</summary>
    /// <returns>加载成功返回图标实例，否则 <c>null</c>。</returns>
    public static WindowIcon? TryLoad()
    {
        try
        {
            var path = ResolvePath();
            return File.Exists(path) ? new WindowIcon(path) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// 候选根目录：先程序集所在目录，再进程基目录。
    /// 前者是为了测试宿主（图标跟着 <c>CkanUI.dll</c> 复制到测试输出目录），后者是常规 exe；
    /// 单文件发布时 <see cref="System.Reflection.Assembly.Location"/> 为空串，跳过。
    /// </summary>
    private static IEnumerable<string> CandidateRoots()
    {
        var assemblyDirectory = System.Reflection.Assembly.GetExecutingAssembly().Location;
        if (assemblyDirectory.Length > 0)
        {
            var directory = Path.GetDirectoryName(assemblyDirectory);
            if (!string.IsNullOrEmpty(directory))
            {
                yield return directory;
            }
        }

        yield return AppContext.BaseDirectory;
    }
}
