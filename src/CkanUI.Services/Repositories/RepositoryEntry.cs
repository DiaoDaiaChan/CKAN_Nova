namespace CkanUI.Services.Repositories;

/// <summary>仓库列表的存储层投影（注册表里一条 <c>Repository</c> 的纯数据形态）。</summary>
/// <param name="Name">仓库名（注册表 key）。</param>
/// <param name="Uri">仓库索引地址（绝对 URI 的字符串形式）。</param>
/// <param name="Priority">优先级，越小越优先。</param>
public sealed record RepositoryEntry(string Name, string Uri, int Priority);
