# 18 · 游戏启动后的资源释放（E6）

> 上层依据：[00-index.md](00-index.md)（范围与决策）、[01-architecture.md](01-architecture.md)（分层）、[05-threading.md](05-threading.md)（线程纪律）
> 用户需求（verbatim）：「启动游戏后自动释放大量资源；」
> 状态：计划稿 v1.0（未开工）
> 一句话：KSP 是内存与 IO 的大户；我们的 UI 在把游戏拉起来之后，应当**按层次把占着的东西逐步还回去**，并且**用户切回来时能按需长回来**——不是简单的 `GC.Collect()`，也不是系统级"内存清理器"。

---

## 1. 目标与非目标

### 1.1 目标

| ID | 目标 | 可测判据 |
|---|---|---|
| **G1** | 游戏启动稳定后，UI 进程的**可释放资源全部归还** | 托管堆、私有字节、工作集、GDI/USER 句柄、文件句柄、GPU 位图五类读数在 §10 阈值内 |
| **G2** | 优先释放**真正的大头**，而不是零头 | 先释放仓库元数据对象图与 `installed_files` 索引（§2.3），UI 位图/字符串排在后面 |
| **G3** | 释放**可逆**，切回 UI 不出现"空白 + 卡死" | 恢复 ≤3s 且有进度；恢复期间列表显示骨架而非空白（§6） |
| **G4** | 释放**不拖慢游戏启动** | 时机编排到 KSP 主窗口出现之后（§4）；L2/L3 线程降优先级（§9） |
| **G5** | 三档可配、可整体关闭 | 保守 / 均衡（默认）/ 激进 三档（§7） |

### 1.2 非目标

- **不做**系统级内存清理器（不遍历/不 trim 其它进程、不调 `EmptyAllWorkingSets`）；
- **不改 Core**（只调用其公有 API，遵守 [00-index.md](00-index.md) §4 的 D5）；
- **不自动删除磁盘文件**（磁盘回收只做"提示 + 一键"，见 §8）；
- **不做**"启动游戏后自动退出 UI 进程"作为默认行为（可作为用户选项，见 §7.3）；
- **不做**内存压缩/写入磁盘的自定义分页（那是 OS 的事，我们只影响自己的工作集）。

---

## 2. 现状取证（全部实测行号）

### 2.1 启动游戏的唯一入口

```csharp
// Core/GameInstance.cs:360
public void PlayGame(string command, IUser user, Action? onExit = null)
{
    if (Game.AdjustCommandLine(command.Split(' '), Version()) is string[] split
        && split.Length > 0 && split[0] is string binary)
    {
        try
        {
            Directory.SetCurrentDirectory(GameDir);            // :370 全局副作用！
            Process p = new Process {                          // :371-380
                StartInfo = new ProcessStartInfo {
                    FileName = binary, Arguments = string.Join(" ", split.Skip(1)),
                    UseShellExecute = true },
                EnableRaisingEvents = true };
            var isSteam = SteamLibrary.IsSteamCmdLine(command); // :382
            p.Exited += (sender, e) => {                        // :383-390
                if (!isSteam) { playTime?.Stop(CkanDir); }
                onExit?.Invoke(); };
            p.Start();                                          // :392
            if (!isSteam) { playTime?.Start(); }                // :393-396
        }
        catch (Exception exception)
        {
            user.RaiseError(Properties.Resources.GameInstancePlayGameFailed, exception.Message); // :398-401
        }
    }
}
```

对 E6 的四个直接后果：

1. **只有一个回调 `onExit`**，且在线程池线程触发——**没有"启动成功"的回调**，我们必须自己监视；
2. `UseShellExecute = true` → `p` 可能是壳进程（Steam URI / 关联启动），`p.Id` 不可靠；
3. `dynamically Directory.SetCurrentDirectory(GameDir)`（`:370`）在**进程范围内改 CWD** → 此后一切相对路径都会落到游戏目录，我们的路径解析必须全部绝对化（R-63）；
4. `isSteam` 为真时既无 playtime 也无可靠进程句柄 → 需要"无监视模式"兜底（§9.5）。

### 2.2 老 GUI 在启动游戏时做了什么

