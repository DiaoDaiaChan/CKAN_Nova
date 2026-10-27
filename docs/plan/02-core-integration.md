# 02 Core 集成契约

> 本文是新 UI 与 `Core/`（24,661 行）之间的**全部接口面**。凡是 Core 覆盖不到的地方，都在这里指出，不要在其他地方绕（例如直接写文件）。
> 所有行号基于 master @ c0650d45。

## 1. 启动序列（精确顺序）

```
① Logging.Initialize()                                  Core/Logging.cs:23
② config  = ServiceLocator.Container.Resolve<IConfiguration>()     Core/ServiceLocator.cs:12
③ repoData= ServiceLocator.Container.Resolve<RepositoryDataManager>()
④ user    = new AvaloniaUser(dispatcher)                // 实现 Core/User.cs:10 的 IUser
⑤ mgr     = new GameInstanceManager(user, config)       Core/GameInstanceManager.cs:49
⑥ inst    = mgr.GetPreferredInstance()                  :75（返回 null 时进实例向导）
⑦ mgr.SetCurrentInstance(inst)                          :476（若来自命令行/上次会话）
⑧ reg     = RegistryManager.Instance(inst, repoData)    Core/Registry/RegistryManager.cs:265
⑨ 订阅事件（见 §6）→ 后台触发 repoData.Update()
... 运行 ...
⑩ RegistryManager.DisposeAll()                          :295 —— 必经
```

顺序不可交换的原因：

| 相邻对 | 为什么不能反 |
|---|---|
| ①→② | 配置读取失败要能记日志；反过来则异常无处可去 |
| ②→⑤ | `GameInstanceManager` 构造要 `IConfiguration` 来读实例列表 |
| ④→⑤ | 构造 `GameInstanceManager` 需要 `IUser` 处理"实例已损坏"等询问 |
| ⑤→⑥ | 必须先有 manager 才能拿偏好实例 |
| ⑥→⑧ | `RegistryManager.Instance` 需要具体 `GameInstance` |
| ⑨→⑩ | 事件未退订就 `DisposeAll` 会收到对已释放对象的回调 |

### 参考骨架

```csharp
// App.axaml.cs — 全应用唯一允许 new 具体服务的地方
public override void OnFrameworkInitializationCompleted()
{
    Logging.Initialize();                                    // ① 必须在任何 Core 调用之前

    var container = ServiceLocator.Container;                // Autofac，只用于取 Core 的 4 个类型
    var config    = container.Resolve<IConfiguration>();     // ②
    var repoData  = container.Resolve<RepositoryDataManager>();

    var ui = new ServiceCollection()
        .AddSingleton<IDispatcher>(new AvaloniaDispatcher(Dispatcher.UIThread))
        .AddSingleton<IUser, AvaloniaUser>()                  // ④
        .AddSingleton(sp => new GameInstanceManager(sp.GetRequiredService<IUser>(), config))  // ⑤
        .AddSingleton<IRepositoryService>(_ => new RepositoryService(repoData))
        .AddSingleton<IInstanceService, InstanceService>()
        .AddSingleton<IRegistryService, RegistryService>()
        .AddSingleton<IInstallerService, InstallerService>()
        .AddSingleton<ISettingsService>(_ => new SettingsService(config))
        .AddSingleton<IOperationQueue, OperationQueue>()
        .BuildServiceProvider();

    var mgr = ui.GetRequiredService<GameInstanceManager>();
    var inst = mgr.GetPreferredInstance();                    // ⑥
    if (inst is null) { ShowInstanceWizard(); }               // 无实例是正常路径
    else { ui.GetRequiredService<IRegistryService>().Open(inst); }   // ⑧

    base.OnFrameworkInitializationCompleted();
}

protected override void OnExit(ExitEventArgs e)
{
    RegistryManager.DisposeAll();                            // ⑩
    base.OnExit(e);
}
```

## 2. 契约 ①：日志

