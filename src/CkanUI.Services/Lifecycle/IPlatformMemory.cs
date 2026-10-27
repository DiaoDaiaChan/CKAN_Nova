namespace CkanUI.Services.Lifecycle;

/// <summary>
/// 平台内存回收的抽象（18 篇 §3 L3/L4）。抽出来是为了让编排层可测：
/// 测试用假实现断言「激进档才调用 L3/L4」，生产实现才真的去压 GC、缩工作集。
/// </summary>
public interface IPlatformMemory
{
    /// <summary>L3：压缩式回收托管堆（进程内，1–5s，会明显卡顿）。</summary>
    MemoryTrimResult CollectManagedHeap();

    /// <summary>L4：把工作集还给系统并降内存优先级（&lt;50ms，恢复时可能 page fault，R-58）。</summary>
    MemoryTrimResult TrimWorkingSet();
}
