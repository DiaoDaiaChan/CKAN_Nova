# 性能基线（2026-11-01，优化前）

> 本文是「优化前」的取证快照，供后续按同一口径复测对比。
> 复测命令见文末，三个探针各自独立进程运行，互不污染。

## 1. 功能基线（正确性不许回退）

| 项 | 值 |
| --- | --- |
| 构建 | `dotnet build ckan-ui.sln -c Release` → 0 警告 0 错误 |
| 全量测试 | 3203 例：3202 通过 / 0 失败 / 1 显式 Ignore（Services 符号链接用例） |
| 分项 | App 239、Architecture 27、Core 3、Localization 76、ModText 19、Services 1211（1 跳过）、Sfs 93、ViewModels 1535 |

## 2. PERF-01 时间门禁（`perf-probe`，无头 + `UseHeadlessDrawing=true`）

| 指标 | 门禁 | 实测 |
| --- | --- | --- |
| 冷启动到首帧 | ≤ 1500ms | 442.3ms |
| 冷启动到可交互（3,000 模组） | ≤ 3000ms | 1291.8ms |
| 3,000 模组滚动 p50 | ≤ 8ms | 2.08ms |
| 3,000 模组滚动 p99 | ≤ 16.7ms | 5.27ms |
| 后台负载滚动 p99 | ≤ 33ms | 2.35ms |

## 3. 内存与空转画像（`perf-probe --mem`，无头进程）

| 观测点 | 托管堆 | 进程工作集 | 线程数 |
| --- | --- | --- | --- |
| 初始化后 | 0.1 MB | 26.3 MB | 8 |
| 3,000 模组首屏后 | 56.5 MB | 159.9 MB | 9 |

- 滚动 300 帧累计分配 **19,751 KB → 67,418 B/帧**
- 空转 CPU（调度档，无光栅化）：19.25% 单核，帧成本 0.011ms
- 空转 CPU（光栅档，每帧 Skia 渲染 1280×800）：14.00% 单核，帧成本 0.306ms
- 进程峰值工作集：171.8 MB

## 4. 真机驻留与空转（历史数据，已作废）

> **这一节的数字全部作废，不要引用。** 它是用「启动后静置 8 秒」的口径测的，而启动瞬态实测能持续 20 秒以上
> （进程内打点：单核 CPU 从 400%+ 一路衰减，约 20 秒后才稳定在 9–19%）。同一份代码在瞬态窗口里能测出
> 5 倍差异，所以 71.15% 这个"空转 CPU"测的其实是加载，不是空转。
> 稳态口径与对照数见 §6.2 与 §7。

| 指标 | 旧口径实测（无效样本，仅存档） |
| --- | --- |
| 空转 CPU | 71.15% 单核 |
| 工作集 | 均 322.9 MB / 峰 328.1 MB |
| 私有字节 | 均 276.5 MB / 峰 280.4 MB |
| 线程 | 峰 114 |
| 句柄 | 峰 1036 |

## 5. 复测命令

```bash
# 功能门禁
dotnet build ckan-ui.sln -c Release -v q --nologo
TEMP="$PWD/artifacts/tmp/temp" TMP="$PWD/artifacts/tmp/temp" \
  dotnet tests/CkanUI.TestRunner/bin/Release/net10.0/CkanUI.TestRunner.dll --config Release

# 时间门禁 + 内存画像
dotnet build tools/perf-probe/CkanUI.Tools.PerfProbe.csproj -c Release -v q --nologo
TEMP="$PWD/artifacts/tmp/temp" ./tools/perf-probe/bin/Release/net10.0/perf-probe.exe
TEMP="$PWD/artifacts/tmp/temp" ./tools/perf-probe/bin/Release/net10.0/perf-probe.exe --mem

# 真机驻留/空转（会自己起一个 Release 实例并杀掉，别在正在用的实例上跑）
# 默认 -WarmupSeconds 45 -IdleSeconds 20：必须等启动瞬态过去再开窗采样，否则测到的是加载
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/perf-probe/Measure-LiveProcess.ps1 \
  -Exe 'C:/path/to/CkanUI.exe' -Out artifacts/perf/live-steady.json
```

