# 性能与内存实测报告（GUI 0.9x 大更新后）

> 全部数字来自本机真机探针：真实 DI、真实实例数据、ANGLE/D3D11 合成、真实窗口。
> 复现脚本都在 `artifacts/`，探针实现见 `src/CkanUI.App/Diagnostics/NavigationProbe.cs`。

## 1. 怎么量

| 场景 | 命令 |
| --- | --- |
| 单页隔离（冷启动只进一页） | `CFG=artifacts/perf/probe-config-large.json TAG=x KEYS=settings node artifacts/nav-iso.mjs` |
| 24 页全遍历（两轮 + 帧窗口） | `node artifacts/nav-large-final.mjs` |
| 加按钮点击 | 追加 `CKANUI_NAV_CLICKS=1` |
| 加页内页签切换 | 追加 `CKANUI_NAV_TABS=1` |
| 加按钮清点 | 追加 `CKANUI_NAV_BUTTONS=1` |
| 内存输出 | `CKANUI_NAV_MEM=1`（`ws/private/heap`） |
| 接入真实整合包 | `$env:CKAN_CONFIG_FILE="$env:APPDATA\CKAN\GUI\config.json"; node tools/perf-probe/measure-instance.mjs`（只读探针，自带 24 页 ×2 轮遍历与 ws/private/heap 采样；逐页 trace 落在 `artifacts/nav-instance.log`） |
| 手工起真实实例 | `CKAN_CONFIG_FILE=<你的配置.json> CKANUI_PROBE_REAL_CONFIG=1 CKANUI_PROBE_INSTANCE=1` |

两套基准实例：`artifacts/perf/instance-steam`（336 模组）与 `instance-large`（3000 模组），均由 junction 指向真实 GameData，`CKAN/CKAN` 为独立副本，不会锁住用户实例。

## 2. 结论一：命令层已经是异步的

3000 模组实例，32 条安全白名单命令（Refresh/Reload/Recompute/Validate/Copy 前缀）全点一遍：

- 命令体同步耗时最大 **14.3ms**
- UI 线程恢复最慢 **82ms**
- 点后 1.2s 帧窗口最大间隔 **35.7ms**，>50ms 掉帧 **0 次**

⇒ 「点按钮卡住」不成立。观感卡顿来自页面首进与大数据回填渲染。

## 3. 结论二：掉帧集中在首进

| 页面 | 冷启动单页 layout | 会话内首进 layout | 二次进入 |
| --- | --- | --- | --- |
| settings | **1410.6ms** | 312.6ms | 37.6ms |
| modconfig | 499.7ms | 803.0ms | — |
| repos | 235.9ms | 1062.7ms | 34.5ms |
| saves | — | 583.0ms | — |
| changes | 186.8ms | 106.8ms(assign) | — |
| downloadstats | 180.8ms | 127.3ms | — |

冷启动时 settings 一页就要 1.4s（2338 个可视节点、36 行 × 65 节点的编辑器面板）；repos 在会话内 1062ms 主要是与前面页面后台加载争抢 UI 线程，脱离会话只有 236ms。

页签遍历：31 页里只有 modpacks（16.6ms）与 rp1-career（56.9ms）真用 `TabControl`，其余「页签」是顶栏 SectionTabs，本质是导航键，已被 24 页 ×2 轮覆盖。

## 4. 结论三：内存是「激活期尖峰 + 全页常驻」

单页隔离（重复 5 次，3000 与 336 模组一致）：heap **62–72MB**、WS **305–325MB**。

24 页走完同一进程的累积堆：

| 到达页面 | 累积 heap | 增量 |
| --- | --- | --- |
| mods | 71MB | 基准 |
| content | 211MB | +140MB |
| changes | 561MB | +350MB |
| repos | 221MB | −340MB（离页释放） |
| downloadstats | 573MB | +270MB |
| saves | 1324MB | +666MB |
| home（收尾） | 868MB | 稳态 |

WS 峰值 1824MB，稳态 1345–1425MB。9 分钟长跑首尾 +3.5MB ⇒ **不是泄漏**，是「激活期持有、离页释放」+ 24 页常驻。

