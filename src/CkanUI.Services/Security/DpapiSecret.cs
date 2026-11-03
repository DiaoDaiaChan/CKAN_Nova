using System.Runtime.InteropServices;
using System.Text;

namespace CkanUI.Services.Security;

/// <summary>
/// Windows DPAPI 的最小封装：只做「按当前用户加密/解密一段字符串」。
/// 为什么不用现成的 ProtectedData 包：本项目没有该 NuGet 依赖，而 crypt32 的两个入口
/// 就在系统 DLL 里，P/Invoke 比为一个方法引入一个包更干净。
/// 加密失败返回 null 而不是回退明文——密钥写进明文文件，比不能保存更糟。
/// </summary>
internal static partial class DpapiSecret
{
    private const int CryptProtectUiForbidden = 0x1;

    /// <summary>加密成 Base64（失败返回 null）。</summary>
    public static string? Protect(string? plainText)
    {
        if (string.IsNullOrEmpty(plainText) || !OperatingSystem.IsWindows())
        {
            return null;
        }

        var input = Encoding.UTF8.GetBytes(plainText);
        var handle = GCHandle.Alloc(input, GCHandleType.Pinned);
        try
        {
            var blobIn = new DataBlob { CbData = input.Length, PbData = handle.AddrOfPinnedObject() };
            if (!CryptProtectData(ref blobIn, "CKAN UI AI key", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out var blobOut))
            {
                return null;
            }

            try
            {
                var protectedBytes = new byte[blobOut.CbData];
                Marshal.Copy(blobOut.PbData, protectedBytes, 0, blobOut.CbData);
                return Convert.ToBase64String(protectedBytes);
            }
            finally
            {
                LocalFree(blobOut.PbData);
            }
        }
        finally
        {
            handle.Free();
        }
    }

    /// <summary>解密（失败或平台不支持返回 null）。</summary>
    public static string? Unprotect(string? base64)
    {
        if (string.IsNullOrEmpty(base64) || !OperatingSystem.IsWindows())
        {
            return null;
        }

        byte[] input;
        try
        {
            input = Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            return null;
        }

        var handle = GCHandle.Alloc(input, GCHandleType.Pinned);
        try
        {
            var blobIn = new DataBlob { CbData = input.Length, PbData = handle.AddrOfPinnedObject() };
            if (!CryptUnprotectData(ref blobIn, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out var blobOut))
            {
                return null;
            }

            try
            {
                var plainBytes = new byte[blobOut.CbData];
                Marshal.Copy(blobOut.PbData, plainBytes, 0, blobOut.CbData);
                return Encoding.UTF8.GetString(plainBytes);
            }
            finally
            {
                LocalFree(blobOut.PbData);
            }
        }
        finally
        {
            handle.Free();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int CbData;
        public IntPtr PbData;
    }

    [LibraryImport("crypt32.dll", EntryPoint = "CryptProtectData", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CryptProtectData(
        ref DataBlob dataIn,
        string? description,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr promptStruct,
        int flags,
        out DataBlob dataOut);

    [LibraryImport("crypt32.dll", EntryPoint = "CryptUnprotectData", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CryptUnprotectData(
        ref DataBlob dataIn,
        IntPtr description,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr promptStruct,
        int flags,
        out DataBlob dataOut);

    [LibraryImport("kernel32.dll", EntryPoint = "LocalFree")]
    private static partial IntPtr LocalFree(IntPtr hMem);
}