- `GUI/Controls/ManageMods.cs:711-721`：菜单点击 → `LaunchGame?.Invoke(cmd)`；
- `GUI/Controls/ManageMods.cs:732-736`：只按 `playing` 切换按钮文案与 `Enabled`；
- 也就是**只改了一个按钮状态，没有任何资源动作**。

### 2.3 内存大头在哪（这是 E6 的全部意义）

| 数据 | 位置 | 规模（实测/推算） | 内存特征 |
|---|---|---|---|
| **仓库元数据对象图** | `%LocalAppData%\CKAN\repos\*.json` → `RepositoryDataManager` | JSON **37.9 MB**；3,799 identifier / **31,132** module_version | 解析成 `Dictionary<string, AvailableModule>` + 每版本一个 `CkanModule`；对象头/字典/字符串开销通常使对象图为磁盘的 **4–8 倍** → 推算 **150–300 MB** |
| **Registry 的 installed_files / installed_modules** | `<GameDir>\CKAN\registry.json` | 每个已装 mod 的每个文件一行；重度实例常见 **数万–十数万** 条 | `Dictionary<string,string>` 反向索引，字符串为主 → 数十–上百 MB |
| UI 自己的位图/图标/字符串 | Avalonia 渲染层 | 缩略图/图标按需加载 | 通常 **<30 MB**，是零头 |
| 日志/命令环形缓冲 | `LogViewModel` / `CommandLog` | 上限 100k 条 | 可控（裁剪即可） |

`Core/Repositories/RepositoryDataManager.cs:18` 自己写着 "Data is cached in memory and on disk to minimize reloading."，`:42-67` 的 `GetAvailableModules` / `GetAllAvailDicts` / `GetAllAvailableModules` 全部**返回缓存对象的引用**——只要有人持有引用，堆就下不来。

### 2.4 关键约束：Core 没有"清缓存"API

- `RepositoryDataManager` **没有** `Clear()` / `Dispose()`；只有 `:253 Updated` 事件让 dependents 自己清缓存（`:219` 注释：Fire an event so affected registry objects can clear their caches）；
- 所以释放只能靠**丢弃实例**：用 Autofac 子作用域（`ILifetimeScope`）持有 `RepositoryDataManager`，释放作用域即断开引用；
- `RegistryManager` 有公开的 `:116 Dispose()`、`:139 registryCache.Remove(directory)`、`:295 static DisposeAll()`——可以直接用。

**这条约束决定了 E6 的架构：Core 侧资源的生命周期必须由我们的 DI 作用域管，而不是由"退出时才释放"的假设管。**

---

## 3. 分层释放模型（L0–L4）

| 层 | 做什么 | 预计收益 | 预计耗时 | 代价 / 可逆性 | 失败处理 |
|---|---|---|---|---|---|
| **L0 视图层** | 清空列表 `ItemsSource`（保留骨架行数）→ 释放缩略图/图标/字体位图缓存 → 停动画与 UI 计时器 → 窗口最小化/退到托盘 | 10–30 MB + GPU 纹理 | <100 ms | 完全可逆（重算列表）；无风险 | 捕获后跳过 |
| **L1 应用缓存** | 搜索索引 / 上次搜索结果 / `DictPack`（译文包，3,639 块）/ 日志环形缓冲裁到 2,000 条并落盘 / 命令历史 | 10–40 MB | <300 ms | 可逆（懒重建） | 单步失败继续 |
| **L2 Core 数据** | 释放 `RepositoryDataManager` 所在 DI 作用域；`RegistryManager.DisposeAll()`；保留 `GameInstanceManager`（小） | **100–300 MB**（最大头） | 1–3 s（含释放时的回收） | 可逆但**要重读 37.9 MB**（R-59） | 失败则记录并放弃后续层 |
| **L3 托管堆** | `GC.Collect(2, Forced, blocking: true, compacting: true)` ×2 + `GC.WaitForPendingFinalizers()`；必要时 `GC.Collect` 后再压一次 LOH | 碎片 + 未引用对象（通常 **20–60%** 堆） | 1–5 s | 不可逆但无需恢复；**会造成一次卡顿** | 只在 UI 不可见时做 |
| **L4 OS 层** | `EmptyWorkingSet`（psapi）/ `SetProcessWorkingSetSizeEx`（kernel32）；进程 `MemoryPriority` 降到 `MEMORY_PRIORITY_LOW`；进程优先级 `BelowNormal` | 工作集可降到几十 MB | <50 ms | 可逆但**切回时 page fault**（R-58） | 失败静默 |