## 5. 4 GB 从哪来：怎么在自己的包上量

我不能替你量你的机器，但可以给你一条命令——`tools/perf-probe/measure-instance.mjs` 只读打开你的 CKAN 配置，走完 24 页 ×2 轮，把每页的 `ws/private/heap` 写进 `artifacts/nav-instance.log`：

```powershell
$env:CKAN_CONFIG_FILE = "$env:APPDATA\CKAN\GUI\config.json"
node tools/perf-probe/measure-instance.mjs
```

看三件事：
1. 哪一页之后 `ws` 跳得最多（本机是 saves +666MB、changes +350MB、downloadstats +270MB）；
2. `private - heap` 的差（本机 24 页收尾 ws 1280 / private 1200 / heap 860 ⇒ 非托管约 340MB，约 14MB/页，合成器 + Skia 表面）；
3. 走完后再静置 30 秒看 `ws` 是否回落（回落慢就是 LOH 段没压）。

### 5.1 外部工作集时间轴（不依赖应用内探针）

`tools/perf-probe/measure-instance.mjs` 现在自己每 5 秒问一次 Windows（`tasklist /FI "PID eq <pid>"`），把工作集打成 `[ws] t=Ns ws=NNNMB` 时间轴，结束时给峰值。为什么另做一套：应用内的 `[nav]` 行只在走完一页时打印，「静静涨上去」的现场抓不到；而且日志目录不可写时 log4net 会静默丢行。

实测（336 模组实例，24 页全扫，2026-11-05）：

| t | 6s | 11s | 16s | 21s | 26s | 31s | 41s | 61s |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| WS | 358MB | 815MB | **938MB** | 545MB | 776MB | 845MB | 752MB | 871MB |

峰值 938MB 落在 t≈16s（首屏 + 首轮预热），不是扫到后面的页面；40s 之后在 735–871MB 之间来回，收尾稳态约 620MB。用法不变，只是多了一段峰值行：

```powershell
$env:CKAN_CONFIG_FILE="$env:APPDATA\CKAN\GUI\config.json"
node tools/perf-probe/measure-instance.mjs
```

### 5.2 峰值的两次误读：单跑不可比，涨幅跟着"看了几页"走

同一个 336 模组实例、同一条命令，三次峰值：**938MB / 1499MB / 1553MB**。差别不在改动，而在这一轮扫到了第几页——t=60s 之后才开始的页面（saves / downloadstats / cache）会把工作集推到 1.2–1.6 GB 区间，60 秒前结束的那次自然只有 938MB。结论：**单次峰值不能横向比较，必须连同轨迹一起看**。

单页隔离（`CKANUI_NAV_ONLY=<key>`，每页独立进程，45s 窗口）把涨幅拆开：

| 只看这一页 | home | cache | downloadstats | saves |
| --- | --- | --- | --- | --- |
| 峰值 WS | 891MB | 1220MB | 1304MB | 1401MB |

相对 home 的边际：saves ≈ +510MB、downloadstats ≈ +413MB、cache ≈ +329MB。这三页都是「首进即把整份数据投影出来、离开也不还」，正好对应全仓只有 content / mods / modtext / kspconfig 四个 VM 实现 `IPageProjectionTrim` 这件事——其余 20 页没有释放契约。访问页数越多，累积越接近 1.6GB；把同样的斜率外推到数据量级更大的真实整合包，就是「4 个 G」的量级。

`System.GC.ConserveMemory=5` 的 A/B（`runtimeconfig.template.json`）：1499/1575MB，与关掉时的 1553MB 无差异 ⇒ **该开关对本应用无效，已回退**，不进主干。原因也清楚：占大头的是页面投影与合成器表面，不是 GC 该收的堆碎片。

### 5.3 空闲回收后的回落：峰值是临时的，常驻才是账

`CKANUI_NAV_ONLY=<key> CKANUI_NAV_IDLE_CHECK=1`（单页、45s 窗口、末尾静置 25s）：每个页面自己一个进程，读 `[nav] r1` 与 `[nav] idle-after-sweep` 两行。

