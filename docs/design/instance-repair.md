# F-08 实例可靠性处置（锁定探测 / 强制解锁 / 坏注册表隔离 / 版本过旧建议）

> 规格来源：[12 全特性对齐清单](../plan/12-feature-parity.md) F-08、[06 WBS 与里程碑](../plan/06-wbs-milestones.md) UI-09 / INT-04。
> 对应上游语义：`external/CKAN/Core/Registry/RegistryManager.cs`（`registry.locked` 探测、坏 JSON 改名后重建空注册表）。
> 姊妹文档：[自动更新与安装](app-update.md)（F-52）、[自动更新检查](update-check.md)（F-52 检查侧）。

---

## §1. 定位与边界

实例页原生的失败模式是「打不开、切不过去、提示重试也没用」：实例被别的 CKAN 进程（或上次崩溃残留）锁着、`registry.json` 被写坏、注册表由更新版本的 CKAN 写入、游戏版本低于注册表声明。上游 CKAN 对这些情况只有「抛异常 / 静默改名」，本特性把它做成**用户可判断、可自决**的一屏自检 + 两个显式动作。

分层与文件：

| 层 | 文件 | 职责 |
|---|---|---|
| 契约 | `src/CkanUI.Services.Abstractions/Instances/InstanceRepairModels.cs`、`IInstanceRepairService.cs` | DTO 与四方法接口，零 CKAN / 零 Avalonia 依赖 |
| 服务 | `src/CkanUI.Services/Instances/InstanceRepairService.cs` | 只读探测 + 两个写动作（删锁、改名），全部返回 `OperationResult` |
| 表现层 | `src/CkanUI.ViewModels/Pages/InstancesPageViewModel.cs`（自检区 :1195 起） | 摘要、问题清单、按钮可用性、确认文案、失败兜底 |

红线：服务层不弹窗、不抛异常给界面；界面层不拼文件路径、不判断 JSON（一切以 `InstanceRepairDiagnosis` 为准）。

## §2. 契约

`IInstanceRepairService`（`src/CkanUI.Services.Abstractions/Instances/IInstanceRepairService.cs:12`）：

| 方法 | 语义 | 写盘 |
|---|---|---|
| `DiagnoseAsync(InstanceRepairTarget, ct)` | 只读自检，产出 `InstanceRepairDiagnosis` | 否 |
| `ForceUnlockAsync(target, reason, ct)` | 删除锁痕迹（游戏未运行才允许），写操作记录 | 是（仅锁路径） |
| `QuarantineRegistryAsync(target, ct)` | 把解析不了的 `registry.json` 改名保留 | 是（改名，不删内容） |
| `CheckCompatibilityAsync(target, ct)` | 单独给出「CKAN 太旧 / 游戏太旧」的可读建议 | 否 |

关键 DTO（`InstanceRepairModels.cs`）：

- `InstanceIssueKind`：`RegistryLocked` / `RegistryCorrupt` / `RegistryFromNewerCkan` / `GameVersionTooOld` / `GameDirectoryMissing`。
- `InstanceLockArtifact(Path, Kind, LastWriteTimeUtc, OccupantHint)`：锁痕迹 + 占用者线索；`Kind` 区分 `RegistryLockFile` 与 `RegistryLockDirectory`（正常只有一个文件，损坏的树里也可能是目录）。
- `InstanceIssue(Kind, Title, Message, Detail, FixHint, CanAutoFix)`：`Message` 给用户看，`Detail` 给「展开详情」，`FixHint` 由界面拼成一段修复建议。
- `InstanceRepairDiagnosis`：`IsHealthy` / `IsLocked` / `HasCorruptedRegistry` / `CanForceUnlock`（= `IsLocked && !IsGameRunning`）/ `Headline`。
- `InstanceUnlockReport` / `InstanceRegistryQuarantine`：动作回执（删除/跳过清单、隔离后路径、操作记录路径、字节数、时间戳）。
- `InstanceCompatibilityInfo`：`CkanTooOld` / `GameVersionTooOld` + 一句 `Advice`。

