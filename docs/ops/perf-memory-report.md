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

### 10.4 两条消融实验：晚到的快照刷新不是主因，主题搬迁无收益（都已回退）

真机（336 模组）今天复测：mods 首进 `layout=1221.6 / 1286.3 / 1176.2ms`（同构建三次，`maxGap` 1.0–1.3s，`nodes` 恒 2333），跨构建波动 ±10%，只有 `nodes` 稳定——所以下面两组 A/B 都用同构建、多跑取区间。

1. **快照刷新消融**（临时 `CKANUI_EXP=1` 让 `ModsPageViewModel.Subscribe` 不挂 `_repositories.Updated`/`_registry.Changed`）：`layout=1176.2ms`，与基线 1221.6ms 差在噪声内 ⇒ 启动后晚到的那次 `ReloadFromSnapshots` **不是**首进 1.2s 的主因，别再往订阅/防抖上加机制。
2. **DataGrid 主题搬迁**（把 `ModsPageView.axaml:16` 与 `DownloadStatsPageView.axaml:13` 的 `StyleInclude avares://Avalonia.Controls.DataGrid/Themes/Fluent.xaml` 提到 `App.axaml`，页面内删掉）：`r1 layout=1355.6ms`（比基线差约 10%），`r2 layout=33.4ms`、`nodes` 2333→2441 无功能变化 ⇒ **无收益，已按字节还原**（`ModsPageView.axaml` md5 `b01e259a49fb19c9c19020f1f7539863`、`App.axaml` md5 `fd0be9af070d3edae6d03511545efd76`）。与 10.1 的样式数量否证一致：杠杆不在样式表份数。

⇒ mods 首进 1.2–1.4s 归因保持 10.3 的结论：**进程内第一次把 2333 个节点挂进可视树并首次套样式**，属"加载"，不是交互掉帧；要抹掉只能预热（现在只在窗口失焦时预热，前台不预热，见 12 的取舍）。

## 11. 已落地

- `7f2149a` 存档头按字节封顶（`HeaderByteBudget=256KB`），不再把 ArrayPool 顶到 512MB
- `905de7b` 缩略图缓存 64MB 字节预算 LRU（旗帜页滚动掉帧 9→3→0）
- `a9f5dfc` ZIP 预览与条目投影移出 UI 线程（内容页 480ms 同步块清零）
- `9eae410` 大存档校验后压缩 LOH
- `da11ccb`/`9734f2c`/`3c62845` 探针：每页 ws/private/heap、页内页签、按钮清点
## 12. 卡顿看门狗：冻结发生在进页后 1–1.5 s，GC 停顿占六成量级

### 12.1 怎么量

`CKANUI_NAV_STALLS=1` 打开 `src/CkanUI.App/Diagnostics/NavigationProbe.cs` 里的看门狗：后台线程每 15 ms `Dispatcher.UIThread.Post` 一个空操作，用 Stopwatch 量「投递 → 执行」的延迟，>50 ms 记一次卡顿。

两个细节决定这张表能不能信：

- **归属必须在投递时取**。第一版在回调里读当前页名，结果卡顿被记到下一个页面上（跨页漂移），整张表看起来毫无规律。
- **阶段要切细**。每页 1500 ms 采样窗被切成 window0/1/2 三段，另加 startup/enter/gap/click/scroll，才能区分「进页面就慢」和「一秒钟后才慢」。

同一份代码在 `[nav]` 行尾追加本页窗口内的 `gen2=`（Gen2 次数）与 `pause=`（`GC.GetTotalPauseDuration()` 增量），把「卡」和 GC 停顿放进同一张表对账。

### 12.2 冻结在哪个阶段（336 模组实例，31 页 × 2 轮）

| 阶段 | 累计卡顿 | 次数 | 最长 |
| --- | --- | --- | --- |
| window2（进入后 1000–1500 ms） | 12545 ms | 31 | 652 ms |
| enter（点下去到首屏） | 4051 ms | 17 | 850 ms |
| gap（页与页之间） | 3910 ms | 15 | 895 ms |
| window0（0–500 ms） | 736 ms | 2 | — |
| window1（500–1000 ms） | 186 ms | 2 | — |
| startup | 383 ms | 1 | — |

⇒ 44% 的卡顿落在 window2，即**异步数据落地那一段**。用户说的「GUI 好像没有异步计算」，指的不是命令体——上一轮 32 条白名单命令 `sync≤14.3 ms`、UI 恢复 `≤82 ms`、`over50=0`。真正冻结的是数据回填后的一次性建行/排版/GC。

