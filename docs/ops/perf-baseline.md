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
> 真机停在 840 MB —— 等于白回收一次。现在的 `MinAttachSeconds = 24` 就是这次实测换来的。

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
| `MinAttachSeconds` | 24 | 启动加载期一律跳过。早期 8 秒就回收的版本会在加载完成后被撑回 840 MB |
| `GrowthThresholdBytes` | 128 MB | 首次回收后，只有工作集比上次回收后长了 128 MB 才再收，避免无意义 GC |
| `MinInterval` | 45 s | 两次回收的最小间隔，防止交互边缘反复停顿 |

安全性：只在 `DynamicRefreshRateController.IsIdle` 为真时执行，两次回收是阻塞式全堆压缩
（满载会话上约 100–300 ms），用户此刻没有任何操作，感知不到；`Detach`（窗口关闭）后定时器立即停止。
接线点在 `DynamicRefreshRateController.Attach/Detach` 里——空闲态由该控制器产出，回收器只消费它。

> 度量口径提醒：工作集是**平台态**指标，回收后才落到底；读 `Process.WorkingSet64` 就够，
> 不要拿 `GC.GetTotalMemory` 判断——它看不见提交未释放的页，那才是这 500 MB 的主体。