设计原则：

1. **顺序即优先级**：L2 的收益远大于 L0+L1，但必须在 KSP 主窗口出现之后做（§4）；
2. **每一层独立成败**：任一层异常只记事件、不阻断后续层（L3/L4 除外：L3 需要 L2 才有意义，L4 建议最后）；
3. **L3 是"唯一会明显卡一下"的层**，因此只在窗口不可见或最小化时执行；
4. **不用 `GC.Collect()` 当日常手段**：只在这一次释放窗口里用（[05 篇](05-threading.md) 会写明禁则在其它路径）。

### 3.1 实现注记（L0/L1 参与方落地，2026-10-28）

L0/L1 原先只有接口（`IViewResourceParticipant` / `IAppCacheParticipant`）而没有实现，保守档实际统计 0 项。现补上生产参与方：

| 层 | 参与方 | 释放什么 | 为什么不影响功能 |
|---|---|---|---|
| L0 | `src/CkanUI.App/Lifecycle/UiViewResourceParticipant.cs` | `NavGlyphToGeometryConverter.ClearCache()`（18 个字形的 `StreamGeometry`）+ `BackgroundHost.TryReleaseCachedBitmap()`（≤1600 宽的背景位图） | 字形是纯加速层，下次绑定重新解析；位图在还原/切回时按设置重建 |
| L1 | `src/CkanUI.App/Lifecycle/PageProjectionCacheParticipant.cs` | 模组页行投影（`Rows` + `_all`）、内容预览的 `Entries`/`RootNodes`、模组文本的 `_allEntries` | 三个页面重新激活时按原流程重载（模组的勾选待变更集**不清**，那是用户的待变更集） |
| L1 | `src/CkanUI.Services/Lifecycle/LocalizerCacheParticipant.cs` | `Localizer.ClearDiagnostics()`（缺译表 + 告警表） | 两张表只服务「缺译清单」，下次取译文重新累计，界面文案一字不变 |

两个实现层面的约束：

1. **位图只在没人引用时 Dispose**（`BackgroundHost.IsReferencedBy` 按引用比图片层图源与平铺画笔图源）：Avalonia 的渲染线程会直接用画笔里的位图，提前释放等于把已销毁对象交给渲染器。所以非 UI 线程调 L0 时，参与者只 `IDispatcher.Post` 一次并在本次报告里少计 1 项（宁可少报不可谎报）。
2. **单页释放失败不连累同层其它页**：`PageProjectionCacheParticipant` 逐页 try/catch，异常记 Warn 后继续累加，返回的是真实释放条数。

测试：`tests/CkanUI.ViewModels.Tests/{ModsProjectionTrimTests,ContentPageViewModelTests,ModTextPageViewModelTests}.cs`（裁剪后可重建）、`tests/CkanUI.App.Tests/{ResourceReleaseParticipantsTests,BackgroundHostTests}.cs`（缓存清理、跨线程排程、引用判定）、`tests/CkanUI.Localization.Tests/DiagnosticsTests.cs`、`tests/CkanUI.Services.Tests/LocalizerCacheParticipantTests.cs`。

---

## 4. 时机编排（T0–T5）

```
T0  用户点「启动游戏」
    ├─ 保存必要上下文（实例路径、视图游标、未提交筛选条件）
    ├─ 调 GameInstance.PlayGame(command, user, onExit: 我们的回调)
    └─ 立即：进程优先级 → BelowNormal（§9.1）

T1  p.Start() 返回（外部无法直接拿到 p → 我们自己 new Process 监视，见 §9.2）
    └─ 启动监视器：500 ms 轮询 MainWindowHandle / Responding / HasExited

T2  KSP 主窗口出现（MainWindowHandle != 0）
    └─ 兜底：若 120 s 内没探测到窗口但进程存活 → 视为已启动（Steam 壳/无窗口启动）

T3  （T2 起 +0 s）执行 L0 + L1 —— 只动我们自己的东西，零风险

T4  （T2 起 +10 s）执行 L2 —— 此时 KSP 已过 GameData 装载的主 IO 峰值

T5  （T2 起 +30 s）执行 L3 + L4 —— 堆压缩与工作集 trim

任意时刻：游戏进程退出（onExit / 监视器 HasExited）→ 取消后续层 → 执行恢复路径（§6）
```