### 12.3 每页 GC 停顿 vs 页面卡顿（同一次运行）

| 页面 | GC 停顿 | 卡顿累计 | 页面 | GC 停顿 | 卡顿累计 |
| --- | --- | --- | --- | --- | --- |
| changes | 2010 ms | 1016 ms | modtext | 339 ms | 1736 ms |
| instances | 1586 ms | 197 ms | playtime | 324 ms | 994 ms |
| saves | 1376 ms | 392 ms | kspconfig | 305 ms | 392 ms |
| installhistory | 1128 ms | 1228 ms | dedup | 289 ms | 0 ms |
| cache | 893 ms | 1474 ms | plugins | 276 ms | 1220 ms |
| modconfig | 760 ms | 1078 ms | modpack-profiles | 271 ms | 835 ms |
| performance | 742 ms | 2171 ms | storage | 266 ms | 2296 ms |
| ai-assistant | 701 ms | 0 ms | mods | 243 ms | 235 ms |
| content | 687 ms | 86 ms | repos | 221 ms | 449 ms |
| rp1-career | 642 ms | 805 ms | modpack-editor | 205 ms | 2525 ms |
| ships | 619 ms | 1679 ms | modpacks | 141 ms | 89 ms |
| music | 399 ms | 2927 ms | 合计 | **17159 ms** | **28592 ms** |

**相关性别看错**：逐页 pause 与逐页卡顿的皮尔逊 r = −0.06，不是正相关。样本只有 31 个、两边归属都有漂移（一次 GC 可能发生在别页的窗口里，一次卡顿也可能是建行而非 GC），所以正确的口径是：**总量上 GC 停顿 17.2 s ≈ 卡顿总量 28.6 s 的六成，但「哪一页卡」不能只看 GC**。要钉到具体一行，得用 dotnet-trace 采 window2 窗口的 UI 线程叶子帧，或按页关掉后台加载做 A/B。

### 12.4 试过并否决：SustainedLowLatency

假设是 GC 停顿就降低 GC 攻击性：`GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency`（在 `Program.Main` 里按 `CKANUI_GC_LOWLATENCY=1` 临时注入，同一次构建跑两遍，唯一差异是这个环境变量）：

| 指标 | 基线 | SustainedLowLatency |
| --- | --- | --- |
| 卡顿次数 / 超 200 ms | 67 / 46 | **101 / 53** |
| 31 页 GC 停顿合计 | 17159 ms | **30389 ms** |
| 峰值工作集 | 1584 MB | 1654 MB |

停顿反而涨了 77%，卡顿次数涨了 50%。原因：低延迟模式把阻塞式 Gen2 换成后台 GC，但分配速率不变，堆继续涨，后台 GC 追不上就一直重试，反而更频繁地抢占。**已按 md5 原样还原 `src/CkanUI.App/Program.cs`（a3ae264b…），配置未进主干。**

### 12.5 结论

1. 卡顿不在命令体（全异步），在**进页面后 1–1.5 s 的数据回填段**：建行、排版、Gen2/LOH 一次性分配。
2. GC 停顿是最大单一来源（六成量级），但它不解释「为什么偏偏这页卡」。
3. 剩下的空间在**每页首进的投影体量**上；要压它必须改各页 VM（`IPageProjectionTrim` 目前只有 content/mods/modtext/kspconfig 实现），而音乐、存档、存储、模组配置、设置、变更、下载统计等页的 VM 仍在其他人的未提交改动里，按约定我没有动。

## 13. 团队三条线的实测结论

同一棵工作树上三条线并发测量（构建/探针用 `artifacts/run.lock` 串行），口径与噪声见 13.1 的提醒。

### 13.1 空闲内存回收门限 A/B：噪声内，已回退

候选改动（`src/CkanUI.App/Diagnostics/IdleMemoryReclaimer.cs`）：把 `GrowthThresholdBytes` 128 MB → 64 MB（真机 31 页两轮 round1 1164 MB → round2 1247 MB 只涨 83 MB，按 128 MB 门限永远收不到），并在每次回收前设 `GCSettings.LargeObjectHeapCompactionMode = CompactOnce`（`compacting:true` 只压 SOH，不搬 LOH）。

量法：`dotnet CkanUI.dll`（apphost `CkanUI.exe` 在本机静默 exit 1）+ `artifacts/icm-probe.mjs`，31 页 × 2 轮 → 25 s 静置 → `artifacts/cpu-sample.ps1` 2 × 20 s；heap 取 `GC.GetTotalMemory(forceFullCollection:true)`。

