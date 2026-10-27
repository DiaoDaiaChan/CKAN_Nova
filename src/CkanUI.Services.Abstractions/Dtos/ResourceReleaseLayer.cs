namespace CkanUI.Services.Abstractions.Dtos;

/// <summary>资源释放的分层（18 篇 §3）。枚举值顺序就是执行顺序，也用于报告里定位失败层。</summary>
public enum ResourceReleaseLayer
{
    /// <summary>L0 视图层：清空列表投影/位图/停掉刷新计时器（10–30MB，&lt;100ms）。</summary>
    View = 0,

    /// <summary>L1 应用缓存：裁日志环形缓冲、丢搜索索引与解析缓存（10–40MB，&lt;300ms）。</summary>
    Caches = 1,

    /// <summary>L2 Core 数据：<c>RegistryManager.DisposeAll()</c> + 丢仓库快照（100–300MB，1–3s，可逆）。</summary>
    CoreData = 2,

    /// <summary>L3 托管堆：压缩式 GC（1–5s，唯一会明显卡顿的层）。</summary>
    ManagedHeap = 3,

    /// <summary>L4 OS 层：把工作集还给系统、降内存优先级（&lt;50ms）。</summary>
    OsMemory = 4,
}