> 注意 1：真机采样前先 `taskkill /F /IM CkanUI.exe`，否则构建会因 `bin/Release` 被占用报 MSB3027。
> 注意 2：`-Exe` 用正斜杠。在 Git Bash 里传反斜杠会被吃掉，路径变成 `C:Users43701...`。
> 注意 3：脚本会把窗口拉到前台并停住光标（`-KeepForeground`，默认开）；采样结果里的
> `foregroundDuringSample` / `focusLostSamples` / `cursorParked` 是口径自证字段，
> `focusLost` 很大说明同机有别的程序在抢焦点，此时 CPU 与工作集仍可读，但别拿它当独占环境。

## 6. 优化后复测（2026-11-01，同机同 worktree A/B）

A/B 口径：另建 `git worktree` 停在基线提交，先测基线，再把本轮改动的 5 个源文件覆盖进去重建复测——
同机器、同 exe 路径、同用户配置，唯一的变量就是这一轮的代码。测三遍取中位数。

| 指标 | 优化前（基线提交） | 优化后 | 变化 |
| --- | --- | --- | --- |
| 空转 CPU（光栅档，每帧 Skia 渲染 1280×800） | 11.64 / 15.43 / 16.27%（中位 **15.43%**） | 7.01 / 7.81 / 13.90%（中位 **7.81%**） | **-49%** |
| 单帧光栅成本 | 0.352 / 0.327 / 0.344 ms（中位 **0.344**） | 0.287 / 0.263 / 0.275 ms（中位 **0.275**） | **-20%** |
| 单帧调度成本（无光栅） | 0.019 / 0.020 / 0.022 ms（中位 **0.020**） | 0.018 / 0.017 / 0.020 ms（中位 **0.018**） | **-10%** |
| 滚动 300 帧分配 | 67,418 B/帧 | 67,418 B/帧 | 不变（本轮未碰滚动路径） |
| 3,000 模组首屏后工作集 | 161.2–162.1 MB | 161.4–162.2 MB | 不变（星轨离屏层 ≈4 MB，被缓存收益抵消） |
| 功能门禁 | 3,203 例全绿（1 例显式 Skip） | 3,203 例全绿（1 例显式 Skip） | 不回退 |

### 6.2 真机稳态 A/B（45 秒静置 + 20 秒采样）

口径：`Measure-LiveProcess.ps1 -WarmupSeconds 45 -IdleSeconds 20`，同一 worktree、同一 exe 路径、
同一用户配置，先测停在基线提交的构建，再把本轮文件覆盖进去重建复测。每组多次采样，取范围与中位。

| 指标 | 基线提交 | 优化后 | 变化 |
| --- | --- | --- | --- |
| 空转 CPU（单核） | 24.61 / 32.74%（中位 **28.68%**） | 14.89 / 16.45 / 21.01%（中位 **16.45%**） | **-43%** |
| 工作集 均 / 峰 | 981.3 / 981.8 MB 与 978.1 / 978.2 MB | 476.0 / 483.3、445.5 / 449.5、481.3 / 482.7 MB（中位均 **476.0 MB**） | **-51%（约 -500 MB）** |
| 私有字节 均 | 932.6 / 925.8 MB | 417.5 / 397.0 / 430.3 MB | **-55%** |
| 线程 峰 / 句柄 峰 | 111 / 1294 | 111 / 1329 | 持平 |

> 工作集那一行的成立前提是「空闲回收真的触发过」。回收策略经历过一次回退：第一版让首个 8 秒 tick 就回收，
> 结果在启动加载期（工作集才 300–400 MB）把额度用掉，等加载完成涨到 840 MB 时又被冷却挡住，
> 真机停在 840 MB —— 等于白回收一次。当时把 `MinAttachSeconds` 拉到 24 秒 + `MinInterval` 45 秒来兜住，
> 代价是首屏那 1 GB 峰值要挂到 t+45~55s 才被压下去；后来改成 12 秒 / 20 秒（见 §6.5），首次回收提前到 t+12~20s。

### 6.1 主因与验证方式（这轮的关键发现）

空转 CPU 的大头不是"画得慢"，而是**该降频的时候没降频**：

