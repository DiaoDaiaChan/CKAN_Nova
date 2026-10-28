namespace CkanUI.Services.Abstractions.Instances;

/// <summary>
/// F-08 实例可靠性处置：只读自检 + 两个受控的破坏性动作（强制解锁、隔离损坏注册表）。
/// <para>
/// 三条硬约束：<br/>
/// 1) 任何方法都不向调用方抛异常，失败一律用 <see cref="OperationResult"/> 的中文消息表达；<br/>
/// 2) 强制解锁必须先确认游戏进程没在跑，否则会和游戏抢文件；<br/>
/// 3) 损坏的注册表只改名隔离，绝不删除用户文件。
/// </para>
/// </summary>
public interface IInstanceRepairService
{
    /// <summary>只读自检：探测锁、试读注册表、判版本，返回问题清单。</summary>
    Task<OperationResult<InstanceRepairDiagnosis>> DiagnoseAsync(
        InstanceRepairTarget target, CancellationToken ct = default);

    /// <summary>
    /// 强制解锁：仅当游戏进程没在跑时删除锁文件，并把这次动作写进操作记录。
    /// <para>锁目录只在空目录时删除，非空一律跳过并在结果里列出来（不做递归删除）。</para>
    /// </summary>
    /// <param name="target">实例事实。</param>
    /// <param name="reason">操作原因，会一并写入操作记录（便于事后追责）。</param>
    /// <param name="ct">取消令牌。</param>
    Task<OperationResult<InstanceUnlockReport>> ForceUnlockAsync(
        InstanceRepairTarget target, string? reason = null, CancellationToken ct = default);

    /// <summary>
    /// 隔离损坏的注册表：把 registry.json 改名为 registry.json.corrupt-时间戳，内容原样保留。
    /// <para>改名后 CKAN 下次打开该实例时会自行创建空注册表（<c>RegistryManager.Load</c> 的缺文件分支）。</para>
    /// </summary>
    Task<OperationResult<InstanceRegistryQuarantine>> QuarantineRegistryAsync(
        InstanceRepairTarget target, CancellationToken ct = default);

    /// <summary>只做版本兼容判定，不写盘、不留痕，供诊断区随时调用。</summary>
    Task<OperationResult<InstanceCompatibilityInfo>> CheckCompatibilityAsync(
        InstanceRepairTarget target, CancellationToken ct = default);
}