为什么必须延后：

- KSP 启动阶段是**重 IO**（读 GameData 里几千个 cfg/dll/贴图）。L2 的释放会触发一次大对象回收 + 若紧接着重建会重读 37.9 MB JSON——**与游戏抢 IO 会明显拖慢装载**（R-60）；
- L3 的 `GC.Collect` 会**暂停所有托管线程**（含我们自己的 UI），在游戏刚起步时做会让磁盘缓存被我们挤掉；
- 因此 L2/L3 的触发条件里加一条：**KSP 进程 CPU 占用回落到 <20% 时才执行**（用 `TotalProcessorTime` 差分，1 s 采样）。

---

## 5. 释放什么、用什么、可逆性

| 资源 | 手段 | 可逆 | 备注 |
|---|---|---|---|
| 仓库元数据对象图 | 释放持有 `RepositoryDataManager` 的 DI 子作用域 | 需重读（1–3 s） | **最大项**；Core 无 `Clear()`，只能丢实例（§2.4） |
| Registry（installed_files 等） | `RegistryManager.DisposeAll()`（`:295`）→ 内部 `:116 Dispose()` / `:139 Remove` | 重开实例 | 同时也释放 registry 锁文件的句柄 |
| 托管堆（未引用对象 + 碎片） | `GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true)` + `GC.WaitForPendingFinalizers()`，再压一次 | 无需恢复 | 只在此窗口调用 |
| 工作集（常驻物理内存） | `EmptyWorkingSet(GetCurrentProcess())`（psapi）或 `SetProcessWorkingSetSizeEx` | page fault 换回 | 数值上的"释放 MB 数"最显眼，但**不减少提交内存** |
| GDI/USER 句柄 | 释放 `RenderTargetBitmap`/图标/字体；清字体缓存 | 重建 | 句柄泄漏本身是 bug，回收只是兜底 |
| GPU 纹理/位图 | Avalonia 无公开 API → 清空视觉树 + 释放位图对象 + 触发一次重排；必要时重建 `TopLevel` | 重建 | 实际效果需 SP-38 验证 |
| 文件句柄 | flush 并关闭日志 writer（再以 append 重开）、关闭 repo 缓存流 | 重开 | 必须 flush，否则丢日志 |
| 计时器/后台循环 | 停日志节流器、UI 动画时钟、AI 流（若有） | 重启 | 恢复时按需重启 |
| 环形缓冲 | 日志裁到 2,000 条、命令历史裁到 200 条，裁剪前落盘 | 不恢复 | 与 [14 篇](14-log-and-ai-debug.md) §4 的缓冲设计一致 |

---

## 6. 恢复路径（用户切回来）

触发条件（任一）：窗口重新显示、托盘图标点击、任务栏激活、用户从"最近启动"里点 "显示 CKAN UI"。

恢复顺序（严格按此序，每步可见进度）：

1. **L4 回滚**：进程优先级回 `Normal`、`MemoryPriority` 回默认（瞬时）；
2. **重建 Core 数据**（后台，1–3 s）：新建 DI 作用域 → `RepositoryDataManager`（读缓存 JSON，必要时先读 37.9 MB）→ `RegistryManager.Instance(...)`；
3. **重建视图数据**：列表行从 Registry + 仓库数据重算（走虚拟化，首屏 200 行）；
4. **重建搜索索引**：后台增量建，先让"标识符前缀搜索"可用；
5. **恢复缓存**：译文包 `DictPack`、日志环形缓冲（从落盘文件续读）。

硬规则：

- **恢复期间不显示空白列表**：保留上次的行数与骨架，进度条显示"正在恢复数据（1.2 MB / 37.9 MB）"；
- **游戏仍在运行时**，默认只恢复到"精简模式"（列表只读、不建全量搜索索引），并在标题栏标注；
- **恢复失败**必须有出口：提示 + "重启 UI"按钮，不允许静默空白（R-59 的兜底）。

---

## 7. 三档设置

### 7.1 档位