1. `DynamicRefreshRateController` 把每一次 `PointerMoved` 都当用户活动。实测采样日志里 8 秒内出现
   **2040 条 `activity via PointerMoved`** —— 光标停在原地不动，Avalonia 的悬停重算仍在持续发事件，
   于是 `IsIdle` 永远进不去，`IdleTargetFps = 10` 形同虚设，星空一直按 33 ms 满帧重画。
   修法：`OnUserInteraction` 里按顶层级坐标做位移判定，X/Y 位移都 < 0.5 DIP 就不算活动。
   修完同样采样窗口的活动事件降到 **4 条**，`idle=True interval=100` 稳定出现。
2. 星轨（两族 604 条 `DrawLine`）每帧全量重画，而角速度只有约 0.014 rad/s —— 一帧位移 0.23 px。
   现在把它们烘进一张 `RenderTargetBitmap`，缓存键 = 尺寸 × 缩放 × 主题强度 × 调色板 × 量化到 100 ms 的模型时间；
   活跃时每 3 帧重建一次，空闲时每帧重建，位移误差始终 ≤1.1 px。

两条叠起来：空闲时星空的帧数降到 1/3，每帧光栅成本再降 20%，空转渲染开销合计约 **3.8 倍**的下降。
诊断口径（`Render` 单帧耗时）不能用来判断这件事——`DrawingContext` 是记录式的，实测打印出来永远是 0.00 ms，
真实光栅在别的线程，只有进程级 CPU 和"帧数 × 单帧成本"这两个尺子能看出差别。

### 6.3 另两处空闲期的白刷

3. 指针事件也照旧立刻重画背景：`StarfieldBackground.TrackPointer` 每收到一次 `PointerMoved` 就
   `InvalidateVisual()`，而 Avalonia 的悬停重算在光标静止时仍持续发事件（实测 8 秒 2040 次）。
   现在按顶层级坐标做位移判定（X/Y 都 < 0.5 DIP 就不算移动），并且动效开启时指针只改模型目标值，
   画面交给下一次 tick 统一推进——不再为本来就要重画的帧多排一次重绘。
4. 窗口被激活被当成"用户活动"：多任务机器上别的进程会不停抢焦点（实测 20 秒采样里丢了 71–74 次），
   每次都把空闲状态重置，空闲降频与空闲回收永远进不去。现在 `OnWindowActivated` 什么都不做——
   真正代表用户输入的指针/键盘事件自己会通知。

### 6.4 窗口化采样后的口径修正（2026-11-01，权威数字）

§6.2 那种"静置 45 秒 + 一个平均数"仍然不可靠：把 60 秒窗口按 5 秒切段后，前 1-2 段经常出现
14%-50% 的单窗口峰值，后面掉到 1%-9%。那一段是 `IdleMemoryReclaimer` 的首次回收
（两次 compacting Gen2 + finalizer），基线进程也有同样的簇发（自身 GC），不是谁的回归。
只给平均值，等于把"一次性 GC 突发"和"稳态空转"混成一个数——同代码能测出 5% 和 45%。

`Measure-LiveProcess.ps1` 已加 `-WindowSeconds`（默认 5），输出 `cpuWindowOneCorePercent` 数组与
`cpuLastWindowPercent`；判断"空转到底多少"看尾部几段，不要看 `idleCpuOneCorePercent`。

| 组（同一 worktree、同机、同一 exe 启动方式，看尾部窗口） | 稳态空转 | 工作集 |
| --- | --- | --- |
| 基线 `c32428f`（3 次） | 16.3-22.8% 单核（3 次尾部高度一致） | 983-1009 MB |
| 当前（含 514aab2，5 次） | 0.9-9.4% 单核 | 440-514 MB |

即空转 CPU 约 **-70%**（16-22% → 1-9%），工作集约 **-50%**。首次回收的突发保留下来：那是用一次性
几秒 CPU 换回约 500 MB 常驻内存，且 `MinInterval` 45 秒 + 128 MB 增长门限决定它一个会话基本只发生一次。

采样器自身也改了两处，避免再产出不可比的数：

- **默认不碰用户桌面**：新增 `-ForceForeground` 才抢前台/停光标；默认只读 `TotalProcessorTime`，
  不调用 `SetForegroundWindow`/`SetCursorPos`（抢焦点会打断正在用机器的人，而且焦点漂移本身会改量级）。
  结果里的 `cursorParked`/`keepForeground` 也随之变成"是否真的动过桌面"。
