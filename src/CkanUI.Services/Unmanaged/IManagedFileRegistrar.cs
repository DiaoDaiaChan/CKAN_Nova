using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Services.Unmanaged;

/// <summary>一次注册表写入的结果：是否真的改了内容，以及写前生成的快照路径。</summary>
/// <param name="Changed">注册表内容是否发生变化；<c>false</c> 表示与现状一致、没有落盘。</param>
/// <param name="BackupPath">写前复制的 <c>registry.json.bak-*</c> 路径；没有生成快照时为 <c>null</c>。</param>
public sealed record DllMergeResult(bool Changed, string? BackupPath);

/// <summary>
/// 把「未管理 DLL 登记进注册表」抽成可替换接缝（12 篇 F-42 / INT-09）：接管逻辑因此完全不碰 CKAN，
/// 可以用假登记器在 headless 测试里跑（04 篇 §3）。
/// </summary>
/// <remarks>
/// 只有 <c>CkanManagedFileRegistrar</c> 会真正读写 <c>registry.json</c>；上游的 <c>SetDlls</c> 是**整体替换**语义，
/// 所以实现必须在写入前把已有映射读出来合并，否则会悄悄清掉其它手工安装的 DLL。
/// </remarks>
public interface IManagedFileRegistrar
{
    /// <summary>读出当前登记的 DLL 映射（标识符 → GameDir 相对路径，正斜杠）。</summary>
    OperationResult<IReadOnlyDictionary<string, string>> GetInstalledDlls(InstanceInfo instance);

    /// <summary>
    /// 把 <paramref name="dlls"/> 合并进当前映射并保存注册表。<paramref name="takeSnapshot"/> 为 <c>true</c> 时
    /// 先把 <c>registry.json</c> 复制成带时间戳的备份，保存失败会用它还原。
    /// </summary>
    OperationResult<DllMergeResult> MergeDlls(
        InstanceInfo instance,
        IReadOnlyDictionary<string, string> dlls,
        bool takeSnapshot = true);

    /// <summary>把从 DLL 文件名猜出的标识符规范成注册表认可的形式（Core 的 <c>Identifier.Sanitize</c>）。</summary>
    string NormalizeIdentifier(string raw);
}
