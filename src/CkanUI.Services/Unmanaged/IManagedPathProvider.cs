using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Services.Unmanaged;

/// <summary>
/// 把「注册表认为哪些文件受管」抽成可替换查询（12 篇 F-42）：扫描器与删除逻辑因此完全不碰 CKAN，
/// 可以用假数据在 headless 测试里跑（04 篇 §3）。
/// </summary>
public interface IManagedPathProvider
{
    /// <summary>返回相对 <c>&lt;GameDir&gt;/GameData</c> 的受管文件路径集合（正斜杠，忽略大小写）。</summary>
    OperationResult<IReadOnlySet<string>> GetManagedPaths(InstanceInfo instance);
}