- 启动时轮询 `MainWindowHandle` 最多 12 秒，不再 2 秒取一次、取不到就把整套前台处理跳过（旧版因此常输出 `foreground=False`）。

### 6.5 首次回收提前到 12 秒（2026-11-01）

§7 的策略原本是 `MinAttachSeconds = 24` + `MinInterval = 45s`：首屏那约 1 GB 的峰值要一直挂到
t+45~55s 才被压回 440-514 MB。对 8 GB 机器来说，前 50 秒就一直在跟页面文件较劲。改成 `12s / 20s` 后：

| 组（非侵入采样，主仓 worktree，同机） | 采样协议 | 空转 CPU（单核） | 工作集 均/峰 | 私有 均 |
| --- | --- | --- | --- | --- |
| 改前（24s / 45s） | 预热 45s + 采样 20s + 5s 窗口 | 0.9-9.4%（尾部窗口） | 440-514 / 514 MB | 397-430 MB |
| 改后（12s / 20s） | 预热 8s + 采样 26s + 4s 窗口 | 51.97%（**采样全落在启动瞬态里**） | 318.4 / 341.0 MB | 305.1 MB |
| 改后（12s / 20s） | 预热 30s + 采样 20s + 4s 窗口 | **0.55%**（尾部窗口 1.52/0.76/0/0/0.5） | **256.5 / 266.9 MB** | 207.5 MB |

结论：`t ≈ 30s` 时工作集已经落在 256 MB（改前同一时点仍在 900 MB 量级），CPU 回到 0.55% 单核。

> 顺带记一条采样纪律：预热 8 秒那行不是回归，是**口径错误**——启动瞬态在这台机器上超过 20 秒
> （CPU 从 400%+ 一路衰减），预热不足时采到的是加载，不是空转。真机稳态一律预热 ≥30 秒。
> 另：默认非侵入模式也会看到 `foreground=True`，因为应用启动时自己抢了前台；要看后台空转就等它被别处夺焦。

### 6.6 首屏那 1.3 秒到底花在哪（2026-11-01，探针分段计时）

给 `perf-probe` 加了分段计时（`[InteractivePhases]`）、`--rows N` 行数扫描和控件树普查（`[VisualTree]`），
才看清「冷启动到可交互 1.27s」的构成——此前一直以为是 3,000 条模组的加载/投影成本：

| 段 | 耗时（3,000 行） |
| --- | --- |
| headless-init | 0.1 ms |
| mock-data-3000 | 7.6 ms |
| vm-reload（快照→行投影/过滤/排序） | 29.0 ms |
| view-new | 150.9 ms |
| window-new | 63.9 ms |
| **window-show（套模板 + 样式解析 + 首轮布局）** | **920-1244 ms** |
| measure-arrange | 65.9 ms |
| first-render | 0.0 ms |

对照实验（同一个 exe，只改行数）：

| 行数 | window-show |
| --- | --- |
| 3,000 | 920-1244 ms |
| 600 | 1072 ms |
| 60 | 883-1009 ms |

**行数从 3,000 降到 60，首屏只快了 0-10%** —— 虚拟化是好的（`DataGridCell` 恒为 180 个），
那 0.9-1.2 秒买的是一个**固定 2,428 个控件**的树：`Rectangle x728 / Grid x378 / Border x347 / TextBlock x216 / ContentPresenter x201 / DataGridCell x180`。
第二个同构窗口再 Show 一次仍要 858 ms，所以也不是 JIT 一次性成本，而是**每个控件实例都要走一遍样式解析**（约 0.35 ms/控件）。

728 个 `Rectangle` 的来源已定位（`[VisualTree] rect-chain`）：DataGrid 默认单元格/表头模板为每个 cell 生成
`FocusVisualPrimary`/`FocusVisualSecondary`/`VerticalSeparator` 三个矩形装饰。

下一步（未做，需动 `Views/ModsPageView.axaml` 与其 DataGrid 模板，当前正被并行改动占用）：
给 `DataGridCell`/`DataGridColumnHeader` 换一套精简模板（去掉 focus 装饰矩形、合并分隔线），
预期把 show 段从 ~0.9s 压到 0.4-0.5s；这一步会改焦点框视觉，必须先与设计口径对齐，不能只看数字。

