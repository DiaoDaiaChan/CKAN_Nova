namespace CkanUI.Services.Abstractions.ModConfig;

/// <summary>
/// adapter 目录（内置 + 用户目录）。加载期即做 schema 校验，
/// 坏 adapter 只停自己、不拖垮整页——所以把"启用了哪些"和"哪些坏了"分成两个只读属性。
/// </summary>
public interface IAdapterCatalog
{
    /// <summary>已启用（校验通过）的 adapter。</summary>
    IReadOnlyList<ModConfigAdapterDescription> Adapters { get; }

    /// <summary>加载期诊断（含被停用的 adapter 的原因）。</summary>
    IReadOnlyList<ModConfigDiagnostic> Diagnostics { get; }

    /// <summary>重新扫描目录。用户导入 adapter 后调用。</summary>
    void Reload();
}

/// <summary>
/// 读取某个实例的 mod 配置状态（纯读，不写盘）。
/// 失败一律返回 <see cref="OperationResult"/>，不抛异常。
/// </summary>
public interface IModConfigService
{
    /// <summary>读取指定实例的配置页模型。</summary>
    /// <param name="instanceName">实例名；空串表示当前实例。</param>
    /// <param name="forceReload">
    /// true 时强制重读磁盘。false（默认）允许服务层复用上一次结果：同一个实例、同一份注册表快照下
    /// 重复进页面不必再扫一遍适配器源文件；注册表一变（安装/卸载/刷新）、切实例都会让缓存失效。
    /// 用户点「刷新」必须传 true，否则看到的还是缓存。
    /// </param>
    /// <param name="cancellationToken">取消。</param>
    Task<OperationResult<ModConfigInstanceModel>> LoadAsync(
        string instanceName,
        bool forceReload = false,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 写盘的**唯一**入口（15 篇 §3 硬约束）：
/// 先校验变更、再快照、再原子替换、最后重新解析自校验；失败自动回滚。
/// </summary>
public interface IAdapterWriter
{
    /// <summary>写入变更。</summary>
    Task<OperationResult<AdapterWriteResult>> WriteAsync(
        AdapterWriteRequest request,
        CancellationToken cancellationToken = default);
}
