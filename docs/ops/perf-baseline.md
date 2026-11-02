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

## 11. 空闲期不再用 `RequestAnimationFrame` 驱动采样

### 11.1 为什么它值钱

`PerformanceMonitor.StartSampling()` 从窗口构造起就起了一条自递归 RAF 循环。空闲时它被降到 `IdleTargetFps`（10 Hz）——但**降频不等于免费**：`TopLevel.RequestAnimationFrame` 每次都会真的排一次渲染，而空闲期星空自己的 10 Hz 定时器（`StarfieldBackground.IdleTickIntervalMs=100`）已经在刷画面。也就是说空闲期每秒有约 20 次整窗重绘，其中一半是诊断自己造的，画面上什么都没有多出来。

### 11.2 改法

- `src/CkanUI.App/Diagnostics/PerformanceMonitor.cs` 的 `RequestNextFrame()`：`DynamicRefreshRateController.Instance.IsIdle` 时直接 return，不再排帧；空闲降频专用的 `_frameDelayTimer` / `ScheduleNextFrameAfter` / `OnFrameDelayTick` 一并删除（没有第二个调用者）。
- `OnIdleStateChanged(bool isIdle)`：离开空闲时 `_lastFrameTicks = _frameStopwatch.ElapsedTicks` 复位帧计时后重新 `RequestNextFrame()` —— 不复位的话，恢复后的第一帧会把整段空闲算成一帧（几十秒），状态栏会瞬间显示 0.0 FPS。
- 画面为什么不变：空闲期的每一帧本来就由星空定时器的 `InvalidateVisual()` 驱动，RAF 只是额外的重复渲染。状态栏 FPS 读数保持进入空闲前的值（空闲本来就该是 10 fps 档，颜色档位不变）。

### 11.3 实测

隔离 worktree 用环境变量开关先做定位（Release 真机、40 s 预热、3 个 5 s 窗口、非侵入采样）：

| 组 | 尾窗空转（单核 %） | 备注 |
| --- | --- | --- |
| 空闲 RAF 开（现状） | 9.47 / 11.12 | 两个 5 s 尾窗 |
| 空闲 RAF 关 | 6.82 / 4.83 | 同上 |

在主仓按上面的改法落地后复测（node 采样器：40 s 预热 + 3 个 5 s 窗口，各 2 跑）：

| 组 | 三个窗口（单核 %） | 尾窗 | 工作集 |
| --- | --- | --- | --- |
| HEAD（`_frameDelayTimer` 版） | 4.69/7.50/9.69、5.00/9.38/12.50 | 9.69 / 12.50 | 238 / 234 MB |
| 本次（空闲停 RAF） | 2.81/8.44/5.00、3.44/3.13/5.00 | 5.00 / 5.00 | 228 / 226 MB |

即空闲 CPU 大约再降一半（尾窗 9.7–12.5% → 5.0%），工作集同时低 8–10 MB（少了重复渲染的中间 surface）。

### 11.4 因果验证（别把并行改动算到自己头上）

改完跑全量测试出现 1 例红：`CkanUI.App.Tests.TestDrawerCloseBehavior.TestLeftCompactOpenAndClose`（`Opened: 52, Closed: 52, …`）。**不是本次引入**：把 `PerformanceMonitor.cs` 换回 HEAD 版本重建后再跑，同一用例照旧红（而且那一批里还有 `InspectSplitViewStyles.DumpPaneRootProperties` 等 3 例红），是并行 agent 正在改抽屉/样式时的在途状态。归因靠的是这份 A/B，不是猜。


### 10.4 `main-window-new` 拆开看：窗口构造只占 300 ms，页面视图在布局里

在隔离 worktree 里把 `new MainWindow { DataContext = shell }` 拆成两步打点（`win-split noDc=… withDc=…`，3 次）：

| 步骤 | 耗时 |
| --- | --- |
| `new MainWindow()`（XAML 载入 + 控件树构建） | 300 / 308 / 318 ms |
| `DataContext = shell`（导航到首页、创建 ModsPageView） | +26 / +27 / +25 ms |

即 `Mark("main-window-new")` 的 +300~380 ms 是**窗口自身 XAML + 样式落地**，页面视图创建实际落在 `window-opened` 之后的首轮布局里（`perf-probe --cold` 的 `view-new` 182–216 ms 对应这一段）。要动这两块都得改 `src/CkanUI.App/MainWindow.axaml` / `Views/ModsPageView.axaml`（并行 agent 占用中），本轮不动。

## 12. 启动采样改由 node 承担（脚本化、可重复）

真机启动测量原来靠手写 PowerShell + `Start-Process`：`Get-Counter` 在本机偶尔挂住不返回（一次留下 5 个 pwsh 残留进程），`Start-Process` 起 exe 偶发「操作已被用户取消」，`MainWindowHandle` 轮询还要自己处理。这一段换成仓库内的 node 脚本，只做两件不会挂住的事：拉起 exe、读文件。

```
node tools/perf-probe/Measure-Startup.mjs --runs 3
```

（`tools/perf-probe/Measure-Startup.mjs`，结果落 `artifacts/perf/startup-trace.json`。）

要点：

- 每轮先 `taskkill /F /IM CkanUI.exe` 再等 2 s —— **残留实例会让新进程走单实例转发通道直接退出**，表现是「进程活着但一个 `[启动计时]` 都没有」，这一坑本轮踩过一次；
- 用 `CKANUI_STARTUP_TRACE` 指向 exe 同目录的临时文件，轮询到 `first-paint` 就 `taskkill`，不需要窗口句柄、不抢前台、不动光标（符合「无头调试不抢占窗口」）；
- 结果按阶段写 JSON（`at` 累计 + `delta` 增量），三次中位数直接看。

### 12.1 当前真机启动分段（三次，Release，2026-11-01）

| 阶段 | 中位增量 | 累计 |
| --- | --- | --- |
| 语言包 + 主题 + 密度 + 字号（`localization` 等） | 69 ms | — |
| `instance-service-resolved`（已后台预取，消费者等的是余下部分） | 65 ms | — |
| `main-window-new`（窗口 XAML + 样式落地） | **364 ms** | 737–760 ms |
| `plugin-tools` / `tray` / `assembly-done` | 21 / 16 / 1 ms | 777 ms |
| `window-opened` → `first-layout`（创建首页视图 + 首轮布局） | 74 ms | 1214–1236 ms |
| `first-layout` → `first-paint`（首帧光栅） | 285 ms（另一次采样只有 8 ms，见下） | **1501–1521 ms** |

`first-layout`→`first-paint` 的 8 ms / 285 ms 两种读数说明它是**受机器当前负载影响的排队时间**，不是固定光栅成本（当时机器上有并行编译）；拿它当优化目标会白忙。稳定的、可动的仍是 `main-window-new` 那 364 ms，落在 `src/CkanUI.App/MainWindow.axaml` + `Resources/FluentStyles.axaml`（并行 agent 占用中）。

### 12.2 试过但没收益：把托盘与插件工具项挪到首帧之后

`plugin-tools`（21 ms）与 `tray`（11 ms）都不参与窗口画面（插件按钮插的是此时还没实例化的页面工具条，托盘图标在系统托盘里），直觉上应该能挪到首帧后面。实测否了：

| 组 | `first-paint` 三次/四次 | 中位 |
| --- | --- | --- |
| 现状（装配时就装） | 1521 / 1511 / 1501 | 1511 ms |
| 挪到 `DispatcherPriority.Background` | 1518 / 1578 / 1494 / 1671 | 1548 ms |

原因：`Background` 优先级仍在首帧渲染之前执行（首帧本身也要排到 `Background` 之后），这一步没有把工作量让开，只是换了队列位置。既然没有可验证的收益，改动已回退 —— 不留没有证据的复杂度。

### 12.3 给「空闲不排帧」上一条测试兜底

空闲期停 RAF 是拿到 −50% 空转的那一刀（见 §11），但它藏在私有方法里，改坏了不会有人察觉。现在把决策抽成纯函数 `PerformanceMonitor.ShouldRequestFrame(bool isSampling, bool hasTopLevel, bool isIdle)`（public，供测试直接调用），并补 `tests/CkanUI.App.Tests/IdleFramePumpTests.cs` 四例：空闲 / 采样中且非空闲 / 未采样 / 无 TopLevel。构建 0 警告 0 错误，App 用例 294（293 通过 + 1 例第三方在途的抽屉用例红）。




## 13. 启动成本的真实构成：首次触碰 vs 结构成本

### 13.1 先分清谁是可优化的

把 `MainWindow` 构造函数拆成标记点后，同一份代码在一个进程里造两个窗口对比（隔离 worktree、`dotnet build` 产物、2026-11-01）：

| 观测点 | 第一个窗口（三次） | 第二个窗口（同进程，三次） |
| --- | --- | --- |
| `InitializeComponent()`（XAML 载入 + 全树落地） | 204 / 209 / 216 / 227 / 241 / 257 ms | **41 / 41 ms** |
| 构造整体（含 7 档 ICO 解码 9–12 ms、`ApplyPageTransition`、指针/涟漪事件挂载、状态栏事实、诊断接线 9–13 ms） | 225 / 229 / 231 / 250 ms | **49 / 50 ms** |

结论：`main-window-new` 那 333–351 ms 里只有约 **50 ms 是结构成本**，剩下 ~175–185 ms 是**一次性首次触碰** —— Avalonia/FluentAvalonia 的 JIT 与类型初始化、首次 `DynamicResource` 解析、首次样式表落地。这条决定了三件事：

1. 靠删控件、拆 XAML 能拿回的只有那 50 ms 里的一部分，前面那 180 ms 拆不动；
2. 任何「改成惰性创建」的重构必须先证明它减的是结构成本而不是首次触碰；
3. 首次触碰的消除办法是 AOT/R2R（见 13.3），不是改代码结构。

### 13.2 试过并否掉：后台线程预热窗口

既然 ~180 ms 是首次触碰、而 UI 线程在启动早期还要阻塞等 I/O，直觉是「后台线程先造一个丢弃的窗口把 JIT 预热掉」。实测直接否：

```
[mw] warm-fail InvalidOperationException: The calling thread cannot access this object because a different thread owns it.
```

三次全失败 —— Avalonia 控件在构造期就会碰 `Application.Current` 的资源与 dispatcher，具备线程亲和。这条继续往下只能走「UI 线程提前造」，而 UI 线程本来就是串行瓶颈，一分钱不省。改动已 `git checkout` 回滚。

### 13.3 校准：开发版数字不等于用户看到的数字

发行流程本来就开 `PublishReadyToRun=true`（`docs/ops/release.md` §3.2 记录了实测 **冷启动 1.58 s → 0.81 s**，R2R 正是消除首次触碰的手段），所以 §10–§12 那些 1.3–1.5 s 的 `first-paint` 全部来自 `dotnet build` 产物 —— 那是**开发版**数字，比用户实际拿到的包里慢一倍。

后果：本轮之后所有启动优化的验收都应以 publish 产物为准。本环境里 `dotnet publish -r win-x64 -p:PublishReadyToRun=true` 跑到 12 分钟仍未生成 `obj/Release/net10.0/win-x64`（已终止），所以本文目前**没有** R2R 后的实测值，这是已知缺口而不是结论。

### 13.4 localization 那 69 ms 的归属

把 `App.axaml.cs` 的语言包装配拆成三段打点（三次）：

| 段 | 耗时 | 内容 |
| --- | --- | --- |
| `_services.GetRequiredService<ILocalizer>()` 解析 | **51 / 56 / 61 ms** | `LocalizerFactory` 读 en-US/zh-CN 两个 JSON + JSON 解析 + 建回退链快照（942 条） |
| `new LocalizationHost(...)` | 0 / 0 / 1 ms | 只存引用 |
| `Install()`（942 次 `ResourceDictionary` 插入 + 挂 `MergedDictionaries`） | **1 / 1 / 1 ms** | 此时还没有控件，插入几乎免费 |

所以这段几乎全是 **ILocalizer 构造**（读盘 + 解析），插入资源字典不是成本。可动的方向是后台预取，但有一个必须一起处理的坑：`src/CkanUI.Localization/Localizer.cs:451-452` 的 `ApplyThreadCulture` 会写 `CultureInfo.DefaultThreadCurrentUICulture` 与**构造线程**的 `CurrentUICulture`；后台线程构造不会改 UI 线程的 culture，必须构造完在 UI 线程重新 apply，否则线程 culture 与产品语义（16 篇 §2.3）不一致。本轮未实施。

## 14. 否定实验：语言包后台预取 + 装配顺序后移（端到端无收益，已回退）

§13.4 指出 `localization` 段的 58-72 ms 全在 `ILocalizer` 构造（读两个 JSON + 解析 + 建 942 条回退链快照），
插进资源字典只要 1 ms - 看起来是纯 I/O，丢了后台就能省掉。本轮把这个假设完整测完再否掉。

### 14.1 试过什么

| 位置 | 改动 |
| --- | --- |
| `src/CkanUI.Localization/Localizer.cs` | 新增 `public void ApplyCultureToCurrentThread()`：`if (_applyThreadCulture) ApplyThreadCulture(_snapshot.Culture);`（构造期那次只作用于构造线程，后台预热后必须在 UI 线程补一次，16 篇 §2.3） |
| `src/CkanUI.App/App.axaml.cs` | 紧挨 `instanceWarmup` 起 `Task<ILocalizer?> localizerWarmup = Task.Run(() => _services.GetService<ILocalizer>());`；取用处 `localizerWarmup.GetAwaiter().GetResult() ?? _services.GetRequiredService<ILocalizer>()` + `ApplyCultureToCurrentThread()`；再把整段语言包装配从 `log-pipeline` 之后移到 `radius` 之后（仍在 `new MainWindow` 之前） |

步骤 2 是必须的：只预取、不移顺序时后台只领先 14 ms（`di-build` 之后到 `log-pipeline` 结束），
`localization` 段仍要 47-48 ms；主题/密度/字号/强调色/圆角这几个宿主只读设置、不引用本地化字符串，
让它们先跑才能把后台余量拉到 150 ms 以上。

### 14.2 实测（隔离 worktree `/c/Users/43701/ckan-ui-mw`，同一 exe，交替重建）

| 组 | `theme` 段 | `localization` 段 | `di-build`→`shell-vm` 合计 | `first-paint`（相对 `di-build`） |
| --- | --- | --- | --- | --- |
| 基线（840ac77） | 2-3 ms | 58 / 72 / 62 / 58 ms | 160 / 220 / 171 / 164（中位 166） | 1027 / 1330 / 996 / 955（中位 1011） |
| 只加后台预取 | 2-3 ms | 47 / 48 / 48 ms | 183 / 170 / 170（中位 170） | 1029 / 968 / 1054（中位 1029） |
| 预取 + 顺序后移 | **33 / 36 / 33 / 34 ms** | **1 / 2 / 1 / 1 ms** | 163 / 186 / 169 / 167（中位 168） | 949 / 1014 / 1027 / 936（中位 982） |

### 14.3 结论：省下的 58 ms 挪进了 `theme` 段，合计不变

`localization` 段确实从 58-72 ms 掉到 1-2 ms，但 `theme` 段同步从 2-3 ms 涨到 33-36 ms -
它是「第一次真正触碰 `Application.Resources`」的那个宿主，原本这 30 ms 的一性次开销被排在前面的语言包装配替它付掉了。
决定性指标是 `di-build`→`shell-vm` 的合计：166 → 170 → 168 ms，三组在噪声内完全一致；
`first-paint` 相对值虽然从 1011 掉到 982 ms，但单跑极差超过 300 ms（并行编译的机器），不足以支撑结论。

判据：省时间要看「关键路径上的一性次工作量」有没有真的变少，不能只看某一阶段变短 -
UI 线程上串行的工作总量没变，把它在几个宿主之间换个位置不会让首帧提前。改动已回退，`Localizer` 也不留 `ApplyCultureToCurrentThread()`（没有生产调用点）。

顺带记录（同一批测出来的）：主仓 3 次实测 `localization` 段 0 / 1 / 1 ms、`theme` 31 / 33 / 37 ms、
`first-paint` 1441 / 1530 / 1552 ms，与 worktree 结论一致。

### 14.4 R2R 仍然拿不到（续 §13.3）

`dotnet publish -p:PublishReadyToRun=true` 在本机报 `NETSDK1094: 无法优化程序集以改进性能: 找不到有效的运行时包`；
补 `-r win-x64` 让 restore 参与后挂死在 restore 阶段（90 s 无任何输出，退出码 124，第一次跑 12 分钟也没生成 `obj/Release/net10.0/win-x64`）。
`microsoft.netcore.app.runtime.win-x64` 与 `microsoft.netcore.app.crossgen2.win-x64` 都在 `~/.nuget/packages` 且版本同为 10.0.12，与共享运行时一致。
发行包本来开 R2R（docs/ops/release.md §3.2），所以这段空白只在开发版出现。

## 15. 复测口径告警：当前机器状态下的空转数字不可比

本节记录一次**测不准**的现场，避免后人拿这批数字去否定或支持任何结论。

现场（2026-11-01 下午）：共享树有 113 个文件被并行改动，机器上同时有 26 个 `dotnet.exe`、1 个 `MSBuild.exe`、20 个 `node.exe`，
`Win32_Processor.LoadPercentage` 26%。同一时刻用同一个采样器（40 s 预热 + 3 个 5 s 窗口，非侵入、不抢前台）测两棵树：

| 被测 | 5 s 窗口单核% | 工作集 |
| --- | --- | --- |
| 主仓构建（含并行改动） | 0.31 / 23.13 / 8.13、11.56 / 11.25 / 18.75 | 280 / 233 MB |
| 干净 worktree（HEAD 840ac77） | 22.19 / 18.13 / 15.00、15.31 / 16.56 / 14.69 | 434 / 434 MB |

同一份代码量级能差 20 倍、同一进程相邻两个 5 s 窗口能从 0.31% 跳到 23.13%，
说明这个环境下**空转采样已不可比**（别的 agent 正在编译、GC 与磁盘争用、以及窗口是否前台的差异都会进来）。

结论：§6.4 / §9.1 的「16.3-22.8% → 1.1-1.6% 单核」「983-1009 MB → 226-238 MB」是在机器安静时取的，
仍然成立但**不可用今天的数字复现**。要复测请先：

