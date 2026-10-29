namespace CkanUI.Tools.Installer;

internal static class ShortcutHelper
{
    public static bool CreateShortcut(string shortcutPath, string targetPath, string workingDirectory, string? description = null)
    {
        try
        {
            var dir = Path.GetDirectoryName(shortcutPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null)
            {
                return false;
            }

            var shell = Activator.CreateInstance(shellType);
            if (shell == null)
            {
                return false;
            }

            var shortcut = shellType.InvokeMember(
                "CreateShortcut",
                System.Reflection.BindingFlags.InvokeMethod,
                null,
                shell,
                new object[] { shortcutPath });

            if (shortcut == null)
            {
                return false;
            }

            var scType = shortcut.GetType();
            scType.InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { targetPath });
            scType.InvokeMember("WorkingDirectory", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { workingDirectory });
            scType.InvokeMember("IconLocation", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { targetPath + ",0" });

            if (!string.IsNullOrEmpty(description))
            {
                scType.InvokeMember("Description", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { description });
            }

            scType.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, shortcut, null);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void DeleteShortcut(string shortcutPath)
    {
        try
        {
            if (File.Exists(shortcutPath))
            {
                File.Delete(shortcutPath);
            }
        }
        catch
        {
            // 忽略删除快捷方式异常
        }
    }
}