| 档位 | 执行的层 | 预计释放 | 切回代价 | 风险 |
|---|---|---|---|---|
| **保守** | L0 + L1 | 20–70 MB | 无感（<0.3 s） | 极低 |
| **均衡（默认）** | L0–L3 | 120–400 MB | 1–3 s（带进度） | 低（重读 37.9 MB） |
| **激进** | L0–L4 | 120–400 MB + 工作集降到几十 MB | 1–5 s（page fault） | 中（早期机器/R-58） |

### 7.2 触发开关

- 默认**仅在"由本 UI 启动游戏"时触发**（用户自己双击 KSP 时不做，因为没有可靠时机）；
- 提供"检测到任意 KSP 进程启动时也释放"的开关（默认关：外部启动时我们的 UI 通常不在场）；
- 提供"游戏运行期间保持精简模式"开关（默认开：不自动恢复全量）。

### 7.3 启动后行为（独立选项）

| 选项 | 行为 |
|---|---|
| 留在托盘（默认） | 窗口最小化 + 托盘图标；托盘菜单可"显示界面/退出" |
| 只最小化 | 保留任务栏项 |
| 关闭 UI | 进程退出（最激进；释放全部，但下次要重新冷启动 3–5 s） |

---

## 8. 磁盘侧顺带回收（默认只提示，绝不自动删）

游戏启动后顺带能回收的磁盘项（**只统计、只提示**）：

- 下载缓存里的过期包（不属于任何已装/可装版本）；
- `saves/**/Backup/` 里超出保留数的旧存档备份（与 [13 篇](13-savegame-sfs.md) §7 的保留策略共用一套）；
- `CKAN.log.1..30` 滚动历史（保留最近 7 天）；
- 旧的解包临时目录 / `.pending` 残留。

硬规则（与 E1 的 L8/L9 一致）：**绝不自动删除**；一律列清单 + 用户确认 + 走回收站/事务；删除前后各写一条 journal。

---

## 9. 与游戏进程的协作

### 9.1 CPU / IO 让路

- 我们自己进程优先级 → `BelowNormal`（`Process.GetCurrentProcess().PriorityClass`），完成后恢复 `Normal`；
- L2 的磁盘读（若触发重建）用 `THREAD_MODE_BACKGROUND_BEGIN`（`SetThreadPriority`）包住，避免抢 KSP 的 IO；
- L3 前后各 1 s 采样 KSP 的 `TotalProcessorTime`，若 CPU >60% 则推迟 10 s 重试（最多 3 次）。

### 9.2 为什么不能依赖 `PlayGame` 的 `Process`

`UseShellExecute = true` 时 Shell 可能返回壳进程；且 `PlayGame` 不把 `Process` 交出来。做法：

- 我们自己 `new Process` 监视：已知可执行文件名（`Game.AdjustCommandLine` 的 `binary`）+ 启动时刻，用 `Process.GetProcessesByName(Path.GetFileNameWithoutExtension(binary))` 过滤 `StartTime` 晚于我们的 T1 的实例；
- 或调 `PlayGame` 时传 `onExit` 只用于"退出"信号，启动成功判定走监视器；
- **不改 Core** 的前提下这两条都能做到。

### 9.3 KSP 进程的退出

- `onExit` 在线程池线程 → 必须封送到 UI 线程后再做恢复（[05 篇](05-threading.md) §14/§15 同一纪律）；
- 与 `playTime` 不重复：`PlayGame` 内部已 Start/Stop（`:387`、`:395`），我们不碰。

### 9.4 崩溃/失败

- `p.Start()` 抛异常 → Core 已 `RaiseError`（`:400`）；我们不执行任何释放层，UI 保持全量；
- 启动后 60 s 内进程消失（且无窗口出现）→ 判定"启动失败"，回滚所有已执行层 + 提示。

### 9.5 Steam URI / 无监视模式

`SteamLibrary.IsSteamCmdLine(command)` 为真时无可靠进程句柄：

- 走"定时兜底模式"：启动后立即 L0+L1；+60 s 无条件执行 L2；+120 s 执行 L3+L4；
- 不尝试精确探测（避免误判其它 Steam 游戏）。

---

## 10. 度量与埋点（验收靠这个）

### 10.1 采集项

