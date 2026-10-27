using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Services.Abstractions;

/// <summary>
/// 搜索语法解析器（12 篇 F-20/F-21 / 03 篇 §3.4）：把搜索框原文解析成
/// <see cref="ModSearchSpec"/>，解析工作全部委托给服务层对 <c>ModSearch</c> 的封装。
/// </summary>
/// <remarks>
/// 契约放在 Abstractions，实现（<c>CkanUI.Services.Search.ModSearchParser</c>）在 Services：
/// 视图模型只消费 DTO，不引用 CKAN 命名空间（01 篇 §11 依赖方向铁律）。
/// </remarks>
public interface IModSearchParser
{
    /// <summary>解析一段查询文本；空文本返回 <see cref="ModSearchSpec.Empty"/>（不抛异常）。</summary>
    ModSearchSpec Parse(string? text);
}
