using System.Drawing;

using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CKAN;
using CKAN.Games;

namespace CkanUI.Services.Labels;

/// <summary>
/// 模组标签组服务（12 篇 F-24）：把上游 <c>ModuleLabelList</c> 包装成 UI 可用的契约。
/// </summary>
/// <remarks>
/// <para>落盘完全交给 Core：读取走 <c>ModuleLabelList.Load</c>，写入走 <c>ModuleLabelList.Save</c>，
/// 本类不写一行 JSON（04 篇 §6：序列化格式只在上游维护一处）。</para>
/// <para>内存实例策略：默认路径（<c>%LocalAppData%\CKAN\labels.json</c>）时复用上游静态单例
/// <c>ModuleLabelList.ModuleLabels</c>，这样"行投影里的 label: 事实"与"本服务写进去的成员关系"
/// 是同一份内存对象，改完立刻对搜索/过滤生效；显式指定其它路径（测试、便携模式）时自己 Load 一份。</para>
/// <para>线程约定（05 篇）：所有成员都是同步的纯计算 + 小文件 IO，用锁串行化；事件统一经
/// <see cref="IDispatcher.Post"/> 投递，订阅方总是在 UI 线程收到通知。</para>
/// </remarks>
public sealed class LabelService : ILabelService
{
    private const string NoInstanceMessage = "当前没有选中的游戏实例，无法读写标签。";
    private const string SaveFailedMessage = "标签文件保存失败，请检查磁盘权限。";

    private readonly GameInstanceManager _instances;
    private readonly IDispatcher _dispatcher;
    private readonly string _path;
    private readonly bool _sharedList;
    private readonly object _gate = new();

    private ModuleLabelList? _list;
    private IReadOnlyList<LabelInfo> _snapshot = Array.Empty<LabelInfo>();

    /// <param name="instances">实例管理器（取当前实例名与其 <see cref="IGame"/>）。</param>
    /// <param name="dispatcher">UI 线程封送器（事件投递）。</param>
    /// <param name="labelsPath">标签文件路径；null 表示上游默认路径并复用其静态单例。</param>
    public LabelService(GameInstanceManager instances, IDispatcher dispatcher, string? labelsPath = null)
    {
        _instances = instances ?? throw new ArgumentNullException(nameof(instances));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _sharedList = labelsPath is null;
        _path = labelsPath ?? ModuleLabelList.DefaultPath;
    }

    /// <summary>标签文件路径（便于日志与测试断言）。</summary>
    public string LabelsPath => _path;

    /// <inheritdoc />
    public IReadOnlyList<LabelInfo> Labels
    {
        get
        {
            lock (_gate)
            {
                if (_snapshot.Count == 0)
                {
                    _snapshot = BuildSnapshot(EnsureLoaded(), _instances.CurrentInstance);
                }
                return _snapshot;
            }
        }
    }

    /// <inheritdoc />
    public event Action<IReadOnlyList<LabelInfo>>? LabelsChanged;

    /// <inheritdoc />
    public bool HasActiveInstance => _instances.CurrentInstance is not null;

    /// <inheritdoc />
    public string? InstanceName => _instances.CurrentInstance?.Name;

    /// <inheritdoc />
    public Task<OperationResult<IReadOnlyList<LabelInfo>>> RefreshAsync(CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            IReadOnlyList<LabelInfo> snapshot;
            lock (_gate)
            {
                _list = Load();
                snapshot = BuildSnapshot(_list, _instances.CurrentInstance);
                _snapshot = snapshot;
            }
            _dispatcher.Post(() => LabelsChanged?.Invoke(snapshot));
            return Task.FromResult(OperationResult<IReadOnlyList<LabelInfo>>.Ok(snapshot));
        }
        catch (OperationCanceledException ex)
        {
            return Task.FromResult(OperationResult<IReadOnlyList<LabelInfo>>.Fail("操作已取消。", ex.Message, true));
        }
        catch (Exception ex)
        {
            return Task.FromResult(OperationResult<IReadOnlyList<LabelInfo>>.Fail("读取标签失败。", ex.ToString(), true));
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetLabelsFor(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier)) return Array.Empty<string>();

