using System.Diagnostics;
using log4net;

namespace CkanUI.Services.Lifecycle;

/// <summary>
/// 平台内存回收的生产实现（18 篇 §3 L3/L4）。
/// </summary>
/// <remarks>
/// L3 只做「一定安全且可测」的部分：两轮阻塞压缩式 GC + 等终结器，并用
/// <see cref="GC.GetTotalMemory(bool)"/> 的差值报告回收量。
/// L4 需要 psapi 的 <c>EmptyWorkingSet</c> / <c>SetProcessWorkingSetSizeEx</c> 与内存优先级，
/// 这些要 P/Invoke 到具体 Windows 版本行为，本迭代刻意不接（见报告）：未接入时返回
/// <see cref="MemoryTrimResult.Unsupported"/>，编排层据此把该层记成「未执行」而不是失败。
/// </remarks>
public sealed class PlatformMemory : IPlatformMemory
{
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
            Log.Info($"L3 托管堆回收完成：释放约 {released / (1024 * 1024)}MB。");
            return new MemoryTrimResult(true, 1, released, $"托管堆回收约 {released / (1024 * 1024)}MB。");
        }
        catch (Exception ex)
        {
            Log.Warn("L3 托管堆回收失败。", ex);
            return new MemoryTrimResult(true, 0, 0, ex.Message);
        }
    }

    public MemoryTrimResult TrimWorkingSet() =>
        MemoryTrimResult.Unsupported("L4 工作集回收需要 psapi（EmptyWorkingSet / SetProcessWorkingSetSizeEx），本版本未接入。");
}