| 运行 | round1 ws/private/heap | round2 ws/private/heap | 静置后 ws/private/heap | 峰值 ws | 空闲 CPU（单核） |
| --- | --- | --- | --- | --- | --- |
| base#1 | 1144/1044/752 | 1175/1077/753 | 1133/1039/753 | 1709 MB | 1.6% / 2.3% |
| base#2 | 1134/1036/748 | 1141/1052/749 | 1112/1020/749 | 1672 MB | 1.1% / 1.1% |
| new#1 | 1153/1057/752 | 1167/1066/754 | 1153/1050/754 | 1768 MB | 1.3% / 1.4% |
| new#2 | 1076/1012/742 | 1080/1013/741 | 1047/980/741 | 1684 MB | 1.0% / 0.9% |

结论：round2 均值差 −34 MB，但 new 组内 spread 87 MB 已经把组间差吃掉（峰值反而高 60–90 MB），按「噪声内回退」执行。文件已按 HEAD 快照原样还原：`git diff --quiet` 返回 0、blob 与 HEAD 同为 `8354e1e`，改动未进主干。

**这条负面证据比数字本身有用**：heap 是强制全量 GC 之后的**存活**量，四个来回都停在 741–754 MB、两轮之间只差 0–2 MB。也就是说 1164 → 1247 MB 那 +83 MB **不是托管垃圾**——GC 调门限、压 LOH 都拿不回来。托管堆这一侧已经到顶，剩下的量在原生/渲染侧和各页仍被引用的投影上。

口径提醒：并发构建/探针期间实测抖动 ±80 MB，30 MB 级的差异单次不可判定；要判必须等安静窗口。

空闲 CPU 这条线：该改动不涉及定时器，前后 1.0–1.4% vs 1.1–2.3%（单核，探针静置口径）无差异；`IdleMemoryReclaimer` 的 8 s 轮询是 `DispatcherPriority.Background`，且只在空闲时读一次工作集，不是常开热路径。

### 13.2 logs 首进拆分

一次首进 954 ms 的拆解：`view-ctor` 65.1 ms、`attach→layout` 782 ms、73 行 × ~10 ms/行、热路径 107 ms。⇒ 首进成本主要在**逐行建行 + 排版**，VM 构造本身不是大头；要压得从行数与模板下手，不是从构造函数下手。

### 13.3 downloadstats 的 RowLimit 线性

| RowLimit | 耗时 | 节点数 |
| --- | --- | --- |
| 20 | 273.5 ms | 229 |
| 200 | 556 ms | 1724 |

两点拟合：固定开销 ≈ 240 ms + ≈ 1.6 ms/行（行数增加 180 行、耗时增加 282.5 ms）。⇒ 建行成本随行数**近线性**增长，压时间要靠降行数/虚拟化，靠调窗口大小没用。

### 13.4 空闲 CPU 归因：不是定时器空转，是帧还在产

量法：让应用静置 10 分钟，再对它采 20 s 样本——`dotnet-trace collect --providers Microsoft-DotNETCore-SampleProfiler --duration 00:00:20 --process-id <Windows PID>`，转 speedscope 后用 `dotnet-trace report topN` 读独占时间。

独占时间前 7 名全是**阻塞等待**，没有一处在跑：

| 函数 | 独占 |
| --- | --- |
| LowLevelLifoSemaphore.WaitForSignal(int32) | 39.1% |
| PortableThreadPool+IOCompletionPoller.Poll() | 20.3% |
| WaitHandle.WaitOneNoCheck(...) | 13.4% |
| WaitHandle.WaitAnyMultiple(...) | 6.8% |
| WinUiCompositorConnection.RunLoop() | 6.8% |
| Monitor.Wait(object, int32) | 6.8% |
| Win32DispatcherImpl.RunLoop(...) | 6.4% |

托管侧真正在干活的只有：`Dispatcher.OnOSTimer` 0.1%、`Dispatcher.ExecuteJobsCore` 0.07%、`TimerQueue.FireNextTimers` 0.09%。

⇒ 静置态没有 spin，也没有哪个 `DispatcherTimer` 在空转——删定时器解决不了真机静置的 6.9%。剩下的是**帧还在产**：合成器与 `RunLoop` 每帧的活。唯一杠杆是空闲时**停帧或降帧**（让渲染循环真正睡下去），不是减定时器。