| 读数 | API |
|---|---|
| 私有字节 / 工作集 / 分页内存 | `Process.PrivateMemorySize64` / `WorkingSet64` / `PagedMemorySize64` |
| 句柄数 | `Process.HandleCount` |
| GDI / USER 对象 | `GetGuiResources(hProcess, 0/1)`（user32） |
| 托管堆与碎片 | `GC.GetTotalMemory(true)`、`GC.GetGCMemoryInfo()` 的 `TotalCommittedBytes`/`FragmentedBytes`/`PinnedObjectsCount` |
| KSP 侧 | 目标进程 `TotalProcessorTime`、`WorkingSet64`（只读，用于判断时机） |

### 10.2 采集点

启动前 → T3 后 → T4 后 → T5 后 → 恢复完成后，各一组；落盘 `%LocalAppData%\CKAN\ckan-ui\metrics.jsonl`（每行一次启动：

```json
{"ts":"2026-10-27T21:04:11Z","ver":"1.0.0","tier":"balanced","stage":"T5",
 "privateMB":92.4,"wsMB":41.0,"heapMB":18.7,"fragMB":0.9,"handles":412,"gdi":31,"user":88,
 "kspCpuPct":18,"elapsedMs":31200}
```

### 10.3 验收阈值（先立目标，SP-36/SP-37 后校准）

| 指标 | 均衡档目标 | 激进档目标 |
|---|---|---|
| 私有字节下降 | **≥40%** | ≥40% |
| 托管堆下降 | **≥60%** | ≥60% |
| 工作集（trim 后） | 不要求 | **≤80 MB** |
| 句柄下降 | ≥20% | ≥20% |
| 恢复耗时 | **≤3 s**（含进度） | ≤5 s |
| 恢复后可交互 | ≤1 s | ≤1.5 s |
| 对 KSP 装载的影响 | **不可测出**（±3%） | ±5% |

---

## 11. WBS（110h）

| ID | 任务 | 工时 | 依赖 | 产出 | 验收 |
|---|---|---|---|---|---|
| RLS-01 | 资源读数采集 + `metrics.jsonl` + 五阶段埋点 | 8 | ARC-04 | 度量层 | 五阶段各一行，字段齐全 |
| RLS-02 | 启动监视器（进程发现、`MainWindowHandle`、CPU 回落判定、HasExited、Steam 兜底） | 10 | RLS-01 | `IGameLaunchMonitor` | 6 种启动形态全部正确判定 |
| RLS-03 | 分层执行器（L0–L4 编排、取消、逐层失败隔离、超时） | 16 | RLS-02 | `IResourceGovernor` | 每层可单独执行/跳过的单测 |
| RLS-04 | Core 数据释放与重建（DI 子作用域 + `RegistryManager.DisposeAll`） | 14 | RLS-03, INT-05 | 作用域管理 | 释放后堆可测下降；重建 ≤3 s |
| RLS-05 | GC 策略与 runtimeconfig（Workstation GC、`GCConserveMemory`、LOH 压缩） | 6 | RLS-03 | 配置 + 封装 | 三项配置生效可验证 |
| RLS-06 | 恢复路径（懒重载、骨架、进度、精简模式、失败出口） | 10 | RLS-04 | 恢复流程 | 恢复 ≤3 s 且无空白列表 |
| RLS-07 | 三档设置 + 启动后行为 + 持久化 | 6 | RLS-03 | 设置项 | 三档行为可复现 |
| RLS-08 | IO/CPU 让路（优先级、后台 IO、前台检测、KSP CPU 探测） | 8 | RLS-02 | 让路策略 | 装载时间差异在 ±3% 内 |
| UI-RLS-01 | 托盘态 + "已为游戏让出 XX MB" 提示 + 恢复进度条 | 10 | UI-02, RLS-06 | UI 组件 | H-12 通过 |
| VM-RLS | `GameLaunchViewModel`（阶段、进度、句柄、错误） | 6 | UI-RLS-01 | VM | headless 可测 |
| TST-RLS | 单测 + 集成（假进程/假监视器）+ H-12 | 12 | 上述全部 | 自动化用例 | [07 篇](07-testing.md) §14 全绿 |
| DOC-RLS | 用户文档（"为什么游戏跑起来后 CKAN 变小了"）+ 排障 | 4 | — | 文档 | 常见疑问 5 条 |
| **合计** | | **110h** | | | |