        try
        {
            lock (_gate)
            {
                if (_instances.CurrentInstance is not { } instance) return Array.Empty<string>();

                var names = new List<string>(2);
                foreach (var label in EnsureLoaded().LabelsFor(instance.Name))
                {
                    if (label is null || !label.ContainsModule(instance.Game, identifier)) continue;
                    if (!names.Contains(label.Name, StringComparer.OrdinalIgnoreCase)) names.Add(label.Name);
                }
                return names;
            }
        }
        catch (Exception)
        {
            // 标签文件损坏不应让模组列表整体失败（与 ModSearchFactsFactory.LabelMembership 一致）。
            return Array.Empty<string>();
        }
    }

    /// <inheritdoc />
    public Task<OperationResult<bool>> SetLabelsAsync(
        string identifier,
        IReadOnlyCollection<string> labels,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return Task.FromResult(OperationResult<bool>.Fail("缺少模组标识符。"));
        }

        var requested = (labels ?? Array.Empty<string>())
                        .Where(name => !string.IsNullOrWhiteSpace(name))
                        .Select(name => name.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray();

        try
        {
            ct.ThrowIfCancellationRequested();
            IReadOnlyList<LabelInfo> snapshot;
            lock (_gate)
            {
                if (_instances.CurrentInstance is not { } instance) return Task.FromResult(OperationResult<bool>.Fail(NoInstanceMessage));

                var list = EnsureLoaded();
                var applicable = list.Labels.Where(label => label.AppliesTo(instance.Name)).ToArray();

                // 未知名称直接失败：静默丢弃会让用户以为设置成功（12 篇 §12.15 的同类要求）。
                foreach (var name in requested)
                {
                    if (!applicable.Any(label => Same(label.Name, name)))
                    {
                        return Task.FromResult(OperationResult<bool>.Fail($"未知的标签组“{name}”。"));
                    }
                }

                foreach (var label in applicable)
                {
                    if (requested.Any(name => Same(name, label.Name)))
                    {
                        label.Add(instance.Game, identifier);
                    }
                    else
                    {
                        label.Remove(instance.Game, identifier);
                    }
                }

                if (!list.Save(_path)) return Task.FromResult(OperationResult<bool>.Fail(SaveFailedMessage, _path));
                snapshot = Remember(list, instance);
            }

            Publish(snapshot);
            return Task.FromResult(OperationResult<bool>.Ok(true));
        }
        catch (OperationCanceledException ex)
        {
            return Task.FromResult(OperationResult<bool>.Fail("操作已取消。", ex.Message, true));
        }
        catch (Exception ex)
        {
            return Task.FromResult(OperationResult<bool>.Fail("设置标签失败。", ex.ToString(), true));
        }
    }

    /// <inheritdoc />
    public Task<OperationResult<LabelInfo>> CreateLabelAsync(LabelEditRequest request, CancellationToken ct = default)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Name))
        {
            return Task.FromResult(OperationResult<LabelInfo>.Fail("标签组名称不能为空。"));
        }

        var name = request.Name.Trim();
        return Mutate((list, instance) =>
        {
            if (FindByName(list, instance, name) is not null)
            {
                return (null, $"标签组“{name}”已存在。");
            }

            var label = new ModuleLabel(name) { InstanceName = request.IsGlobal ? null : instance.Name };
            Apply(label, request, instance);
            list.Labels = list.Labels.Append(label).ToArray();
            return (label, null);
        });
    }

    /// <inheritdoc />
    public Task<OperationResult<LabelInfo>> UpdateLabelAsync(LabelEditRequest request, CancellationToken ct = default)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Name))
        {
            return Task.FromResult(OperationResult<LabelInfo>.Fail("标签组名称不能为空。"));
        }

        var name = request.Name.Trim();
        return Mutate((list, instance) =>
        {
            if (FindByName(list, instance, name) is not { } label)
            {
                return (null, $"找不到标签组“{name}”。");
            }

            Apply(label, request, instance);
            return (label, null);
        });
    }

    /// <inheritdoc />
    public Task<OperationResult<bool>> DeleteLabelAsync(string name, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Task.FromResult(OperationResult<bool>.Fail("缺少标签组名称。"));
        }

        try
        {
            ct.ThrowIfCancellationRequested();
            IReadOnlyList<LabelInfo> snapshot;
            lock (_gate)
            {
                if (_instances.CurrentInstance is not { } instance) return Task.FromResult(OperationResult<bool>.Fail(NoInstanceMessage));

                var list = EnsureLoaded();
                var remaining = list.Labels.Where(label => !Same(label.Name, name.Trim())).ToArray();
                if (remaining.Length == list.Labels.Length)
                {
                    return Task.FromResult(OperationResult<bool>.Fail($"找不到标签组“{name}”。"));
                }

                list.Labels = remaining;
                if (!list.Save(_path)) return Task.FromResult(OperationResult<bool>.Fail(SaveFailedMessage, _path));
                snapshot = Remember(list, instance);
            }

            Publish(snapshot);
            return Task.FromResult(OperationResult<bool>.Ok(true));
        }
        catch (OperationCanceledException ex)
        {
            return Task.FromResult(OperationResult<bool>.Fail("操作已取消。", ex.Message, true));
        }
        catch (Exception ex)
        {
            return Task.FromResult(OperationResult<bool>.Fail("删除标签组失败。", ex.ToString(), true));
        }
    }

    /// <inheritdoc />
    public bool IsHidden(string identifier) => HasMembership(identifier, FindHidden);

    /// <inheritdoc />
    public bool IsFavourite(string identifier) => HasMembership(identifier, FindFavourite);

    /// <inheritdoc />
    public bool IsHeld(string identifier) => HasMembership(identifier, FindHeld);

    /// <inheritdoc />
    public Task<OperationResult<bool>> SetHiddenAsync(string identifier, bool hidden, CancellationToken ct = default)
        => SetMembershipAsync(identifier, hidden, FindHidden,
            (list, instance) => EnsureLabel(list, instance, HiddenName, label => label.Hide = true), ct);

    /// <inheritdoc />
    public Task<OperationResult<bool>> SetFavouriteAsync(string identifier, bool favourite, CancellationToken ct = default)
        => SetMembershipAsync(identifier, favourite, FindFavourite,
            (list, instance) => EnsureLabel(list, instance, FavouriteName, label => label.Color = Color.PaleGreen), ct);

    /// <inheritdoc />
    public Task<OperationResult<bool>> SetHeldAsync(string identifier, bool held, CancellationToken ct = default)
        => SetMembershipAsync(identifier, held, FindHeld,
            (list, instance) => EnsureLabel(list, instance, HeldName, label => label.HoldVersion = true), ct);

    /// <inheritdoc />
    public IReadOnlyCollection<string> HeldIdentifiers => IdentifiersOf(static (list, instance) => list.HeldIdentifiers(instance));

    /// <inheritdoc />
    public IReadOnlyCollection<string> IgnoreMissingIdentifiers => IdentifiersOf(static (list, instance) => list.IgnoreMissingIdentifiers(instance));

    // ---- 内部实现 ---------------------------------------------------------

    private IReadOnlyCollection<string> IdentifiersOf(Func<ModuleLabelList, GameInstance, IEnumerable<string>> read)
    {
        try
        {
            lock (_gate)
            {
                if (_instances.CurrentInstance is not { } instance) return Array.Empty<string>();

                return read(EnsureLoaded(), instance).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            }
        }
        catch (Exception)
        {
            return Array.Empty<string>();
        }
    }

    private bool HasMembership(string identifier, Func<ModuleLabelList, GameInstance, ModuleLabel?> find)
    {
        if (string.IsNullOrWhiteSpace(identifier)) return false;

        try
        {
            lock (_gate)
            {
                if (_instances.CurrentInstance is not { } instance) return false;

                var list = EnsureLoaded();
                return find(list, instance)?.ContainsModule(instance.Game, identifier) ?? false;
            }
        }
        catch (Exception)
        {
            return false;
        }
    }

    private Task<OperationResult<bool>> SetMembershipAsync(
        string identifier,
        bool member,
        Func<ModuleLabelList, GameInstance, ModuleLabel?> find,
        Func<ModuleLabelList, GameInstance, ModuleLabel> create,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return Task.FromResult(OperationResult<bool>.Fail("缺少模组标识符。"));
        }

        try
        {
            ct.ThrowIfCancellationRequested();
            IReadOnlyList<LabelInfo> snapshot;
            lock (_gate)
            {
                if (_instances.CurrentInstance is not { } instance) return Task.FromResult(OperationResult<bool>.Fail(NoInstanceMessage));

                var list = EnsureLoaded();
                var label = find(list, instance) ?? create(list, instance);
                if (member)
                {
                    label.Add(instance.Game, identifier);
                }
                else
                {
                    label.Remove(instance.Game, identifier);
                }

                if (!list.Save(_path)) return Task.FromResult(OperationResult<bool>.Fail(SaveFailedMessage, _path));
                snapshot = Remember(list, instance);
            }

            Publish(snapshot);
            return Task.FromResult(OperationResult<bool>.Ok(true));
        }
        catch (OperationCanceledException ex)
        {
            return Task.FromResult(OperationResult<bool>.Fail("操作已取消。", ex.Message, true));
        }
        catch (Exception ex)
        {
            return Task.FromResult(OperationResult<bool>.Fail("设置标签成员失败。", ex.ToString(), true));
        }
    }

    private Task<OperationResult<LabelInfo>> Mutate(
        Func<ModuleLabelList, GameInstance, (ModuleLabel? Label, string? Error)> action)
    {
        try
        {
            IReadOnlyList<LabelInfo> snapshot;
            LabelInfo? created;
            lock (_gate)
            {
                if (_instances.CurrentInstance is not { } instance) return Task.FromResult(OperationResult<LabelInfo>.Fail(NoInstanceMessage));

                var list = EnsureLoaded();
                var (label, error) = action(list, instance);
                if (error is not null || label is null) return Task.FromResult(OperationResult<LabelInfo>.Fail(error ?? "标签操作失败。"));

                if (!list.Save(_path)) return Task.FromResult(OperationResult<LabelInfo>.Fail(SaveFailedMessage, _path));
                snapshot = Remember(list, instance);
                created = Project(label, instance);
            }

            Publish(snapshot);
            return Task.FromResult(OperationResult<LabelInfo>.Ok(created));
        }
        catch (Exception ex)
        {
            return Task.FromResult(OperationResult<LabelInfo>.Fail("保存标签组失败。", ex.ToString(), true));
        }
    }

    private ModuleLabelList EnsureLoaded() => _list ??= Load();

    private ModuleLabelList Load()
    {
        // 默认路径复用上游静态单例，保证与行投影（ModSearchFactsFactory.LabelMembership）同源。
        if (_sharedList) return ModuleLabelList.ModuleLabels;

        try
        {
            return ModuleLabelList.Load(_path) ?? ModuleLabelList.GetDefaultLabels();
        }
        catch (Exception)
        {
            // 文件损坏不应让整个标签功能不可用：退化成三组默认标签，下次保存会覆盖。
            return ModuleLabelList.GetDefaultLabels();
        }
    }

    private IReadOnlyList<LabelInfo> Remember(ModuleLabelList list, GameInstance? instance)
    {
        _snapshot = BuildSnapshot(list, instance);
        return _snapshot;
    }

    private void Publish(IReadOnlyList<LabelInfo> snapshot)
        => _dispatcher.Post(() => LabelsChanged?.Invoke(snapshot));

    private static IReadOnlyList<LabelInfo> BuildSnapshot(ModuleLabelList list, GameInstance? instance)
    {
        var infos = new List<LabelInfo>(list.Labels.Length);
        foreach (var label in list.Labels)
        {
            if (label is null) continue;
            if (instance is not null && !label.AppliesTo(instance.Name)) continue;
            infos.Add(Project(label, instance));
        }
        return infos;
    }

    private static LabelInfo Project(ModuleLabel label, GameInstance? instance) =>
        new(label.Name,
            ToHex(label.Color),
            label.InstanceName is null,
            label.Hide,
            label.NotifyOnChange,
            label.RemoveOnChange,
            label.AlertOnInstall,
            label.RemoveOnInstall,
            label.HoldVersion,
            label.IgnoreMissingFiles,
            instance is null ? 0 : label.ModuleCount(instance.Game));

    private static void Apply(ModuleLabel label, LabelEditRequest request, GameInstance instance)
    {
        label.Color = ParseColor(request.ColorHex);
        label.InstanceName = request.IsGlobal ? null : label.InstanceName ?? instance.Name;
        label.Hide = request.HideFromOtherFilters;
        label.NotifyOnChange = request.NotifyOnChanges;
        label.RemoveOnChange = request.RemoveOnChanges;
        label.AlertOnInstall = request.AlertOnInstall;
        label.RemoveOnInstall = request.RemoveOnInstall;
        label.HoldVersion = request.HoldVersion;
        label.IgnoreMissingFiles = request.IgnoreMissingFiles;
    }

    private static ModuleLabel EnsureLabel(
        ModuleLabelList list,
        GameInstance instance,
        string name,
        Action<ModuleLabel> configure)
    {
        if (FindByName(list, instance, name) is { } existing) return existing;

        var label = new ModuleLabel(name) { InstanceName = null };
        configure(label);
        list.Labels = list.Labels.Append(label).ToArray();
        return label;
    }

    private static ModuleLabel? FindByName(ModuleLabelList list, GameInstance instance, string name)
        => list.Labels.FirstOrDefault(label => label is not null
                                               && label.AppliesTo(instance.Name)
                                               && Same(label.Name, name));

    private static ModuleLabel? FindFavourite(ModuleLabelList list, GameInstance instance)
        => FindByName(list, instance, FavouriteName);

    private static ModuleLabel? FindHidden(ModuleLabelList list, GameInstance instance)
        => list.Labels.FirstOrDefault(label => label is not null && label.AppliesTo(instance.Name) && label.Hide)
           ?? FindByName(list, instance, HiddenName);

    private static ModuleLabel? FindHeld(ModuleLabelList list, GameInstance instance)
        => list.Labels.FirstOrDefault(label => label is not null && label.AppliesTo(instance.Name) && label.HoldVersion)
           ?? FindByName(list, instance, HeldName);

    private static bool Same(string? left, string? right)
        => string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>默认标签组名称：与上游 <c>ModuleLabelList.GetDefaultLabels</c> 同一来源（资源串），取不到时用英文常量。</summary>
    private static string FavouriteName => ResourceName(static () => CKAN.Properties.Resources.ModuleLabelListFavourites, "Favourites");

    private static string HiddenName => ResourceName(static () => CKAN.Properties.Resources.ModuleLabelListHidden, "Hidden");

    private static string HeldName => ResourceName(static () => CKAN.Properties.Resources.ModuleLabelListHeld, "Held");

    private static string ResourceName(Func<string> read, string fallback)
    {
        try
        {
            var value = read();
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }
        catch (Exception)
        {
            return fallback;
        }
    }

    private static string? ToHex(Color? color)
        => color is { } value ? $"#{value.R:X2}{value.G:X2}{value.B:X2}" : null;

    /// <summary>解析 <c>#RRGGBB</c> / <c>RRGGBB</c>；空值或非法值一律视为"不着色"。</summary>
    private static Color? ParseColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;

        var text = hex.Trim().TrimStart('#');
        if (text.Length != 6 || !int.TryParse(text, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out var value))
        {
            return null;
        }

        return Color.FromArgb(255, (value >> 16) & 0xFF, (value >> 8) & 0xFF, value & 0xFF);
    }
}