口径限制：同一台机器上探针静置态（2 × 20 s `cpu-sample.ps1`）实测只有 1.0–2.3% 单核，复现不到真机 6.9%，所以这份栈只回答「剩余 CPU 的形态」，不回答「点数为什么是 6.9」。



## 14. 全量 32 tab 遍历复核（本轮修复后）

口径：真机镜像实例（336 模组），`CKANUI_NAV_ROUNDS=2 CKANUI_NAV_STALLS=1 CKANUI_NAV_MEM=1`，32 个导航键 × 2 轮，无抽样（键由 `shell.NavItems` 全量枚举）。layout = 导航命令到 UI 线程首次可用的时间；maxGap = 该页帧窗口内最大帧间隔。

| tab | 首进 layout | 首进 maxGap | 重进 layout | 重进 maxGap | 节点 |
|---|---|---|---|---|---|
| mods | 1249.4 | 1063.1 | 305.1 | 282.5 | 2333 |
| modconfig | 623.2 | 385.2 | 66.4 | 46.4 | 1295 |
| settings | 499.6 | 474.6 | 31.7 | 32.2 | 2016 |
| kspconfig | 439.9 | 338.5 | 53.8 | 43.7 | 888 |
| rp1-career | 330.3 | 165.3 | 51.3 | 44 | 418 |
| modtext | 277.6 | 139.5 | 95.4 | 84.9 | 631 |
| saves | 253.6 | 154.8 | 133.2 | 118.8 | 420 |
| logs | 233.8 | 190 | 161.8 | 137.6 | 573 |
| downloadstats | 210.5 | 719 | 41.3 | 34.3 | 1724 |
| ships | 193.4 | 62.7 | 81.4 | 69.6 | 422 |
| kos | 153 | 29.2 | 23.3 | 32.1 | 128 |
| ai-assistant | 131.3 | 34.4 | 17.2 | 32.9 | 99 |
| modpack-profiles | 109 | 30.8 | 40.4 | 38.2 | 106 |
| spacedock | 106.9 | 40.3 | 44.9 | 34.6 | 119 |
| modpack-editor | 106.2 | 87.6 | 16.6 | 34.3 | 466 |
| music | 97.2 | 48.3 | 35.4 | 25.8 | 2098 |
| performance | 96.2 | 42.6 | 30.6 | 55.5 | 68 |
| loadingimages | 87.1 | 41.3 | 31.4 | 43.9 | 137 |
| cache | 86.2 | 77.8 | 22.9 | 28.2 | 429 |
| instances | 85 | 51.8 | 28.1 | 26.9 | 320 |
| storage | 67.3 | 35.5 | 34 | 34.7 | 106 |
| content | 64.3 | 34 | 21.4 | 50.1 | 89 |
| flags | 61.7 | 61.7 | 22 | 45.7 | 695 |
| modpacks | 53.6 | 33.3 | 21.4 | 32.6 | 233 |
| plugins | 51.7 | 38.5 | 24.7 | 25.6 | 267 |
| repos | 51 | 33.4 | 13.6 | 29.5 | 192 |
| dedup | 48.7 | 32.4 | 26.9 | 32.1 | 186 |
| installhistory | 42.1 | 36.1 | 26.9 | 32.7 | 98 |
| playtime | 39.8 | 33.1 | 18.4 | 33.5 | 62 |
| home | 38.8 | 28.4 | 44.1 | 33.6 | 248 |
| changes | 36.3 | 36 | 18.4 | 39.1 | 121 |
| unmanaged | 28.5 | 25.8 | 25.3 | 41.5 | 108 |

本轮提交：`48c3e88`（kspconfig 虚拟化 13,466→888 节点）、`936f8e4`（logs 虚拟化 1281→573 节点，滚动 59 步 p95 0.3ms）、`c7a7e4e`+`8e31669`（downloadstats 分批 + 重进复用，r2 maxGap 1213→34ms）、`98c7dcf`（modconfig 同对象复用，r2 maxGap 1400.9→46.4ms）、`890efef`（flags 跨帧回填，UI 最长阻塞 541→7.5ms）。

结论：重进成本基本消灭（mods 305ms / logs 162ms / saves 133ms 之外全部 ≤100ms）；首进最重的是 mods（1249/1063ms，2333 节点首次 ApplyStyles，需动行模板＝影响显示，待抉择）、settings（500/475ms）、kspconfig（440/339ms）、downloadstats（maxGap 719ms，分批与排版撞帧）。内存：24 页走完 heap 702MB / WS 1118MB，两轮差 1MB。