## §3. 锁探测（只读）

`ProbeLocks`（`InstanceRepairService.cs:391`）只看一个路径：`<GameDir>/CKAN/registry.locked`——这正是上游 `RegistryManager.IsInstanceMaybeLocked` 的判据，多探别的名字会造出上游不认的「假锁」。

- **锁种类**：先 `File.Exists` 再 `Directory.Exists`，两者都不在就返回空（自检通过）。
- **时间线索**：`TryLastWriteUtc` 取最后写时间，取不到就 `null`（不伪造）。
- **占用者线索**（尽力而为，`BuildOccupantHint` :420）：拿 `IGameProcessProbe.GetProcessImagePaths()` 的进程镜像路径，按优先级给三种文案——
  1. 进程名以 `ckan` 开头（不分大小写）→「占用者疑似 CKAN 进程「…」：完整路径」；
  2. 路径落在该实例游戏目录内（`GameProcessProbe.IsInsideGameDir`）→「占用者疑似游戏进程「…」」；
  3. 都定位不到 →「未定位到占用进程（更可能是上次异常退出留下的残留锁），只保留路径与最后写入时间」。
  探针本身抛异常时吞掉并当「没定位到」处理：探测失败不该让自检失败。

## §4. 强制解锁（唯一的写动作之一）

前置条件（任何一条不满足就直接失败、不碰磁盘）：

1. 实例必须有非空 `GameDir`（`实例没有配置游戏目录，无法自检。` / `必须提供要自检的实例。`）；
2. `<GameDir>/CKAN` 目录存在；
3. 确实探测到锁，否则「当前没有被锁定，无需解锁。」；
4. **游戏进程未运行**（`IsGameRunning`），否则「游戏正在运行，无法解锁实例「X」。请先退出游戏再重试。」并标 `retryable=true`——游戏运行中删锁会让两个进程同时写注册表，这是数据损坏的直接通道，因此宁可拒绝。

删除策略（`InstanceRepairService.cs:203-242`）：`RegistryLockFile` 直接 `File.Delete`；`RegistryLockDirectory` **只在空目录时** `Directory.Delete(path, recursive: false)`，非空目录跳过并在回执里列出，绝不递归删用户目录。全部跳过则失败：「锁路径都删不掉…（非空锁目录不做递归删除，请人工确认内容。）」

操作记录：每次成功解锁后追加一行 JSONL 到 `%LocalAppData%/CKAN/instance-repair-journal.jsonl`（`AppendJournal` :533，`DefaultJournalPath` :60），字段 = 时间戳 + `action` + 实例名 + 游戏目录 + `reason`（界面传「实例页手工强制解锁」）+ 受影响路径。**写记录失败不影响解锁结果**：回执里 `JournalWritten=false`，界面的动作语义仍算成功（用户要的是能打开实例，不是日志）。

## §5. 坏注册表隔离

判据（`ReadRegistryFacts` :480）：文件存在 + 顶层是 JSON 对象 + `JsonDocument.Parse` 成功。解析失败（`JsonException`）或顶层不是对象都算「不可读」；读文件本身 IO 报错也只算不可读，不升级为异常。

隔离动作（`QuarantineRegistry` :256）：`registry.json` → `registry.json.corrupt-yyyyMMdd-HHmmss`（`CorruptTag=".corrupt-"` :32，重名时追加 `-2`、`-3`），**只改名、不删除、不改字节**，回执带原路径、新路径与字节数。