**可整段独立交付**：RLS-01/02/03 + RLS-05 + RLS-07 = **46h ≈ 1.2 周**，产出"均衡档能用、量化可见"，不含 Core 数据释放（RLS-04）与恢复路径（RLS-06）。

---

## 12. 测试（T-RLS-01..10 + 手工组 H-12）

| ID | 类型 | 内容 |
|---|---|---|
| T-RLS-01 | 单测 | 分层执行器：每层独立成功/抛异常/超时 → 后续层按规则继续或中止 |
| T-RLS-02 | 单测 | 时机判定：假监视器注入 6 种形态（正常/Steam/秒退/无窗口/CWD 变更/双开） |
| T-RLS-03 | 单测 | 阈值：私有字节/堆/句柄下降比例达标才判"有效"，否则记预警 |
| T-RLS-04 | 集成 | 假 Core 作用域：释放后引用确实断开（弱引用可被回收） |
| T-RLS-05 | 集成 | 恢复路径：释放 → 触发恢复 → 3 s 内列表可用、无空白帧 |
| T-RLS-06 | 集成 | 精简模式：游戏运行中恢复只读列表正确、安装按钮禁用 |
| T-RLS-07 | 集成 | 失败回滚：T4 抛异常 → 已执行的 L0/L1 可继续、UI 不崩 |
| T-RLS-08 | 集成 | 三档行为差异：同样场景下各档执行的层集合与读数变化 |
| T-RLS-09 | 集成 | `metrics.jsonl` 追加正确、不丢字段、不无限增长（>1 万行轮转） |
| T-RLS-10 | 安全 | 磁盘回收只提示不删除（构造过期缓存 → 弹提示 → 不删则零变化） |

**手工组 H-12**（真机，KSP 1.12 + 100+ mod 实例）：

1. 记录启动前读数 → 启动游戏 → 观察 KSP 装载是否变慢（秒表 + 对照两次）；
2. 游戏进主界面后回来点 UI：计时恢复到可用；
3. 检查 `metrics.jsonl` 五阶段读数，人工核对"已让出 XX MB"提示与实际一致；
4. 切换三档各做一次，比对读数；
5. 游戏运行中尝试安装 mod（应为精简模式拒绝并给理由）；
6. 关闭游戏 → UI 自动恢复全量 → 再装一个 mod 验证功能完好。

---

## 13. 风险

| ID | 风险 | 概率 | 影响 | 缓解 |
|---|---|---|---|---|
| **R-58** | `EmptyWorkingSet` 后切回 UI 大量 page fault，反而更卡 | 中 | 中 | 激进档才做；切回时先显示骨架 + 预热；`metrics.jsonl` 记录恢复耗时 |
| **R-59** | 释放仓库数据后重建需重读 37.9 MB，用户切回等待 1–3 s | 高 | 中 | 恢复带进度；均衡档默认"游戏运行期保持精简"避免反复横跳；SP-38 量化 |
| **R-60** | 释放动作与 KSP 装载抢 IO/CPU，拖慢游戏启动 | 中 | **高** | 时机延后到主窗口 + CPU 回落；后台 IO 线程优先级；±3% 门禁 |
| **R-61** | L3 的 LOH 压缩造成瞬时内存翻倍，32 位/低内存机器 OOM | 低 | 高 | 前置检查可用物理内存 >2 GB 才压缩；否则跳过；只用 `compacting` 的第二遍 |
| **R-62** | 与 `onExit`/playTime/监视器竞态：重复恢复或漏恢复 | 中 | 中 | 单一时序状态机 + 幂等恢复；`onExit` 与监视器共用一个 `TaskCompletionSource` |
| **R-63** | `PlayGame` 的 `Directory.SetCurrentDirectory(GameDir)`（`Core/GameInstance.cs:370`）改变全局 CWD，导致我们之后的相对路径错乱 | **高** | 高 | 我们自己**全程只用绝对路径**；启动后立刻把 CWD 恢复为我们自己的目录；`metrics.jsonl`/日志路径一律绝对化 |
| **R-64** | 用户误以为"释放的内存会被游戏用到"，预期落空 | 中 | 低 | UI 文案只说"已让出 XX MB 给系统"，不承诺给 KSP；文档解释提交内存 vs 工作集 |

---

## 14. SPIKE（SP-36..SP-40）