1. `taskkill /F /IM CkanUI.exe` 并等 2 s（残留实例会走 SingleInstanceGuard 直接退出，看起来像「启动很快」）；
2. 确认没有并行编译（`tasklist` 里 `dotnet.exe` 数量回到个位数）；
3. 至少跑 3 次，看**尾部窗口**而不是平均值；单次结果不足以支撑任何结论。

另外：worktree 那份 434 MB 与主仓 233-280 MB 的差异尚未归因，可能是并行改动里的指针光晕 / 玻璃层 / 导航项动画，也可能是这次启动落到了不同的实例状态 - 未验证，不当结论。
## 16. 内存与空转回归门禁（`perf-probe --gate-mem`）

§15 说明了空转数字在忙机器上不可比。为了让「已经优化到位的指标不会悄悄退回去」，
把内存/空转画像做成可判定的门禁：`perf-probe --gate-mem` 跑完画像后按下表判定，退化则返回退出码 1。

```bash
TEMP=$PWD/artifacts/tmp/temp TMP=$PWD/artifacts/tmp/temp \
  ./tools/perf-probe/bin/Release/net10.0/perf-probe.exe --gate-mem
```

| 指标 | 阈值 | 实测（本次） | 阈值余量依据 |
| --- | --- | --- | --- |
| 滚动每帧分配 | ≤ 90,000 B/帧 | 67,281 B/帧 | 实测 67,418 B/帧的 1.33 倍；滚动路径每帧分配是最容易偷偷长胖的指标（`§2` 的 300 帧采样） |
| 首屏后工作集 | ≤ 260 MB | 200.4 MB | 实测 161-162 MB 的 1.6 倍，容忍不同机器/实例规模 |
| 峰值工作集 | ≤ 300 MB | 214.6 MB | 实测 172 MB 的 1.7 倍；1 GB 那种峰值已被 `IdleMemoryReclaimer` 消掉，回到 300 MB 以上就是机制失效 |
| 空转 CPU（光栅档） | ≤ 22% 单核 | 8.48% 单核 | 实测 7.0-16.3%，取 22% 只挡「回到 30fps 全量重绘 / RAF 复活」这类数量级退化 |
| 线程数（首屏后） | ≤ 24 | 9 | 正常 8-9 个；膨胀说明又有人在线程池上阻塞排队 |

**不门禁的指标：调度档空转 CPU。** 它测的是「不渲染、只跑调度」的成本，同一次跑里能从 19% 跳到 55%，
因为 32 核机器上线程池唤醒与别的进程争抢都会进来 —— 拿它当门禁只会给假红。
真正的空转体感由光栅档代表（每帧真的画一次 1280×800），它稳定得多。

阈值写在 `tools/perf-probe/MemoryGate.cs` 的常量里，改动请连带更新本节的「阈值余量依据」。

### 16.1 阈值重标：行模板被视觉改版改重（2026-11）

`--gate-mem` 的滚动分配阈值原本按 67,418 B/帧标成 90,000。b4f0a19 上实测变成 **97,674 B/帧**，A/B 验证与 `ModsPageViewModel` 的改动无关（换回旧 VM 仍是 97,674，两次完全一致）：

```
git log --oneline 1399b8e..b4f0a19 -- src/CkanUI.App/Views/ModsPageView.axaml
f0d8f81 feat(motion): 全站交互动效改为帧驱动弹簧并补齐入场编排    （+270 / -197 行）
```

即行模板被视觉改版加重了 45%。阈值按新实测重标到 130,000（1.33 倍余量），**这是随行模板重标护栏，不是把门禁调松**；若将来有人再改重行模板，先跑 `--mem` 拿到新地板再谈阈值。

## 17. 模组列表的真正地板：36 MB 仓库索引解析（2.8–4.0 s / 1.8 GB 分配）

前 16 节把「空转 CPU」「稳态内存」「启动到首帧」都压下来了，但只要用户点开模组页，
还有一段固定成本摆在那里：把 36.2 MB 的仓库索引 JSON 解析成 CKAN 对象图。

### 17.1 实测：解析就是地板

```bash
./tools/perf-probe/bin/Release/net10.0/perf-probe.exe \
  --repo-index "$LOCALAPPDATA/CKAN/repos/4A85405A-KSP-默认数据库.json"
```

| 轮次 | 解析 | 模块数 | 下载计数 | 分配 |
| --- | --- | --- | --- | --- |
| 1 | 4,027 ms | 3,820 | 3,763 | 1,877.8 MB |
| 2 | 3,669 ms | 3,820 | 3,763 | 1,765.1 MB |
| 3 | 3,214 ms | 3,820 | 3,763 | 1,768.6 MB |

解析发生在 `RepositoryData.FromJson`（`external/CKAN/Core/Repositories/RepositoryData.cs`），
本项目改不动，1.8 GB 的瞬时分配也不是我们造成的 —— 但它决定了「列表什么时候出现」，
也解释了早期出现过的工作集 1 GB 峰值。

### 17.2 逐模组投影：重复查询值 50–100 ms

`RepositoryService.BuildSnapshot` 对同一 `AvailableModule` 查两次 `Latest(...)`
（一次取最新版、一次判兼容）、查两次 `InstalledModule(...)`。探针分别量「每模块一次」与
「复现旧实现的第二次」：

| 轮次 | 每模块一次 | 复现重复查询 | 重复开销 |
| --- | --- | --- | --- |
| 1 | 122 ms | 204 ms | +82 ms |
| 2 | 95 ms | 149 ms | +54 ms |
| 3 | 287 ms | 41 ms* | — |

*第三轮两遍都落在同一个 GC 抖动窗口里，只作噪声参考。

已改：`compatibleVersion` 与 `installedModule` 各查一次复用（`src/CkanUI.Services/Repositories/RepositoryService.cs`），
语义不变（`compatible` 仍等价于「兼容版存在」）。

### 17.3 磁盘缓存：指纹一致就不解析

新增 `src/CkanUI.Services/Repositories/SnapshotCache.cs`：把投影出来的行按「输入指纹」落盘，
下次 `LoadCachedAsync` 命中就直接出列表。设计要点：

| 关注点 | 做法 |
| --- | --- |
| 指纹覆盖 | 缓存结构版本 + 实例 `CkanDir` + 仓库清单（name/uri/priority）+ 每个索引文件与 `registry.json` 的大小与修改时间（SHA-256） |
| 失效 | 任一文件被重写、仓库清单变化、结构版本递增 → 指纹变 → 不命中，退回真实解析 |
| 权威性 | 只加速不做权威：读失败 / 写失败 / JSON 损坏 / 版本不符，一律安静退回解析，绝不让用户看不到列表 |
| 落盘 | `%LOCALAPPDATA%/CKAN/ckanui-cache/mods-<指纹前16位>.json`，先写 `.tmp` 再 `File.Move(overwrite)`；空列表不落盘 |
| 指纹来源 | `SnapshotCache.KeyFrom` / `StampFile`，单测覆盖「顺序敏感」「文件重写后指纹变化」「缺失文件不抛异常」 |

### 17.4 无变化的刷新不再重建

后台刷新 `RefreshAsync` 原来无论结果如何都要 `BuildSnapshot` 一遍。现在：
`result == UpdateResult.NoChanges && 当前行来自磁盘缓存 && 非空` → 直接沿用，跳过解析与投影
（`_servedFromDiskCache` 标记，一旦真实重建就清零）。这样热启动的 36 MB 解析整段消失，
1.8 GB 分配也不发生。

### 17.5 未完成的验证（诚实记录）

端到端验证（真实实例上冷跑 vs 热跑）本轮没做成，原因是环境而不是代码：

- 实例 `D:/Kerbal Space Program_newmod` 被并发进程占用；`registry.locked` 残留时
  `IsInstanceMaybeLocked` **只看文件是否存在**（`external/CKAN/Core/Registry/RegistryManager.cs:48-49`），
  于是启动序列在 `SwitchAsync` 就失败退出，压根到不了仓库加载（表现为 `bootstrap-done` 只用 7 ms）。
- 该锁文件由 `taskkill` 强杀进程留下，删掉后又被并发进程立刻重建（采样时读到 `EBUSY`）。

因此本节 17.1/17.2 的数字来自探针（同一份 36.2 MB 索引、同一台机器），17.3/17.4 的正确性由
`tests/CkanUI.Services.Tests/SnapshotCacheTests.cs` 的 7 例保证，**端到端时延待有独占实例时补测**。

### 17.6 复测命令

```bash
# 解析 + 投影
./tools/perf-probe/bin/Release/net10.0/perf-probe.exe \
  --repo-index "$LOCALAPPDATA/CKAN/repos/4A85405A-KSP-默认数据库.json"

# 缓存往返与指纹
TEMP=$PWD/artifacts/tmp/temp TMP=$PWD/artifacts/tmp/temp \
  dotnet tests/CkanUI.TestRunner/bin/Release/net10.0/CkanUI.TestRunner.dll --config Release Service
```

## 18. 换页成本的实测口径，与三项「重复干活」的修复

用户反馈「切到设置会卡顿、实例页切存档/飞船掉帧」。这一节记录怎么把「换页」这件事变成可复现的数字，以及实测之后真正值得修的是什么。

### 18.1 探针：`perf-probe --pages-real [key]`

`tools/perf-probe/PageSwitchProbe.cs` 在**真实 DI** 下换页，逐段计时：

| 段 | 含义 |
| --- | --- |
| resolve | `GetService` 解析页面 VM（首次 vs 再次，DI 单例） |
| deactivate / activate | 上一个 VM 的 `OnDeactivated`、目标 VM 的 `OnActivated`（数据刷新在这里） |
| settle300 | 泵 300 ms 调度队列（异步加载落地） |
| view | `ViewLocator.Build` 造视图 |
| layout | Content 赋值 + Measure + Arrange |
| reveal | 六套动效 Attach + `EntranceChoreographer.Play` + `TextRevealChoreographer.Play` |
| render | `ForceRenderTimerTick(16)` |
| nodes | 该页可视树节点数 |

`--node-hist <key>` 打印单页类型直方图；`--save-validate` 量一次真实存档校验（本机无可用实例时输出 `no instance`）。

### 18.2 23 个导航键全量结果（HEAD `f0d8f81`，round1 / round2，ms）

| 页面 | view | layout | reveal | nodes |
| --- | --- | --- | --- | --- |
| **settings** | 26.4 | **686.9 / 777.8** | 0.2 | **2338** |
| mods | 191.9 | 218.4 | 6.1 | 146（DataGrid 单元格模板贵） |
| modpacks | 62.1 | 173.2 | 3.4 | 250 |
| modpack-editor | 61.5 | 147.6 / 50.8 | 2.1 | 459 |
| home | 41.2 | 81.4 / 86.5 | 1.4 | 158 |
| changes | 33.6 | 85.9 / 33.0 | 1.2 | 132 |
| modtext | 30.1 | 68.4 / 30.0 | 1.1 | 121 |
| cache | 24.8 | 61.1 / 14.1 | 0.9 | 96 |
| saves | 22.4 | 55.6 / 27.0 | 0.8 | 104 |
| 其余 14 键 | ≤28 | ≤50 | ≤1.5 | ≤180 |

两条结论：

1. **`reveal` 全页 0.1–16 ms（settings 只有 0.2 ms）** —— 之前怀疑的「每次换页 5–6 次全树 BFS」并不成立：`Attach` 有 `ConditionalWeakTable` 去重，`Play` 只碰 ≤8 个目标。动效不是瓶颈，不要去优化它。
2. **layout ≈ 0.33 ms/节点**，与 `--micro` 的 0.31 ms/控件基线一致 → 换页成本 ∝ 节点数，减节点是唯一抓手。

settings 的节点直方图（total 2338）：ContentPresenter 406、TextBlock 296、Border 281、Grid 197、StackPanel 176、TextBox 94、Viewbox 80、ViewboxContainer 80、Panel 56、DockPanel 48、ContentControl 47、Button 46、FASettingsExpander 36、Expander 36、ToggleButton 36、FASettingsExpanderItem 36、FANumberBox 36、ToggleSwitch 36 —— 每行 65 节点，因为 5 种编辑器（TextBox 单行/多行、FANumberBox、ToggleSwitch、ComboBox）全部实例化，4 个靠 `IsVisible=false` 藏着。

### 18.3 否定实验：按 kind 只建一个编辑器（已回退）

新增 `SettingEditorHost : ContentControl`，在 `OnDataContextChanged` 里按 `SettingItemViewModel` 的 `IsMultiline` / `IsNumericKind` / `IsBooleanKind` / `IsChoiceKind` 选 `x:Key` 模板，模板搬进 `SettingsPageView.axaml` 的 `UserControl.Resources`。

结果：**nodes 2338 → 2302（-1.5%）**，layout 仍在 789 / 924 / 1408 / 1541 ms 之间漂移。原因：每行省下 4 个隐藏编辑器，但换来 `ContentPresenter +36`、`SettingEditorHost +36`、`FASymbolIcon +36`，净减极小。已 `git checkout` 回退。

同一轮还量到：把 settings 的 ItemsControl 整段 `IsVisible="False"`（本该让布局变便宜）后，layout 反而 1215–1804 ms → **当前机器上 headless 布局采样被负载支配，±2 倍漂移**，任何 <5% 的节点改动都测不出来。

> 判据：节点数是**可复现**的指标；headless 的 layout 毫秒在当前环境下不可作为小改动的验收依据。

### 18.4 真正值得修的三处「重复干活」

换页时最贵的不是造控件，而是每次回到页面都**重做一遍已经做过的事**。三处已修（`src/CkanUI.ViewModels/Pages/SettingsPageViewModel.cs`、`src/CkanUI.ViewModels/Pages/SavesPageViewModel.cs`、`src/CkanUI.Services/Settings/UiSettingsStore.cs`）：

| # | 问题 | 修法 | 可观测差别 |
| --- | --- | --- | --- |
| 1 | `SettingsPageViewModel.OnActivated` 无条件 `LoadAsync`：重灌 36 行编辑值、闪 `IsLoading` 骨架、冲掉未保存编辑 | 加 `_hasLoadedOnce` 门；再次进入只走 `RefreshInstanceStateAsync()`（`GetAllAsync` → `ApplyInstanceState` → `LoadStabilityToleranceAsync`），手动「重新加载」仍走 `ReloadAsync` | `SecondActivation_KeepsInFlightEdits`：改过的 `DevBuilds` 值经 Deactivate→Activate 后保持，且 `IsLoading=false` |
| 2 | `UiSettingsStore.Load()` 每次都 `ReadAllBytes` + 反序列化；启动期 7 个外观宿主各读一次，设置页每次 `GetAllAsync` 再读一次 | `ReadWithCache()` 按 `FileInfo` 的 `Length` + `LastWriteTimeUtc` 复用；`Save` 之后 `RememberWritten` 同步戳；文件不存在清缓存，IO/Unauthorized 回退 `TryRead` | 同一份 `ckan-ui.json` 一个会话只解析一次 |
| 3 | `SavesPageViewModel` 每次选中/刷新都 `File.ReadAllBytes` 整份 sfs + `SfsParser.ParseBytes` + `SfsValidator.Validate`，且无条件 `IsBusy=true` 闪遮罩 | 按 `row.SizeBytes` + `row.LastWriteTime` 落 16 条 `_reportCache`；同实例且有数据时不再置 `IsBusy`；**用户显式点「校验」走 `preferCache: false` 强制真读** | `RefreshingTwice_ReusesValidationForUnchangedFile`：两次刷新后 `ValidateRequests==1`；把文件大小改成 8192 后变 2 |

第 3 条的 `preferCache` 参数是被测试逼出来的：第一版让显式校验也吃缓存，`ValidateFailure_ShowsErrorAndClearsValidation` 立刻红 —— 用户点「校验」时想看到的是此刻磁盘上的真相，缓存只能服务「切回页面」这种隐式路径。

### 18.5 剩下的 settings 大头（未做）

settings 的 2338 节点 / 686–778 ms 布局仍然是全场唯一 O(700ms) 项。要真正拿下来只有两条路，都需要动视觉：虚拟化整页（`ItemsControl` → 虚拟化面板，滚动条行为会变），或把 36 行折叠成分组手风琴（默认不展开，用户得多点一次）。两条都违反「不影响视觉效果」，暂时只记录不动手。

子标签（modpacks 两个 TabItem、ModDetailsView 四个 TabItem）尚未单独计时 —— 现有探针按导航键走，覆盖不到 TabControl 内部的切换。

### 18.6 子标签（TabControl）实测

`perf-probe --tabs [key]` 对每个导航键把视图挂进窗口、布局完成，再逐个 `TabControl` 切 `SelectedIndex`，计 layout + render（`PageSwitchProbe.TabSweep`）。

23 个导航键里只有 **modpacks** 含 `TabControl`（2 个 TabItem）；ModDetailsView 的 4 个 TabItem 在详情侧栏里，探针没有选中模组所以取不到。

| 轮次 | item0 layout / render | item1 layout / render | nodes |
| --- | --- | --- | --- |
| 1（首次，含模板实例化） | 0.0 / 1.2 ms | 34.0 / 0.5 ms | 202 → 205 |
| 2 | 12.7 / 0.8 ms | 14.8 / 0.5 ms | 202 → 205 |

即子标签切换是 13–34 ms 量级，与「掉帧」不在一个数量级；用户感知的实例页卡顿来自 `saves` / `ships` 这两个**平级导航键**（§18.2：saves layout 55.6 ms，且旧实现每次都要重读并解析整份 sfs），不是 TabControl。

### 18.7 待办候选（未做，理由写在下面）

| 候选 | 值不值 | 现在的障碍 |
| --- | --- | --- |
| `PathToThumbnailConverter` 固定按 640 宽解码 | 可能值：`FlagsPageView` 的旗帜源图只有 64×40，解码到 640 是 100 倍像素浪费；`ShipsPageView` 列表槽位 40×24 却解码到 640（16 倍） | 想按显示尺寸传 `ConverterParameter` 前先量出真实省下的毫秒，但 headless 里 `Bitmap.DecodeToWidth` 是惰性解码，`RenderTargetBitmap` 在无头环境又不真光栅，`--thumb-cost` 量出 0.0–0.8 ms 无法采信；view 文件也在并行改版范围内 |
| flags / loadingimages 用 `WrapPanel`（非虚拟化） | 未知 | 换 `ItemsRepeater` 会改滚动/换行行为，违反「不影响视觉效果」 |
| settings 虚拟化 | 值：单项 O(700ms) | 见 §18.5，要动视觉 |

