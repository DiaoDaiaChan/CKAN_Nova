using CkanUI.Services.Abstractions;
using CkanUI.ViewModels.Pages;

namespace CkanUI.App;

/// <summary>
/// 内容预览页的选中目标：取模组页当前选中的行（F-29）。
/// 只暴露一个窄接口，避免 ViewModels 层反向依赖模组页。
/// </summary>
internal sealed class ModsSelectionContentTarget(ModsPageViewModel mods) : IContentTargetProvider
{
    private readonly ModsPageViewModel _mods = mods;

    public Task<string?> GetSelectedIdentifierAsync()
    {
        var identifier = _mods.SelectedRow?.Identifier;
        return Task.FromResult(string.IsNullOrWhiteSpace(identifier) ? null : identifier);
    }
}