## 7. 空闲内存回收（`IdleMemoryReclaimer`）

诊断依据：满载 3,000 模组会话的托管活对象只有约 200 MB（`dotnet-gcdump` 量级），但托管堆占 645 MB、
工作集逼近 1.0 GB，且静置 90 秒不回落。也就是说 1.0 GB 里大头是**未回收的垃圾 + 提交了没还的页**，
不是活对象膨胀。手工做一次压缩式第 2 代回收（`GC.Collect(2, Aggressive, blocking: true, compacting: true)`
+ `WaitForPendingFinalizers` 再来一次）实测：

| 时点 | 工作集 | 私有字节 | 托管堆 |
| --- | --- | --- | --- |
| 回收前 | 995 MB | 946 MB | 711 MB |
| 回收后 | **433 MB** | 382 MB | 199 MB |
| 之后 25 秒 | 446 MB | — | — |

所以回收器做的事很简单：**在用户确实不动的时候，替他把这部分还给操作系统**。

| 参数 | 值 | 为什么是这个值 |
| --- | --- | --- |
| `PollSeconds` | 8 | 轮询节拍。不订阅 `IdleStateChanged`——空闲状态会被抢焦点打断到毫秒级抖动，每次抖动都重排定时器就永远等不到静置 |
| `MinAttachSeconds` | 12 | 启动加载期一律跳过。早期 8 秒回收被撑回 840 MB 是冷却太长（当时 120 秒）造成的，不是等得不够久；冷却是 20 秒后 12 秒足够 |
| `GrowthThresholdBytes` | 128 MB | 首次回收后，只有工作集比上次回收后长了 128 MB 才再收，避免无意义 GC |
| `MinInterval` | 20 s | 两次回收的最小间隔，防止交互边缘反复停顿；配合 128 MB 增长门限，一个会话最多多收一两次 |

安全性：只在 `DynamicRefreshRateController.IsIdle` 为真时执行，两次回收是阻塞式全堆压缩
（满载会话上约 100–300 ms），用户此刻没有任何操作，感知不到；`Detach`（窗口关闭）后定时器立即停止。
接线点在 `DynamicRefreshRateController.Attach/Detach` 里——空闲态由该控制器产出，回收器只消费它。

> 度量口径提醒：工作集是**平台态**指标，回收后才落到底；读 `Process.WorkingSet64` 就够，
> 不要拿 `GC.GetTotalMemory` 判断——它看不见提交未释放的页，那才是这 500 MB 的主体。


## 8. 真机启动归因（进程内打点）

`MainWindowHandle` 轮询测出的 2.4–2.7 s 是**探测滞后**，不是应用时间：原生窗口早已可见，脚本还在等句柄。

权威口径只能来自进程内部。`src/CkanUI.App/App.axaml.cs` 里加了 `_startupWatch` + `Mark(phase)` 打点（`StartAsync` 另有三点），沿路输出 `[启动计时] {phase}={累计 ms} (+{本段 ms})`。
日志默认走 log4net，而 log4net 的落盘目录跟实例走、还会被并行跑的测试进程污染——所以打点同时支持环境变量直写文件：

```bash
CKANUI_STARTUP_TRACE=C:/Users/43701/Documents/github/ckan-ui/artifacts/perf/startup-trace.log \
  timeout 10 ./src/CkanUI.App/bin/Release/net10.0/CkanUI.exe
```

（`artifacts/perf/startup-phases.ps1` 是等价的 PowerShell 采样脚本，会被 Start-Process 等待卡住，直接 timeout 起进程更稳。）

Release / 1280×800 / 三次一致，单位 ms：