| 页面 | 首进峰值 ws / heap | 静置 25s 后 ws / heap |
|---|---|---|
| home | 996 / 573 MB | 869 / 225 MB |
| cache | 1096–1370 / 486–684 MB | 1017–1024 / 368–369 MB |
| downloadstats | 1323 / 638 MB | 1341 / 364 MB |
| saves | 1268 / 583 MB | 837 / 245 MB |

⇒ 堆（heap）在静置后一律回到 225–370MB：**首进那 300–600MB 是可回收的垃圾，不是常驻**。工作集（ws）回落幅度不一（saves −431MB、cache −80MB、downloadstats 持平），因为 GC 提交过的段不会立刻还给操作系统——这部分靠 `IdleMemoryReclaimer` 的整理式回收才慢慢降，属于「已提交未使用」，不是泄漏。

**这条否证了「这 20 页的投影永久钉住内存」的推断**：同一页面有/无行投影裁剪时，静置后 ws 分别是 1021MB 与 1017–1024MB，差在噪声里。

### 5.4 试过并回退的实验：给 cache 页补 `IPageProjectionTrim`

`CachePageViewModel`（当轮干净文件）实现 `TrimProjection()`：`IsActive` 时返回 0，否则清空 `Entries`/`EntryCount`/`TotalBytes`/`UsagePercent` 并复位 `_hasLoadedOnce`，让下次 `OnActivated` 重新读盘；两条单测（丢行 + 重进重载、可见页面不清空）全绿。

A/B（同一实例、同一命令，各 1–2 次）：

- 有裁剪：首进 ws=1130MB/heap=483MB → 静置 ws=1021MB/heap=369MB
- 无裁剪：首进 ws=1096–1370MB/heap=486–684MB → 静置 ws=1017–1024MB/heap=368–369MB

⇒ 差在噪声内：缓存列表的行对象只有几 MB，真正占内存的是条目 DTO 与字符串，GC 本来就能收。代价是每次重进页面多读一次盘（约 60ms）。**已整体回退（文件与测试均复原到 HEAD），代码不进主干**。结论：给页面补裁剪对这类「可重读列表」没有收益，别再加了。
## 6. 待决策的三个杠杆

| 杠杆 | 预期收益 | 阻塞 |
| --- | --- | --- |
| 20 个页面补 `IPageProjectionTrim` | 稳态 868MB 至少砍半 | `ChangesPageViewModel` / `DownloadStatsPageViewModel` / `ModsPageViewModel` / `SettingsPageViewModel` 等是他人未提交改动 |
| 四个尖峰页离页即释放（saves 666 / changes 350 / downloadstats 270 / content 140） | 峰值 1824→约 900MB | 同上 + `src/CkanUI.Plugin.Saves/*` |
| 页面驻留预算（最近 N 页，其余摘舞台） | 常驻再砍一半 | PageStage 在 `src/CkanUI.App/MainWindow.axaml.cs` |

## 8. 滚动与「每次进入」实测（3000 模组实例）

滚动窗口 60 帧、步进 24px、取最大可见 ScrollViewer（`CKANUI_NAV_ONLY=<key> CKANUI_NAV_SCROLL=1`）：

| 页面 | p50 | p95 | max | dropped |
| --- | --- | --- | --- | --- |
| settings | 5.6 / 5.4ms | 10.8 / 7.2ms | 11.6 / 11.1ms | 0 / 0 |
| logs | 0.1 / 0.0ms | 11.2 / 0.1ms | 16.6 / 0.1ms | 0 / 0 |
| modconfig | 10.6 / 6.1ms | 16.6 / 16.9ms | 22.0 / 22.1ms | 2 / 5 |
| saves | — / 5.7ms | — / 13.8ms | — / 17.5ms | — / 2 |

⇒ 稳态滚动已经不掉帧；mods / kspconfig / downloadstats 的滚动量不到（`[scroll] skip … reason=no-scroller`，虚拟化列表 Extent 未展开），这三页的滚动缺口仍在。

