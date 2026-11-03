# 性能页签 · KPP 双向遥测桥契约（kpp.telemetry/1）

状态：**v1 冻结**（两侧并行开发的唯一接口真相源）。

## 0. 为什么是文件桥

KPP 是 KSP 进程内的 Unity/Mono Mod（net472），ckan-ui 是独立 Avalonia 进程（net10.0）。
两者不能共内存，命名管道在 Mono + 进程随时被玩家杀掉的前提下不可靠（断管、重启、权限）。
文件桥复用 KPP 已有的 `scan_once.flag` 约定（`Data/ProfilerData.Memory.cs:199`），可离线、可抓包、崩溃安全。

## 1. 目录布局（全部位于 KSP 实例的 GameData 下）

```
<Instance>/GameData/KSPPerformanceProfiler/Telemetry/
  latest.json              # KPP 写，单文件整快照，周期性原子替换
  status.json              # KPP 写，桥自身健康度（独立于数据，用于区分"没数据"和"Mod 没装"）
  commands/<seq>-<id>.json # ckan-ui 写，KPP 消费后删除
  commands/acks/<id>.json  # KPP 写，ckan-ui 读后删除
```

- 探针判定（ckan-ui 侧）：目录存在且 `status.json.writerAlive` 为真 → 在线；目录不存在 → Mod 未安装；
  目录不存在但同级找到了 KPP 本体（有 `Plugins/` 或 `*.version`）→ **Mod 是旧版，不带文件桥**（提示升级，不说"没装"）；
  目录存在但 status 陈旧（`publisherAgeMs` > 3× publishIntervalMs）→ 游戏已暂停/已退出。
  判据必须用 KPP 自己的目录特征而不是"父目录存在"——后者会把 `%TEMP%` 之类本来就有的目录误判成"装过 Mod"。
- **不递归**扫描子目录（`commands/` 除外）。

## 2. 原子写（双方通用）

写 `<name>.tmp` → `File.Replace`（目标存在时）或 `File.Move`（目标不存在时）；失败回退为
`File.Delete(dest)` + `File.Move(tmp,dest)`。**读方必须容忍读到一半**：解析失败即视为该轮无数据，
不得抛出到 UI 线程，也不得清空上一份好数据。
编码 UTF-8 无 BOM。

## 3. latest.json —— KPP → ckan-ui

顶层（缺字段一律视为"该能力不可用"，UI 显示 `N/A` 而不是 0）：

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| schema | string | 固定 `kpp.telemetry/1` |
| seq | number | 单次会话内单调递增；UI 用它判断"数据是新的" |
| publishedUnixMs | number | 发布时刻（epoch ms） |
| publishIntervalMs | number | 发布周期，UI 据此判断陈旧 |
| writer | object | `{ pid, dllVersion, kspVersion, unityVersion, os }` |
| session | object | `{ id, startedUnixMs, uptimeSec, frameCount }` |
| context | object | `{ scene, vesselName, partCount, modCount, paused, timeScale, gameTimeSec }` |
| frame | object | `{ fps, avgFps, onePercentLowFps, pointOnePercentLowFps, frameMs, avgFrameMs, jitterMs, ptr, dt, physSteps }` |
| breakdownMs | object | `{ moduleScripts, pluginScripts, physx, renderResidual, camera, tufxPostFx, tufxCpu, gc }` |
| gpu | object | 见 §3.1；`available=false` 时只有 `statusText` |
| gpuBrackets | array | `[{ camera, eventName, owner, ms, samples }]`，可能为空 |
| memory | object | 见 §3.2 |
| hotspots | object | 见 §3.3 |
| timeline | object | `{ window, frames: [{ t, frameMs, module, plugin, physx, render, gpu, gc, fps }] }`，环形缓冲，最新在末尾。**`gc` 是 bool**（该帧是否发生了一次 GC），毫秒数在 `breakdownMs.gc` |
| diagnostics | object | 见 §3.4 |
| spikes | array | `[{ frame, atUnixMs, frameMs, reason, culprits: [热点条目] }]`，最多 8 条。**`culprits` 是热点条目形状**（与 `hotspots.*` 的 E 同构），不是 `diagnostics.culprits` 的 `{source,name,assembly,ms,pct}` |
| counters | object | `{ gcCount, spikeCount, droppedFrames, reportExports }` |

### 3.1 gpu
`{ available, statusText, frameSpanMs, stageTotalMs, unaccountedMs, publishedFrames, invalidStageFrames, dropped, reordered,
stages: [{ key, ms, valid }] }`
`stages[].key` 固定为 `depth | opaque | transparent | postFx | final`。`valid=false` 时 `ms` 必须是 `null`——
**绝不允许**用 0.0 冒充一次被驱动拒绝的计时（沿用 `ProfilerData.Reporting.cs:11` 的 NaN 纪律）。