| 阶段 | 累计 | 本段 | 备注 |
| --- | --- | --- | --- |
| app-xaml | 184–194 | 184–194 | `App.axaml` 反序列化，含 Tokens/FluentStyles 引用 |
| di-build | 227–230 | +12–14 | 容器注册 |
| log-pipeline | 239–242 | +11–14 | |
| localization | 299–307 | +59–68 | 建已本地化键表 |
| theme → radius | 302–325 | +11–17 | 六组设置宿主，各自只 2–4 ms |
| instance-service-resolved | 308–318 | +0–1 | **140 ms 已在上一段花掉**，见下 |
| shell-vm | 447–468 | +132–143 | 外壳 VM 构造（要实例列表） |
| main-window-new | 755–783 | +308–315 | `new MainWindow()` 反序列化整个 XAML |
| backdrop+background → tray | 786–819 | +32–36 | |
| assembly-done | 787–820 | +1 | 托管装配结束 |
| window-opened | 1065–1103 | +257–276 | Show()：建原生窗口 + 首次布局 |
| first-layout | 1124–1162 | +59–68 | |
| first-paint | 1527–1550 | +305–427 | 第一次真正光栅化 |
| **总计** | **1417–1563** | | |

首帧前四大块：`main-window-new` ~320 / `first-paint` ~380 / `window-opened` ~265 / `app-xaml` ~188。

**已被否定的改法（留证，别重复走）**：把 `IInstanceService` 解析推迟到首帧之后。逐宿主插桩证明 theme/density/fonts 各只 2–4 ms、accent 自身 apply 只 1–2 ms，那 140 ms 全在 `_services.GetService<IInstanceService>()`（初始化 Core 的实例管理：读 config.json + 探测实例目录）。改成首帧后再挂 `AccentHost.AttachInstances`/`BackgroundHost.AttachInstances` 之后，**140 ms 原样搬到 `shell-vm`**（+132/+143 ms）——外壳要实例列表才能建，一秒都没省，还让侧栏实例名晚出现。已回退。

剩余空间都在并行改动中的文件上，等那一路落地再动：

| 目标 | 预计 | 需要改的文件 |
| --- | --- | --- |
| main-window-new 320 ms | 诊断 HUD/覆盖层子树改成延迟内容 | `src/CkanUI.App/MainWindow.axaml` |
| app-xaml 188 ms | 拆资源字典、按需加载 | `src/CkanUI.App/Resources/Tokens.axaml`:1、`FluentStyles.axaml`:1 |
| localization 60 ms | 惰性建键表 | `src/CkanUI.App/Localization/LocalizationHost.cs`:1 |
## 9. 当前稳态数字与已排除的优化方向（2026-11-02）

### 9.1 稳态实测（Release exe，默认非侵入采样）

`tools/perf-probe/Measure-LiveProcess.ps1 -WarmupSeconds 45 -IdleSeconds 20 -WindowSeconds 5`，两次：

| 采样 | 空转 CPU（单核） | 工作集 均/峰 | Private 均 | 线程 | 句柄 | 尾部 5 秒窗口 |
| --- | --- | --- | --- | --- | --- | --- |
| run1 | 1.09% | 282.1 / 282.4 MB | 212.7 MB | 111 | 1017 | 0.31 / 0.62 / 1.26 / 1.65% |
| run2 | 1.62% | 320.7 / 375.0 MB | 258.3 MB | 111 | 1017 | 5.30 / 0.94 / 0.31 / 0% |

对照优化前的基线（同口径 §6.4）：空转 16.3–22.8% 单核、工作集 983–1009 MB。**空转约 -92%，驻留约 -68%**。
早期 §4 的 71.15%、§6 的 112.51/164.88/89.45 全部是「启动瞬态当空转」的口径错误，继续作废。

### 9.2 稳态内存构成（ClrMD 扫堆）

`artifacts/perf/scan-heap.ps1` 在 50 秒静置后扫同一进程：ws 239.5 MB / private 215.8 MB，**托管堆只有 35.1 MB / 346,911 个对象**。

| 类型 | 大小 | 数量 | 解读 |
| --- | --- | --- | --- |
| Free（GC 空闲段） | 6.28 MB | 316 | 已回收但未还给 OS 的段 |
| `byte[]` | 3.15 MB | 1761 | 图像/序列化缓冲 |
| `Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExpression` | 2.19 MB | 16,879 | 每个 `DynamicResource` 绑定一份 |
| `Avalonia.Styling.StyleInstance` | 1.97 MB | 18,468 | 2,428 个控件 ≈ 7.6 个样式实例/控件 |
| `AvaloniaPropertyDictionary.Entry[]` | 1.74 MB | 18,617 | 样式 setter 落地表 |
| `string` | 1.59 MB | 26,315 | 文案与标识符 |
| `LogEvent[]` | 0.15 MB | 1 | 容量 20000 的日志环，当前是空的 |