### 18.8 缩略图按显示尺寸解码（列表槽位 40×24，却解码到 640 宽）

`PathToThumbnailConverter` 原来无条件 `DecodeToWidth(640, HighQuality)`。但三处列表的显示尺寸远小于此：

| 视图 | 槽位 | 旧解码宽 | 新解码宽 |
| --- | --- | --- | --- |
| `ShipsPageView.axaml` 列表行 | 40×24 | 640 | `ConverterParameter=96` |
| `FlagsPageView.axaml` 卡片 | 150 宽 / 76 高（源图常是 64×40，等于被放大 10 倍） | 640 | 160 |
| `LoadingImagesPageView.axaml` 卡片 | 148 宽 / 76 高 | 640 | 320 |
| 各详情面板 | 300–640 | 640 | 640（不变） |

转换器改动：`ConverterParameter` 解析目标宽度（`int` 或字符串，clamp 到 16–1024），缓存键加上宽度，避免 96 与 640 互相顶掉。

**解码 + 高质量缩放的实测成本**（`perf-probe --thumb-cost`，3 张 1296×809 PNG，用 SkiaSharp `Decode` + `Resize(Mitchell)` 复刻 Avalonia 的两步）：

| 目标宽 | 每张图 | 单张 640×等比 RGBA 内存 |
| --- | --- | --- |
| 640 | 13.2 ms | ~1.6 MB |
| 256 | 3.6 ms | ~0.26 MB |
| 128 | 3.4 ms | ~0.07 MB |
| 96 | 3.0 ms | ~0.037 MB |

即列表槽位 40×24 的解码从 **13.2 ms / 1.6 MB** 降到 **3.0 ms / 0.037 MB**，每张省约 10 ms UI 线程与 1.56 MB。转换器缓存上限 64 条，最坏情况下这 64 张缩略图的位图占用从约 **100 MB 降到约 2.4 MB** —— 这一条同时命中内存目标。

> `perf-probe --thumb-conv` 走真实转换器：Avalonia 的 `DecodeToWidth` 是惰性的，转换器本身只花 0.03–0.8 ms，返回的 `PixelSize` 已经是 96×96 而不是 640×640；真正的解码/缩放成本发生在首帧光栅时，所以上表用 SkiaSharp 复刻来取数，不拿转换器返回耗时当结论。

## 18.9 存档 / 飞船页：磁盘快照没变就不重建列表

用户在「实例 → 存档 / 飞船」两个子页之间来回切时会掉帧。原因是两个页的 `RefreshCoreAsync` 每次激活都无条件换掉整表：

- `src/CkanUI.ViewModels/Pages/SavesPageViewModel.cs`：`_allRows = rows; ApplyRowFilter(preferPath); ResetValidation();` —— 每次激活都把 `Rows` 换成新数组，表格行容器被整体回收重建。
- `src/CkanUI.ViewModels/Pages/Assets/ShipsPageViewModel.cs`：`Crafts.Clear()` 后逐项重加 —— ListBox 的容器缓存（`supportsRecycling`）也跟着失效。

改法（保持视觉效果不变，只是不再做无意义的重建）：

| 页 | 判据 | 命中后的行为 |
| --- | --- | --- |
| 飞船 | `_lastAssets`（`CraftAsset` 是 record，值相等即磁盘没变）与本次 `items` 序列相等，且 `Crafts.Count` 一致 | 只 `NotifyEmptyState()`，不碰 `Crafts`、不改 `SelectedCraft` |
| 存档 | `_lastList` 与本次 `SaveGameInfo` 序列相等，且 `_allRows.Count` 一致 | 跳过 `_allRows`/`ApplyRowFilter`/`ResetValidation`，状态栏与选中项原样保留 |

两条判据都只在「同一实例 + 同一份磁盘快照」时命中：切实例、新建/删除/改名、文件体积或写入时间变化都会让序列不等，走原路径重建。

### 换表 vs 不动：值多少钱

新增 `perf-probe --list-rebind <rows>`（`PageSwitchProbe.ListRebindCost`：ListBox + `supportsRecycling` 的 DataTemplate，量「换掉 `ItemsSource` 后跑完一次布局」与「什么都不做再跑一次布局」）：

| 行数 | 换表 p50 | 不动 p50 | 差值 |
| --- | --- | --- | --- |
| 20 | 45.0 ms | 0.15 ms | 44.9 ms |
| 200 | 23.9 ms | 0.08 ms | 23.8 ms |
| 1000 | 19.1 ms | 0.05 ms | 19.0 ms |

差值近似常数（19–24 ms，20 行那次被机器负载抬高），因为虚拟化下真正落地的容器数量只跟视口有关，跟总行数无关。即每次无意义重建 ≈ 一帧多的整表布局，正是切子页时那种「顿一下」。门命中后就为 0。

> 量法注意事项：在已经落地的窗口上只调 `Measure/Arrange` 会短路，实测恒为 0.00 ms；必须在 `InvalidateMeasure` 之后 `Dispatcher.UIThread.RunJobs(DispatcherPriority.Loaded)` 把布局队列跑空，数字才有意义。

## 18.10 旗帜 / 加载图页：扫描结果没变就不重建卡片（WrapPanel 是重排重灾区）

这两页的卡片网格用的是显式 `WrapPanel`（非虚拟化），每次激活 `RefreshCoreAsync` 都 `Clear()` 后逐张重加，整片卡片要重新测量排布。改法与存档/飞船页同构：

| 页 | 判据 | 命中后的行为 |
| --- | --- | --- |
| 旗帜 `FlagsPageViewModel` | `_lastAssets`（`FlagAsset` record）、数量一致、且默认旗帜 `_lastDefaultConfig` 未变 | `NotifyEmptyState()` 后返回，卡片与选中项都保留 |
| 加载图 `LoadingImagesPageViewModel` | `_lastAssets`（`LoadingImageAsset` record）与数量一致 | 同上 |

旗帜把「默认旗帜」也纳入判据，是因为默认徽标挂在卡片上：只比资产会让「设为默认」后徽标不刷新（`Flags_RefreshTwice_RebuildsWhenDefaultFlagChanged` 看着这条）。

### WrapPanel 重建成本（`perf-probe --list-rebind <rows> --wrap`）

| 卡片数 | 换表 p50 | 不动 p50 | 差值 |
| --- | --- | --- | --- |
| 40 | 106.9 ms | 0.03 ms | 106.8 ms |
| 200 | 280.9 ms | 3.44 ms | 277.4 ms |

非虚拟化的 `WrapPanel` 是线性成本（每张约 1.3–2.7 ms），200 张卡片重建一次接近 0.3 秒 —— 这就是旗旈/加载图页「切回来卡一下」的全部来源。门命中后这一步归零，画面与数据完全不变（卡片、缩略图、选中态都还是原来那批实例）。
### 日志页：存储驱动刷新按 150 ms 时间窗合并（§18.11）

日志页每次刷新要重算两样东西：过滤查询（20k 条缓冲实测 0.32 ms）与聚合视图（时间轴分桶 + 指纹 Top-N，实测 13.16 ms），加上行对象构造，一次完整 `Refresh()` 约 20.5–22.5 ms。合成一次刷新本身没问题，问题是 `LogStore.Changed` 每来一行就触发一次：写日志密集时 UI 线程被刷新占满，日志页一打开就持续掉帧。

改法（`src/CkanUI.ViewModels/Pages/Logs/LogPageViewModel.cs`）：只给**存储驱动**的刷新加 150 ms 时间窗，窗内的多次变更合并成一次尾部刷新；手动 `Refresh()`（筛选变化、用户操作）不经过这道门，仍然即时。为保证不丢最后一次更新，被压住的刷新用 `System.Threading.Timer` 排到窗口末尾重投；初值取「很久以前」让第一条日志立刻上屏；`OnDeactivated` 把定时器停掉，不留给已退出的页面。

`perf-probe --logs-stream <appends> <fill>`（新）逐条写日志、每写一条把调度队列跑空，量日志洪流下 UI 线程被吃掉的总时间（`fill=20000` 先把环缓冲垫满，让每次刷新付全额聚合成本）：

| 场景 | 200 条流入 UI 线程耗时 | 每条 | 结果行数 |
| --- | --- | --- | --- |
| 不限流（`StoreRefreshThrottleMs = 0`） | 1096.2 / 1195.6 ms | 5.5–6.0 ms | 500 |
| 150 ms 时间窗 | 22.5 / 22.9 ms | 112–115 µs | 500 |

同一条流入的 UI 开销降约 98%，且末条日志仍会落到界面上（尾部刷新实测 `rows=500`）。画面上的差异只是「新行最多晚 150 ms 出现」，日志列表本来就是流式刷新的，肉眼无从分辨。

`--logs` 那份拆解（activate 45.9–50.3 ms / refresh p50 20.5–22.5 ms / query 0.32 ms / aggregate 13.16 ms）留在同章节前文，它说明了为什么限流比「优化行构造」更值：聚合一项就占了六成。

## 19. 空闲 CPU 的真正大头：首页截图胶片带（32–54% → 10–14% 单核）

§12 之后空闲 CPU 一直在 30–50% 单核徘徊（早期 1.1–1.6% 的样本是胶片带还不存在的旧构建，不可复用）。这一节把它拆到可归因，并把**被观看时画面完全不变**的那部分拿回来。

### 19.1 归因：逐层关掉再看

仪器：worktree `/c/Users/43701/ckan-ui-head`（base `bd187a1`）+ `artifacts/perf/idle-ab.mjs`（spawn Release exe → 静置 42 s → 连续 3 个 5 s 窗口按 `TotalProcessorTime` 差分算单核%，窗口不抢前台、不动光标）。各组开关只加在 worktree 里，未进主仓。

| 组 | 3 个 5 s 窗口（单核%） | 工作集 | 结论 |
| --- | --- | --- | --- |
| 默认 | 54.4 / 45.0 / 47.2、47.8 / 32.2 / 46.9 | 446 / 321 MB | 基线 |
| 关 4 个指针特效层（`CKANUI_NO_LAYERS`） | 43.1 / 39.7 / 35.9、42.5 / 38.1 / 47.8 | 453 / 310 MB | 与基线同量级 → 不是它们 |
| 关星空（`CKANUI_NO_STARFIELD`） | 14.7 / 11.9 / 15.0、15.0 / 13.4 / 19.1 | 433 / 302 MB | — |
| 关诊断采样（`CKANUI_NO_PERF`） | 12.2 / 12.5 / 12.5、11.9 / 14.4 / 14.7 | 298 / 439 MB | — |
| 关弹簧帧泵 / 骨架脉动 / 换页缩放（`CKANUI_NO_ANIM`） | 20.6 / 13.1 / 12.5、13.8 / 15.6 / 12.5 | 435 / 298 MB | — |
| **只关首页胶片带（`CKANUI_NO_RAIL`）** | **10.3 / 13.1 / 11.3、18.4 / 12.8 / 11.9** | 438 / 298 MB | **等于全关** |
| 全关（`CKANUI_NO_FX`） | 10.3 / 18.1 / 12.8、12.2 / 17.2 / 14.4 | 437 / 300 MB | 地板 |

只关胶片带就等于把 4 个特效层和胶片带一起关掉 —— 空闲 CPU 的绝大部分（约 25–35 个百分点）来自 `src/CkanUI.App/Views/HomePageView.axaml.cs` 的自动滚动：

```csharp
private const double RailFrameMilliseconds = 16;  // 60 fps
private const double RailStepPixels = 0.8;        // 约 48 px/s
```

`OnAttachedToVisualTree` 里无条件 `_railTimer.Start()`，每帧改一次 `ScreenshotRail.Offset` —— 一次偏移变更就要重排、重合成整条胶片带（里面是若干张截图卡片），60 次/秒。

### 19.2 改法：没人看的时候停表

画面要保：**窗口在前台且页面可见时，60 fps 与 0.8 px/帧一点都不动**。只加一条运行前提（`src/CkanUI.App/Views/HomePageView.axaml.cs`）：

```csharp
public static bool ShouldRunRail(bool attached, bool isVisible, bool? windowIsActive)
    => attached && isVisible && windowIsActive != false;
```

`OnAttachedToVisualTree` 起订阅 `Window.IsActiveProperty`，`UpdateRailTimer()` 按上式起停 `_railTimer`；`OnDetachedFromVisualTree` 退订；`OnRailTick` 里再兜一层（属性通知还没到的那一帧）。抽成纯函数是为了能无头断言这条不变量（`tests/CkanUI.App.Tests/HomeRailFramePumpTests.cs`，5 例），不必起真窗口。

### 19.3 实测

同一 exe、同一采样口径，改前 / 改后：

| 组 | 3 个 5 s 窗口（单核%） | 中位 |
| --- | --- | --- |
| 改前（`bd187a1`） | 54.4 / 45.0 / 47.2、47.8 / 32.2 / 46.9 | 47.2 |
| 改后 | 11.3 / 10.3 / 12.8、13.8 / 10.3 / 13.8 | 12.5 |

约 **-73%**；工作集不变（窗口失焦时胶片带本来就停在原地，画面无从分辨）。

### 19.4 残留的 ~12% 与已排除项

改后仍剩 10–14% 单核，且以下开关都压不动它：星空、诊断采样、弹簧帧泵、骨架脉动、换页缩放、4 个指针特效层（见 19.1 表）。按线程采样（`Get-Process ... .Threads` 逐个取 `TotalProcessorTime` 差分）显示：**只有一个线程在跑（10.42% 单核），其余线程合计 <2.5%**，说明剩下的不是「一堆定时器各烧一点」，而是某条线程上的周期性工作。未定位到具体来源，**不作为结论**。

顺带确证（关闭此前的开放问题）：运行中的进程加载 `av_libGLESv2.dll`、`Avalonia.OpenGL.dll`、`Avalonia.Skia.dll`、`d3d11.dll`、`libSkiaSharp.dll` → 走 ANGLE→D3D11 **硬件加速**，不是软件光栅；空闲 CPU 与光栅方式无关。


## 20. 设置页的 2338 个节点：三条虚拟化路子为什么都不能走

用户点名「切到设置会卡顿」。这一节记录对 `src/CkanUI.App/Views/SettingsPageView.axaml` 的四组实验——**全部回退，一处未改**，并留下可复现的判据与取证工具，避免后来者重复踩。

### 20.1 基线：成本由行数 × 每行 chrome 决定

`perf-probe --pages-real settings`（真实 DI，空数据也成立）：

| 指标 | 值 |
| --- | --- |
| nodes | **2338**（稳定，是唯一可信指标） |
| layout | 1560.6 / 1179.6 ms（round1/round2；同机同 exe 在 700–1560 ms 漂移，机器负载支配） |
| view / activate / reveal / render | 37.0 / 2.8 / 14.5 / 1.3 ms |

节点直方图：ContentPresenter 406、TextBlock 296、Border 281、Grid 197、StackPanel 176、TextBox 94、Viewbox 80、FASettingsExpander 36、Expander 36、ToggleButton 36、FANumberBox 36、ToggleSwitch 36 —— **36 行 × 65 节点/行**。每行并排实例化 5 种编辑器（多行 TextBox / 单行 TextBox / FANumberBox / ToggleSwitch / ComboBox），只有 1 个可见，其余靠 `IsVisible=false`。

布局成本 ≈ 0.33 ms/节点，与 `--micro` 测得的 0.31 ms/控件基线一致 → **换页成本 ∝ 节点数，减节点是唯一量级抓手**。

### 20.2 四条路线的结论

| 路线 | nodes | layout | 否决理由 |
| --- | --- | --- | --- |
| 按 kind 只建一个编辑器（`SettingEditorHost` + `DataTemplate`） | 2302（-1.5%） | 789–1541（漂移带内） | 减得太少；ContentPresenter/宿主控件把省下的 TextBox 又补回去了 |
| 删掉 3 个不可见编辑器 | 1706（-27%） | 1164 / 1354（-12~15%） | 收益不足且改结构（不再是「5 种编辑器同一套模板」），有后续维护风险 |
| `ListBox` 替换外层（自带 VirtualizingStackPanel） | **513（-78%）** | 419–453 / 157–201 | **破视觉**：像素比对 13.9%（footer 编辑器全部不渲染、行距漂移）；补 `HorizontalContentAlignment=Stretch` 后编辑器回来但间距累积漂移 17.9%（下载缓存标题 y 429→496） |
| 保留原视觉树，只换 `ItemsPanel` 为裸 `VirtualizingStackPanel` | **502（-78.5%）** | 496.2 / 235.0（-60~80%） | **破滚动**：整页像素差仅 0.82% 且逐行核对都在 y≤60 的背景星座上、内容区逐像素一致，但滚轮步进模拟显示 extent 在 1508↔5442 间抖动、offset 卡在 2935 **滚不到底**（最后一组永远实体化不出来） |

框架侧的硬事实：**Avalonia 12.1.4 核心已移除 `ItemsRepeater`**（`Avalonia.Controls.xml` 里只剩两句 "Not implemented in Avalonia, preserved here for ItemsRepeater's usage"），独立包 `Avalonia.Controls.ItemsRepeater` 在 nuget 上最高 **12.0.0**（`dotnet add package ... --version 12.1.4` → `NU1102: 找不到版本为 (>= 12.1.4) 的包`），与本仓核心版本错配，不引入。

### 20.3 结论与后续

- 设置页的卡顿是**框架内滚动容器的既定成本**：36 行 × 65 节点全部实体化，0.33 ms/节点。要动它就必须么换容器（破视觉）、么换布局模型（破滚动），两条都不满足「不影响视觉效果」。
- 本轮**不落地**任何设置页结构改动。真正能省的是「别每次都重建」——这一条已在 §18 落地（`SettingsPageViewModel` 首次全量、后续只 `RefreshInstanceStateAsync`）与 `UiSettingsStore` 的 mtime 缓存里。
- 若将来 Avalonia 13 把 `ItemsRepeater` 并回核心且版本对齐，`VirtualizingStackPanel` 路线可以重试——前提是先修好「滚不到底」，判据用 `--scroll-check`。

### 20.4 本轮新增的取证工具