为什么改名而不是删除或清空：`registry.json` 里是用户装过的全部模组清单，删掉就再也说不清装过什么；改名后 ① 用户随时能翻回去人工抢救；② CKAN 下次打开该实例时因为找不到 `registry.json` 会 `Create` 一个空注册表（上游 `RegistryManager.Load` 的既有行为），实例立刻可用。上游自己检测到损坏时用的是 `registry.json_CORRUPTED_<时间戳>` 后缀，这里刻意不复用那个名字，避免与 CKAN 原生产物混淆、也避免被上游清理逻辑误认。

前置：`registry.json` 存在且确实不可读。文件不存在 →「没有 registry.json，无需隔离。」；能正常解析 →「可以正常解析，不需要隔离。」（两道闸门都在服务层，界面再挡一次是为了给即时文案）。

## §6. 版本过旧与升级建议

| 情况 | 判据 | 建议文案（`FixHint`） |
|---|---|---|
| CKAN 太旧 | registry.json 顶层 `registry_version` > `InstanceRepairService.SupportedRegistryVersion`（=3，镜像上游私有常量 `Registry.LATEST_REGISTRY_VERSION`） | 「升级 CKAN 到最新版本后再打开该实例，否则读写注册表会失败。」 |
| 游戏太旧 | registry.json 顶层（可选）`compatible_ksp_version` 高于 `InstanceRepairTarget.GameVersion` | 「把游戏升级到 X 或更新版本后再操作；不打算升级就别在这个实例上装模组。」 |
| 游戏目录丢了 | `<GameDir>` 不存在 | 「检查移动硬盘/清理工具是否删过目录；实在找不回就在实例页移除这条记录。」 |

已知口径差异（诚实记录）：上游把兼容 KSP 版本放在 `<GameDir>/CKAN/compatible_ksp_versions.json`（`Core/Configuration/CompatibleGameVersions.cs`，属性 `Versions`），而 `registry.json` 里并没有这个字段。因此**「游戏太旧」这一条只在注册表真的带 `compatible_ksp_version` 时才触发**（大小写不敏感读取）；缺失即跳过，不猜、不误报。补齐路径是后续再加一个 `compatible_ksp_versions.json` 读取器，届时只需在 `DiagnoseAsync` 里多喂一个事实来源，契约不变。

## §7. 界面落点（`InstancesPageViewModel`）

- 视图：`src/CkanUI.App/Views/InstancesPageView.axaml` 详情面板新增「实例自检」区——自检按钮、摘要文案、问题清单（`ItemsControl` 绑 `RepairIssues`，逐条显示 `Title`/`Message`）、修复建议（`RepairAdvice`）、两个处置按钮（分别绑 `CanForceUnlock`/`CanQuarantineRegistry`）。该页 `x:CompileBindings="False"`，绑错属性名不会在编译期报错，因此改名时必须同步搜一遍 XAML。
- 宿主注入：`public IInstanceRepairService? Repair { get; set; }`（:1195，与 `FolderPicker`/`PlayTime` 同款属性注入，因为实现活在 Services 程序集、而 ViewModels 不许反向依赖）。**DI 注册与页面接线由 Lead 在 `src/CkanUI.App/ServiceRegistration.cs` 完成**：`services.AddSingleton<IInstanceRepairService>(new InstanceRepairService())`，并在创建 `InstancesPageViewModel` 后赋 `Repair`。
- 状态：`Diagnosis`、`RepairIssues`、`RepairAdvice`（由各 issue 的 `FixHint` 去空拼接）。
- 派生：`HasDiagnosis`、`HasRepairIssues`、`CanForceUnlock`、`CanQuarantineRegistry`、`DiagnosisSummary`（「尚未自检。」/「自检通过…」/「发现 N 个问题：X。」）。
- 命令：`DiagnoseCommand`(:1243)、`ForceUnlockCommand`(:1278)、`QuarantineRegistryCommand`(:1352)，每个都有一份 `public async Task XxxInstanceAsync(InstanceRowViewModel? row)` 供 headless 单测直调。
- 界面侧二次闸门（服务层之外）：没选中实例 →「请先选择一个实例，再点「自检」。」；没注入服务 →「实例自检功能尚未接线…」；解锁/隔离前必须已有**同一实例**的自检结论，否则要求先自检（避免拿 A 的结论去动 B 的目录）；`!CanForceUnlock` / `!HasCorruptedRegistry` 各有专属不可重试文案。
- 动作前必有一次确认弹窗（写清「删什么 / 不删什么」），取消只改 `StatusText`（「已取消解锁。」/「已取消隔离。」），不调服务。
- 动作成功后先重新自检、再写状态栏：复核会刷新「问题清单」与按钮可用性，处置结果文案放在复核之后写，避免被「自检摘要」覆盖。
- 切换选中实例时：若旧结论属于别的实例则清空（同一实例因刷新重建行对象时保留结论）。