| 项 | 事实 |
|---|---|
| 入口 | `Logging.Initialize()`，`Core/Logging.cs:23` |
| 配置文件 | 从 `Environment.CurrentDirectory` 找 `log4net.xml`；找不到则退回控制台/默认级别 |
| 风险 | 若新 exe 的 CWD 不是程序目录（例如通过快捷方式、`ckan://` 启动），会拿不到配置 → 日志级别异常、文件不落盘 |
| 对策 | 启动最早期 `Directory.SetCurrentDirectory(AppContext.BaseDirectory)`；或随包分发 `log4net.xml` 并显式配置 |
| 复用 | 新 UI 用 `LogManager.GetLogger("CKAN.UI")` 写同一 appender，不另开日志文件（便于用户一次反馈即可提供全链路） |

**待验证**：`Logging.Initialize()` 是否幂等、重复调用是否重复添加 appender（spike 项 SP-02）。

## 3. 契约 ②：配置与 ServiceLocator

| 项 | 事实 |
|---|---|
| 全局容器 | `ServiceLocator.Container`，`Core/ServiceLocator.cs:12`，Autofac，**只注册 4 个类型**：`IConfiguration`、`RepositoryDataManager`、`GameInstanceManager`、`IUser`（以源码为准） |
| 配置实现 | `Core/Configuration/JsonConfiguration.cs:87`，落盘 `%LocalAppData%/CKAN/config.json` |
| 路径工具 | `Core/IO/CKANPathUtils.cs:15`（`AppDataPath`） |
| 读写语义 | `IConfiguration` 属性即持久化：赋值后由实现负责落盘（`Save()`/`ConfigurationChanged` 以源码为准） |

**规则**：

1. 新 UI 的 DI 容器与 Core 的 Autofac 容器**不合并**。Core 的容器是全局静态单例，注册进去的类型会污染 CLI/其他入口；新 UI 只从它 `Resolve` 那 4 个 Core 类型。
2. 新 UI **不缓存** `IConfiguration` 的快照用于写入：设置页每次改一项就写回 `IConfiguration`，再读回校验，避免与 CLI 并发修改时丢字段。
3. 写操作串行化：`ISettingsService` 内部用 `SemaphoreSlim(1)` 保护"读-改-写"，因为 Core 的落盘不是原子的（无 `.tmp`+rename，以源码为准，spike 项 SP-03）。

## 4. 契约 ③：`IUser`（唯一人机交互出口）

`Core/User.cs:10` 全部 6 个成员：

| 成员 | 语义 | 封送要求 | 新 UI 实现要点 |
|---|---|---|---|
| `bool Headless { get; }` | 是否无 UI 模式 | 无 | 恒 `false` |
| `bool RaiseYesNoDialog(string message)` | 同步二选一 | **可能从后台线程调用** | `Dispatcher.Invoke(() => dialog.ShowDialog<bool>())`，**必须** `Invoke` 不可 `Post`（要返回值） |
| `int RaiseSelectionDialog(string message, params object[] args)` | 同步单选 | 同上 | 同上；`args` 是候选对象，显示 `ToString()`，返回索引 |
| `void RaiseError(string message, params object[] args)` | 报错 | 可 `Post`（非阻塞） | 非模态错误条或对话框；**不要**用 MessageBox 堆叠 |
| `void RaiseProgress(int percent, string message = "")` | 进度 | 可 `Post` + 节流 | 交给 `IOperationQueue`，见 05 篇 §4 |
| `void RaiseProgressPercent(int percent)` | 仅百分比 | 同上 | 同上 |
| `void RaiseMessage(string message, params object[] args)` | 一般信息 | 可 `Post` | Toast / 状态栏 |

> 注：`RaiseMessage`/`RaiseProgress` 的确切成员集合以 `Core/User.cs:10` 源码为准，实现在 spike 阶段对齐（SP-01）。

### 关键：Core 会**主动**弹框

`ModuleInstaller.InstallList` 内部会调用 `User.RaiseYesNoDialog(ModuleInstallerContinuePrompt)`（`Core/IO/ModuleInstaller.cs:119`）。这意味着：

- 新 UI 不能假设"我发起安装 → 我一直持有控制权"，**弹框可能在任何时刻从安装线程冒出来**；
- 因此对话框必须能**从任意线程**被安全地创建在 UI 线程上，且不依赖调用方的窗口状态；
- 若不想让 Core 决定"要不要继续"，传 `ConfirmPrompt: false` 自己先确认（推荐：一致的用户体验 + 自己的措辞 + 自己的"不再提示"逻辑）。

