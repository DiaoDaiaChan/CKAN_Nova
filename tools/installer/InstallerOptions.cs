namespace CkanUI.Tools.Installer;

internal sealed class InstallerOptions
{
    public bool IsUninstall { get; set; }

    public bool Quiet { get; set; }

    public string? InstallDir { get; set; }

    public string? SourceDir { get; set; }

    public bool NoShortcuts { get; set; }

    /// <summary>只关桌面快捷方式（与 <see cref="NoStartMenuShortcut"/> 独立，向导逐项勾选）。</summary>
    public bool NoDesktopShortcut { get; set; }

    /// <summary>只关开始菜单快捷方式。</summary>
    public bool NoStartMenuShortcut { get; set; }

    /// <summary>不注册 HKCU App Paths。</summary>
    public bool NoAppPath { get; set; }

    /// <summary>完全不写注册表（绿色/可移植安装：无「应用和功能」条目，用安装目录里的卸载器）。</summary>
    public bool NoRegistry { get; set; }

    /// <summary>
    /// 快捷方式根目录覆盖（可移植版 / 自动化测试用）。给定后开始菜单链接落在
    /// &lt;root&gt;\StartMenu，桌面链接落在 &lt;root&gt;\Desktop，不碰真实用户目录。
    /// </summary>
    public string? ShortcutRoot { get; set; }

    /// <summary>安装过程中可降级的失败（快捷方式没建成之类），由向导在完成页展示。</summary>
    public List<string> Warnings { get; } = [];

    /// <summary>Warnings 的技术细节（完整路径与失败原因），只打到控制台，界面上不占版面。</summary>
    public List<string> WarningDetails { get; } = [];

    public bool NoLaunch { get; set; }

    public bool Force { get; set; }

    public bool RemoveUserData { get; set; }

    public bool ShowHelp { get; set; }

    /// <summary>只打印安装环境诊断（权限、路径、上次安装痕迹），不改动任何文件。</summary>
    public bool Diagnose { get; set; }

    public static InstallerOptions Parse(string[] args)
    {
        var options = new InstallerOptions();

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i].Trim();
            if (string.IsNullOrEmpty(arg))
            {
                continue;
            }

            if (arg.StartsWith("/DIR=", StringComparison.OrdinalIgnoreCase))
            {
                options.InstallDir = arg.Substring(5).Trim('"');
                continue;
            }

            switch (arg.ToLowerInvariant())
            {
                case "-uninstall":
                case "/uninstall":
                case "--uninstall":
                    options.IsUninstall = true;
                    break;

                case "-quiet":
                case "/quiet":
                case "--quiet":
                case "-silent":
                case "/silent":
                case "--silent":
                case "/s":
                case "-q":
                    options.Quiet = true;
                    break;

                case "-noshortcuts":
                case "/noshortcuts":
                case "--noshortcuts":
                    options.NoShortcuts = true;
                    options.NoDesktopShortcut = true;
                    options.NoStartMenuShortcut = true;
                    break;

                case "-nodesktopshortcut":
                case "/nodesktopshortcut":
                case "--nodesktopshortcut":
                    options.NoDesktopShortcut = true;
                    break;

                case "-nostartmenushortcut":
                case "/nostartmenushortcut":
                case "--nostartmenushortcut":
                    options.NoStartMenuShortcut = true;
                    break;

                case "-noregistry":
                case "/noregistry":
                case "--noregistry":
                    options.NoRegistry = true;
                    break;

                case "-noapppath":
                case "/noapppath":
                case "--noapppath":
                    options.NoAppPath = true;
                    break;

                case "-shortcutroot":
                case "/shortcutroot":
                case "--shortcutroot":
                    if (i + 1 < args.Length)
                    {
                        options.ShortcutRoot = args[++i].Trim().Trim('"');
                    }
                    break;

                case "-diagnose":
                case "/diagnose":
                case "--diagnose":
                    options.Diagnose = true;
                    break;

                case "-nolaunch":
                case "/nolaunch":
                case "--nolaunch":
                    options.NoLaunch = true;
                    break;

                case "-force":
                case "/force":
                case "--force":
                case "-f":
                    options.Force = true;
                    break;

                case "-removeuserdata":
                case "/removeuserdata":
                case "--removeuserdata":
                    options.RemoveUserData = true;
                    break;

                case "-help":
                case "/help":
                case "--help":
                case "/?":
                case "-h":
                    options.ShowHelp = true;
                    break;

                case "-installdir":
                case "/installdir":
                case "--installdir":
                    if (i + 1 < args.Length)
                    {
                        options.InstallDir = args[++i].Trim().Trim('"');
                    }
                    break;

                case "-sourcedir":
                case "/sourcedir":
                case "--sourcedir":
                    if (i + 1 < args.Length)
                    {
                        options.SourceDir = args[++i].Trim().Trim('"');
                    }
                    break;

                default:
                    // 兼容未带参数名的直接路径传参
                    if (!arg.StartsWith('-') && !arg.StartsWith('/') && string.IsNullOrEmpty(options.SourceDir) && Directory.Exists(arg))
                    {
                        options.SourceDir = arg;
                    }
                    break;
            }
        }

        return options;
    }
}
