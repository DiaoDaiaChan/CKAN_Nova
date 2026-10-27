using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Services.Abstractions;

/// <summary>
/// 模组标签组（12 篇 F-24：Favourites / Hidden / Held 等自定义标签）。
/// </summary>
/// <remarks>
/// <para>数据源是上游 <c>ModuleLabelList</c>（<c>%LocalAppData%\CKAN\labels.json</c>，缺失时退化成
/// Favourites/Hidden/Held 三组默认值）；序列化与落盘全部交给 Core，本层不写一行 JSON。</para>
/// <para>标签按"游戏实例 + 游戏短名"两个维度生效：<c>AppliesTo(instanceName)</c> 决定标签组是否作用于该实例，
/// 成员关系按 <c>IGame.ShortName</c> 分开存放，因此切实例后成员列表可能不同。</para>
/// <para>与 <see cref="IModPackService"/> 一致：所有成员都有默认实现（DIM），既有替身无需为新增成员改代码；
/// 默认实现明确返回"不可用 / 空"，不假装成功。</para>
/// </remarks>
public interface ILabelService
{
    /// <summary>当前实例适用的标签组快照（整体替换，不做增量通知）。</summary>
    IReadOnlyList<LabelInfo> Labels => Array.Empty<LabelInfo>();

    /// <summary>标签组集合被替换；已在 UI 线程触发。</summary>
    /// <remarks>默认实现是空事件：没有实现的替身订阅不到通知，也不会崩。</remarks>
    event Action<IReadOnlyList<LabelInfo>>? LabelsChanged
    {
        add { }
        remove { }
    }

    /// <summary>当前是否有可用的游戏实例（没有实例时所有成员关系都无从谈起）。</summary>
    bool HasActiveInstance => false;

    /// <summary>当前实例名；没有实例时为 null。</summary>
    string? InstanceName => null;

    /// <summary>从磁盘重新读取标签文件并重建快照。</summary>
    Task<OperationResult<IReadOnlyList<LabelInfo>>> RefreshAsync(CancellationToken ct = default)
        => Task.FromResult(OperationResult<IReadOnlyList<LabelInfo>>.Fail("当前标签服务不可用。"));

    /// <summary>某模组所属的标签组名称（当前实例 + 当前游戏限定）。</summary>
    IReadOnlyList<string> GetLabelsFor(string identifier) => Array.Empty<string>();

    /// <summary>整体替换某模组的标签组集合：多出来的移除、缺的加入，未知名称直接失败而不静默丢弃。</summary>
    Task<OperationResult<bool>> SetLabelsAsync(
        string identifier,
        IReadOnlyCollection<string> labels,
        CancellationToken ct = default)
        => Task.FromResult(OperationResult<bool>.Fail("当前标签服务不可用。"));

    /// <summary>新建标签组。</summary>
    Task<OperationResult<LabelInfo>> CreateLabelAsync(LabelEditRequest request, CancellationToken ct = default)
        => Task.FromResult(OperationResult<LabelInfo>.Fail("当前标签服务不可用。"));

    /// <summary>编辑标签组属性（按名称定位，不支持改名）。</summary>
    Task<OperationResult<LabelInfo>> UpdateLabelAsync(LabelEditRequest request, CancellationToken ct = default)
        => Task.FromResult(OperationResult<LabelInfo>.Fail("当前标签服务不可用。"));

    /// <summary>删除标签组（连同其在当前游戏里的成员关系）。</summary>
    Task<OperationResult<bool>> DeleteLabelAsync(string name, CancellationToken ct = default)
        => Task.FromResult(OperationResult<bool>.Fail("当前标签服务不可用。"));

    /// <summary>该模组是否落在任一"隐藏"标签组里（上游 <c>ModuleLabel.Hide</c>）。</summary>
    bool IsHidden(string identifier) => false;

    /// <summary>设置 / 取消隐藏；没有隐藏标签组时按默认名创建一个。</summary>
    Task<OperationResult<bool>> SetHiddenAsync(string identifier, bool hidden, CancellationToken ct = default)
        => Task.FromResult(OperationResult<bool>.Fail("当前标签服务不可用。"));

    /// <summary>该模组是否收藏（上游 Favourites 标签组成员）。</summary>
    bool IsFavourite(string identifier) => false;

    /// <summary>设置 / 取消收藏；没有收藏标签组时按默认名创建一个。</summary>
    Task<OperationResult<bool>> SetFavouriteAsync(string identifier, bool favourite, CancellationToken ct = default)
        => Task.FromResult(OperationResult<bool>.Fail("当前标签服务不可用。"));

    /// <summary>该模组是否被锁定版本（上游 <c>ModuleLabel.HoldVersion</c>）。</summary>
    bool IsHeld(string identifier) => false;

    /// <summary>设置 / 取消锁定；没有锁定标签组时按默认名创建一个。</summary>
    Task<OperationResult<bool>> SetHeldAsync(string identifier, bool held, CancellationToken ct = default)
        => Task.FromResult(OperationResult<bool>.Fail("当前标签服务不可用。"));

    /// <summary>锁定版本的标识符集合（上游 <c>ModuleLabelList.HeldIdentifiers</c>），供升级器跳过。</summary>
    IReadOnlyCollection<string> HeldIdentifiers => Array.Empty<string>();

    /// <summary>忽略缺失文件的标识符集合（上游 <c>ModuleLabelList.IgnoreMissingIdentifiers</c>）。</summary>
    IReadOnlyCollection<string> IgnoreMissingIdentifiers => Array.Empty<string>();
}