### 骨架

```csharp
public sealed class AvaloniaUser : IUser
{
    private readonly IDispatcher _ui;
    private readonly IDialogService _dialogs;
    public bool Headless => false;

    public bool RaiseYesNoDialog(string message) =>
        _ui.Invoke(() => _dialogs.Confirm(message));           // 同步，必须 Invoke

    public int RaiseSelectionDialog(string message, params object[] args) =>
        _ui.Invoke(() => _dialogs.Select(message, args));       // 返回索引；用户取消 → -1 需与 Core 期望一致（SP-04）

    public void RaiseError(string message, params object[] args) =>
        _ui.Post(() => _dialogs.Error(string.Format(message, args)));

    public void RaiseProgress(int percent, string message = "") =>
        _ui.Post(() => _progress.Report(percent, message));
    // ...
}
```

## 5. 契约 ④：`GameInstanceManager`

| 成员 | 行 | 用途 |
|---|---|---|
| `ctor(IUser user, IConfiguration config)` | :49 | 构造时读实例列表 |
| `GetPreferredInstance()` | :75 | 返回 `null` 表示无可用实例 |
| `SetCurrentInstance(GameInstance)` | :476 | 切换当前实例 |
| `AddInstance(...)` | :204 | 新建实例（可自动探测路径） |
| `CloneInstance(...)` | :245 | 克隆现有实例配置 |
| `event InstanceChanged` | :35 | 实例切换通知 |
| `event CacheChanged` | :38 | 缓存路径变化（影响下载器） |

**切换实例的完整序列**（少一步就会出现"数据串台"）：

```
1. 若当前有 RegistryManager：RegistryManager.DisposeAll()          :295  ← 释放旧锁
2. 检查目标实例是否 IsMaybeLocked → 是则提示并中止
3. mgr.SetCurrentInstance(newInst)                                :476
4. reg = RegistryManager.Instance(newInst, repoData)               :265  ← 取新锁
5. 重建所有依赖实例数据的 Service/VM（注册表快照、可用模组过滤、变更集清空）
6. 取消所有针对旧实例的进行中操作（下载可保留，安装必须取消）
```

## 6. 契约 ⑤：`RegistryManager` 与事件

### 6.1 注册表

| 项 | 事实 |
|---|---|
| 获取 | `RegistryManager.Instance(GameInstance, RepositoryDataManager)`，`Core/Registry/RegistryManager.cs:265` |
| 锁 | 每实例一个锁文件；`GameInstance.IsMaybeLocked` 可探测 |
| 释放 | `RegistryManager.DisposeAll()`，`:295` —— **进程退出必经**，否则下次启动报锁冲突 |
| 读 | `registry.InstalledModules`（已装）、`registry.InstalledDlls`、`registry.InstalledDlc` 等（以源码为准） |
| 写 | 由 `ModuleInstaller` 负责；UI 只读 |

### 6.2 事件表（进度都从 Core 推）

| 事件 | 位置 | 用途 | 线程 |
|---|---|---|---|
| `RepositoryDataManager.Update()` | :144 | 主动触发仓库索引刷新（网络） | 调用方→内部异步 |
| `RepositoryDataManager.Updated` | :253 | 刷新完成/失败通知 | 后台 |
| `GameInstanceManager.InstanceChanged` | :35 | 当前实例变化 | 调用方 |
| `GameInstanceManager.CacheChanged` | :38 | 缓存路径变化 | 调用方 |
| `ModuleInstaller.InstallProgress` | :46 | 安装进度（每文件百分数） | 后台 |
| `ModuleInstaller.RemoveProgress` | :47 | 卸载进度 | 后台 |
| `ModuleInstaller.OneComplete` | :48 | 单个模组安装完成 | 后台 |
| `NetAsyncModulesDownloader.*` | `Core/Net/NetAsyncModulesDownloader.cs:23-27` | 下载进度/完成 | 后台 |

**约束**：这些事件全部在后台线程触发，订阅者必须自行封送。**唯一允许订阅它们的地方是 `Services` 层**，ViewModel 只订阅 Service 层已经封送过的 `IObservable`/事件（05 篇 §3）。

## 7. 契约 ⑥：`CkanTransaction`（文件事务）