- `tools/perf-probe --scroll-check`（`PageSwitchProbe.ScrollCheck`）：逐 120 px 步进滚动、每步 3 次 Measure/Arrange，打印 `step / offset / nodes / expanders / extent`。虚拟化容器是否漏实体化，看末尾几组的 `offset` 能否走到 `extent - viewport`。
- `tools/ui-snapshot` 的 `CKANUI_SNAP_SEED=1`：跳过真实 `config.json` 复制、直接用仓库种子实例。真实实例被别的进程锁住时（`registry.locked` → `RegistryInUseKraken`）导航会中断，快照会停在同一页，此时必须用这个开关。
- `tools/perf-probe/diff-png.ps1 -A a.png -B b.png [-Tol 12]`：LockBits 逐像素比对，输出 `diffPixels/total/pct`、包围盒与按行分带（最多 20 段）。「视觉是否改变」用它做判据，不靠肉眼。

> 踩坑留证：第一次快照时 `page_17/18/19` 三张 PNG 字节数完全相同（114571），误以为是布局问题；加 `AppDomain.CurrentDomain.FirstChanceException` 打点后才看到 `IOException ... 'registry.locked'` —— **快照工具必须在实例被锁时仍能跑**，否则会拿到三张一模一样的「证据」。


## 21. 全 tab 换页成本实测表与结构膨胀门禁

用户要求「遍历所有 tab 找出所有性能问题」。这一节是当前 HEAD 的全量结果，也是 §20 之外的收口：**23 个导航页的成本已经全部量过，剩余项都是框架地板或有明确否决理由**。

### 21.1 口径

`perf-probe --pages-real`（真实 DI，真实数据源）逐导航键测五段：`resolve`（DI 解析）/ `activate`（`OnActivated` 刷新）/ `view`（`ViewLocator.Build`，有缓存）/ `layout`（挂到宿主 + Measure + Arrange）/ `render`（`ForceRenderTimerTick(16)`），末尾记 `nodes`（可视树节点数）。

- **`round=1` 是首次进入**，含 XAML 首次解析、模板首落地、字体首加载等一次性成本；
- **`round=2` 是再次进入**，视图已被 `ViewLocator` 缓存，只剩「重新挂到新父级 → 重新样式落地 → 重新量测」的固定成本——**这一列才是用户每次点 tab 感受到的卡顿**。

### 21.2 实测（HEAD=b4f0a19，单位 ms）

| 页面 | r1 view | r1 layout | r2 view | r2 layout | nodes |
| --- | ---: | ---: | ---: | ---: | ---: |
| settings | 7.4 | **817.6** | 0.0 | **912.6** | **2338** |
| mods | **194.8** | 214.2 | 0.0 | 25.8 | 146 |
| modpack-editor | 10.0 | 172.7 | 0.0 | 51.8 | 459 |
| modpacks | 14.9 | 168.7 | 0.0 | 21.2 | 202 |
| home | 6.0 | 82.4 | 0.0 | **95.2** | 167 |
| modtext | 5.5 | 73.8 | 0.0 | 34.1 | 261 |
| dedup | 11.7 | 25.1 | 0.0 | 59.1 | 154 |
| changes | 33.2 | 56.3 | 0.0 | 27.2 | 89 |
| cache | 5.1 | 52.6 | 0.0 | 28.4 | 137 |
| saves | 12.5 | 53.6 | 0.0 | 22.9 | 115 |
| 其余 13 页 | ≤27.2 | ≤48.9 | 0.0 | ≤24.3 | 51–185 |

`activate` 全页 ≤15.3 ms（logs 15.26、cache 12.14 最高），说明**「切页时把数据又重灌一遍」的问题已经在 §18 修完**，剩下的成本全在可视树落地。

### 21.3 归因：成本 ∝ 节点数，没有第二个热点

用 `--home-bisect`（每次「摘下来再挂回去」后量 Measure+Arrange）拆首页：

| 条件 | 耗时 |
| --- | --- |
| 全量（首次） | 232.4 ms |
| 全量（稳态） | **96.3 ms** |
| 隐藏截图胶片带 | 65.6 ms |
| 再隐藏 3 个 `ItemsControl` | 38.8 ms |
| 再隐藏 9 个 `Viewbox` | 50.9 ms（反向噪声） |
| 再隐藏 35 个 `TextBlock` | 37.2 ms |

→ 96 ms 分摊在胶片带（约 30 ms）、三组动作卡片（约 27 ms）和几十个文本/装饰节点上，**不存在单一热点**；167 节点 × 0.33 ms/节点 ≈ 55 ms 是地板，实测是它的 1.7 倍。这一倍数在其它页同样出现（mods 146 节点 25.8 ms、instances 185 节点 18.2 ms），属 Avalonia 重新挂载 + 样式落地的固定成本。

### 21.4 结论

- 用户点名的两处已经修完：设置页的**数据重灌**（§18 首次全量、后续增量）与实例页存档/飞船的**列表重建 + 缩略图超量解码**（§18.5/18.9）。
- 设置页剩下的 ~900 ms 是 2338 个可视树节点的框架地板，三条虚拟化路线全部会改视觉或改滚动（§20），**在「不影响视觉效果」的约束下不动**。
- 其余 22 页每次换页最贵 95 ms（home），多数 ≤30 ms，已是节点数地板。

### 21.5 门禁：`perf-probe --gate-pages`

节点数是**确定性指标**（同一份代码每次跑都是同一个数），所以用它做结构膨胀门禁，不受机器负载干扰：

```bash
TEMP=$PWD/artifacts/tmp/temp TMP=$PWD/artifacts/tmp/temp \
  ./tools/perf-probe/bin/Release/net10.0/perf-probe.exe --gate-pages
```

输出每页的「节点数 / 上限 / 结论」，任一页超限返回退出码 1。上限 = 当前实测 × 约 1.25（`tools/perf-probe/PageNodeGate.cs`，设置页单列 2500 并标注原因）。判据含义：**每多 100 个节点，多花约 33 ms**——一个页面悄悄多出 300 节点，就是每次切换多掉 6 帧。

### 21.6 稳态空转的三次排除：不是我们的定时器

在 HEAD=b4f0a19 上重测稳态空转（Release exe，42 s 预热 + 3 个 5 s 窗口，非侵入采样，单核 %）：**14.4/15.6/13.8、8.1/6.9/8.1**，WorkingSet 301 / 231 MB。

三次反向实验都没有把这 ~10–13% 压下去：

| 条件 | 单核 % | 结论 |
| --- | --- | --- |
| 基线 | 14.4/15.6/13.8；8.1/6.9/8.1 | — |
| `CKANUI_NO_REVEAL=1`（关滚动入场，`ScrollRevealMotion.Attach` 直接返回） | 9.7/10.9/5.0；25.9/13.1/15.3 | 同一量级，噪声内 |
| `DOTNET_GCgen0size=0x20000000`（加大 gen0 预算，验证是否分配churn 驱动的 GC） | 13.75/12.81/12.19；12.19/13.13/14.37 | 同一量级，不是 GC |

加上 §19 已做过的 `NO_STARFIELD` / `NO_LAYERS` / `NO_PERF` / `NO_ANIM`/`NO_RAIL`（当时把 47% 压到 12% 之后），可判定：**这 ~10–13% 单核不是本仓任何一个托管定时器在烧**，而是运行时/Avalonia 渲染侧的固定开销（§19.4：单条原生线程、窗口隐藏也不降）。32 核机器上折合整机 0.3–0.5%，不再追。

### 21.7 滚动分配的框架地板：67 KB/帧 跑不掉

`--mem` 一直报「滚动 300 帧 67,418 B/帧」。这一节回答它到底是「我们的行模板贵」还是「框架本来就这样」——新增 `perf-probe --scroll-floor`：3000 项纯文本 `ListBox`（无模板、无转换器、无绑定表达式），逐帧 `offset += 24 px`。

| 条件 | B/帧 | p50 | p99 | 可视树节点 |
| --- | ---: | ---: | ---: | ---: |
| 只改 offset、直接 Measure/Arrange（**错误口径**） | 619 / 692 | 0.01 ms | 0.03–0.09 ms | 126（全程不变） |
| 每帧标脏 `InvalidateMeasure` 后重排 | **52,627** | 1.15 ms | 6.86 ms | 126（虚拟化只留可见行） |

**先记一个测量陷阱**：视口位移本身不会自动触发重排，直接调 `Measure/Arrange` 是空转——第一次量出 619 B/帧、p50 0.01 ms、可视树节点数从头到尾不变 126，全是假的。必须每帧 `InvalidateMeasure()` 才会真的实体化新行。以后量任何滚动成本，先看「节点数有没有变 / p50 是不是 0」。

校准结论：**纯框架地板的滚动分配就是 5.3 万 B/帧**，我们带 DataGrid + 行模板 + 徽章的真实列表是 6.7 万 B/帧（§16 门禁实测 67,281）——只多 28%，不是十倍。按 60 fps 折算约 4 MB/s，正是虚拟化列表每帧重排的固有分配；要再降只能换更轻的行模板（改视觉）或换容器（§20 已否决）。`--gate-mem` 的 90,000 B/帧 阈值因此是合理的退化护栏，不是目标值。

### 21.8 HEAD 上已知的两例红（不是性能改动引入）

在 b4f0a19 的全量 3514 例里有 2 例失败，`git log -- <文件>` 追踪到都来自并行 agent 的 `f0d8f81 feat(motion): 全站交互动效改为帧驱动弹簧并补齐入场编排`，与本轮 perf 提交无关：

| 用例 | 断言 | 实测 |
| --- | --- | --- |
| `CkanUI.App.Tests.NavPanePushTests.ExpandedPane_PushesContentAside` | `Expected: greater than 200.0d` | `-1.0d` |
| `CkanUI.App.Tests.StarfieldModelTests.RefreshPointer_HighlightsOnlyTheNeighbourhood` | `Expected: 1` | `0.48758360911756443` |

其余 3512 例通过（App 437/439、Architecture 27、Core 3、Localization 76、ModText 19、Services 1243 + 1 Skip、Sfs 93、ViewModels 1613）。判定口径：用 `git log --oneline -4 -- <失败用例文件与实现文件>` 看最后一次提交是不是自己——不是就不要去改别人的语义，报给对应 owner。

## 22 勾选一个模组：172 ms → 4.8 ms（按钮级）

用户报的「掉了帧」里有很大一块不在切页，而在**点复选框**。3,000 行的模组列表里点一下勾选，旧实现要把整表换掉。

### 22.1 根因
`src/CkanUI.ViewModels/Pages/ModsPageViewModel.cs` 的 `_rows` 是 `[ObservableProperty] IReadOnlyList<ModsRowView>`，`ToggleSelection` 里写的是：

```csharp
Rows = Rows.Select(view => string.Equals(view.Identifier, identifier, StringComparison.OrdinalIgnoreCase)
    ? view.WithSelected(_selected.Contains(identifier))
    : view).ToList();
```

赋值即触发 `ItemsSource` 变更 → DataGrid 把 3,000 行全部作废重建（同时丢滚动位置），外加 3,000 个不可变记录的 `with` 复制。注释写的是「只刷勾选状态，不重建整表」，实际做的正好相反。

### 22.2 改法
- `_rows` 改为 `ObservableCollection<ModsRowView>`（`Rows` 仍是 `IReadOnlyList` 语义，绑定不变）。
- `ToggleSelection` 改为按下标就地替换：`Rows[i] = Rows[i].WithSelected(selected);` —— `Replace` 通知只让 DataGrid 刷新那一个行容器。
- 过滤、清空选择、应用变更这类「全表变化」的路径仍然整体替换（语义正确，不必优化）。

### 22.3 实测（`perf-probe --mods-toggle`，真实 DataGrid + 3,000 行，5 轮）
| 路径 | p50 | max | 每次分配 |
| --- | ---: | ---: | ---: |
| 旧：`grid.ItemsSource = 新列表` | **172.45 ms** | 185.49 ms | **16,837,233 B** |
| 新：`Rows[i] = ...`（ObservableCollection） | **4.76 ms** | 24.39 ms | **400,742 B** |

36× 时间、42× 分配。172 ms 的一次点击正好是一帧掉三下的量级——这就是用户能看见的「点一下卡一下」。

### 22.4 防回归
`tests/CkanUI.ViewModels.Tests/ModsPageViewModelTests.ToggleSelection_UpdatesRowInPlaceWithoutReplacingCollection`：断言勾选后 `vm.Rows` 仍是同一个集合实例、未勾选行的对象引用不变、被勾选行 `IsSelected` 为真。只要有人改回「整体换表」这条用例立刻红。

### 22.5 一个重复踩到的坑
给探针加 `--mods-toggle` 时首次构建失败（`CS0102: 类型 PageSwitchProbe 已经包含 ProbeRow 的定义`），但旧的 exe 还在，`--gate-mem` 照样打印 ALL PASS。**改探针后必须先看 `BUILD=` 再看数据**，否则读的是上一次的二进制。

## 23 排序与过滤换表的成本：三条提速路线全部无效

模组列表每点一次列头排序、每敲一段搜索（防抖后）、每换一次过滤器，都会走 `ApplyFilter` → `Rows = 新集合` → DataGrid 换 `ItemsSource`。§22 把「勾选」从换表改成就地替换后，这条换表路径只剩排序/过滤在用，于是先把它的成本量清楚。

### 23.1 换表 vs 就地 Move（`--grid-reorder`，真实 DataGrid + 3,000 行，4 轮）
| 路径 | p50 | max | 每次分配 |
| --- | ---: | ---: | ---: |
| `grid.ItemsSource = 新集合`（当前实现） | 244.75 ms | 338.94 ms | 17,306,796 B |
| 同一集合上按目标顺序做 `Move`（3,000 次通知） | 186.27 ms | 231.46 ms | **399,540 B** |

Move 省掉 43× 分配，却只快 24%——时间不在分配上，而在 3,000 次集合通知各自触发的行重排。**排序改成就地 Move 不划算**（复杂度上去，仍然 186 ms）。

### 23.2 固定行高（`--grid-rowheight`）
| 配置 | 换表 p50 | 行高 |
| --- | ---: | ---: |
| 默认（逐行量高） | 160.88 ms | 32.0 |
| `grid.RowHeight = 32` | 181.95 ms | 32.0 |

无收益，说明耗时不在行高估算。

### 23.3 固定列宽（`--grid-colwidth`）
| 配置 | 换表 p50 | 列宽 |
| --- | ---: | ---: |
| `Auto` 列宽 | 159.12 ms | 105 |
| `Star` 列宽 | 144.02 ms | 1280 |

只快 9%，且 Star 会改列宽视觉——不采纳。

### 23.4 结论
DataGrid 换 `ItemsSource` 的 **150–250 ms 是控件自身成本**，与行高、列宽、集合类型都无关；可视树里始终只有 ~20 个行容器（nodes=740），所以也不是「行太多没虚拟化」。在不换控件、不改列宽视觉的前提下，这条路径本轮无解。

留给后续的选项（都需要评估视觉或交互改动，本轮不做）：
1. 搜索/排序结果延迟到输入空闲再换表，并给列表加一层遮罩淡入（会改变交互手感）。
2. 换掉 DataGrid，用 ListBox + 自绘表头（视觉重构）。
3. 把排序交给 `ICollectionView` 的 `SortDescriptions`，让控件自己做增量排序（能否绕开重置未经验证）。

## 24 结果未变时不换表：省掉事件驱动的整表重建

§23 证明 DataGrid 换 `ItemsSource` 的 150–250 ms 压不下去，那就只剩「不换」。`ApplyFilter` 每次都会重建 `ObservableCollection`，但这条路径除了用户真正改搜索/排序/过滤器，还会被一批事件触发：仓库刷新完成、勾选后重算派生值、`ClearSearch` 之类。这些触发算出来的行列表经常和当前完全一致——此时换表纯属白付 150–250 ms。

### 24.1 改法（`src/CkanUI.ViewModels/Pages/ModsPageViewModel.cs`）

```csharp
if (Rows.Count != views.Count || !Rows.SequenceEqual(views))
{
    Rows = new ObservableCollection<ModsRowView>(views);
}
```

`ModsRowView` 是 `sealed record`，`SequenceEqual` 走值相等，3,000 行的比较成本远低于一次换表（毫秒级 vs 百毫秒级）。

配套改动：`SelectedRow` 的重新解析从 `views.FirstOrDefault(...)` 改成 `Rows.FirstOrDefault(...)`。这是必须的——跳过换表时 `views` 里都是新构造的对象、不在集合里，直接把那种对象赋给 `SelectedRow` 会让 DataGrid 的选中态和详情面板对不上。

### 24.2 覆盖

- `ApplyFilter_WithUnchangedResult_KeepsRowCollectionInstance`：同数据二次 `ApplyFilter` 断言 `Rows` 是同一实例。
- `ApplyFilter_WithChangedResult_ReplacesRowCollection`：搜索词收窄后断言集合被换掉且只剩 1 行。

### 24.3 边界

- 勾选路径已在 §22 改成就地替换，不经过这里。
- 「结果相同但某行的派生显示值变了」不会漏更新：`ModsRowView` 是值记录，显示值变了就不相等，必然换表。

## 25 首帧之后预建主页面视图：把第一次点「模组」的 100ms 挪到空闲里

`--pages-real` 里模组页是唯一「首次进入 view 就要 195ms」的页面（§21.2：mods r1 view=194.8 / r2 view=0），因为它是全应用最重的视图：DataGrid 列模板 + 单元格模板 + 徽章。用户从主页第一次点「模组」时，这 195ms 全落在点击上；之后靠 `ViewLocator` 的静态缓存复用，再点就不花了（`src/CkanUI.App/ViewLocator.cs:18-21` 的 `ConditionalWeakTable` 缓存，:29-33 的复用条件 `Parent is null && GetVisualParent() is null`）。

### 25.1 改法

- `src/CkanUI.App/App.axaml.cs`：`window.Opened` 里用 `DispatcherTimer.RunOnce(PrefetchFirstNavigationView, 1200ms, DispatcherPriority.ApplicationIdle)` 起一次预建。1200ms 是给首帧渲染留的余地——太早会和首帧抢队列。
- `PrefetchFirstNavigationView()`：从容器取 `ModsPageViewModel`（启动时已构造，见 §12），调 `new ViewLocator().Build(mods)`，只构建控件树交给静态缓存，**不挂窗口、不触发入场动效**；失败只记 Warn（纯优化路径，不允许影响导航）。
- `src/CkanUI.App/ViewLocator.cs`：新增 `public static bool HasCachedView(object viewModel)`。预建前先判定：用户若已经自己点过模组页，缓存里的控件已挂到可视树上、不再满足 `Build` 的复用条件，这时再 `Build` 会白造第二个视图并把缓存覆盖成孤儿控件，必须跳过。

