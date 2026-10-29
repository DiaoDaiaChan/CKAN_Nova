namespace CkanUI.Tools.Installer;

internal sealed class InstallerOptions
{
    public bool IsUninstall { get; set; }

    public bool Quiet { get; set; }

    public string? InstallDir { get; set; }

    public string? SourceDir { get; set; }

    public bool NoShortcuts { get; set; }

    public bool NoLaunch { get; set; }

    public bool Force { get; set; }

    public bool RemoveUserData { get; set; }

    public bool ShowHelp { get; set; }

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
