using System.Runtime.InteropServices;
using System.Runtime.Versioning;

[assembly: SupportedOSPlatform("windows")]

namespace CkanUI.Tools.Installer;

internal static partial class NativeMethods
{
    public const int ATTACH_PARENT_PROCESS = -1;

    public const uint MB_OK = 0x00000000;
    public const uint MB_OKCANCEL = 0x00000001;
    public const uint MB_YESNO = 0x00000004;

    public const uint MB_ICONERROR = 0x00000010;
    public const uint MB_ICONQUESTION = 0x00000020;
    public const uint MB_ICONWARNING = 0x00000030;
    public const uint MB_ICONINFORMATION = 0x00000040;

    public const int IDOK = 1;
    public const int IDCANCEL = 2;
    public const int IDYES = 6;
    public const int IDNO = 7;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AttachConsole(int dwProcessId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool FreeConsole();

    [LibraryImport("user32.dll", EntryPoint = "MessageBoxW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    public static partial int MessageBox(IntPtr hWnd, string lpText, string lpCaption, uint uType);
}