结论：**稳态驻留里约 200 MB 是非托管**（Skia 光栅缓存、合成器 surface、JIT 代码、映射的程序集），托管侧 35 MB 且大头是 Avalonia 样式系统本身。托管堆已经没有可观的抓手。

### 9.3 已排除的优化方向（留证，别重复走）

| 候选 | 实测/核实结果 | 结论 |
| --- | --- | --- |
| `Theming/BackgroundHost.cs` 的 `TryReleaseCachedBitmap` 返回 `false` 是泄漏 | 位图正被 `Image` 或平铺 `ImageBrush` 持有时本就不能 `Dispose`（`:297 IsReferencedBy`） | 正确行为，不动 |
| 日志环每条存两份字符串（`LogStore.cs:212 _keys[_head] = LogSearchKeys.Build(stored)`） | 空闲会话里环是空的（`LogEvent[]` 仅 1 个 0.15 MB）；只有重日志会话（数千行）才 4–5 MB | 收益太小，暂不做 |
| 星空首帧占 300–450 ms 首屏 | `StarfieldBackground.Render` 内分段计时：首帧 trails 15.8 ms / mid 2.7 ms / stars 0.9 ms / 合计 19.4 ms；之后每帧 1.5–2.3 ms。此前 300 ms 是 `first-layout → first-paint` 的调度队列深度差，机器上有并行编译时的噪声 | 星空不是首屏主因 |
| 星轨改成按「线宽档 × alpha 档」攒批（一档一条 `StreamGeometry`，替掉 604 次 `DrawLine`） | 实测重建帧 1.5–1.6 ms → 1.2–1.8 ms，同一噪声带内；光栅成本由像素覆盖决定，与绘制调用次数无关 | 已回退，不留复杂度 |
| 空闲时停掉星轨重建（星轨冻结） | 会让「长曝光」主角在静置时定格 | 属画面变化，不做 |
| 按 `IsOpen` 门控 `PerformanceMonitor` 的 `PropertyChanged` | 每秒约 31 KB 垃圾，且 `PerformanceMonitor_RefreshSnapshot_NotifiesPropertyChanges` 断言了全部属性通知 | 收益不值，不做 |
| mods 页搜索每次按键全量重算投影（`RefreshDerived` 27 项 + `RefreshDetail` 17 项 + `SyncDetails`） | `perf-probe --search` 实测（3000 模组、逐键输入 rendering）：每键同步部分 p50 **0.51–0.56 ms** / max 1.13 ms；防抖后的 `ApplyFilter` p50 **2.78–3.95 ms** / max 33.59 ms | 不在人可感知量级，不改 |
| mods 页行选中整体重算 | `perf-probe --select` 实测（3000 行、20 次跳选）：p50 **0.10–0.11 ms** / p90 0.11–0.12 ms / max 1.4–2.9 ms | 不是热点，不改 |

### 9.4 还剩下的空间（都被并行改动挡着）

| 目标 | 量级 | 卡在哪 |
| --- | --- | --- |
| 真机启动 1.4–1.6 s | 首屏 0.9 s 中约 0.6 s 在 DataGrid 单元格/表头模板与 `window-show` | `src/CkanUI.App/Views/ModsPageView.axaml`、`src/CkanUI.App/MainWindow.axaml` 正被并行改动 |
| main-window-new 320 ms / app-xaml 188 ms | 见 §8 表格 | 同上 + `Resources/Tokens.axaml`、`Resources/FluentStyles.axaml` |
| mods 页搜索/选中路径 | 已用 `perf-probe --search` / `--select` 量化排除（见 §9.3），不再列为空间 | — |

## 10. 真机启动分段计时与实例服务后台预取

### 10.1 打点设施（已有，别再加一套）