```csharp
using (var tx = CkanTransaction.CreateTransactionScope())   // Core/CkanTransaction.cs:24
{
    // 所有改盘操作
    tx.Complete();
}
```

| 事实 | 说明 |
|---|---|
| 实现 | 基于 `TxFileManager`（TxF） |
| 用例 | `GUI/Main/MainInstall.cs:259`、`GUI/Main/MainRepo.cs:88`、`ConsoleUI/InstallScreen.cs:57` |
| 何时需要 | 任何"多个文件要么全成要么全不成"的场景：安装、卸载、升级、接管未托管文件、写注册表 |
| 何时不需要 | 只读、纯内存、纯网络 |

**注意**：`ModuleInstaller.InstallList` 内部已经处理了自己的事务边界（以源码为准，SP-05）。新 UI **不要**在外层再套一层事务去包住 `InstallList`——TxF 嵌套行为未定义，可能导致外层回滚忽略内层已提交。规则：**UI 层的事务只包自己直接做的文件操作**（例如"把用户的 `.ckan` 文件复制到缓存目录"）。

## 8. 关键调用签名速查

| 调用 | 签名要点 | 行 |
|---|---|---|
| `ModuleInstaller.InstallList` | `(IEnumerable<CkanModule> modules, RelationshipResolverOptions options, RegistryManager registry_manager, ref HashSet<string>? possibleConfigOnlyDirs, IDependencyAnalyzer? deduper, string? userAgent, NetAsyncModulesDownloader? downloader, List<CkanModule>? autoInstalled, bool ConfirmPrompt)` | :60-68 |
| `ModuleInstaller.FindRecommendations` | `(Registry registry, List<CkanModule>? sourceModules, List<CkanModule>? toInstall, List<CkanModule>? toRemove, bool CriticallyDependant)` — 用于"推荐安装" | :1572 |
| `ModuleInstaller.CanInstall` | 判断某模组在当前环境下是否可装（含兼容性） | :1661 |
| `RepositoryDataManager.Update` | `(IUser user, bool priorityToGUIMods)`（以源码为准） | :144 |
| `NetAsyncModulesDownloader` | 构造需 `IUser` + `IConfiguration`；可设 `userAgent` | `Core/Net/NetAsyncModulesDownloader.cs` |

## 9. 明确禁用 / 特殊处理

| Core 能力 | 处理 | 原因 |
|---|---|---|
| `Core/AutoUpdate/AutoUpdate.cs:51` `CanUpdate` | 禁用，UI 里隐藏"检查更新" | 基于"运行中的 exe"判断，新 exe 语义不同 |
| `:62` `StartUpdateProcess` | 禁用 | 会替换 exe 并拉起 `ckan.exe` |
| `RegistryManager` 并发 | 串行化 | 文件锁 |
| `RepositoryDataManager` 多实例 | 只用一个（单例） | 内部有缓存与文件读写 |
| 直接 `File.*` 写 `config.json` | 禁止 | 会绕过 `JsonConfiguration` 的字段完整性，丢用户设置 |

## 10. 待 spike 验证清单

| ID | 待验证 | 影响 | 验证方式 |
|---|---|---|---|
| SP-01 | `IUser` 的确切成员集合与签名 | 骨架能否编译 | 读源码 + 编译 |
| SP-02 | `Logging.Initialize()` 幂等性 | 重复初始化是否重复 appender | 连续调两次看输出行数 |
| SP-03 | `JsonConfiguration` 落盘原子性 | 并发写是否可能截断 | 读实现 + 杀进程测试 |
| SP-04 | `RaiseSelectionDialog` 用户取消时 Core 期望什么（-1? 0? 异常?） | 取消流程正确性 | 调用点逐个查 |
| SP-05 | `InstallList` 内部事务边界 | 是否需外层事务 | 读 `ModuleInstaller` 实现 |
| SP-06 | `Update()` 是否阻塞、是否可重入 | UI 刷新策略 | 断网/中途再点刷新 |
| SP-07 | `GetPreferredInstance()` 在实例目录被删除时的行为 | 无实例路径健壮性 | 手工删目录 |
| SP-08 | 3,000 mod 时 `RepositoryDataManager` 的内存与加载耗时 | 启动性能目标 | 用真实 repo 压测 |
