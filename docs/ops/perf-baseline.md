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

## 4. 真机驻留与空转（`Measure-LiveProcess.ps1`，Release exe 挂机 10s）

| 指标 | 实测 |
| --- | --- |
| **空转 CPU** | **71.15% 单核**（32 核机器上 2.22% 整机） |
| 工作集 | 均 322.9 MB / 峰 328.1 MB |
| 私有字节 | 均 276.5 MB / 峰 280.4 MB |
| 线程 | 峰 114 |
| 句柄 | 峰 1036 |

> 空转 71% 单核是最刺眼的一条：没有任何用户操作、窗口静止，进程仍在按 30–60Hz 重画整窗背景与自递归 RAF。
> 这条也是本轮优化的主指标（先把它压到个位数百分比）。

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
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/perf-probe/Measure-LiveProcess.ps1
```

> 注意：真机采样前先 `taskkill /F /IM CkanUI.exe`，否则构建会因 `bin/Release` 被占用报 MSB3027。

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

真机桌面口径（`Measure-LiveProcess.ps1`，同一启动状态）：

| 项 | 优化前 | 优化后 |
| --- | --- | --- |
| 空转 CPU（单核） | 112.51% / 164.88% | **89.45%** |

> 真机这一对只能作方向性参考：采样期间同机有并行 agent 在构建，且这台机器上 Avalonia 走了软件光栅，
> 两档被测进程的工作集都在 0.9–1.0 GB（与基线一致，不是本轮引入），绝对值和 §4 那次 322 MB 的
> 挂机样本不是同一状态，不能直接跨表比较。

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