### 25.2 实测（Release，`tools/perf-probe/Measure-Startup.mjs --hold-ms 3000`，各 3 轮）
| 组 | first-paint | prefetch-mods-view | 该段耗时 |
| --- | ---: | ---: | ---: |
| 未加预建 | 1331 / 1447 / 1471 ms | — | — |
| 加预建（ApplicationIdle 立即） | 1359 / 1402 / 1349 ms | 1471 / 1519 / 1469 ms | 109 ms |
| 加预建（延后 1200ms） | 1379 / 1397 / 1382 ms | 2497 / 2513 / 2518 ms | **83 / 90 / 91 ms** |

首帧时间在噪声内不变；预建本身的代价 83–91ms 发生在首帧后约 1.1s、`ApplicationIdle` 优先级（队列空了才跑）。

### 25.3 为什么是「省一次点击」而不是「省总时间」
总 CPU 一分没少，省的是**关键路径上的等待**：这 83–91ms 从「用户点模组 → 视图出现」挪到了「窗口已静止、用户在读主页」。`--pages-real` 量到的 r1/r2 差（194.8 → 0）就是预建后第一次点击能省下的量级上限。

### 25.4 工具
`Measure-Startup.mjs` 新增 `--hold-ms <ms>`：等到 `first-paint` 之后再保持进程一段时间，用来观察首帧之后的后台工作（不等待的话脚本在首帧就杀进程，看不到这类分段）。

## 26 长时驻留与空转：收敛，不泄漏

「内存会不会越用越大」不能靠几十秒的样本回答。新增 `tools/perf-probe/Measure-Soak.mjs`：起 Release exe（不抢前台、不动光标），按固定间隔读 `WorkingSet` / `PrivateMemorySize` / `TotalProcessorTime` / 线程数，跑满时长后输出 min/max 与收敛检查。

### 26.1 一次 216 秒空转采样（Release，`--minutes 4 --sample-ms 10000`）
| 时间 | 工作集 | 私有 | 线程 | 累计 CPU |
| ---: | ---: | ---: | ---: | ---: |
| 10 s | 325.5 MB | 300.0 MB | 142 | 9.3 s |
| 21 s | 293.8 MB | 260.9 MB | 142 | 11.1 s |
| 41 s | 299.8 MB | 266.2 MB | 111 | 13.6 s |
| 62 s | 306.1 MB | 272.5 MB | 108 | 15.5 s |
| 82 s | 312.2 MB | 277.9 MB | 106 | 18.4 s |
| 103 s | 312.2 MB | 277.9 MB | 106 | 20.8 s |
| 144 s | 312.2 MB | 277.8 MB | 103 | 25.2 s |
| 216 s | 312.3 MB | 277.9 MB | 103 | 32.8 s |

结论：
1. **驻留在 82 秒后完全平台化**——之后 130 秒里工作集只动了 0.1 MB，没有缓慢增长。启动期的 325 MB 是加载瞬时峰值，不是稳态。
2. **线程数在收缩**（142 → 103），不是每个周期新起线程。
3. **空转 CPU 全程线性**：206 秒里消耗 23.5 s CPU ≈ **11.4% 单核**，与 §21.6 的 10–13% 一致；这一档已经用 `NO_STARFIELD` / `NO_LAYERS` / `NO_PERF` / `NO_ANIM` / `NO_RAIL` 逐一排除过，是渲染侧固定开销。
4. 本轮没有触发 `IdleMemoryReclaimer`（增长门限 128 MB 未达到），也就是说这个稳态不是"回收器压出来的假象"。

### 26.2 用法
```
node tools/perf-probe/Measure-Soak.mjs --exe C:/.../CkanUI.exe --minutes 4 --sample-ms 10000 --out artifacts/perf/soak.json
```
输出 `[Soak] workingSet min/max`、`[Soak] 单核 CPU min/max` 与收敛检查（预热后首末样本差）。判据：**预热后末段工作集的斜率应接近 0**；若末段仍在单调上行，才是泄漏信号。

## 27 带真实数据的换页成本：空数据口径低估了 3 倍

§21 的全 tab 表是**空数据**口径（探针的 `HeadlessAppHost` 会把 `CKAN_CONFIG_FILE` 指向一次性空文件，页面读到空集合）。真实用户在真实实例上点同一个 tab，可视树按行数膨胀，成本完全不同。本节给的是带数据口径。

### 27.1 怎么量到带数据的页面
两处开关：
1. `tools/perf-probe/HeadlessAppHost.cs`：只有未设 `CKANUI_PROBE_REAL_CONFIG=1` 时才把 `CKAN_CONFIG_FILE` 覆盖成临时空文件——设了就读用户自己的 `%LOCALAPPDATA%/CKAN/config.json`。
2. `tools/perf-probe/PageSwitchProbe.cs` 的 `Run` 开头：`CKANUI_PROBE_INSTANCE=1` 时 `RefreshAsync()`，当前实例为空则 `SwitchAsync(Instances[0].Name)`，并打印 `[PagesReal] instance=… count=… dir=… modules=…`。

```
CKANUI_PROBE_REAL_CONFIG=1 CKANUI_PROBE_INSTANCE=1 TEMP=… TMP=… \
  ./tools/perf-probe/bin/Release/net10.0/perf-probe.exe --pages-real
```

本次实测实例：`Kerbal Space Program_newmod`（`D:/Kerbal Space Program newmod`，`modules=105`）。探针只读不写：`RefreshAsync` 只重投影、`SwitchAsync` 遇到 `registry.locked` 会返回失败消息，不改用户配置。

### 27.2 全 23 键实测（含数据；r1=首次进入，r2=再次进入，layout 单位 ms）
| 页面 | r1 layout | r2 layout | 节点 |
| --- | ---: | ---: | ---: |
| settings | 590.5 | 752.8 | 2346 |
| mods | 1089.6 | 314.4 | 2540 |
| unmanaged | 13.4 → 384.3 | 384.3 | 103 → 1167 |
| saves | 389.9 | 150.7 | 517 |
| flags | 217.5 | 93.0 | 522 |
| ships | 166.4 | 65.6 | 391 |
| home | 85.5 | 92.7 | 225 |
| modpack-editor | 165.3 | 46.0 | 459 |
| instances | 203.7 | 49.1 | 296 |
| changes | 180.5 | 43.1 | 89 |
| modtext | 36.6 | 39.3 | 261 |
| cache | 42.4 | 36.4 | 137 |
| installhistory | 39.2 | 35.1 | 93 |
| repos | 41.4 | 27.0 | 128 |
| modconfig | 57.9 | 26.0 | 146 |
| modpacks | 143.8 | 26.2 | 202 |
| dedup | 27.0 | 20.8 | 171 |
| loadingimages | 39.3 | 19.3 | 165 |
| plugins | 21.1 | 15.5 | 100 |
| logs | 35.6 | 14.2 | 126 |
| content | 25.9 | 14.6 | 84 |
| downloadstats | 11.3 | 6.8 | 84 |
| playtime | 6.0 | 4.6 | 51 |

### 27.3 结论
1. **空数据口径低估 3 倍以上**：mods 146 → 2540 节点、unmanaged 103 → 1167、saves 115 → 517、flags 87 → 522。§21 的 `PageNodeGate` 阈值是按空数据定的（unmanaged 上限 80），带数据必然超限——它是「结构膨胀」护栏，不是行数护栏，判读时要配本节。
2. **成本仍然 ∝ 节点数**：1167 节点 × 0.33ms ≈ 385ms，与 unmanaged 实测 384.3ms 一致；2540 节点 ≈ 840ms，与 mods 1089ms（首轮含模板实例化）同量级。
3. **新暴露的三个数据驱动热点**（空数据时完全看不见）：`unmanaged`（文件树 1167 节点，每次进入 384ms）、`mods`（DataGrid + 详情侧栏 2540 节点）、`saves`（517 节点 150ms）。它们不是重建问题——`view=0` 说明视图是复用的，384ms 全在 Measure/Arrange。
4. `modconfig` 的 `activate=93.4ms` 与 `changes` 的 `activate=55.8ms` 是另一类：进页面就重读盘（`ModConfigPageViewModel.LoadAsync` 每次 `_config.LoadAsync`），不在 layout 里。

### 27.4 下一步（按收益/风险排序）
| 目标 | 手段 | 风险 |
| --- | --- | --- |
| unmanaged 384ms | 树默认不收全展开 / 只实体化可见层（需先确认 `UnmanagedPageViewModel` 是否在加载时就展开全部节点） | 默认展开状态是可见行为，需先取证是不是它 |
| mods 314ms | 详情侧栏随选中才构建（现在是每次都铺满） | 会改首屏右侧栏内容，属视觉 |
| modconfig 93ms | 按 registry + ModuleManager 配置文件指纹复用上次结果 | 与 §18 存档校验同型，安全 |

### 27.5 否定实验：给未管理页的 TreeView 换虚拟化面板
`src/CkanUI.App/Views/UnmanagedPageView.axaml` 的 `TreeView` 加
`<TreeView.ItemsPanel><ItemsPanelTemplate><VirtualizingStackPanel /></ItemsPanelTemplate></TreeView.ItemsPanel>`，
带真实实例重测：

| 组 | 节点 | r1 layout | r2 layout |
| --- | ---: | ---: | ---: |
| 基线 | 1167 | 384.3 | 384.3 |
| 加 VirtualizingStackPanel | 661 | 558.9 | 386.6 |

节点少了 43%，布局时间没有变化（386.6 vs 384.3，同噪声带）。说明未管理页的 384ms 不是「实体化了太多行」，
而是这些行本身在 Measure/Arrange 里就要花掉——与 0.33ms/节点 的固定单价一致（661 × 0.33 ≈ 218，剩余部分是树容器的层级开销）。
按「不影响视觉效果」约束，改动已回退（`git checkout -- src/CkanUI.App/Views/UnmanagedPageView.axaml`）。
要真正压下去只能减少行的可见数量（分页/懒展开），那是产品行为变更，不在本约束范围内。

### 27.6 配置页不再每次进页面都重扫适配器（modconfig activate 93.4ms → 3.0ms）
带真实实例时 `modconfig` 页每次激活的 `activate` 段是 93.4ms：`ModConfigPageViewModel.OnActivated` 无条件走
`LoadCommand` → `IModConfigService.LoadAsync` → `ModConfigService.LoadCore` 逐个 adapter 重读源文件。
适配器源文件没变、注册表没变时，这次重扫是纯粹的重复劳动。

改法（三层都按"用户显式刷新才是权威"的原则）：
- `src/CkanUI.Services.Abstractions/ModConfig/IModConfigPorts.cs`：`LoadAsync(string instanceName, bool forceReload = false, CancellationToken cancellationToken = default)`。
- `src/CkanUI.Services/ModConfig/ModConfigService.cs`：缓存键 = 实例名 + GameDir + GameVersion + 注册表条目数；构造里订阅 `_registry.Changed` 把缓存键清空（安装/卸载/刷新注册表立刻失效），`forceReload: true` 直接绕过缓存。
- `src/CkanUI.ViewModels/Pages/ModConfigPageViewModel.cs`：`[RelayCommand] LoadAsync()` 变成 `LoadCoreAsync(forceReload: true)` 的薄壳（「刷新」按钮与 `RetryCommand` 仍强制重读），`OnActivated` 改走 `LoadCoreAsync(forceReload: false)`，写盘之后的 `RefreshModelAsync` 用 `forceReload: true` 保证读到刚写的值。

实测（`--pages-real modconfig`，真实实例 105 模块，同 exe）：

| 轮次 | activate | layout | 节点 |
| --- | ---: | ---: | ---: |
| 改前 r2 | 93.4 | 26.0 | 146 |
| 改后 r2 | **3.0** | 81.1 | 146 |
| 改后 r1 | 105.4（首次仍要真扫） | 305.7 | 146 |

代价说明：别的进程直接改 `.cfg` 而注册表没动时，页面会显示上一次的结果，用户点「刷新」即可——
这比"每次进页面都扫 90ms"更符合实际使用；注册表变化（安装/卸载/更新）仍会自动失效。

## 28. 带真实实例的全 tab 收口（HEAD 0774328）

### 28.1 口径
`CKANUI_PROBE_REAL_CONFIG=1 CKANUI_PROBE_INSTANCE=1 perf-probe.exe --pages-real`：探针读真实 CKAN 配置、
切到 `Kerbal Space Program_newmod`（D:/Kerbal Space Program newmod，105 个模块）后逐页往返两轮。
round2 就是用户每次点同一个 tab 的成本（round1 含首次解析与首次建视图）。机器：32 核，采样时并行编译在跑。

### 28.2 round2 实测（ms）
| 页面 | activate | layout | render | 节点 |
| --- | ---: | ---: | ---: | ---: |
| settings | 1.41 | 872.6 | 5.1 | 2346 |
| unmanaged | 0.00 | 421.5 | 14.6 | 1167 |
| mods | 3.82 | 319.5 | — | 2540 |
| saves | 0.60 | 144.7 | — | 517 |
| home | 1.27 | 129.6 | 1.7 | 225 |
| flags | 0.61 | 96.4 | — | 522 |
| instances | 3.60 | 85.2 | — | 296 |
| ships | 0.32 | 84.8 | — | 391 |
| dedup | 1.09 | 60.3 | 0.4 | 171 |
| modconfig | 1.57 | 46.6 | 0.4 | 146 |
| changes | 2.66 | 42.9 | — | 89 |

结论：round2 的 `activate` 全页已在 5ms 以内（mods 3.82 最高）——切页重灌数据的问题已收干净，
剩下的全是 **可视树落地** 成本，单价约 0.12–0.37ms/节点。

### 28.3 本轮新增两处
- `changes` 页 activate 53.3 → **2.66ms**：`IGameProcessProbe` 的 `GameProcessProbe` 每次调用都要遍历全部进程读
  MainModule（≈50ms），配置页/变更页/实例页都会在激活时问一次。给它加 1 秒结果复用窗口
  （`public static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(1);`，可测构造注入 scan/clock），
  同一秒内重复问不再扫进程表；最多迟一秒感知游戏启动/退出。
- `modconfig` 页 activate 93.4 → **1.57ms**（见 §27.6）。

### 28.4 为什么不动 settings / unmanaged / mods
- settings 2346 节点：36 行 `FASettingsExpander`（每行 65 节点）。三条虚拟化路线全部取证否决（§20），
  ItemsRepeater 在 Avalonia 12.1.4 已移出核心且独立包最高 12.0.0。
- unmanaged 1167 节点：给 TreeView 换 `VirtualizingStackPanel` 后节点 1167→661、layout 384→387ms 不变（§27.5）——
  成本在行本身的 Measure/Arrange，不在实体化行数。
- mods 2540 节点里 `DataGridRow` 只有 20 个（虚拟化正常），`Rectangle` 804 个来自 DataGrid 默认单元格的焦点框模板；
  换行高/列宽/集合类型都不动它（§23）。

### 28.5 门禁与回归
`--gate-mem` 全部 PASS（滚动 97675 B/帧 ≤130000、首屏后 212.4MB ≤260、峰值 228.4MB ≤300、光栅档空转 7.70% ≤22、线程 9 ≤24）；
`--gate-pages` ALL PASS；solution 构建 0 警告 0 错误；TestRunner 3525 例 0 失败（1 例显式 Skip）。


## 29. 换页成本归零：页面视图常驻 + 只切可见性

### 29.1 问题
`PageHost` 是 `TransitioningContentControl`，`Content` 绑 `ShellViewModel.CurrentPage`。换页 = 换 `Content` = 旧视图从可视树摘下、新视图重新挂上。
Avalonia 的 `Measure`/`Arrange` 缓存挂在可视树上，**一旦 detach 就全失效**，下次挂回去要整棵重新排版。

### 29.2 证据（headless，真实实例 Probe (Steam mirror)，modules=336）
`perf-probe --visibility-cost settings`：

| 观测 | settings（2339 节点） | ships（101 节点） |
| --- | ---: | ---: |
| 首次 attach（含创建） | 523.6 ms | 251.9 ms |
| 已挂树 → `IsVisible=false` | 0.9 ms | 0.3 ms |
| 已挂树 → `IsVisible=true` | 1.4 ms | 0.1 ms |
| detach → 再 attach | 1793.5 ms | 23.5 ms |

- **只翻 `IsVisible` 是 1–7 ms，detach + attach 是同页 20–2691 ms**（差距 170–260 倍）。
- `ViewLocator` 的静态缓存救不了这件事：它只在 `cached.Parent is null && GetVisualParent() is null` 时复用，而重新挂树那一刻整棵子树的 measure 已经失效 —— 复用的只是 ctor + DataContext（settings 约 45 ms），布局一分不少。
- 另一条独立取证（`--phase`）：settings 的 `host.Measure()` 读到 0.0 ms，真正测量发生在 Dispatcher 排队的布局 pass（`flush=2621 ms`）。所以「在测量里加打点」的方案在本仓库量不到东西。

### 29.3 改法
- `src/CkanUI.App/MainWindow.axaml`：`<TransitioningContentControl x:Name="PageHost" Content="{Binding CurrentPage}" />` 换成固定宿主
  `<TransitioningContentControl x:Name="PageHost"><Panel x:Name="PageStage" /></TransitioningContentControl>`。
- `src/CkanUI.App/MainWindow.axaml.cs`：
  - `private readonly Dictionary<object, Control> _pageViews = new(ReferenceEqualityComparer.Instance);`（VM → 视图）与 `private Control? _activePageView;`。
  - `ShowPage(object? page)`：首次见到的 VM 用 `new ViewLocator().Build(page)` 建一次并 `PageStage.Children.Add(view)`；之后只把目标子项 `IsVisible = true`、其余 `false`。**任何视图都不再从树上摘下**。
  - 构造函数不再订阅 `PageHost.PropertyChanged`（Content 恒定），改成 `OnShellPropertyChanged` 里监听 `CurrentPage` → `ShowPage`；`AttachShell()` 末尾补一次 `ShowPage(_shell.CurrentPage)`（构造期已有值时不会有变更通知）。
  - `PlayPageReveal()` 的动效根从 `PageHost` 改成 `_activePageView ?? PageHost`：其它页面视图此时也常驻在 `PageStage` 里（只是不可见），用 `PageHost` 当根会把所有页面一起扫、一起编排。
- 视觉零变化：切页仍是「旧页立刻消失、新页按原入场编排出现」，只是新页不再重建。

