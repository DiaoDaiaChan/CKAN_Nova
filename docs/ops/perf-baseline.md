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
