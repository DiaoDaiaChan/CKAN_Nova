using System.Runtime.InteropServices;

namespace CkanUI.Tools.Installer;

/// <summary>
/// 快捷方式创建：直接用 COM vtable 函数指针调 IShellLinkW / IPersistFile。
///
/// 历史坑（真炸过）：原先用 <c>Marshal.GetObjectForIUnknown</c> 取 RCW，这在 Native AOT 下
/// 不被支持（需要 BuiltInComInteropSupport），异常被 catch 吞掉后 CreateShortcut 安静返回 false。
/// 结果就是：装完了，但开始菜单和桌面一个快捷方式都没有——用户报「没注册到开始菜单」。
/// 现在改成纯 vtable 调用，AOT 下确定可用，并且创建后回读文件存在性。
/// </summary>
internal static unsafe partial class ShortcutHelper
{
    private static readonly Guid ClsidShellLink = new("00021401-0000-0000-C000-000000000046");
    private static readonly Guid IidIShellLinkW = new("000214F9-0000-0000-C000-000000000046");
    private static readonly Guid IidIPersistFile = new("0000010B-0000-0000-C000-000000000046");
    private const uint CLSCTX_INPROC_SERVER = 1;

    // IShellLinkW vtable 序号（IUnknown 占 0..2）
    private const int SlSetDescription = 7;
    private const int SlSetWorkingDirectory = 9;
    private const int SlSetArguments = 11;
    private const int SlSetIconLocation = 17;
    private const int SlSetPath = 20;
    // IPersistFile vtable 序号：GetClassID=3 IsDirty=4 Load=5 Save=6
    private const int PfSave = 6;
    private const int IUnkRelease = 2;
    private const int IUnkQueryInterface = 0;

    public static bool CreateShortcut(string shortcutPath, string targetPath, string workingDirectory, string? description = null, string? arguments = null)
        => CreateShortcut(shortcutPath, targetPath, workingDirectory, out _, description, arguments);

    /// <summary>
    /// 带失败原因的重载。<c>false</c> 只是"没建成"，用户需要知道是权限、路径还是 COM 拒绝——
    /// 早先异常被吞成 false，界面上什么都看不到，才有了"装了但开始菜单没有图标"这种悬案。
    /// </summary>
    public static bool CreateShortcut(string shortcutPath, string targetPath, string workingDirectory, out string? error, string? description = null, string? arguments = null)
    {
        try
        {
            return CreateShortcutCore(shortcutPath, targetPath, workingDirectory, description, arguments, out error);
        }
        catch (Exception ex)
        {
            error = Describe(ex);
            return false;
        }
    }

    private static bool CreateShortcutCore(string shortcutPath, string targetPath, string workingDirectory, string? description, string? arguments, out string? error)
    {
        error = null;

        var dir = Path.GetDirectoryName(shortcutPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        // 先删后建：旧文件可能是只读或被占用，留着会让 Save 失败却看不出来。
        if (File.Exists(shortcutPath))
        {
            try { File.Delete(shortcutPath); } catch { }
        }

        var hr = CoCreateInstance(in ClsidShellLink, IntPtr.Zero, CLSCTX_INPROC_SERVER, in IidIShellLinkW, out var pLink);
        if (hr != 0 || pLink == IntPtr.Zero)
        {
            error = $"COM 创建 IShellLink 失败（HRESULT 0x{hr:X8}）";
            return false;
        }

        try
        {
            var vtbl = *(nint**)pLink;
            if (CallString(vtbl, SlSetPath, pLink, targetPath) != 0)
            {
                error = "IShellLink::SetPath 失败";
                return false;
            }

            CallString(vtbl, SlSetWorkingDirectory, pLink, workingDirectory);

            if (!string.IsNullOrEmpty(description))
            {
                CallString(vtbl, SlSetDescription, pLink, description);
            }

            if (!string.IsNullOrEmpty(arguments))
            {
                CallString(vtbl, SlSetArguments, pLink, arguments);
            }

            CallStringInt(vtbl, SlSetIconLocation, pLink, targetPath, 0);

            var iid = IidIPersistFile;
            var queryInterface = (delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)vtbl[IUnkQueryInterface];
            nint pPersist;
            if (queryInterface(pLink, &iid, &pPersist) != 0 || pPersist == 0)
            {
                error = "QueryInterface(IPersistFile) 失败";
                return false;
            }

            var pv = *(nint**)pPersist;
            try
            {
                var save = (delegate* unmanaged[Stdcall]<nint, char*, int, int>)pv[PfSave];
                fixed (char* pName = shortcutPath)
                {
                    var saveHr = save(pPersist, pName, 1);
                    if (saveHr != 0)
                    {
                        error = $"IPersistFile::Save 失败（HRESULT 0x{saveHr:X8}）";
                        return false;
                    }
                }
            }
            finally
            {
                Release(pPersist, pv);
            }
        }
        finally
        {
            Release(pLink, *(nint**)pLink);
        }

        // 回读：Save 返回 S_OK 不代表文件真落地（杀软拦截、路径被重定向都会静默失败）。
        if (!File.Exists(shortcutPath))
        {
            error = "写入后目标 .lnk 文件不存在（可能被杀毒软件拦截）";
            return false;
        }

        return true;
    }

    private static string Describe(Exception ex) => ex switch
    {
        UnauthorizedAccessException => "拒绝访问（目录不可写或被安全软件拦截）",
        DirectoryNotFoundException => "目标目录不存在",
        PathTooLongException => "路径过长",
        IOException => "目录不可写或路径被占用",
        NotSupportedException => "系统不支持该 COM 组件",
        _ => ex.GetType().Name + ": " + ex.Message
    };

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

    private static int CallString(nint* vtbl, int index, nint self, string value)
    {
        var fn = (delegate* unmanaged[Stdcall]<nint, char*, int>)vtbl[index];
        fixed (char* p = value)
        {
            return fn(self, p);
        }
    }

    private static int CallStringInt(nint* vtbl, int index, nint self, string value, int extra)
    {
        var fn = (delegate* unmanaged[Stdcall]<nint, char*, int, int>)vtbl[index];
        fixed (char* p = value)
        {
            return fn(self, p, extra);
        }
    }

    private static void Release(nint persist, nint* vtbl)
    {
        try
        {
            var release = (delegate* unmanaged[Stdcall]<nint, uint>)vtbl[IUnkRelease];
            release(persist);
        }
        catch
        {
            // 释放失败不阻塞安装
        }
    }

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(
        in Guid rclsid,
        IntPtr pUnkOuter,
        uint dwClsContext,
        in Guid riid,
        out IntPtr ppv);
}