真正被感知的卡顿是**每次进入 mods 页**：`CKANUI_NAV_ONLY=mods` 两轮都是 `layout≈3003ms`，且 336 模组与 3000 模组完全一样（`layout=3015.3ms`、`nodes=2333` 两者相同）⇒ 与模组数无关，是 DataGrid 建容器/套主题的一次性成本，不是数据量问题。稳态帧 `maxGap=37.0ms`，帧间隔 p50 16.8ms。

saves 页滚动偶发 `max=1107.6ms`（4 轮复现 1 次）：`SavesPageViewModel.KickValidate()` 在 `OnSelectedRowChanged` 里无条件 `_ = ValidateCoreAsync(row)`（src/CkanUI.Plugin.Saves/SavesPageViewModel.cs:645/847），选中一个 120MB 存档就整文件 `File.ReadAllBytes` 解析一次。热缓存后不再出现。

## 10. mods 页每次进入 1–3 秒的归因（dotnet-trace 采样）

方法：`CKANUI_NAV_ONLY=mods` + `CKANUI_NAV_WARMUP_MS=12000`，第 10 秒起 `dotnet-trace collect -p <pid> --providers Microsoft-DotNETCore-SampleProfiler --duration 00:00:10`，再 `dotnet-trace convert --format speedscope`。

UI 线程（采样）自时间排名：`Avalonia.StyledElement.ApplyStyles` **1105 次采样**，第二 `Layoutable.MeasureCore` 只有 567 次；`ApplyStyles` 的调用者统计里 1053 次是**自己递归自己**（`ApplyStyles <- ApplyStyles`），外层只有 26 次来自 `OnAttachedToLogicalTreeCore`。

⇒ 进入 mods 页的卡顿不是数据量，是**整棵可视子树被重新套一遍样式**（2333 个节点 × 样式匹配 ≈ 0.5ms/节点）。这解释了为什么 336 模组与 3000 模组的 `layout` 完全一样（3015.3ms / 3003.4ms）、`nodes` 都是 2333。

可疑触发点（都在他人未提交的改动里，不动）：
- `src/CkanUI.App/Views/ModsPageView.axaml:16` 在页面根上写了 `<StyleInclude Source="avares://Avalonia.Controls.DataGrid/Themes/Fluent.xaml" />` —— 视图每次进树都带着整套 DataGrid 样式重新套用；
- `ModsPageViewModel.OnActivated`（src/CkanUI.ViewModels/Pages/ModsPageViewModel.cs:1066-1090）每次都先改 `IsDetailsPaneVisible` 再 `ReloadFromSnapshots()`，属性变化会再触发一次子树样式失效。

基线对照做不到：`git worktree` 出来的 HEAD 副本缺 `external/CKAN`（未入库）与本地 NuGet 缓存，`dotnet restore` 直接 `MSB3202 未找到 external\CKAN\Core\CKAN-core.csproj`，所以没拿到「HEAD 版本 mods 进入耗时」的对照值。

验证这两条只需要把 StyleInclude 移到 App 级、把密度/详情窗格属性改成「与当前值不同才赋值」，但两个文件都是别人的 WIP。

### 10.1 样式表数量不是杠杆（实测否证）

`CKANUI_NAV_STYLES=1` 统计 PageStage 里 31 个页面视图自带的样式表：`[styles] app=2 pages=31 total=148 top=LogPageView=24,SoundtrackPageView=23,Rp1CareerPageView=16,PerformancePageView=14,AiAssistantPageView=9,SavesPageView=9`。全应用只有 2 个 App 级样式表，31 个页面加起来 148 个 Style，ModsPageView 连前 6 都进不去。

⇒ 「每个页面各带一份 DataGrid 主题，样式数量爆炸拖慢 ApplyStyles」这个假设**不成立**，不用去合并 StyleInclude。

剩下的解释只有两条，都指向同一个动作——**减少进树节点或减少重进树次数**：
1. 单个节点的 `ApplyStyles` 在 Avalonia 12 里要约 0.5ms（2333 节点 ≈ 1.1s），settings 页同样 2338 节点却只要 29ms ⇒ 差异在 DataGrid 行容器的实现方式，不在样式数量；
2. mods 页每次进入都重跑一遍这 1.1–3.0s，说明视图确实在重新进树（否则 Loaded 优先级的回调不会被饿 3 秒）。