## §8. 失败矩阵

| 场景 | 服务返回 | 界面表现 |
|---|---|---|
| 未选实例 / 未注入服务 | 不调服务 | 错误条一句中文，`CanForceUnlock` 保持 false |
| 游戏运行中想解锁 | `Fail(…请先退出游戏再重试。)` `retryable=true` | 错误条 + 「（可重试）」，按钮仍禁用 |
| 锁目录非空 | `Fail(…仍处于锁定状态)` + detail 列出跳过路径 | 错误条 + 详情可展开 |
| 删除中途 IO 失败 | `Fail(删除锁文件失败…)`，detail 记已删清单 | 同上，实例仍是锁定态（不清空自检结论） |
| 注册表可正常解析 | `Fail(不需要隔离)` | 错误条（不可重试） |
| 隔离时 `File.Move` 失败 | `Fail(隔离失败…)` | 错误条 + detail |
| 操作记录写不进去 | 动作仍 `Ok`，`JournalWritten=false` | 状态栏回执文案正常，不额外报警 |
| 探针/文件系统抛异常 | 诊断按「未定位到占用进程」降级 | 自检照常出结论 |
| 服务整体抛异常（不该发生） | — | `OnException(ex, "强制解锁实例")`：错误条 + 上报 `IErrorReporter`，界面不崩 |

## §9. 不可变式（回归时必须保持）

1. 诊断只读：`DiagnoseAsync` 不创建、不删除、不改名任何文件。
2. 用户文件不静默消失：坏注册表只改名；锁路径只删 `registry.locked` 且非空目录不递归。
3. 游戏运行中永不解锁。
4. 所有对外方法返回 `OperationResult`，永不抛给调用方；取消统一映射成「操作已取消。」。
5. 日志只记路径与计数，不把注册表内容写进日志。

## §10. 测试落点

- 服务层 `tests/CkanUI.Services.Tests/InstanceRepairServiceTests.cs`（24 例，全离线）：`TempWorkspace` 造实例树、`FakeProcessProbe` 造假进程镜像、固定时钟。覆盖锁文件/锁目录探测与占用者文案、健康实例、新版本注册表、损坏注册表不被诊断动作改动、KSP 版本过旧/相等、目录缺失、空入参、探针抛异常、解锁成功写记录、游戏运行中拒绝、无锁拒绝、空锁目录删除、非空锁目录跳过、记录写失败仍成功、预取消令牌、隔离保留内容且不重名、健康注册表拒绝、缺文件拒绝、CKAN 太旧 / 游戏太旧 / 注册表缺失。
- 表现层 `tests/CkanUI.ViewModels.Tests/InstancesRepairTests.cs`（22 例）：`InstancesFakeRepairService` 预设回执，逐条验证按钮可用性、前置闸门、确认/取消、成功后重新自检、失败文案、切换实例清结论、刷新保留结论。
- 跑法：`dotnet build tests/CkanUI.Services.Tests/CkanUI.Services.Tests.csproj && dotnet run --project tests/CkanUI.TestRunner --no-build -- CkanUI.Services.Tests`（表现层同理换 `CkanUI.ViewModels.Tests`）。
