using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security;
using CkanUI.Services.Abstractions.Dtos;
using Microsoft.Win32.SafeHandles;

namespace CkanUI.Services.Dedup;

/// <summary>
/// 真实硬链接实现：Windows 走 <c>kernel32!CreateHardLinkW</c>，Linux/macOS/FreeBSD 走 libc 的 <c>link(2)</c>。
/// </summary>
/// <remarks>
/// 平台守卫一律用 <see cref="OperatingSystem.IsWindows"/> 之类的运行时判断（而不是把 TFM 改成 <c>-windows</c>），
/// 这样 CA1416 能看懂守卫、构建保持 0 警告，同时 Linux/macOS 的 CI 也能跑到真实硬链接路径。
/// 已知缺口：非 Windows 平台读不到文件标识（inode），<see cref="TryGetIdentity"/> 返回 false，
/// 上层因此无法识别「已经是同一份文件」的组（见 docs/design/dedup.md）。
/// </remarks>
public sealed class NativeDedupLinker : IDedupLinker
{
    private const int ErrorAccessDenied = 5;
    private const int ErrorNotSameDevice = 17;
    private const int ErrorFileExists = 80;
    private const int ErrorAlreadyExists = 183;
    private const int ErrorNoLink = 1142;

    // Unix errno：EXDEV / EEXIST / EACCES / EPERM / ENOENT / EMLINK
    private const int ErrnoPermission = 1;
    private const int ErrnoAccess = 13;
    private const int ErrnoNoEntry = 2;
    private const int ErrnoExists = 17;
    private const int ErrnoCrossDevice = 18;
    private const int ErrnoTooManyLinks = 31;

    /// <summary>无状态实现，服务层直接复用同一个实例。</summary>
    public static NativeDedupLinker Instance { get; } = new();

    /// <inheritdoc />
    public DedupPlatformSupport Support =>
        OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() || OperatingSystem.IsFreeBSD()
            ? DedupPlatformSupport.Supported
            : DedupPlatformSupport.UnsupportedPlatform;

    /// <inheritdoc />
    public bool TryGetIdentity(string path, out DedupFileIdentity identity)
    {
        identity = DedupFileIdentity.Unknown;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        if (OperatingSystem.IsWindows())
        {
            return TryGetWindowsIdentity(path, out identity);
        }

        // 非 Windows：.NET 不暴露 inode，这里如实返回「未知」而不是瞎猜。
        return false;
    }

    /// <inheritdoc />
    public void CreateLink(string linkPath, string existingPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(linkPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(existingPath);

        if (OperatingSystem.IsWindows())
        {
            CreateWindowsLink(linkPath, existingPath);
            return;
        }

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() || OperatingSystem.IsFreeBSD())
        {
            CreateUnixLink(linkPath, existingPath);
            return;
        }

        throw new PlatformNotSupportedException("当前平台不支持创建硬链接。");
    }

    [SupportedOSPlatform("windows")]
    private static void CreateWindowsLink(string linkPath, string existingPath)
    {
        if (CreateHardLinkW(linkPath, existingPath, IntPtr.Zero))
        {
            return;
        }

        var error = Marshal.GetLastPInvokeError();
        throw error switch
        {
            ErrorNotSameDevice => new IOException("跨卷不能创建硬链接。"),
            ErrorFileExists or ErrorAlreadyExists => new IOException($"目标路径已存在：{linkPath}"),
            ErrorAccessDenied => new UnauthorizedAccessException($"没有权限创建硬链接：{linkPath}"),
            ErrorNoLink => new IOException("文件系统不支持硬链接（例如 FAT32/exFAT）。"),
            _ => new IOException($"创建硬链接失败（Win32 错误 {error}：{new Win32Exception(error).Message}）。"),
        };
    }

    [SupportedOSPlatform("windows")]
    private static bool TryGetWindowsIdentity(string path, out DedupFileIdentity identity)
    {
        identity = DedupFileIdentity.Unknown;
        try
        {
            using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (!GetFileInformationByHandle(handle, out var info))
            {
                return false;
            }

            var index = ((ulong)info.FileIndexHigh << 32) | info.FileIndexLow;
            identity = new DedupFileIdentity(info.VolumeSerialNumber, index);
            return identity.IsKnown;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return false;
        }
    }

    private static void CreateUnixLink(string linkPath, string existingPath)
    {
        if (UnixLink(existingPath, linkPath) == 0)
        {
            return;
        }

        var errno = Marshal.GetLastPInvokeError();
        throw errno switch
        {
            ErrnoCrossDevice => new IOException("跨卷不能创建硬链接。"),
            ErrnoExists => new IOException($"目标路径已存在：{linkPath}"),
            ErrnoPermission or ErrnoAccess => new UnauthorizedAccessException($"没有权限创建硬链接：{linkPath}"),
            ErrnoNoEntry => new FileNotFoundException($"保留副本已不存在：{existingPath}"),
            ErrnoTooManyLinks => new IOException("该文件的硬链接数已达上限。"),
            _ => new IOException($"创建硬链接失败（errno {errno}）。"),
        };
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
    [SupportedOSPlatform("windows")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkW(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);

    [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandle", SetLastError = true)]
    [SupportedOSPlatform("windows")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle hFile, out ByHandleFileInformation lpFileInformation);

    [DllImport("libc", EntryPoint = "link", SetLastError = true)]
    private static extern int UnixLink(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string oldPath,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string newPath);

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public uint CreationTimeLow;
        public uint CreationTimeHigh;
        public uint LastAccessTimeLow;
        public uint LastAccessTimeHigh;
        public uint LastWriteTimeLow;
        public uint LastWriteTimeHigh;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }
}