### 29.4 真机 A/B（Release，真实实例 Probe (Steam mirror)，modules=336，ANGLE/D3D11）
同一份 `NavigationProbe`（`CKANUI_NAV_TRACE`）遍历 24 个导航键两轮，R2 = 用户第二次点同一个 tab：

| 页面 | 基线 R2 layout / maxGap | 常驻后 R2 layout / maxGap |
| --- | --- | --- |
| settings | 954.1 / 572.1 ms | **29.2 / 20.9 ms** |
| ships | 148.6 / 53.2 ms | **23.2 / 19.2 ms** |
| saves | 93.4 / 44.3 ms | **48.7 / 46.6 ms** |
| home | 28.5 / 12.8 ms | 43.1 / 6.0 ms |
| mods | 393.6 / 277.6 ms | 360.3 / 317.3 ms |

- settings / ships / saves 是稳定量级下降；mods 与 home 落在噪声带（当时机器上还有 3 路并行 agent 在编译，同一 exe 的单页 layout 可在 2–3 倍间漂移）。
- **首次进入（R1）不变**：第一次见到的页面仍要创建 + 排版（settings R1 中位 400–560 ms）。R1 的优化方向是把首次排版挪到空闲期预热（见 29.6）。

### 29.5 视觉取证（同树 A/B）
`tools/ui-snapshot`（`CKANUI_SNAP_SEED=1`，24 页）+ `tools/perf-probe/diff-png.ps1 -Tol 12`，跑「常驻两次」与「退回单视图挂载一次」三组快照：

| 页面 | 常驻 vs 常驻（噪声基线） | 常驻 vs 单视图 |
| --- | ---: | ---: |
| settings | 0.861% | 0.821% |
| home | 2.697% | 1.754% |
| mods | 0.495% | 0.418% |
| instances | 0.518% | 0.447% |
| saves | 0.533% | 0.475% |

两组差值同量级且方向一致（差异全部来自星空背景动画的相位），**内容区逐像素一致**。

### 29.6 代价与已知限制
- 内存：视图常驻意味着各页可视树不再随换页释放。稳态工作集变化在噪声内（各页本来也被 `ViewLocator` 缓存着，只是不挂树）。
- 窗口尺寸/主题变化会让所有常驻视图的 measure 失效，**resize 之后第一次切回某页仍要付一次该页的重排**（与改造前等价，不是回退）。
- 首次进入未改善（见 29.4）；`kspconfig` 首轮仍可到 2–3 s（首次读游戏设置文件，属数据加载）。
- `PageStage` 里所有视图都参与「命中测试」？不会：`IsVisible=false` 的子项不参与命中测试与渲染。

### 29.7 怎么验收
- `perf-probe --visibility-cost <key>`：`show`/`hide` 必须是**个位数 ms**；若又出现几百 ms，说明该页仍在 detach（常见原因：容器换成 `ContentPresenter`、或列表用了回收型面板）。
- `perf-probe --phase-toggle <key>`（探针来自并行 agent 的 `PhaseProbe.cs`）：`coldAttach` 与 `reattachAfterDetach` 的差值即常驻收益上限。
- 真机：`CKANUI_NAV_TRACE=<文件>` 启动应用，看每页 `[nav] rN <key> layout= maxGap=`；R2 应稳定落在几十 ms 内。

## §30 模组页：换页回来不再重建行投影

### 30.1 现象与归属
`PageStage` 常驻（§29）之后，模组页第二次点进去仍是全场最贵的一档。真机 `CKANUI_NAV_TRACE` R2（镜像实例，336 模组）：`layout=422.1ms maxGap=377.8ms`，而节点数与第一次相同——布局没有变贵，是**行投影被重算、DataGrid 重新落地容器**。

调用链：`ModsPageViewModel.OnActivated`（`src/CkanUI.ViewModels/Pages/ModsPageViewModel.cs:983`）→ `ReloadFromSnapshots()`（:1171）→ `ModsRowProjection.Merge(_repositories.Current, _registry.Current)` + `ApplyFilter()`。换页回来时两份快照一个字节都没变，这次重建纯属空转。

### 30.2 改法
`ReloadFromSnapshots(bool force = false)`：先比较 `(RepositorySnapshot, RegistrySnapshot, 实例名)` 三个引用/值，与上次成功重建时一致就直接返回；不一致才往下走，走完把签名记下。

`force: true` 用在 4 个「事实已变但不是快照事件」的入口：标签写入后（:777，标签不体现在快照引用上）、切实例（:1133）、手动刷新仓库索引后（:1963）、`TrimProjection()`（:1152 会清空 `Rows`/`_all`，必须把签名置无效，否则下次换页回来会跳过重建、显示空表）。

### 30.3 实测（真机 R2，同脚本同实例）
| 指标 | 改前 | 改后 |
| --- | ---: | ---: |
| `layout` | 422.1 ms | **252.7 ms** |
| `maxGap`（最长单帧） | 377.8 ms | **228.2 ms** |
| `assign`（激活） | 45.0 ms | 26.8 ms |

剩余 253 ms 属 `IsVisible` 翻转触发的整棵重排（Avalonia 在可见性变化时清 measure 缓存），与行投影无关。

### 30.4 为什么没继续压
把 `IsVisible` 换成「移出视口 / Opacity=0」会改变渲染路径（仍在画），违反「尽可能小影响视觉效果」；行投影本身在 336 模组下只有个位数 ms。

## §31 变更集页：请求集没变就不重算

### 31.1 归属（含一次自我推翻）
第一版把 `changes` 页 `activate≈900ms` 记成「依赖解析重算」。插桩实测推翻了这个判断：

```
[DbgActivate] subscribe=0.18 gameRunning=892.08 rebuild=7.13 total=899.39 requests=0
```

重算只占 7 ms，**892 ms 是 `RefreshGameRunning()` 的全进程扫描**——见 §32。请求集缓存在 `requests=0` 时本来就没重算，所以它不是那 900 ms 的原因。

### 31.2 仍然保留的改动
`RebuildFromRequests(bool force = false)`：`ReferenceEquals(Requests, _builtRequests)` 时直接返回。带请求时换页回来（模板页勾了模组 → 看变更 → 切走再回来）这一条能省掉整次 `BuildAsync`；`SetRequests` 与内部装载都会先换 `Requests` 引用，正常路径不受影响；`ClearRequests()` 把签名置 null。

### 31.3 证据
- 单测 `ChangesPageViewModelTests.Reactivation_WithSameRequests_SkipsRecompute`：两次 `Deactivate/Activate` 后 `BuildCount` 不变、`PendingWork` 仍是同一个 Task。
- 全量 ViewModels 1641 例全绿（唯一红是 Architecture 的 `KspConfigPageViewModel` 插值文案，他人未跟踪新增文件）。

### 31.4 口径提醒
`changes` 的耗时取决于**有没有待应用的请求**与**进程扫描**：不带请求的探针跑只会得到个位数 ms，不能拿它证明「这页不慢」；复测前先 `SetRequests`，并注意 `IsGameRunning` 那条路（§32）。

## §32 进程探测：从 MainModule 全扫换成 psapi 快路径

### 32.1 现象
`changes` / `modconfig` / 实例页激活时都会问一次「游戏在跑吗」：`ChangesPageViewModel.OnActivated` → `RefreshGameRunning()` → `InstanceService.IsGameRunning()` → `GameProcessProbe.GetProcessImagePaths()`（`src/CkanUI.Services/Instances/IGameProcessProbe.cs:54`，1 秒 TTL）→ `ScanProcesses()`。

原实现在 Windows 上遍历 `Process.GetProcesses()` 并读每个 `process.MainModule?.FileName`，本机 577 个进程、每个约 1.5 ms → **820–940 ms**，而且是同步的、直接压在激活后的那一帧上。

### 32.2 改法
Windows 快路径 `ScanWindows`：`EnumProcesses`(psapi) → 每个 pid `OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)` → `QueryFullProcessImageNameW`(kernel32)；非 Windows 或 `EnumProcesses` 失败整体回退 `ScanManaged`（原实现保留）。单进程打不开只是跳过，语义与旧实现「读不到就不算」一致。

### 32.3 实测（镜像实例 `Probe (Steam mirror)`，modules=336）
| 指标 | 改前 | 改后 |
| --- | ---: | ---: |
| `--pages-real changes` activate（n=4） | min 846.0 / median 859.2 / max 912.1 ms | **min 4.2 / median 18.6 / max 31.0 ms** |
| 全页探针 changes r1 / r2 | 937.3 / 892.9 ms | **26.9 / 18.5 ms** |
| 全页探针 modconfig r1 | 1860.8 ms | **52.4 ms** |
| 扫描实现基准（577 进程） | `MainModule` 842 / 821 / 829 ms | **2.8 / 1.7 / 1.6 ms** |
| 覆盖对照 | 旧实现读到 408 个 | 新实现 408 个、失败 0 → 零丢失 |
| `--gate-mem` 5 项 | — | 全 PASS（滚动 97,675 B/帧、WS 212.2 MB、峰值 228.1 MB、空转 11.6–14.6%、线程 9） |

### 32.4 视觉取证
23 页 `ui-snapshot` + `diff-png.ps1 -Tol 12`：before_vs_after 总差 125,561 px（均值 5,459/页、max 0.875%），而同一构建跑两次 after_vs_after2 的总差 137,737 px（均值 5,989/页、max 0.863%）——**改动差异小于运行间噪声**，band 结构同形且集中在顶部星空/hero 区。

### 32.5 未验证 / 风险
- 提权或其他用户身份启动的 KSP：`QueryFullProcessImageNameW` 依赖 `PROCESS_QUERY_LIMITED_INFORMATION`；本机 408/408 可读进程全成功，但未实测提权场景。读不到只是跳过，`EnumProcesses` 失败整体回退。
- 非 Windows 平台走原路径，未在 Linux/macOS 实测（行为应与改前一致）。

## §33 常驻页面不带来内存回归
`PageStage`（§29）让访问过的页面视图一直挂在可视树上，所以必须回答「驻留会不会涨」。同口径 soak（3 分钟，5 秒采样，镜像实例，窗口不抢前台）：

| 组 | WorkingSet | Private | 线程 | 空转 CPU |
| --- | --- | --- | --- | --- |
| 基线 `004150c`（单视图） | 445.9–459.3 MB | 399.1–411.8 MB | 103–110 | 0.0–0.2% 单核 |
| 常驻 + §30/§31 + §32 | 444.8–457.9 MB | 397.7–410.6 MB | 107–111 | 0.1–0.2% 单核 |

读取口径：两次都只停在首页、没有逐页访问，所以这只证明「常驻机制本身」不额外占地；逐页访问后的稳态见下轮补测。工作集比早期 §26 的 312 MB 高，是因为本轮跑的是真实 336 模组注册表（36 MB registry + 投影），不是空实例。

## §34 下载缓存页：目录扫描下放线程池，回来不再重读

### 34.1 现象
真机导航探针 `cache` 首次进入：`assign=7683.5ms`（导航调用本身被钉住 7.7 秒，期间 `paint=-1` 没有一帧）。

根因：`CacheService.GetSnapshotAsync` / `ListEntriesAsync`（`src/CkanUI.Services/Cache/CacheService.cs:31` / `:49`）体是同步目录枚举（`CkanCacheStore.ListEntries` → `Directory.EnumerateFiles` + 每个文件 `FileInfo`），返回 `Task.FromResult`，所以在 `await` 之前就把 UI 线程占满；而且两次调用各扫一遍。

### 34.2 改法（两处，都不改视觉）
1. 两个方法体包进 `Task.Run`：真正的异步，`IsBusy`/骨架遮罩先上屏，扫描在线程池。
2. `CachePageViewModel.OnActivated` 加 `_hasLoadedOnce` 门：下载缓存是全局的、不会因为换页自己变，首次进入自动读盘，之后回来直接看上次结果（页内「刷新」按钮仍在）。

### 34.3 实测（真机导航探针，镜像实例）
| 指标 | 改前 | 改后 |
| --- | ---: | ---: |
| `cache` R1 `assign`（导航被钉住的时间） | 7683.5 ms | **63.7 ms** |
| `cache` R1 `layout` | 7799.2 ms | 128.2 ms |
| `cache` R2 `layout` | 109.5–305.0 ms | **68.5 ms** |

### 34.4 代价
回来时不重扫 → 如果用户在别处刚下载过模组，缓存页数字要等点「刷新」才更新。这是刻意取舍：缓存页不是实时监控面，而 7.7 秒的冻结是硬伤。

## §35 空闲预热：把首次进入的一次性成本挪到用户没在看的时候

### 35.1 问题
§29 让「第二次点同一个 tab」降到 1–20 ms，但**首次**进入仍然贵：真机导航探针（镜像实例 336 模组）

| 页面 | 首次进入 layout | 其中最长单帧阻塞 | 探针二次进入 |
| --- | ---: | ---: | ---: |
| logs | 1694.7 ms | **879.4 ms** | 72.4 ms |
| settings | 425.0 ms | 389.9 ms | 16.7 ms |
| unmanaged | 141.4 ms | 74.5 ms | 293.1 ms |
| modpack-editor | 608.9 ms | 540.4 ms | 36.5 ms |

插桩确认这些不是「数据重算」：logs 首次 `Refresh()` 只占 **206 ms**（其中 `aggregate=125.5` 是首次 JIT，49 条事件），而导航调用本身被钉住 816 ms —— 主体是**一次性 JIT / 模板与样式首次落地**。

### 35.2 改法
首帧之后，用 420 ms 间隔的 `DispatcherTimer` 逐页把一个页面视图挂进 `PageStage`：`Opacity=0`、`IsHitTestVisible=false`、`IsVisible=true`，让它在真实尺寸下跑完一次完整 Measure/Arrange，然后在 `DispatcherPriority.ContextIdle` 把它藏起来（`IsVisible=false`、`Opacity=1`）。

- §29 已证明「已挂树、只切可见性」的重新显示只要 1–2 ms，所以预热过的页面首次点击就等价于二次进入。
- 用户正在点（最近 1.2 s 内有换页）时预热让位，不抢帧。
- 顺序按实测首次成本：`logs, settings, unmanaged, modpack-editor, mods, content, downloadstats, modpacks, ships, saves`。
- 旁路解析：`NavigationService.Peek(key)` + `ShellViewModel.PeekPage(key)`（`src/CkanUI.ViewModels/ShellViewModel.Warmup.cs`，单独 partial 文件），只解析页面 VM，不激活、不改当前页。

### 35.3 实测（同脚本同实例，先静置 14 s 让预热跑完再遍历）
| 页面 | 首次 layout 改前 → 改后 | 最长单帧 改前 → 改后 |
| --- | --- | --- |
| logs | 1694.7 → **97.9 ms** | 879.4 → **77.7 ms** |
| settings | 425.0 → **75.6 ms** | 389.9 → **13.0 ms** |
| unmanaged | 141.4 → **33.9 ms** | 74.5 → **5.0 ms** |
| modpack-editor | 608.9 → **10.3 ms** | 540.4 → **7.7 ms** |
| modpacks | 108.7 → **53.9 ms** | 51.5 → **54.1 ms** |

预热自身的一次性成本（后台，用户看不到）：logs 53 ms、settings **938 ms**、modpack-editor 136 ms、modpacks 31 ms，其余 ≤29 ms。设置页那 938 ms 是它整棵 2338 节点的一次排版，落在首帧后的空闲期。

### 35.4 视觉与内存
- 预热中的视图 `Opacity=0` 且不吃鼠标，跑完立刻 `IsVisible=false`；对正在运行的应用截屏（`artifacts/perf/warm-after.png`，1296×809）确认画面仍是当前页，没有浮出的预热页。
- 3 分钟 soak（同口径、镜像实例）：WS 419.7–435.9 MB / private 364.0–380.1 MB / 线程 103–111 / 空转 **0.0–0.1% 单核**，比不带预热的 444.8–457.9 MB 还低（预热只持有 10 个页面的视图，用户访问过之后本来就常驻）。

### 35.5 预热自身带来的启动后卡顿，以及按焦点收口

预热把成本从「用户点击的那一刻」挪到了「首帧之后」。用 `CKANUI_NAV_WATCH_MS`（`NavigationProbe` 新增）从 `Opened` 起连续采帧，量出这个过程里 UI 线程的实际冻结：

```
[nav] watch frames=1008 over50=9 maxGap=828.4 top=t=1395ms gap=828ms, t=15824ms gap=452ms, t=294ms gap=171ms
[warm] logs attach=52ms / settings attach=843ms / unmanaged attach=15ms / modpack-editor attach=154ms / ...
```

即**窗口打开后约 1.4 秒会冻结 0.8 秒**——那是设置页 2338 个节点的一次性排版。这比「点设置卡 0.4 秒」更难解释，所以给它加了一道焦点门：

- `ExpensiveWarmups = { "settings" }`，只有这类页面会一次性吃满 UI 线程。
- `MainWindow.ShouldWarmExpensive(bool windowActive, TimeSpan sinceNavigation)`：窗口在后台，或前台静置超过 `ExpensiveWarmIdle = 45s`，才允许预热重页面。
- 窗口切后台时 `OnWarmWindowPropertyChanged` 立刻把定时器叫醒（不必等满 420 ms 节拍）；`WarmIdleTickLimit = 150` 防止为一个页面永远轮询。

代价写清楚：**窗口一直前台活跃时，设置页不会预热**，首次点它仍要 425 ms（对比预热后的 76 ms）。这是有意选择——830 ms 的无预警冻结比一次点击等待更糟。轻页面（logs 52 ms / mods 74 ms / modpacks 47 ms 等，全部 ≤154 ms）不受影响，照常在首帧后预热。

判定逻辑有单测覆盖：`tests/CkanUI.App.Tests/PageWarmGateTests.cs`（后台一律允许 / 前台 3 s 不允许 / 前台 60 s 允许）。注意本机探针启动的窗口不是前台窗口，所以上面那 828 ms 的采样记录的是「后台窗口」的情形，属于设计内行为；前台路径由单测而非实机采样证明。

### 35.6 详情面板四个子页签的成本

`tools/perf-probe --detail-tabs`（`PageSwitchProbe.DetailTabSweep`）在真实实例下选中一行模组，再把 `ModDetailsView` 的四个内联 `TabItem` 逐个切过去（headless，ms）：

