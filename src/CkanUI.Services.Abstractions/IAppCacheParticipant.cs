namespace CkanUI.Services.Abstractions;

/// <summary>
/// L1 应用缓存参与方（18 篇 §3 L1）：把日志环形缓冲裁到 2000 条、丢搜索索引与解析缓存。
/// 与 L0 分开是为了让「不动 UI、只丢可重建缓存」这一步能在保守档单独执行。
/// </summary>
public interface IAppCacheParticipant
{
    /// <summary>参与方名字，只用于日志与报告。</summary>
    string Name { get; }

    /// <summary>丢弃可重建的缓存，返回释放的条目数（被裁掉的日志行 / 索引项）。</summary>
    int TrimAppCache();
}
