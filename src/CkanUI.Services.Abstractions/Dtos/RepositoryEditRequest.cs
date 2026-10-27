namespace CkanUI.Services.Abstractions.Dtos;

/// <summary>
/// 新增仓库的入参（03 篇 §6 V-04「添加：名称 + URL」）。
/// </summary>
/// <remarks>
/// 两个字段都可空：空值校验属于服务层的职责（服务返回带中文文案的
/// <see cref="OperationResult{T}"/>），VM 不需要重复实现一套规则。
/// </remarks>
/// <param name="Name">仓库名；为空表示用户没填。</param>
/// <param name="Uri">仓库索引地址；为空表示用户没填。</param>
public sealed record RepositoryEditRequest(string? Name, string? Uri)
{
    /// <summary>空请求（表单初始状态）。</summary>
    public static RepositoryEditRequest Empty { get; } = new(null, null);
}