`src/CkanUI.App/App.axaml.cs:50` 的 `_startupWatch` + `:57` 的 `TraceStartup(string)` 逐段打点（写 log4net）；**设了环境变量 `CKANUI_STARTUP_TRACE=<路径>` 时**额外把每段 `[启动计时] <phase>=<累计>ms (+<增量>ms)` 追加到该文件。段名：`app-xaml / di-build / log-pipeline / localization / theme / density / fonts / instance-service-resolved / accent / motion / radius / shell-vm / main-window-new / backdrop+background / plugin-tools / tray / assembly-done / start-async-sync-begin / window-opened / first-layout / first-paint`。

采样口径（踩过的坑）：

- 用 node `spawn` 起 `src/CkanUI.App/bin/Release/net10.0/CkanUI.exe`，`cwd` 设为该目录，`stdio` 必须是 `pipe`；`stdio: "ignore"` 时 trace 文件读不到。
- 每次采样前 `taskkill /F /IM CkanUI.exe`，等 1.2–1.5 s 再起（否则 exe 被锁、或新进程被单实例守卫吃掉）；再轮询 trace 出现 `first-paint` 为止。
- 解析数字不要用带转义的正则，直接取 `<key>=` 之后连续的数字。
- 冷盘首跑可到 3.0 s（一次性 I/O），A/B 必须交替多轮取中位。

### 10.2 优化：实例服务在后台线程预取

`IInstanceService` 首次解析 = 读 `config.json` + 探测实例目录，实测 **140–156 ms 纯磁盘 I/O**，而它的消费者（`AccentHost` / 背景宿主）要等语言包、主题、密度、字号几段 UI 线程工作之后才轮到。改为在 `di-build` 之后立即 `Task.Run(() => _services.GetService<IInstanceService>())`，主线程继续往下走，用到时 `instanceWarmup.GetAwaiter().GetResult()` 直接拿成品。

- 容器对单例的并发解析是线程安全的，拿到的仍与原路径同一个实例；首帧前的可见时序完全不变。
- **注意这不等于「整体挪到首帧之后」**：那样这 140 ms 会原样搬进 `ShellViewModel` 构造（外壳要实例列表），一秒都不省。

A/B（隔离 worktree `/c/Users/43701/ckan-ui-dg`，HEAD=10075c5，交替各重建 4 轮取中位）：

| 分段 | 基线（HEAD） | 预取后 | 变化 |
| --- | --- | --- | --- |
| `instance-service-resolved` | 509 ms | 414 ms | −95 ms |
| `main-window-new` | 866 ms | 783 ms | 噪声（同步等待消失后队列提前） |
| `window-opened` | 1466 ms | 1213 ms | −253 ms |
| `first-paint`（总计） | 1401 ms | 1270 ms | **−131 ms（−9.4%）** |

主仓 Release 实测（同机）：`instance-service-resolved` 417–419 ms、`first-paint` 1483–1576 ms。

### 10.3 启动剩余大头

| 分段 | 量级 | 说明 |
| --- | --- | --- |
| `main-window-new` | 780–870 ms | 主窗口构建（含 mods 页视图创建 182–216 ms）；`src/CkanUI.App/MainWindow.axaml` 在并行改动 |
| `window-opened` → `first-layout` | 410–490 ms | 窗口显示后的首轮布局；DataGrid 表头/单元格模板 728 个 `Rectangle`（见 §6.6） |
| `first-paint` 收尾 | 60–80 ms | 首次光栅 |

### 10.4 `main-window-new` 拆开看：窗口构造只占 300 ms，页面视图在布局里

在隔离 worktree 里把 `new MainWindow { DataContext = shell }` 拆成两步打点（`win-split noDc=… withDc=…`，3 次）：

| 步骤 | 耗时 |
| --- | --- |
| `new MainWindow()`（XAML 载入 + 控件树构建） | 300 / 308 / 318 ms |
| `DataContext = shell`（导航到首页、创建 ModsPageView） | +26 / +27 / +25 ms |

即 `Mark("main-window-new")` 的 +300~380 ms 是**窗口自身 XAML + 样式落地**，页面视图创建实际落在 `window-opened` 之后的首轮布局里（`perf-probe --cold` 的 `view-new` 182–216 ms 对应这一段）。要动这两块都得改 `src/CkanUI.App/MainWindow.axaml` / `Views/ModsPageView.axaml`（并行 agent 占用中），本轮不动。