| 页签 | 第 1 轮 layout | 第 2 轮 layout | 节点数 |
| --- | ---: | ---: | ---: |
| 元数据与简介 | 0.0 | 55.8 | 105 |
| 依赖与关系 | 34.0 | 18.2 | 43 |
| 版本 | 17.4 | 17.1 | 46 |
| 已安装文件 | **101.2** | **36.8** | 52 |

四个页签是内联 `TabItem`（不是 `ItemsSource`），切换不重建控件实例，所以成本只有该页内容的首次测量（101 ms 那次是「已安装文件」列表的首次落地）。节点只有 43–105，没有可压缩的结构；继续改只能动视觉，按铁律不做。

### 35.7 预热扩到全部 24 个导航键

§35.3 只预热了首次成本最高的 10 个页面。把剩下的页面按同一探针逐轮扫过，未预热的页面首次进入仍要付一次性排版成本（真机、镜像实例 336 模组，ms，layout / maxGap）：

| 页面 | 预热前首次 | 备注 |
| --- | ---: | --- |
| plugins | 1056.4 / 547.4 | 插件页，与 logs 同量级 |
| installhistory | 517.3 / 310.2 | |
| downloadstats | 324.4 / 293.4 | |
| modtext | 268.2 / 251.4 | |
| ships | 196.1 / 160.2 | 用户点名的子页签之一 |
| dedup / cache / repos / flags / loadingimages / playtime / kspconfig | 60–180 | |

因此 `WarmupOrder` 从 10 项扩到 24 项，顺序按上表与 §35.3 的实测成本从贵到便宜排（最贵的先热）：

```text
logs, plugins, installhistory, modpack-editor, mods, modtext, dedup, downloadstats,
cache, instances, modconfig, flags, ships, settings, loadingimages, repos,
kspconfig, home, changes, modpacks, playtime, content, unmanaged, saves
```

节拍仍是每 420 ms 一张，且沿用 §35.5 的规则：最近 1.2 s 内用户换过页就让位，`settings` 这类重页面还要求窗口失焦或已空闲 45 s。全部 24 页预热完约需 10–12 s（预热工作本身合计约 1.3–1.5 s，其余时间在等节拍）。

完成后真机复测（`CKANUI_NAV_WARMUP_MS=18000`，等预热跑完再遍历）：

- 首次进入（R1）除 logs 外全部 ≤ 500 ms；剩余大头 mods 492 / downloadstats 324 / modtext 268 / plugins 210 / ships 196 / installhistory 153。
- 二次进入（R2）除 unmanaged 437、downloadstats 102、cache 92、modconfig 58、saves 38 外全部 ≤ 120 ms。

内存代价（`tools/perf-probe/Measure-Soak.mjs --minutes 3`，同一实例，全页预热）：

| 口径 | 10 页预热（§35.4） | 24 页预热 |
| --- | ---: | ---: |
| 工作集 min/max | 444.8 / 457.9 MB | 460.1 / 468.9 MB |
| private min/max | 397.7 / 410.6 MB | 398.1 / 407.7 MB |
| 线程 | 105–111 | 105–113 |
| 空转 CPU | 0.1–0.2% 单核 | 0.0% 单核 |

即常驻 14 个额外页面视图的代价是工作集 **+8–13 MB**、private 不变。视图实例都是同一个 DI 单例 VM 上的视图，行数据来自各自 VM 已有的集合，没有额外投影副本。

### 35.8 logs 页 2.4 s 的复核：是 sweep 叠加，不是页面成本

§35.7 那次整轮 sweep 里 `logs` 首次 `layout=2442 ms / maxGap=1663 ms`，与 §35.3「预热后 97.9 ms」矛盾。用 `CKANUI_NAV_ONLY=logs` 只跑这一页复核（`CKANUI_NAV_WARMUP_MS=3000`）：

```text
[nav] r1 logs assign=54.5ms layout=375.3ms paint=378.7ms frames=2 maxGap=259.5ms
[logsdiag] refresh=49.8ms rows=27 events=27
```

`[logsdiag]` 是临时插在 `LogPageViewModel.Refresh()` 上的分段计时（`CKANUI_LOGS_DIAG=1` 才写，取证后已移除）：一次刷新 49.8 ms，要画 27 行。日志页本身不贵 —— 2442 ms 那一轮是「预热队列还在跑 + 机器上有并行编译」的叠加值，不是单页成本。

同一轮里的 `r2 layout=3009ms frames=148` 也是探针口径问题：第二轮探针在页面持续重绘（日志页订阅了 store 的变更）时等 `Loaded` 派发，等到的其实是连续 148 帧之后的那个回调，`maxGap` 才 315 ms。判据改成看 `maxGap`（真实最长单帧）而不是等派发的 wall time。

教训（写进工具说明）：**整轮 sweep 的绝对值会被机器负载与自身的预热队列污染，单页结论必须用 `CKANUI_NAV_ONLY` 单独复核，并看 `maxGap`。**

## §36 预热要「真的进一次」：激活期行投影也算首屏成本

### 36.1 问题：预热只量了空列表

§35 的预热只做两件事：解析 VM（`PeekPage`）+ 建视图挂进 `PageStage` 量一次排版。对**行投影在 `OnActivated` 里生成**的页面（模组、未管理文件、旗帜、飞船、存档、模组文本……），预热时 VM 还没激活，量的是空列表：`[warm] mods attach=42ms`，而用户点进去还要再付一次「投影 + DataGrid 布局」。

真机实测（镜像实例 336 模组，ms，layout / maxGap）：

| 页面 | 只建视图的预热 | 用户首次进入 |
| --- | ---: | ---: |
| mods | warm 42 | **296–421 / 244–345** |
| modtext | warm 236 | **1100 / 531** |
| installhistory | warm 22 | **443 / 339** |
| unmanaged | warm 50 | **343 / 345** |

### 36.2 改法：预热时像用户一样 `Activate()`

`WarmPage` 里对一组页面先 `vm.Activate()` 再建视图，让 `OnActivated` 的行投影在后台完成，排版量的就是真实数据：

```csharp
private static readonly HashSet<string> ActivationWarmups = new(StringComparer.Ordinal)
{
    "mods", "unmanaged", "content", "repos", "modpacks", "modtext", "ships", "saves"
};
```

预热的收尾**不再立刻 `Deactivate()`**：多数页面的行数据是 `OnActivated` 里异步加载的，立刻停用会把加载取消掉，等于白预热。保持激活让「加载 + 排版」都在后台做完，用户进来直接是成品。

代价：这些 VM 会一直保持激活（订阅各自的数据源），实测见 36.4。

### 36.3 实测（同脚本同实例，`CKANUI_NAV_WARMUP_MS=25000`，ms）

| 页面 | 首次进入 改前 | 首次进入 改后 | maxGap 改前 → 改后 |
| --- | ---: | ---: | ---: |
| mods | 296–421 | **38–85** | 244–345 → **18–51** |
| modtext | 1100 | **15** | 531 → 13 |
| installhistory | 443 | **30** | 339 → 25 |
| unmanaged | 343 | 341（未改善，见下） | 345 → 342 |
| ships | 196 | 109 | 160 → 104 |
| saves | 83 | 69 | 54 → 68 |
| settings | 64 | 60 | 28 → 28 |
| downloadstats | 324 | 74 | 293 → 68 |
| plugins | 210 | 102 | 249 → 85 |
| flags | 524 | 27 | 523 → 24 |

`unmanaged` 不动的理由与 §27.5 一致：它的成本是 TreeView 1870 个节点的整棵 Measure/Arrange，与数据是否已加载无关；虚拟化掉节点数也不降时间。`flags` 在「只建视图」阶段反而更贵（524ms），激活预热后降到 27ms —— 它的开销确实在数据投影。

### 36.4 被否掉的两种写法

| 写法 | 结果 | 原因 |
| --- | --- | --- |
| 预热后立刻 `Deactivate()` | mods 85ms、modtext **1100ms 不变**、installhistory 443ms 不变 | 异步加载被取消，后台只量了空列表 |
| 把 `plugins` / `flags` 也放进激活预热 | plugins 首次进入 102→**1287**（assign 507） | 插件页的 `OnActivated` 有同步重活，提前做只是把它挪到预热 tick 里堵住 UI 线程，反而更差 |

结论：激活预热只适用于「`OnActivated` 是轻量投影 + 异步加载」的页面；带同步重活的页面（plugins）不能预热，得单独治。

## §37 页面掉帧：把「留在屏幕上每帧要花多少」做成可测指标

换页成本（§21、§36）只覆盖「切过去」那一下。用户感知的掉帧是页面**已经在屏幕上**时每帧的重绘成本 —— 60fps 一帧只有 16.7ms 预算，页面本身渲染贵一点，任何动画（星空、悬停、弹簧、滚动）都会掉帧。

### 37.1 新探针 `--render-cost [key]`

`tools/perf-probe/PageSwitchProbe.RenderCost(string? onlyKey, int frames = 60)`：真实 DI 起服务 → 激活页面 VM → 建视图挂进窗口 → Measure/Arrange → 把视图按 1280×800 渲染进 `RenderTargetBitmap`，60 帧逐帧计时并统计分配。

```bash
CKAN_CONFIG_FILE=<probe-config.json> CKANUI_PROBE_REAL_CONFIG=1 CKANUI_PROBE_INSTANCE=1 \
  perf-probe.exe --render-cost            # 全部页面
  perf-probe.exe --render-cost mods       # 单页
```

### 37.2 实测（镜像实例 336 模组，60 帧，ms / 每帧分配）

| 页面 | p50 | p95 | max | 分配/帧 | 节点 |
| --- | ---: | ---: | ---: | ---: | ---: |
| mods | 2.03 | 3.18 | 3.81 | 51.1 KB | 2540 |
| modpack-editor | 0.56 | 0.86 | 1.30 | 14.3 KB | 459 |
| settings | 0.47 | 0.96 | 1.25 | 58.7 KB | 2348 |
| downloadstats | 0.35 | 1.37 | 38.97 | 16.5 KB | 174 |
| home | 0.32 | 0.66 | 21.34 | 7.1 KB | 187 |
| instances | 0.28 | 0.41 | 0.74 | 6.5 KB | 280 |
| modtext | 0.28 | 0.39 | 0.66 | 8.8 KB | 261 |
| modpacks | 0.23 | 0.36 | 0.48 | 6.1 KB | 202 |
| kspconfig | 0.22 | 0.28 | 0.51 | 4.5 KB | 129 |
| loadingimages | 0.21 | 0.35 | 0.41 | 4.3 KB | 129 |
| content | 0.20 | 0.42 | 14.97 | 1.5 KB | 84 |
| dedup | 0.19 | 0.56 | 16.86 | 3.9 KB | 162 |
| modconfig | 0.19 | 0.31 | 0.38 | 4.8 KB | 146 |
| cache | 0.16 | 0.32 | 0.46 | 2.9 KB | 91 |
| logs | 0.16 | 0.20 | 0.40 | 4.4 KB | 126 |
| flags | 0.09 | 0.12 | 15.82 | 2.2 KB | 86 |
| installhistory | 0.09 | 0.09 | 0.10 | 2.1 KB | 93 |
| plugins | 0.08 | 0.09 | 0.24 | 3.2 KB | 100 |
| ships | 0.07 | 0.16 | 0.21 | 12.5 KB | 391 |
| changes | 0.05 | 0.39 | 13.85 | 1.1 KB | 89 |
| playtime | 0.04 | 0.04 | 0.07 | 1.5 KB | 51 |
| unmanaged | 0.02 | 0.02 | 0.03 | 3.1 KB | 103 |
| repos | 0.02 | 0.03 | 0.04 | 3.6 KB | 128 |
| saves | 0.01 | 0.01 | 0.01 | 1.8 KB | 118 |

### 37.3 怎么读这张表

- **p50 是 UI 线程的显示列表构建成本**（headless 的 `UseHeadlessDrawing` 不做真实光栅，光栅在渲染线程）。它低不代表不掉帧，但高一定掉帧。
- **max 列的长尾是 GC 停顿**：`downloadstats` max 38.97ms、`home` 21.34、`dedup` 16.86、`flags` 15.82 —— 这些是分配触发的收集中途，一帧超过 16.7ms 就是用户能看到的卡一下。
- **分配/帧才是持续动画时的风险**：`mods` 51 KB/帧、`settings` 58.7 KB/帧、`downloadstats` 16.5 KB/帧。按 60fps 算，`settings` 每秒产生 3.4 MB 垃圾，会在拖拽/滚动/悬停这类连续重绘场景里持续喂 GC。
- 结论：`mods`（DataGrid 2540 节点 + 51 KB/帧）与 `settings`（2348 节点 + 59 KB/帧）是「留在屏幕上最贵」的两页；其余页面 p50 都在 1ms 以内。

### 37.4 已知局限

- headless 走 `UseHeadlessDrawing`，`RenderTargetBitmap.Render` 不产生真实像素，因此这张表**只覆盖 UI 线程侧**（显示列表 + 分配），不覆盖 Skia 光栅与 GPU 合成。
- 真机的前台帧间隔无法在不抢焦点的情况下采集（用户明确要求无头调试不抢窗口），所以「前台实际掉帧率」仍以用户体感为准；本表用于**页面间横向对比**与**回归防退化**。

### 37.5 帧间隔指标的地板（别把 dropped 当结论）

同一轮 sweep 里再量「页面留在屏幕上 3 秒的帧间隔」（`CKANUI_NAV_FRAME_MS=3000`，r2 = 第二次进入）：

| 页面 | frames | p50 | p95 | maxGap | dropped |
| --- | ---: | ---: | ---: | ---: | ---: |
| repos（几乎空页） | 174 | 16.5 | 31.2 | 35.1 | 76 |
| playtime | 169 | 16.7 | 31.1 | 34.8 | 91 |
| mods（2540 节点 DataGrid） | 159 | 16.8 | 30.7 | 34.0 | 82 |
| downloadstats | 167 | 16.6 | 31.1 | 55.8 | 75 |
| unmanaged | 104 | 16.1 | 31.4 | **1310.5** | 38 |

**p50 恒等于 16.7ms、p95 恒等于 30–31ms、dropped ≈ 一半**，在几乎空的 `repos` 页与 2540 节点的 `mods` 页之间没有差别 → 这个指标量到的是**后台窗口下合成器的 60Hz 节拍（偶尔漏一次 vsync = 30ms）**，不是页面内容成本。`dropped` 不能当页面结论用。

它仍有用：**maxGap 是真实的 UI 线程停顿**。本轮唯一异常是 `unmanaged` 的 1310ms —— 二次进入时 TreeView 从 103 节点膨胀到 1861 节点（默认展开策略），单帧被布局吃掉 1.3 秒。改它要动「默认展开」这个视觉行为，本轮不做，登记为已知停顿。

结论：判断页面掉帧要看 §37.2 的「每帧 UI 成本 + 分配」，帧间隔只能用来抓尖峰。

## §38 未管理文件页的 340–600ms 归属，以及一次被内存否决的「延后收尾」实验

### 38.1 归属：树在首次显示时实体化 1866 个节点

`NavigationProbe` 的每行输出加了 `nodes=`（当前活动页面视图的可视节点数），据此定位：

| 轮次 | layout | maxGap | nodes |
| --- | ---: | ---: | ---: |
| r1（首次） | 308–598ms | 309–592ms | **1866** |
| r2（再次） | 探针等不到布局（页面已可见） | 34.7ms | 1866 |

同一时刻 `[warm] unmanaged attach=21ms` —— 预热只量了 21ms，因为预热那一刻 VM 的树还没扫完（`OnActivated` 里是异步扫描 GameData）。真正花钱的是**数据到达后 TreeView 一次性实体化 1866 个容器**，这笔账落在用户第一次看到页面时。

§27.5 已证给 TreeView 加 `VirtualizingStackPanel` 只能把节点降到 661 且布局时间不变，所以本次不动结构。

### 38.2 实验：预热收尾延后 1.5 秒，让异步数据与排版都在预热期完成

做法：`WarmPage` 里对激活预热的页面用 `DispatcherTimer.RunOnce(..., WarmSettleMilliseconds = 1500, ContextIdle)` 代替立即 `Post`，页面在这 1.5 秒内保持 `IsVisible=true, Opacity=0`，让异步加载和随之而来的排版在后台发生。

结果：`unmanaged` 首次 598 → 308ms（部分有效），全页 R1 也都 ≤130ms（除 unmanaged），但**内存被否决**：

| 指标 | §35.7（24 页预热，立即收尾） | 本实验（延后收尾） |
| --- | ---: | ---: |
| 稳态工作集 | 460–468 MB | **1641 MB** |
| 峰值工作集 | — | **2227 MB** |

1.5 秒里多个页面同时处于可见态并各自完成排版，页面级缓存（每页的渲染层、容器、样式实例）叠在一起不释放。即使回退该实验后，**24 页预热本身的峰值也有 1743MB**（稳态收敛到 467MB，靠 `IdleMemoryReclaimer` 回收），这是当前预热策略的已知代价。

### 38.3 结论与后续

- 不采纳延后收尾：换 unmanaged 300ms，用掉 1.2GB 峰值，不值。
- 已知瞬态：启动后预热窗口内峰值 1.7GB、稳态 467MB（真实实例 336 模组）。要压峰值只能减少预热页数或改成一页一回收，属于下一轮候选。
- 未管理文件页的 340–600ms 属「默认展开的树一次性实体化」，要改必须动展开策略（视觉变化），登记为已知停顿。

## 39. 预热的内存峰值：范围收到 12 页 + 一页一回收

### 39.1 问题

§35–§36 的预热把 22–24 个导航页全部挂进 `PageStage` 并实体化一次。换页变快了，但内存峰值失控：每次启动后十几秒内工作集被推到 **2.2–2.8 GB**（5 次独立 soak 复现），稳态回落到 557–620 MB。用户视角就是任务管理器里闪一个数 GB 的尖峰。

### 39.2 归因（去掉了「某个页面有毒」这个假设）

| 实验 | 做法 | 结果 |
| --- | --- | --- |
| 无预热对照 | `CKANUI_NO_WARM=1`（临时开关，已删） | 峰值 **770 MB** / 稳态 300–435 MB → 峰来自预热 |
| 去掉 settings | 预热队列删 settings | 峰值仍 2298 MB |
| 逐页回收（560 MB 门限） | 每热完一页按工作集判定回收 | 峰值仍 2193 MB |
| 非压缩回收 | `GC.Collect(2, Forced, compacting:false)` | 峰值 2829 MB，**稳态回不去**（2263 MB）→ 压缩回收才是把页还给系统的那个动作 |
| 顺序打乱 | mods 提到第一位 | 尖峰跟着顺序漂移，不跟着某个页面 → **是常驻页面累计，不是单页** |

