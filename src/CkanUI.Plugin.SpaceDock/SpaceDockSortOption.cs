using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Plugin.SpaceDock;

/// <summary>排序下拉框的一项。</summary>
/// <param name="Label">显示文案。</param>
/// <param name="Order">SpaceDock orderby 值。</param>
/// <param name="Ascending">是否升序。</param>
public sealed record SpaceDockSortOption(string Label, SpaceDockSortOrder Order, bool Ascending);