### 3.2 memory
`{ monoHeapMb, reservedMb, monoUsedMb, gcLastMs, gcAvgMs, gcCount,
   padder: { enabled, targetMb, heldMb, maxMb, physicalMb },
   probe: { state, assetsScanned, texturesMb, meshesMb, soundsMb, dbTexturesMb, lastScanUnixMs },
   topAllocators: [{ name, assembly, bytesPerFrame }] }`

### 3.3 hotspots
`{ modules: [E], plugins: [E], methods: [E], parts: [E], assemblies: [E] }`，每个数组最多 24 条、按 `ms` 降序（assemblies 同理）。
`E = { name, assembly, type, friendly, category, ms, peakMs, pct, calls, bytesPerFrame, isTufx }`（无值字段省略）。
`pct` 是占当前帧总耗时的百分比（0-100）。

### 3.4 diagnostics
`{ title, description, advice, severity: "ok"|"warn"|"critical"|"unknown",
   culprits: [{ source, name, assembly, ms, pct }],
   checks: [{ id, label, state: "ok"|"warn"|"critical"|"unknown", value, detail }] }`

`severity` 的 `unknown` 表示「诊断本身没跑起来」（KPP 侧 Detector 抛异常时），与 `ok`（跑起来且没发现问题）必须区分——读方把 unknown 渲染成灰条 + 原文 tooltip，不能画成绿色。

**`checks[].value` 是字符串**（已格式化、可直接显示，例如 `"93.4%"`、`"271 次"`），不是数字——检查项的量纲本来就各不相同，写方负责格式化，读方不做单位推断。

## 4. status.json —— 桥健康度

`{ schema, writerAlive, pid, uptimeSec, publishIntervalMs, lastPublishUnixMs, lastPublishMs, writeFailures,
   commandsSeen, commandsApplied, commandsFailed, bridgeVersion, telemetryDirectory }`

`writeFailures`/命令计数让 UI 能解释"在线但没数据"：磁盘满、权限、路径错都会在这里现形。

## 5. command（ckan-ui → KPP）

写入 `commands/<seq>-<id>.json`，`<seq>` 为 8 位零填充序号（保证按名字排序即按发出顺序）：

```json
{ "schema": "kpp.command/1", "id": "<uuid>", "op": "set_pad", "issuedUnixMs": 0, "args": { "megabytes": 4096 } }
```

| op | args | 语义 |
| --- | --- | --- |
| `ping` | {} | 探活，ack 回写版本与会话 |
| `describe` | {} | 回写能力清单（op 列表、padder 上限、是否支持 GPU 实测） |
| `export_report` | {} | 导出文本报告，ack.result 带 `path` |
| `set_pad` | `{ megabytes }` | 垫高 Mono 堆；`0` = 释放 |
| `release_pad` | {} | 归还物理内存 |
| `set_capture` | `{ enabled, thresholdPct, thresholdMs, freeze }` | 掉帧抓拍阈值与自动急冻 |
| `set_publish_interval` | `{ ms }` | 发布周期，钳制 250–5000 |
| `reset_stats` | {} | 清空平滑统计与尖峰列表（不动 padder） |
| `request_scan` | {} | 触发资产/内存诊断扫描（等价于写 `scan_once.flag`） |

**禁止**在 v1 引入会改画面的 op（重排相机缓冲等）——两侧版本不同步时那等于远程改游戏。

ack（`commands/acks/<id>.json`）：
`{ schema, id, op, ok, completedUnixMs, message, result: {} }`
失败必须 `ok:false` + 可读 `message`，不许静默吞掉（这是原版四处静默失败的反面）。

## 6. 兼容与降级

- UI 只认 `schema` 前缀 `kpp.telemetry/1`；更高次版本（`1.3`）要能读，缺字段显示 `N/A`。
- 主版本不同（`2`）→ 灰条提示"遥测契约不兼容"，不尝试解析。
- KPP 收到不认识的 `op` → ack `ok:false, message:"不支持的命令 op"`，不删不认的命令文件，等 UI 自己超时清理。

## 7. 侧别归属

| 侧 | 目录 | 责任 |
| --- | --- | --- |
| KPP | `Telemetry/` | 写 latest/status、消费 commands、写 acks |
| ckan-ui 插件 | `src/CkanUI.Plugin.Performance/` | 读 latest/status、写 commands、读 acks |
| ckan-ui 宿主 | `src/CkanUI.Services/Plugins/` | 插件发现/激活持久化/内置插件目录 |
