namespace CkanUI.Services.Abstractions;

using CkanUI.Services.Abstractions.Dtos;

/// <summary>
/// 结构化搜索条件到搜索语法的反向投影（12 篇 F-20/F-21）。
/// 图形化筛选面板改的是字段，搜索框里显示的必须是同一套语法文本，
/// 因此格式化规则只允许有一处实现——上游 <c>ModSearch.getCombined</c>，
/// 本接口把它收成一层，视图模型不必自己维护前缀与 <c>is:</c>/<c>not:</c> 拼装规则。
/// </summary>
public interface IModSearchFormatter
{
    /// <summary>
    /// 把结构化条件转成可与 <see cref="IModSearchParser"/> 往返的查询文本。
    /// 空条件返回空串。
    /// </summary>
    string Format(ModSearchSpec spec);
}
