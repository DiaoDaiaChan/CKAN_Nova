using CkanUI.Services.Abstractions;
using CkanUI.ViewModels.Pages;

namespace CkanUI.ViewModels.Navigation;

/// <summary>深链（<c>ckan://</c>，ARC-05）的处理结果。</summary>
/// <param name="Handled">是否真的落到了目标：导航成功并定位到具体对象。</param>
/// <param name="Summary">给日志与状态栏看的中文说明。</param>
public sealed record DeepLinkOutcome(bool Handled, string Summary)
{
    /// <summary>处理成功。</summary>
    public static DeepLinkOutcome Done(string summary) => new(true, summary);

    /// <summary>没有落到目标（页面未就绪、对象不存在等），调用方记日志即可，不算错误。</summary>
    public static DeepLinkOutcome Ignored(string summary) => new(false, summary);
}

/// <summary>
/// 深链路由器：把 <c>ckan://mod/&lt;identifier&gt;</c>、<c>ckan://instance/&lt;name&gt;</c>、
/// <c>ckan://repo/&lt;name&gt;</c> 落到具体页面与实例（ARC-05）。
/// </summary>
/// <remarks>
/// <para>只吃 <c>(kind, value)</c> 两个字符串：解析 <c>ckan://</c> 的 <c>CkanUri</c> 在 Platform 层，
/// 而视图模型层不引用 Platform（架构铁律），由 App 层解析后转交。</para>
/// <para>页面实例从容器按导航键解析；解析不到时退化成「只导航不定位」，不抛异常 ——
/// 深链是尽力而为的入口，不能因为索引没加载就崩掉外壳。</para>
/// </remarks>
public sealed class DeepLinkRouter
{
    /// <summary>模组深链类型，取值是模组标识符（<c>ckan://mod/KerbalEngineer</c>）。</summary>
    public const string ModKind = "mod";

    /// <summary>实例深链类型，取值是实例名（<c>ckan://instance/默认</c>）。</summary>
    public const string InstanceKind = "instance";

    /// <summary>仓库深链类型，取值是仓库名或索引地址（<c>ckan://repo/default</c>）。</summary>
    public const string RepoKind = "repo";

    private readonly INavigationService _navigation;
    private readonly IInstanceService? _instances;
    private readonly Func<string, object?>? _pageResolver;

    /// <summary>构造路由器。</summary>
    /// <param name="navigation">导航服务（切页）。</param>
    /// <param name="instances">实例服务；为 <c>null</c> 时实例深链不可用。</param>
    /// <param name="pageResolver">按导航键取页面视图模型；为 <c>null</c> 时只导航不定位。</param>
    public DeepLinkRouter(
        INavigationService navigation,
        IInstanceService? instances = null,
        Func<string, object?>? pageResolver = null)
    {
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        _instances = instances;
        _pageResolver = pageResolver;
    }

    /// <summary>执行一条深链命令。</summary>
    /// <param name="kind">命令类型（mod / instance / repo，大小写不敏感）。</param>
    /// <param name="value">命令取值。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>处理结果；未知类型与空取值返回 <see cref="DeepLinkOutcome.Ignored"/>。</returns>
    public async Task<DeepLinkOutcome> DispatchAsync(string? kind, string? value, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(kind))
        {
            return DeepLinkOutcome.Ignored("深链缺少命令类型。");
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            return DeepLinkOutcome.Ignored($"深链 ckan://{kind.Trim()} 缺少取值。");
        }

        var key = kind.Trim().ToLowerInvariant();
        var argument = value.Trim();

        try
        {
            return key switch
            {
                ModKind => await DispatchModAsync(argument, cancellationToken).ConfigureAwait(true),
                InstanceKind => await DispatchInstanceAsync(argument, cancellationToken).ConfigureAwait(true),
                RepoKind => await DispatchRepositoryAsync(argument, cancellationToken).ConfigureAwait(true),
                _ => DeepLinkOutcome.Ignored($"未知的深链类型「{kind.Trim()}」。"),
            };
        }
        catch (OperationCanceledException)
        {
            return DeepLinkOutcome.Ignored($"深链 ckan://{key} 已取消。");
        }
    }

    /// <summary>模组深链：切到模组页，把标识符交给页面定位并选中。</summary>
    private async Task<DeepLinkOutcome> DispatchModAsync(string identifier, CancellationToken cancellationToken)
    {
        await _navigation.NavigateAsync("mods", null, cancellationToken).ConfigureAwait(true);

        if (_pageResolver?.Invoke("mods") is not ModsPageViewModel page)
        {
            return DeepLinkOutcome.Ignored($"已切到模组页，但页面尚未就绪，没能定位「{identifier}」。");
        }

        var located = await page.ShowIdentifierAsync(identifier, cancellationToken).ConfigureAwait(true);

        return located
            ? DeepLinkOutcome.Done($"已定位到模组「{identifier}」。")
            : DeepLinkOutcome.Ignored($"模组「{identifier}」不在当前索引里，已把标识符填进搜索框。");
    }

    /// <summary>实例深链：切换当前实例并落到实例页。</summary>
    private async Task<DeepLinkOutcome> DispatchInstanceAsync(string name, CancellationToken cancellationToken)
    {
        if (_instances is null)
        {
            return DeepLinkOutcome.Ignored("当前宿主没有实例服务，实例深链不可用。");
        }

        var listed = await _instances.RefreshAsync(cancellationToken).ConfigureAwait(true);
        if (!listed.IsSuccess)
        {
            return DeepLinkOutcome.Ignored($"实例列表读取失败：{listed.Message}");
        }

        var match = listed.Value?.FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            return DeepLinkOutcome.Ignored($"找不到名为「{name}」的实例。");
        }

        var switched = await _instances.SwitchAsync(match.Name, cancellationToken).ConfigureAwait(true);
        if (!switched.IsSuccess)
        {
            return DeepLinkOutcome.Ignored($"切换到实例「{match.Name}」失败：{switched.Message}");
        }

        await _navigation.NavigateAsync("instances", null, cancellationToken).ConfigureAwait(true);
        return DeepLinkOutcome.Done($"已切换到实例「{match.Name}」。");
    }

    /// <summary>仓库深链：切到仓库页并选中匹配的仓库。</summary>
    private async Task<DeepLinkOutcome> DispatchRepositoryAsync(string nameOrUri, CancellationToken cancellationToken)
    {
        await _navigation.NavigateAsync("repos", null, cancellationToken).ConfigureAwait(true);

        if (_pageResolver?.Invoke("repos") is not ReposPageViewModel page)
        {
            return DeepLinkOutcome.Ignored($"已切到仓库页，但页面尚未就绪，没能定位「{nameOrUri}」。");
        }

        return page.SelectRepository(nameOrUri)
            ? DeepLinkOutcome.Done($"已选中仓库「{nameOrUri}」。")
            : DeepLinkOutcome.Ignored($"仓库列表里没有匹配「{nameOrUri}」的条目。");
    }
}