| ID | 问题 | 做法 | 通过标准 | 不过时的退路 |
|---|---|---|---|---|
| **SP-36** | 仓库元数据对象图**真实占用**（决定 E6 的收益量级） | 用现成的 37.9 MB 缓存 JSON，在独立小程序里反序列化成 `Dictionary<string, AvailableModule>`（或等价的轻量模型）测堆 | 对象图 ≥150 MB 且 L2 后堆下降 ≥60% | 若只有几十 MB → **降级为只做 L0+L1**（收益不足 30h 投入） |
| **SP-37** | `EmptyWorkingSet` 后切回的实际卡顿 | 三档各跑 5 次，记录恢复耗时分布 | 激进档 P95 ≤5 s | 关闭 L4，只保留 L0–L3 |
| **SP-38** | Avalonia 的 GPU 位图能否通过"清视觉树"真正释放 | 用 3,000 行列表 + 缩略图，前后对比 GPU 进程内存 | 可测下降 | 降级：只释放显式创建的位图对象 |
| **SP-39** | KSP（Unity）能否被 `MainWindowHandle` 稳定探测 | 实测 5 次启动，记录首次非零时刻 | 100% 在 60 s 内探测到 | 用"CPU 时间增长 + 窗口标题匹配"兜底 |
| **SP-40** | LOH 压缩在 2–4 GB 堆上的耗时与瞬时峰值 | 构造大堆后压缩，记录耗时与 `TotalCommittedBytes` 峰值 | 耗时 ≤5 s、峰值增幅 ≤15% | 只做第一遍 `GC.Collect`（不压 LOH） |

**SP-36 是 E6 的 Go/No-Go**：若"大量资源"其实不在仓库数据上，整个 L2/RLS-04 的 14h 就该砍掉，改为在 L0/L1 上做到极致。

---

## 15. 决策（2026-10-27 已拍板）

> **决策**：Q1 = **均衡档（L0–L3）**（激进档需用户自己开）；Q2 = **留在托盘**；Q3 = **外部启动的 KSP 不释放**（无可靠时机、易误判）；Q4 = **每次启动后最多提示一次**（可"不再提示"）。工时不变（110h）。

| ID | 问题 | 建议 |
|---|---|---|
| Q1 | 默认档位 | **均衡**（L0–L3）；激进档需用户在设置里自己开 |
| Q2 | 启动后默认行为 | **留在托盘**（可选只最小化 / 关闭 UI） |
| Q3 | 是否也在"检测到外部启动的 KSP"时释放 | 默认**否**（无可靠时机、易误判） |
| Q4 | 磁盘回收提示的频率 | **每次启动后最多一次**，可"不再提示" |

---

## 16. 修订记录

- **v1.3（L0/L1 落地后）**：§3.1 记录 L0/L1 的生产参与方（字形缓存与背景位图、三页行投影、缺译诊断表）与两条实现约束；L4 仍返回 `Unsupported`。

- **v1.2（E8 落盘后）**：本文 110h / M12（W49–W51）不变；全量口径随 E8 变为 **2196h / 60 周**（[20 篇](20-appearance-personalization.md) 206h / M14 = W56–W60）。E8 与本篇的交点：**外观里的背景图与模糊属于"视图侧缓存"**，在 T3 阶段（L0/L1）一并释放，但需在恢复时按用户选择重建（20 篇 §14 的 8MB/2 张预算即为此）。

- **v1.1（E7 落盘后）**：本文的 110h / M12（W49–W51）不变，但全量口径已随 E7 变为 **1990h / 55 周**（[19 篇](19-windows-install-flow.md) 净增 144h，M13 = W52–W55）。本文 §10 的 `metrics.jsonl` 与诊断包（14 篇）共用同一落位与脱敏规则。

| 日期 | 版本 | 变更 |
|---|---|---|
| 2026-10-27 | v1.0 | 首版。依据用户指令「启动游戏后自动释放大量资源」；实测 `Core/GameInstance.cs:360-403` 的 `PlayGame`（仅 `onExit` 回调、`UseShellExecute=true`、`:370` 改全局 CWD）、`Core/RepositoryDataManager.cs:18/42-67`（内存缓存、无 `Clear()`）、`Core/Registry/RegistryManager.cs:116/139/295`；确立 L0–L4 分层、T0–T5 时机、三档、110h / M12 |
