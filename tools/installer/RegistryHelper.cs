using Microsoft.Win32;

namespace CkanUI.Tools.Installer;

internal static class RegistryHelper
{
    private const string UninstallSubKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\CkanUI";
    private const string AppPathsSubKey = @"Software\Microsoft\Windows\CurrentVersion\App Paths";

    public static void RegisterUninstallEntry(
        string version,
        string installLocation,
        string appExePath,
        string uninstallCommand,
        string quietUninstallCommand,
        int estimatedSizeKb)
    {
        using var key = Registry.CurrentUser.CreateSubKey(UninstallSubKey);
        if (key == null)
        {
            throw new InvalidOperationException("无法创建 HKCU 卸载注册项: " + UninstallSubKey);
        }

        key.SetValue("DisplayName", "CKAN UI", RegistryValueKind.String);
        key.SetValue("DisplayVersion", version, RegistryValueKind.String);
        key.SetValue("Publisher", "CKAN UI Project", RegistryValueKind.String);
        key.SetValue("DisplayIcon", appExePath + ",0", RegistryValueKind.String);
        key.SetValue("InstallLocation", installLocation, RegistryValueKind.String);
        key.SetValue("UninstallString", uninstallCommand, RegistryValueKind.String);
        key.SetValue("QuietUninstallString", quietUninstallCommand, RegistryValueKind.String);
        key.SetValue("EstimatedSize", estimatedSizeKb, RegistryValueKind.DWord);
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
    }

    public static void RegisterAppPath(string exeName, string exePath, string workingDir)
    {
        var subKey = AppPathsSubKey + @"\" + exeName;
        using var key = Registry.CurrentUser.CreateSubKey(subKey);
        if (key != null)
        {
            key.SetValue(string.Empty, exePath, RegistryValueKind.String);
            key.SetValue("Path", workingDir, RegistryValueKind.String);
        }
    }

    public static void CleanStaleAppPaths(string[] candidateExes, string currentExeName, string installLocation)
    {
        foreach (var candidate in candidateExes)
        {
            if (string.Equals(candidate, currentExeName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var subKey = AppPathsSubKey + @"\" + candidate;
            using var key = Registry.CurrentUser.OpenSubKey(subKey, false);
            if (key != null)
            {
                var val = key.GetValue(string.Empty) as string;
                if (!string.IsNullOrEmpty(val) && val.StartsWith(installLocation, StringComparison.OrdinalIgnoreCase))
                {
                    key.Close();
                    Registry.CurrentUser.DeleteSubKeyTree(subKey, false);
                }
            }
        }
    }

    public static void UnregisterUninstallEntry(string installLocation)
    {
        using var key = Registry.CurrentUser.OpenSubKey(UninstallSubKey, false);
        if (key != null)
        {
            var registeredLocation = key.GetValue("InstallLocation") as string;
            var regLoc = registeredLocation?.TrimEnd('\\', '/');
            var instLoc = installLocation.TrimEnd('\\', '/');

            if (string.IsNullOrEmpty(regLoc) || string.Equals(regLoc, instLoc, StringComparison.OrdinalIgnoreCase))
            {
                key.Close();
                Registry.CurrentUser.DeleteSubKeyTree(UninstallSubKey, false);
            }
        }
    }

    public static void UnregisterAppPaths(string[] exeNames, string installLocation)
    {
        foreach (var name in exeNames)
        {
            var subKey = AppPathsSubKey + @"\" + name;
            using var key = Registry.CurrentUser.OpenSubKey(subKey, false);
            if (key != null)
            {
                var val = key.GetValue(string.Empty) as string;
                key.Close();
                if (string.IsNullOrEmpty(val) || val.StartsWith(installLocation, StringComparison.OrdinalIgnoreCase))
                {
                    Registry.CurrentUser.DeleteSubKeyTree(subKey, false);
                }
            }
        }
    }
}
