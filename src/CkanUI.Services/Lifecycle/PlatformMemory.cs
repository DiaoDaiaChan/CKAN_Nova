using System.Diagnostics;
using System.Runtime.InteropServices;
using log4net;

namespace CkanUI.Services.Lifecycle;

/// <summary>
/// 平台内存回收的生产实现（18 篇 §3 L3/L4）。
/// </summary>
/// <remarks>
/// <para>L3 做的是「一定安全且可测」的部分：两轮阻塞压缩式 GC + 等终结器，并用
/// <see cref="GC.GetTotalMemory(bool)"/> 的差值报告回收量；纯托管，跨平台一致。</para>
/// <para>L4 只在 Windows 上接：psapi 的 <c>EmptyWorkingSet</c>（源生成 P/Invoke，不用运行时 IL stub），
/// 用 trim 前后 <see cref="Process.WorkingSet64"/> 的差值报告「还了多少」。工作集 trim 只是把页面推到
/// 待机/空闲列表，<b>不减少提交内存</b>，切回时靠 page fault 换回（R-58）；所以文案只说「让给系统」，
/// 绝不说「给 KSP 用」（R-64）。非 Windows 没有等价物（<c>madvise(MADV_DONTNEED)</c> 只能作用于自己的映射，
/// 换不回进程级工作集），按契约返回 <see cref="MemoryTrimResult.Unsupported"/>，编排层记「未执行」不算失败。</para>
/// <para>失败路径一律降级成结果对象（<c>Supported=true</c>、回收 0 次、带原因）并 <c>Log.Warn</c>，
/// 不向调用方抛异常；测量失败也算失败路径，因为没观测就没法报数。</para>
/// </remarks>
public sealed partial class PlatformMemory : IPlatformMemory
{
    /// <summary>MB 换算（只用于文案，报告与断言都按字节）。</summary>
    private const long BytesPerMegabyte = 1024 * 1024;

    private static readonly ILog Log = LogManager.GetLogger(typeof(PlatformMemory));

    public MemoryTrimResult CollectManagedHeap()
    {
        try
        {
            var before = GC.GetTotalMemory(forceFullCollection: false);

            // 两轮：第一轮回收主对象图，第二轮清掉上一轮变成垃圾的短命对象；中间等终结器，
            // 否则带终结器的对象（文件句柄、位图包装）会把后续对象一起顶住。
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

            var after = GC.GetTotalMemory(forceFullCollection: false);
            var released = Math.Max(0, before - after);
            Log.Info($"L3 托管堆回收完成：释放约 {released / BytesPerMegabyte}MB。");
            return new MemoryTrimResult(true, 1, released, $"托管堆回收约 {released / BytesPerMegabyte}MB。");
        }
        catch (Exception ex)
        {
            Log.Warn("L3 托管堆回收失败。", ex);
            return new MemoryTrimResult(true, 0, 0, ex.Message);
        }
    }

    public MemoryTrimResult TrimWorkingSet()
    {
        if (!OperatingSystem.IsWindows())
        {
            // 与 psapi 无关：别的平台根本没有「让系统收走本进程工作集」的公开入口，
            // 这不是没实现完，而是平台没有对应能力，所以按契约报 Unsupported。
            return MemoryTrimResult.Unsupported(
                $"当前平台（{RuntimeInformation.RuntimeIdentifier}）不是 Windows，工作集归还未接入：仅 Windows 有 psapi 的 EmptyWorkingSet。");
        }

        long before = 0;
        long after = 0;
        bool emptied;
        int win32Error = 0;

        try
        {
            using var process = Process.GetCurrentProcess();
            before = process.WorkingSet64;

            emptied = EmptyWorkingSet(process.Handle);

            // 立刻取错误码：中途再走别的 P/Invoke 会把线程的 last error 覆盖掉。
            if (!emptied)
            {
                win32Error = Marshal.GetLastWin32Error();
            }

            // 重新读一次而不是复用缓存的计数：WorkingSet64 是快照值，trim 后需要 Refresh 才看得到新值。
            process.Refresh();
            after = process.WorkingSet64;
        }
        catch (Exception ex)
        {
            Log.Warn("L4 工作集回收失败：无法采集 trim 前后的工作集。", ex);
            return new MemoryTrimResult(true, 0, 0, $"工作集回收未能测量：{ex.Message}");
        }

        if (!emptied)
        {
            Log.Warn($"L4 EmptyWorkingSet 调用失败（Win32 错误码 {win32Error}）。");
            return new MemoryTrimResult(true, 0, 0, $"EmptyWorkingSet 调用失败（Win32 错误码 {win32Error}），工作集未回收。");
        }

        var released = Math.Max(0, before - after);
        Log.Info($"L4 工作集回收完成：释放约 {released / BytesPerMegabyte}MB（{before} → {after} 字节）。");
        return new MemoryTrimResult(true, 1, released, $"工作集回收约 {released / BytesPerMegabyte}MB。");
    }

    /// <summary>
    /// psapi 的 <c>EmptyWorkingSet</c>：把本进程的工作集页推回系统待机列表（同步、瞬时返回）。
    /// </summary>
    /// <param name="hProcess">本进程句柄（<see cref="Process.Handle"/>，调用方负责生命周期）。</param>
    /// <returns>成功为 true，失败为 false，错误码由 <see cref="Marshal.GetLastWin32Error()"/> 取。</returns>
    [LibraryImport("psapi.dll", EntryPoint = "EmptyWorkingSet", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EmptyWorkingSet(IntPtr hProcess);
}
