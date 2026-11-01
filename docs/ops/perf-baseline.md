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
TEMP="$PWD/artifacts/tmp/temp" dotnet tools/perf-probe/bin/Release/net10.0/perf-probe.dll
TEMP="$PWD/artifacts/tmp/temp" dotnet tools/perf-probe/bin/Release/net10.0/perf-probe.dll --mem

# 真机驻留/空转（会自己起一个 Release 实例并杀掉，别在正在用的实例上跑）
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/perf-probe/Measure-LiveProcess.ps1
```

> 注意：真机采样前先 `taskkill /F /IM CkanUI.exe`，否则构建会因 `bin/Release` 被占用报 MSB3027。