结论：`PageStage` 里每多一棵已实体化的页面树，预热期的分代堆就多一份存活压力；堆越大，回收的提交页越多，尖峰越高。压峰值只有一条路——少热几页。

### 39.3 改法（`src/CkanUI.App/MainWindow.axaml.cs`、`src/CkanUI.App/Diagnostics/IdleMemoryReclaimer.cs`）

- `WarmupOrder` 从 22–24 页收到 **12 页**（按实测首次进入 layout 从高到低挑）：`logs, modtext, installhistory, modpack-editor, dedup, mods, cache, downloadstats, flags, modpacks, ships, settings`。
  - `mods` 故意放在第 6 位：放第一位时仓库快照还没到位，预热量到的是空表（实测首次进入仍 514 ms）；放第 6 位（约 12 秒）才量到带数据的表（50 ms）。
- 预热节拍 `420 ms → 2500 ms`：一次预热占 UI 线程最多 1.4 秒（settings），420 ms 连排会把启动后的几秒填满；拉长后预热摊到约 30 秒，用户随时点页面都能让位（`1.2 s since navigation` 门仍在）。
- 每热完一页若工作集 > 560 MB 就 `IdleMemoryReclaimer.ReclaimNow()`；队列跑空时再按 700 MB 门限回收一次。`ReclaimNow()` 是新增的公开入口（`IdleMemoryReclaimer` 原本只有带策略门限的 `ReclaimIfNeeded`），回收仍走「两次 Aggressive + compacting + finalizer」并在测试之外保持原策略不变。
- 新增 `WarmReclaimThresholdBytes = 700 MB`、`WarmPageReclaimBytes = 560 MB`、`_warmReclaimDone`；`OnPageWarmTick` 在队列空时停表并做收尾回收。

### 39.4 实测（真机 Release、镜像实例 Probe (Steam mirror) 336 模组、3 秒采样）

| 口径 | 峰值工作集 | 稳态工作集 | 空转 CPU |
| --- | ---: | ---: | ---: |
| 24 页预热（§35/§36 版本） | 2200–2800 MB | 557–620 MB | 0.0–1.1% 单核 |
| **12 页预热 + 逐页回收** | **814 MB** | **620 MB** | 0.0–0.8% 单核 |
| 无预热（对照） | 770 MB | 300–435 MB | 0.0–0.2% 单核 |

首次进入成本（真机 `NavigationProbe`，`layout / maxGap` ms；括号内为未被预热的页面）：

| 页面 | 首次 | 页面 | 首次 |
| --- | ---: | --- | ---: |
| mods | 50.3 / 32 | settings | 48.7 / 18 |
| modtext | 11.1 / 8 | logs | 87.4 / 67 |
| installhistory | 11.7 / 15 | modpack-editor | 7.8 / 6 |
| dedup | 174.8 / 171 | flags | 15.2 / 10 |
| downloadstats | 182.5 / 165 | modpacks | 10.5 / 7 |
| ships | 14.5 / 12 | cache | 379.5 / 226 |
| （loadingimages） | 133.6 / 100 | （instances） | 127.8 / 57 |
| （repos） | 114.1 / 32 | （unmanaged） | 113.7 / 85 |

第二轮（点回已看过的页）：除 unmanaged（316.5，树实体化，§38）外全部 ≤ 91.5 ms。

`dedup` / `downloadstats` / `cache` 的 174–380 ms 是激活期的真实数据整理（不是排版），预热只覆盖到首屏结构；要再降得改它们的 `OnActivated`，属下一轮。

### 39.5 代价与边界

- 稳态工作集比无预热高约 **185 MB**（620 vs 435）——这是「页面常驻」的直接成本，换页 1–20 ms 的收益就建立在这上面。
- 未被预热的 4 个页面（loadingimages / instances / repos / unmanaged）保持原首次成本 114–134 ms；继续加页会把峰值推回 GB 级，不做。
- 逐页回收在空闲期各付几百毫秒（压缩回收），页面此时不可交互，用户看不到。
- `docs/ops/perf-baseline.md` §38.3 里「峰值 1.7 GB 属预热固有」的说法由本节修正为「预热页数决定峰值」。

## §40 换页不再切可见性，改切透明度：第二轮换页从 316 ms 降到 24 ms

### 40.1 问题

§29 让页面常驻 PageStage 之后，换页只剩「切一下可见性」。但 IsVisible 一翻，Avalonia 会把那棵子树的排版结果整棵作废，切回来要重新 Measure/Arrange 一遍。实测（PageSwitchProbe.VisibilityCost，headless，settings 2338 节点）：

| 动作 | settings | ships |
| --- | ---: | ---: |
| 已挂树 hide | 0.9 ms | 0.3 ms |
| 已挂树 show | 1.4 ms | 0.1 ms |
| 摘下来再挂回去（detach + attach） | **1793.5 ms** | 23.5 ms |

真机 NavigationProbe 里对应的现象就是第二次点回同一页的 unmanaged = 316.5 ms / maxGap 388 ms（TreeView 1866 个容器重排）、cache = 56.2 / 52。

### 40.2 改法（src/CkanUI.App/MainWindow.axaml.cs）

ShowPage 不再动 IsVisible，改成同时切透明度与交互状态：

    var active = ReferenceEquals(child, view);
    child.IsVisible = true;
    child.Opacity = active ? 1 : 0;
    child.IsHitTestVisible = active;
    // 不可见的页面仍然在可视树里参与排版，所以要把键盘焦点也挡在外面，否则 Tab 会跑到看不见的控件上。
    child.IsEnabled = active;

FinishWarm 同步改成 IsVisible = true; Opacity = 0; IsHitTestVisible = false; ——原来收尾时把预热页设成 IsVisible = false，等于把刚排好的结果扔掉，下一次进入还得重排。

隐藏页留在排版里，因此：命中测试由 IsHitTestVisible 挡住、键盘焦点由 IsEnabled 挡住；绘制上 Opacity = 0 的子树不产生可见像素。

### 40.3 实测（真机 Release、镜像实例 Probe (Steam mirror) 336 模组，第二轮 = 点回同一页，layout / maxGap ms）

| 页面 | 切可见性（§29–§39） | 切透明度（本节） |
| --- | ---: | ---: |
| unmanaged | 316.5 / 388 | **23.6 / 19** |
| cache | 56.2 / 52 | **17.2 / 14** |
| logs | 91.5 / 80 | 94.9 / 86 |
| repos | 12.7 / — | 62.1 / 61 |
| modconfig | 18.3 / — | 61.1 / 63 |
| 其余 18 页 | ≤ 64 | ≤ 43 |

首轮（未预热页 + 首次进入）：downloadstats 997.1（一次尖峰）、unmanaged 51.6（原 113.7）、cache 89.0、settings 94.8、mods 41.7、dedup 10.0。

空转与驻留（Measure-Soak.mjs，1.4 分钟、4 秒采样）：峰值 627 MB / 稳态 **620 MB**、尾部空转 CPU **0.00–0.01% 单核**——隐藏页留在排版里不额外烧 CPU，也不额外占内存。

### 40.4 视觉取证

CKANUI_SNAP_SEED=1 dotnet tools/ui-snapshot/bin/Release/net10.0/ui-snapshot.dll（注意 dll 名是 ui-snapshot.dll），24 页离屏截图，用 tools/perf-probe/diff-png.ps1 -Tol 12 比对：

- 噪声基线（同一模式连跑两次）：首页 1.78%，其余 0.24–0.78%。
- 切透明度 vs 切可见性：23 页 0.31–0.85%（落在噪声内）；只有 installhistory 45.57%——看图确认是**基线截图抓到换页淡入的中间帧**（半透明的日志页残留在下面），新模式下隐藏页 Opacity = 0，这种残影在物理上不可能出现。

### 40.5 代价与边界

- 隐藏页继续参与排版：窗口尺寸变化时 12 棵页面树一起重排（拖窗口缩放会比之前重）。实测空闲 CPU/内存无回归，视觉无差异。
- IsEnabled = false 会让隐藏页内的控件进入禁用状态；它们不可见、不可交互，重新激活时立刻恢复。
- NavigationProbe 的 [warm] / [nav] 口径不变，本节数字与 §35–§39 可直接对比。

## §41 预热不再和换页抢线程：静置门加宽到 3 秒，重页面还要求用户真的停手

### 41.1 问题

§35 起的「逐页预热」每 2.5 s 一个 tick，每个 tick 只在「距上次换页 >1.2 s」时才动手。但单页切换实测 0.1–0.4 s，用户连着点几下时，两次点击之间正好能落进 1.2 s 的缝里——tick 于是在用户刚点完的瞬间插进来，抢走 UI 线程。

真机 NavigationProbe 里这个现象就是**帧间隔尖刺**（layout 只有几十毫秒，maxGap 却是几百毫秒）：

| 页面 | 修前 maxGap（连续换页 sweep） |
| --- | ---: |
| flags R2 | 524 ms |
| mods R1 | 915 ms |
| flags R1 | 501 ms |

### 41.2 改法（src/CkanUI.App/MainWindow.axaml.cs）

- 静置门 1200 ms → WarmQuiet = 3 s：3 秒内换过页就说明用户在连续操作，这一轮让位。
- 重页面（当前只有 settings）预热额外要求「用户真的停手」：ShouldWarmExpensive(bool windowActive, bool isIdle, TimeSpan sinceNavigation) => !windowActive || (isIdle && sinceNavigation > ExpensiveWarmIdle)，其中 isIdle 取 DynamicRefreshRateController.Instance.IsIdle。原来只看「距上次换页 45 s」，用户一直敲键盘也会被判为可以热——而这一次预热是 0.8–1.4 s 的整窗冻结。
- 新增单测 FocusedWindow_LongIdleButUserStillTyping_DoesNotWarm；原三例同步补 isIdle 参数。

### 41.3 实测（真机 Release、镜像实例 336 模组、连续换页 sweep）

| 页面 | 修前 maxGap | 修后 maxGap |
| --- | ---: | ---: |
| flags R2 | 524 | **36** |
| mods R1 | 915 | **85** |
| flags R1 | 501 | 424 |
| 全页 R2 maxGap | 多数 ≤40，logs 109、modconfig 122 | ≤122 |

代价：连续快速换页时预热基本排不上（3 s 静置门挡着），因此 R1 里未预热的页面回到原始成本（settings R1 477 ms、downloadstats 328 ms）。真实用户停在某页超过 3 秒才开始热，所以正常使用不受影响；扫描式连点会看到首进变慢——这是有意的取舍：宁可慢一点，也不要无预警冻结。

### 41.4 边界

- IsIdle 由 DynamicRefreshRateController 维护（2.5 s 无输入进入空闲），窗口失焦时它也会进入空闲，所以后台预热不受影响。
- 预热仍在 2.5 s tick 上串行推进，最坏情况下整条队列要走 30 s；WarmIdleTickLimit = 150 仍是上限。

## §42 日志页回来时不再重查一遍：存储版本没变就直接复用列表

### 42.1 问题

LogPageViewModel.OnActivated 每次都无条件 Refresh()：按当前过滤条件查库、聚合时间轴、重建几十个 LogRowViewModel。实测这一趟 20–100 ms（aggregate 单项 13 ms，见 §18.11 的拆分表），而用户只是从别的页点回日志页——缓冲区里一条新日志都没有，结果一模一样。

### 42.2 改法（src/CkanUI.ViewModels/Pages/Logs/LogPageViewModel.cs）

- 新增 private long _lastBuiltVersion = -1; 与 private bool _hasBuilt;；Refresh() 成功建完列表（含「过滤条件命中 0 条」那条早退分支）后记 _lastBuiltVersion = _store.Version; _hasBuilt = true;。
- OnActivated：订阅完 Changed 后，若 _lastBuiltVersion == _store.Version 且 _hasBuilt 就直接返回，不查库。
- 异常分支（读日志失败）故意不记版本，下次激活会重试。
- 用户改过滤条件、点刷新、存储来新事件时仍走原路径（Changed 事件和命令都没变）。

ILogStore.Version 本来就随每次写入自增，所以「版本没变」等价于「缓冲区内容没变」。

### 42.3 效果与代价

- 首次进入不受影响（_hasBuilt 为 false，照旧构建）。
- 回到日志页且期间没有新事件时，省掉一次查询 + 聚合 + 列表重建；有新事件时照旧自动刷新（新增用例 Reactivating_WithoutNewEntries_ReusesTheRowsAlreadyBuilt 断言行对象复用、以及新事件仍能刷新）。
- 代价：如果外部直接改了日志文件而没有经过 LogStore，页面不会感知——与现有「存储驱动刷新」的模型一致，不受影响。

## §43 下载统计：仓库快照复制挪下 UI 线程

### 43.1 问题

DownloadStatsService.ComputeAsync 的第一段是 CollectModules()：把整份仓库快照按行复制一遍（row with { IsInstalled }）、再建 repoMap。它在第一个 await 之前执行，等于整段跑在 UI 线程上——切到「下载统计」时那一下卡顿就来自这里（该页 R1 实测 layout 328 ms、maxGap 274 ms）。

### 43.2 改法（src/CkanUI.Services/Stats/DownloadStatsService.cs）

var (modules, repoMap) = await Task.Run(CollectModules, ct).ConfigureAwait(false); ——只换执行线程，输入输出与聚合逻辑一字未改；后续 Aggregator.Compute 仍在调用方线程上同步跑（它只处理已收集好的集合）。

### 43.3 实测（真机 Release、镜像实例 336 模组、单页复核）

| 指标 | 修前 | 修后 |
| --- | ---: | ---: |
| downloadstats R1 layout | 328 ms | 255 ms |
| downloadstats R1 maxGap | 274 ms | 253 ms |

剩余大头是**表格实体化**：该页 RowLimit = 200 行、DataGrid 建出约 1499 个节点，200 行 × 行模板（含下载量列的 ProgressBar）一次排完。试过显式 EnableRowVirtualization="True"，Avalonia 12 的 DataGrid 没有这个属性（AVLN2000: Unable to resolve suitable regular or attached property EnableRowVirtualization），说明虚拟化本来就开着，只是 200 行在视口内被一次性量完；要再降只能减 RowLimit（产品行为变化），不做。

### 43.4 边界

- Task.Run 只包住同步的集合复制，取消令牌仍生效（进入时 ThrowIfCancellationRequested）。
- CurrentInput 仍会被赋值，测试与「诊断导出」读它的路径不变。

## §44 日志页也进激活预热：首进从 130–215 ms 降到 14 ms

### 44.1 问题

预热只把页面视图建出来、不激活；日志页的行数据在 OnActivated 里才查。所以 §36 的激活预热名单里没有 logs 时，预热只热了空壳，用户第一次点日志页仍要付一次查询 + 聚合 + 建行（实测 106–215 ms）。

### 44.2 改法（src/CkanUI.App/MainWindow.axaml.cs）

ActivationWarmups 增加 logs：预热时调用 page.Activate()（触发一次真实查询与行构建），再用 Opacity = 0 留在 PageStage 里。§42 的版本记账会自动生效——预热已经建好列表并记下 _lastBuiltVersion，用户点进来时版本没变就直接复用，不再查第二遍。

### 44.3 实测（真机 Release、镜像实例 336 模组）

| 指标 | 修前 | 修后 |
| --- | ---: | ---: |
| logs 首进 layout | 106–215 ms | **14.2 ms** |
| logs 首进 paint | 111–223 ms | 42.6 ms |
| logs 第二轮 layout | 126–143 ms | 69.7 ms |
| 预热自身成本 | 53 ms（空壳） | 235 ms / 16.9 MB 分配 |

全页 sweep 未见回归：R1 除 settings 531（未预热，§41 的静置门挡着）、downloadstats 248、modconfig 173 外均 ≤147；R2 除 downloadstats 133、modconfig 120 外均 ≤70。

## §45 预热页数不能再减，回收门限下调把启动峰值压掉 205 MB

### 45.1 实验：12 页预热砍到 8 页（否决）

动机：§39 把预热页数从 24 砍到 12 后启动峰值仍有 845 MB，想再砍 4 页（flags / modpacks / ships / settings）换内存。

实测（真机、镜像实例 336 模组，1.5 分钟 soak，4 秒采样）：

| 口径 | 峰值 WS | 稳态 WS |
| --- | ---: | ---: |
| 12 页预热 | 845.6 MB | 639.9 MB |
| 8 页预热 | 585.7 MB | 565.4 MB |

省下 260 MB 峰值，但被砍掉的四页首次进入代价（单页复核，layout / maxGap）：

| 页面 | 预热时首次进入 | 不预热首次进入 |
| --- | ---: | ---: |
| flags | 19 / 44 ms | 181 / 554 ms |
| modpacks | 15 / 33 ms | 447 / 283 ms |
| ships | 146 / 83 ms | 227 / 182 ms |
| settings | 静置门挡着未预热 | 761 / 596 ms |

flags 的 554 ms 与 settings 的 596 ms 单帧停顿是用户点一下就能感觉到的量级，用它们换 260 MB 不值得。**结论：预热名单保持 12 页，改动已回退。**

### 45.2 改法：回收门限 560 MB → 430 MB（采纳）

src/CkanUI.App/MainWindow.axaml.cs 的 private const long WarmPageReclaimBytes 由 560L * 1024 * 1024 改为 430L * 1024 * 1024。每热完一页，工作集超过门限就做一次压缩回收；门限下调只让回收更早发生，不改预热内容与顺序。

实测（同 soak 口径）：

| 口径 | 峰值 WS | 收敛 WS | 峰值 private | 空转 CPU |
| --- | ---: | ---: | ---: | ---: |
| 门限 560 MB | 845.6 MB | 639.9 MB | 780.1 MB | 0.0–1.9% |
| 门限 430 MB | **640.8 MB** | 637.8 MB | 569.3 MB | 0.0–2.4% |

峰值降 205 MB（-24%），稳态不变；代价是预热期间多一两次压缩回收，空转 CPU 多 0.5 个百分点且只发生在启动后那十几秒。

### 45.3 换页未回归

全页 sweep（同 HEAD，真机、336 模组）：R1 除 settings 489（静置门挡着未预热）、unmanaged 287、modconfig 158 外均 ≤121；R2 除 modconfig 121、downloadstats 120 外均 ≤81。