动作归属：`ModsPageView.axaml` / `ModsPageViewModel.cs` 都是他人 WIP，需要授权。

### 10.2 更正：所谓「每次进入 3 秒」是探针假象，真实重进 28ms

`CKANUI_NAV_ONLY=mods` 之前每轮的第 2 次那条 `assign=-1.0 layout=3004ms` 不是页面成本，而是**探针自己制造的空转**：同一页面重复导航会被 Shell 当成 no-op（`CurrentPage` 不变），探针等满 `NavigateAsync` 里的 `Task.Delay(3000)` 才继续。

修法（已提交）：重复 key 时先跳到另一个页面再回来。

修好后实测（336 模组，同一进程）：

| 轮次 | assign | layout | paint | maxGap | nodes |
| --- | --- | --- | --- | --- | --- |
| r1 首进 | 421.1ms | 2007.4ms | 2022.0ms | 1572.8ms | 2333 |
| r2 真重进 | 10.6ms | 28.2ms | 29.9ms | 24.4ms | 2441 |

⇒ mods 页只有**进程内首进**约 2 秒（JIT + 建视图 + 首次套样式），重进 28ms，稳态 60fps。上一轮「每次进入都卡 3 秒」的结论作废；`ApplyStyles` 递归那部分仍然成立，但它只发生在首进那一次。

同理，之前 24 页表里所有 `assign=-1.0` 的行都要按这个口径重读——它们是 no-op + 3 秒探针超时，不是页面耗时。

### 10.3 修好探针后的干净首进表（3000 模组实例，24 页 + 插件页，r1 首进 / r2 真重进）

| 页面 | r1 assign | r1 layout | r1 paint | r1 maxGap | r2 layout |
| --- | --- | --- | --- | --- | --- |
| changes | 444.5ms | **966.7ms** | 511.5ms | 86.1ms | — |
| modconfig | 220.7ms | **632.0ms** | 408.0ms | 553.1ms | 273.8ms |
| saves | 81.8ms | **539.9ms** | 305.8ms | 274.5ms | 42ms |
| settings | 24.7ms | **434.6ms** | 405.6ms | 323.1ms | 42.5ms |
| logs | 38.5ms | 341.5ms | 304.8ms | 288.4ms | — |
| modtext | 54.7ms | 332.7ms | 198.4ms | — | — |
| rp1-career | 142.5ms | 317.9ms | 160.2ms | 22.1ms | 54.0ms |
| mods | 180.2ms | 363.0ms | 145.1ms | 244.3ms | 264.3ms |
| instances | 57.6ms | 235.5ms | 176.4ms | 36.7ms | — |
| ships | 89.8ms | 203.5ms | 64.4ms | 11.3ms | — |

重进（r2）最差是 modconfig 273.8ms、mods 264.3ms，其余都在 110ms 以内；settings 重进只要 42.5ms。

⇒ 结论定稿：**首进成本是真实存在的（changes 0.97s、modconfig 0.63s、saves 0.54s、settings 0.43s），重进基本不掉帧**。用户体感的「进页面卡一下」属于首进，不是稳态缺陷；把这批页面预热（已有 PageWarmup 机制）或把首进的重活挪到后台，是把 0.4–1s 抹掉的唯一路径。

## 11. 已落地

- `7f2149a` 存档头按字节封顶（`HeaderByteBudget=256KB`），不再把 ArrayPool 顶到 512MB
- `905de7b` 缩略图缓存 64MB 字节预算 LRU（旗帜页滚动掉帧 9→3→0）
- `a9f5dfc` ZIP 预览与条目投影移出 UI 线程（内容页 480ms 同步块清零）
- `9eae410` 大存档校验后压缩 LOH
- `da11ccb`/`9734f2c`/`3c62845` 探针：每页 ws/private/heap、页内页签、按钮清点
