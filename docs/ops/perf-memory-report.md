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

## 15. settings / downloadstats 首进停帧收口，以及一条测量口径纠错

### 15.1 settings：分组逐帧挂载（925d0cd）

根因不是读值慢——`SettingsPageViewModel.LoadAsync` 全段只有 67.4ms（getAll 54.2 / apply 62.7 / finish 66.9），剩下 1100ms 是「2016 个节点在同一帧里套样式 + 首次布局」。

改法：`Groups` 由 `IReadOnlyList` 改为 `ObservableCollection`，先把值灌完、撤掉骨架占位，再逐组 `Add`、组间 `Task.Delay(16)` 让出一帧。样式与布局零改动，节点数恒为 2016。

| NAV_ONLY=settings r1 | base1 | base2 | fix1 | fix2 |
|---|---|---|---|---|
| layout | 1180.4 | 566.5 | 194.1 | 212.4 |
| maxGap | 1091.0 | 519.2 | 250.0 | 227.9 |
| nodes | 2016 | 2016 | 2016 | 2016 |

剩余约 240ms 来自最大的单组（appearance）自身实体化；再往下要组内按行分批（`Items` 换 `ObservableCollection`），影响面更大，未做。

### 15.2 downloadstats：首屏后先让布局跑完（b260cb2）

根因：首次布局前就把 200 行塞进 DataGrid，绕过行虚拟化，一帧实体化约 1724 节点。改法：先上 20 行 → 等 200ms 让布局量到有限高度 → 每批 8 行、批间 16ms。

| NAV_ONLY=downloadstats r1 | 改前 | 改后（两跑） |
|---|---|---|
| layout | 556 | 207.8 / 348.3 |
| maxGap | 1448 | 155.8 / 288.4 |
| nodes | 1724 | 229 / 229 |
| 重进 layout | 37.5 | 33.9 / 55.8 |

对照实验（批 32、首屏后只等 1ms）：nodes 229、layout 375.7（同 run 地板 403.3）⇒ 关键在「别在首次布局前灌满」，批大小不敏感。

### 15.3 口径纠错：`CKANUI_NAV_MEM=1` 会污染 maxGap

`MemoryProbe` 打点时会做两次阻塞式 `GC.Collect(2, Forced, Blocking, Compacting)`（见 `src/CkanUI.App/Diagnostics/MemoryProbe.cs`），实测 pause 1.6–3.6s 且 `worstAt` 落在测量窗尾部。⇒ **§14 表里 MEM=1 那批 maxGap 有相当一部分是测量动作本身**。今后：

- 量帧间隔/掉帧：不要开 `CKANUI_NAV_MEM`；
- 量内存：可以开，但同一轮的 maxGap 不作数。

### 15.4 被否决的实验：kspconfig 分组逐帧挂载

照 settings 的方子给 `KspConfigPageViewModel.RebuildSections` 做首进分帧（首帧 2 组、其余每帧 1 组，`CKANUI_KSP_STAGED=0` 可关），单页冷启 A/B（各两跑，`NAV_ONLY=kspconfig`，未开 MEM）：

| r1 | 基线1 | 基线2 | 分帧1 | 分帧2 |
|---|---|---|---|---|
| layout | 1212.9 | 929.4 | 888.3 | 1050.4 |
| maxGap | 961.0 | 733.9 | 709.8 | 812.3 |

两组区间重叠（基线自身就跨 929–1213ms），收益在噪声内；该页只有约 10 个分组，分帧能省的帧内工作量本来就小。已整体回退，代码未入库。结论：**分帧只对「单帧实体化量级 ≥ 1500 节点」的页面有意义**（settings 2016、downloadstats 1724），小分组页不要照搬。

## 16. 内存时间轴：给「4GB」取证

### 16.1 为什么另开一条线

`[nav]` 行只在走完一页时打一次，抓不到「什么都没点，内存自己涨上去」的现场；而应用自己的 log4net 在这台机器上写不进 `%LOCALAPPDATA%\CKAN\logs`（目录写被拒，实测 `UnauthorizedAccessException`），只有探针显式指定的路径能落盘。所以新增 `src/CkanUI.App/Diagnostics/MemoryTimeline.cs`：`File.AppendAllText` 直写，不依赖日志框架。

### 16.2 怎么开

- 显式：`CKANUI_MEM_TRACE=<路径>`；
- 缺省：跟着导航 trace 走——设了 `CKANUI_NAV_TRACE` 就会在它旁边多出一份 `<trace>.mem`；
- 采样间隔 2s，只读计数器（`GC.GetTotalMemory(forceFullCollection: false)`、`Process.WorkingSet64/PrivateMemorySize64`、Gen0/Gen2 次数、线程数），**不触发 GC**，不影响同轮的帧间隔；
- 落盘连续失败 3 次自动停，绝不因为诊断拖垮应用。

### 16.3 实测样例（真机镜像 336 模组，32 页遍历）

| 时刻 | ws | private | heap | gen2 |
|---|---|---|---|---|
| t=0.0s | 166.9MB | 110.6MB | 20.7MB | 1 |
| t=2.0s | 355.0MB | 304.2MB | 76.2MB | 4 |
| t=74.0s | 851.6MB | 829.1MB | 509.0MB | 38 |
| 遍历结束 | 1199.5MB | 1110.6MB | — | — |
| **峰值** | **1701.2MB** | — | — | — |

要点：

- 峰值只有时间轴抓得到（`[nav]` 行在页面切换间隙里看不到）；
- `private` 与 `ws` 差 60–90MB，缺的那截是已提交的非托管页（合成器 + Skia 表面，约 14MB/页）；
- heap 与 private 的巨大差值说明大整合包场景要盯的是**非托管 + 提交段**，不是托管对象存活。

### 16.4 在你的整合包上取证

```powershell
$env:CKAN_CONFIG_FILE="$env:APPDATA\CKAN\GUI\config.json"
node tools/perf-probe/measure-instance.mjs
```

跑完把这两个文件发回来即可定位 4GB 属于哪一类：

1. `artifacts/nav-instance.log`（每页 layout/maxGap/nodes/heap）；
2. `artifacts/nav-instance.log.mem`（2s 一条的时间轴，看是阶梯上涨、还是某页一次性跳升）。

判读口径：

- 时间轴**锯齿形**（涨上去又落回来）＝可回收垃圾 + 提交段未归还，不是泄漏；
- 时间轴**单调阶梯**且 heap 同步上涨 ＝活着的数据没有释放，看是哪个 tab 之后开始爬；
- `private − heap` 一路变大 ＝非托管侧（位图/纹理/合成表面），重点查缩略图与大图页面。

## 17. saves：选中即校验改成「选中停下来再校验」（2217d1d）

### 17.1 现象

存档页滚动时偶发一次 1.1s 的冻结（§8 里记的「一次性 1107.6ms」）。根因不是滚动，是**选中行在变**：`OnSelectedRowChanged` → `KickValidate()` → 每换一行就把整份 `.sfs` 读进来解析（实测最大 120MB，解析产物全落 LOH）。连续按方向键/拖动列表 = 一串「读盘 + 解析 + 大对象分配」。

### 17.2 改法

`src/CkanUI.Plugin.Saves/SavesPageViewModel.cs`：选中后延迟 300ms 再校验，新的选中会取消上一批（`CancellationTokenSource`）。校验本身仍在 `Task.Run` 里（服务层 `ValidateAsync` 早已不在 UI 线程解析），这次改的是**触发频率**。

### 17.3 A/B（真机镜像，`NAV_ONLY=saves NAV_SCROLL=1`，各一跑，同一构建，`CKANUI_SAVES_DEBOUNCE=0` 关掉去抖）

| r2 滚动（59 步） | 选中即校验 | 去抖 300ms |
|---|---|---|
| p50 | 11.3ms | **5.7ms** |
| p95 | 17.8ms | **10.9ms** |
| max | 23.3ms | **16.2ms** |
| dropped | 5 | **0** |

r2 进页的 maxGap 也从 99.3ms（worstAt 798ms，含一次校验停顿）降到 38.9ms。节点数不变（420/494），诊断面板唯一差异是内容晚 300ms 出现。

### 17.4 顺带记两条测量纪律

- `measure-instance.mjs` 现在支持 `CKANUI_PROBE_MEM=0` 关掉 `CKANUI_NAV_MEM`（默认仍是 1，向后兼容）——量帧间隔时应关。
- 并发构建/多 agent 同时压机器时，全量 32 tab 的 layout 能漂 2–10 倍（同一份代码 dedup 117ms → 1317ms、storage 113ms → 1382ms），**只有 nodes 是稳定量**。要发布首进数字，必须确认机器上没有别的构建在跑。

## 18. modconfig：假异步（Task.FromResult）改成真异步（e1998ef）

### 18.1 这是一类问题，不是一个页面

`ModConfigService.LoadAsync` 原来是：

```csharp
var result = LoadCore(instanceName, ct);   // 扫适配器目录、解析 .cfg、建模型
_cachedKey = key; _cached = result;
return Task.FromResult(result);            // 包成「已完成的任务」
```

调用方 `await _config.LoadAsync(...)` 拿到的是已完成任务，**根本不会让出线程**——导航命令里的同步耗时（探针的 `assign`）就是这么来的。往 `OnActivated` 里写 `_ = LoadAsync()` 也没用：方法的同步前缀照样在 UI 线程跑完。

### 18.2 改法

`LoadAndCacheAsync`：`await Task.Run(() => LoadCore(instanceName, ct), ct).ConfigureAwait(false)`，缓存写回放在 await 之后。

### 18.3 实测

| modconfig 首进 | assign（导航命令同步耗时） |
|---|---|
| 改前（全量复核） | 362.2ms |
| 改后（隔离首进） | 236.3ms |

剩余 assign 主要是**页面视图构造**（1295 节点 XAML 首次实体化），不属于数据加载。重进 assign 11.5ms、layout 42.7ms、maxGap 30.6ms。

回归：`CkanUI.Plugin.ModConfig.Tests` 173 项全绿（0.79s）。

### 18.4 排查同类问题的办法

```bash
grep -rn "Task.FromResult" src --include=*.cs | grep -i "load\|refresh\|scan"
```

凡是「先同步干活、再 `Task.FromResult` 包一层」的方法，对外都是假异步；调用点写 `await` 看起来是异步，实际整段都在 UI 线程。判据：探针的 `assign` 里出现与数据量正相关的毫秒数。

## 19. 全量 32 tab 复核（修复后，两轮独立 sweep）

口径：真实实例镜像（`artifacts/perf/instance-real`，D 盘 Kerbal Space Program 的 44 个 junction + 18 个根文件副本，336 模组），`CKANUI_PROBE_MEM=0`（关掉两次阻塞 Gen2，否则 maxGap 混入测量动作），32 个导航键 × 2 轮，同一构建连跑两次独立进程。

### 19.1 首进 layout / maxGap（两次 sweep 的区间）

| # | tab | 首进 layout | 首进 maxGap | 节点 | 重进 layout |
|---|---|---|---|---|---|
| 1 | mods | 1108 / 1349 | 952 / 1108 | 2333 | 253 / 270 |
| 2 | modconfig | 589 / 627 | 318 / 335 | 1295 | 76 / 90 |
| 3 | downloadstats | 419 / 429 | 283 / 352 | 1724 | 26 / 28 |
| 4 | kspconfig | 421 / 453 | 185 / 207 | 888 | 49 / 49 |
| 5 | rp1-career | 302 / 337 | 151 / 184 | 418 | 61 / 73 |
| 6 | modtext | 266 / 282 | 116 / 128 | 631 | 107 / 125 |
| 7 | saves | 198 / 205 | 90 / 98 | 420 | 61 / 94 |
| 8 | logs | 187 / 202 | 146 / 158 | 573 | 83 / 122 |
| 9 | settings | 111 / 500* | 69 / 475* | 2016 | 32 / 45 |
| 10 | instances | 194 / 200 | 97 / 128 | 320 | 38 / 49 |
| 11 | flags | 57 / 62 | 62 / 80 | 2039 | 22 / 38 |
| 12 | music | 134 / 118 | 42 / 388 | 2098 | 35 / 52 |

\* settings 第一次 sweep 的 500/475 是未开 MEM 但排在 mods 之后的样本，第二次 sweep 为 111/69；同页隔离跑（队友）为 302–464 / 181–302。区间下沿更接近真实，上沿混入了前序页面的后台收尾。

完整 32 行见 `artifacts/final-table.mjs <log>`。

### 19.2 与修复前对比（同一台机器、同一实例）

| 页面 | 修复前 layout / maxGap | 修复后 layout / maxGap | 手段 |
|---|---|---|---|
| kspconfig | 3365 / 3365（13,466 节点） | 421–453 / 185–207（888 节点） | ListBox + VirtualizingStackPanel（48c3e88）+ 解析/投影出 UI 线程（e2a54fb） |
| settings | 499 / 474（隔离跑 1410） | 111 / 69 | 组级分帧 + 组内 4 行/帧（925d0cd、3f6c67e） |
| downloadstats | 556 / 1448 | 419 / 283（重进 26 / 33） | 首屏后让出 + 分批（b260cb2、8e31669） |
| logs | 1822 / 1718（1281 节点） | 187–202 / 146–158（573 节点） | ListBox 虚拟化 + 固定行高（936f8e4） |
| modconfig | 906 / 1132 | 589 / 318（重进 76） | 真异步 + 复用投影（e1998ef、98c7dcf） |
| saves | 351 / 234 | 198 / 90（滚动掉帧 5→0） | 选中校验去抖 300ms（2217d1d） |
| flags | 58 / 1112 | 57 / 62 | 缩略图 8 条一批 + 让帧（890efef） |

### 19.3 唯一剩下的硬骨头：mods

2333 个节点、2650 行数据，首进 layout 1.1–1.35s、maxGap 0.95–1.1s，重进 ~250ms。dotnet-trace 显示 UI 线程自身时间第一是 `Avalonia.StyledElement.ApplyStyles`（14.55s 累计采样）≫ `Layoutable.MeasureCore`（8.54s），即**首次套样式**，不是数据加载。

已排除（都在同构建上做过 A/B，且都回退）：
- `ApplyColumnWidths()`（Loaded 里逐列设宽度）：关掉后 2290/3004ms，无改善。
- DataGrid 主题 StyleInclude 提到 App.axaml：1355ms，更差 ~10%。
- 订阅 `_repositories.Updated` / `_registry.Changed` 触发的晚到 ReloadFromSnapshots：1176ms ≈ 基线。
- 就地替换行（而不是换集合）：1407ms，无收益。

剩下的只有「行模板做减法」（每行 ~116 个节点）。这会改变显示，按用户「会影响显示效果的改动先保留」的约束，**停在这里等拍板**。

### 19.4 ai-assistant 重进 1.8s 的定性

两次整机 sweep 里 ai-assistant 的 r2 maxGap 都是 1.75 / 1.85s（r1 只有 66–75ms）。单独跑 `NAV_ONLY=ai-assistant` × 3 轮：r1 426ms、r2 8ms / maxGap 32ms、r3 5ms / maxGap 191ms，**隔离跑不出 1.8s**。⇒ 这 1.8s 属于「上一页的收尾工作 / GC」落在下一段帧窗口的 gap 里，不是该页自身。判读整机表时，gap 类尖峰要看它前一页是谁。

## 20. 模组页首进 1s 的定量拆解：成本在「视口内 20 行 × 110 节点」，不在数据量

### 20.1 关键实验：把行数压到 20 行，耗时不变

临时给 `ModsPageViewModel.ApplyFilter` 加环境变量开关 `CKANUI_MODS_ROWS` 截断行数（诊断用，测完已按备份还原，未入库）：

| CKANUI_MODS_ROWS | 首进 layout | 首进 maxGap | nodes |
|---|---|---|---|
| 20（只给 20 行数据） | 810.8ms | 695.1ms | 2333 |
| 0（不截断，2,650 行） | 699.0ms | 556.8ms | 2333 |

**行数从 2,650 砍到 20，耗时和节点数都不变。** DataGrid 只实体化视口内的行容器（约 20 个），所以：

- 2,333 个节点 ≈ 20 行 × ~110 节点 + 表头/边框骨架；
- 首进那 0.7–0.9s 就是这 2,333 个节点**在同一帧里第一次套样式 + 首布局**（dotnet-trace 里 UI 线程自时间第一是 `Avalonia.StyledElement.ApplyStyles`，是 `MeasureCore` 的 1.7 倍）；
- 分帧填行（settings/flags/downloadstats 那套）在这里**没有用**：视口一次就要 20 行，拆开只是把一个 800ms 的帧切成 20 个 40ms 的帧，长齐时间反而拉长到 0.8s 以上，而且每帧仍超过 16.7ms 预算。

### 20.2 结论：只剩「行模板做减法」一条路

每行 ~110 个节点，是 DataGrid 行的模板结构决定的（状态条 + 复选框 + 名称/版本 + 4 个文本列 + 分类/标签 + 大小 + 两个命令按钮 + 各处的 ToolTip）。要压首进就得动模板：

- **方案 A（减模板）**：把每行 110 节点压到 ~60（去掉逐行的 ToolTip 包装、把只读文本列合并成一段格式化文本、命令按钮收进右键菜单）。首进预计 0.7–0.9s → 0.35–0.5s，行高不变，但列内呈现会变（少图标/少按钮）。
- **方案 B（不动显示）**：接受模组页首进 ~0.8–1.3s、maxGap ~0.55–1.1s；重进只有 250–270ms，日常交互不受影响。

按用户「可能会影响显示效果的改动先保留等我抉择」，本项停在方案 B，等拍板。

### 20.3 这次实验的副产品

- DataGrid 的行虚拟化是开着的，且**不受行数影响**：`nodes` 恒 2333（20 行视口），所以「行数多导致卡」是误判；卡在每行的节点密度。
- 判据可复用：`CKANUI_NAV_ONLY=<key>` 跑两次，若 `nodes` 与 `layout` 都不随数据量变，成本就在视口模板，不在数据管线。

## 21. 存档页内存：峰值是瞬时垃圾，不是常驻（更正 §14 的 +510MB）

### 21.1 更正

§14 里「saves 单页 heap +525MB」来自 `artifacts/heap-delta.js`，而该脚本把日志路径**写死**成 `artifacts/nav-exhaustive-run.log`——那是修复前（去抖 2217d1d 之前）的一次全量跑，传参被忽略。以脚本输出的数字为依据的「某页常驻多少」结论一律作废，只看 `[nav]` 行自带的 `heap=`。

### 21.2 存档页隔离实测（真实实例镜像，649 个存档 / 1003MB，CKANUI_NAV_MEM=1）

| 轮次 | layout | maxGap | heap | ws | private | gen2 | GC 停顿 |
|---|---|---|---|---|---|---|---|
| r1（首进） | 741.2ms | 409.4ms | 576MB | 896MB | 870MB | 4 | 1283ms |
| r2（重进） | 43.2ms | 38.4ms | 110MB | 991MB | 962MB | 3 | 375ms |

**首进的 576MB 在重进时回落到 110MB**，没有跨页常驻；ws 停在 ~990MB 是因为 GC 已提交的段不立刻还给系统（`GC.Collect` 之后 `WorkingSet` 仍高是正常的）。

### 21.3 判读纪律（补一条）

- 抓「静静涨上去」必须用 `[mem] t=..s ws=.. private=.. heap=..` 时间轴（`CKANUI_MEM_TRACE`，§16），不要用 per-page 的 heap 快照；快照落在解析中途就会把瞬时缓冲当成常驻。
- 探针进程会被单实例守卫挡掉：任何 `CkanUI.exe` 还活着时，新的探针进程会**静默退出、trace 不写**，measure-instance 会干等 15 分钟才报「应用可能启动失败」。跑前先 `tasklist | grep CkanUI.exe` 清干净。

## 22. 32 页的顺序访存账本：稳态 739MB 活堆，峰值 1.85GB 工作集

### 22.1 口径

真实实例镜像（336 模组 / 3,899 条仓库索引 / 649 个存档 1,003MB），`CKANUI_PROBE_MEM=1`（每页打点前强制一次 compacting GC，所以 `heap=` 是**活对象**，不是垃圾），一轮 32 键全走。每一行的 `d=` 是相对上一页的活堆增量。

| 页面 | 活堆 heap | 相对上一页 |
|---|---|---|
| mods | 147 | — |
| content | 150 | +3 |
| changes | 225 | +75 |
| repos | 349 | +124 |
| modpacks | 299 | -50 |
| modpack-editor | 309 | +10 |
| instances | 320 | +11 |
| playtime | 322 | +2 |
| unmanaged | 335 | +13 |
| cache | 397 | +62 |
| downloadstats | 434 | +37 |
| dedup | 401 | -33 |
| installhistory | 422 | +21 |
| plugins | 427 | +5 |
| ai-assistant | 431 | +4 |
| kos | 435 | +4 |
| kspconfig | 462 | +27 |
| modconfig | 498 | +36 |
| rp1-career | 509 | +11 |
| spacedock | 515 | +6 |
| storage | 518 | +3 |
| saves | 1038 | +520 |
| performance | 537 | -501 |
| modpack-profiles | 541 | +4 |
| modtext | 560 | +19 |
| ships | 576 | +16 |
| flags | 629 | +53 |
| loadingimages | 623 | -6 |
| music | 690 | +67 |
| logs | 690 | +0 |
| settings | 738 | +48 |
| home | 739 | +1 |

### 22.2 读法

- 起点 **147MB**（mods 首进），走完 32 页 **739MB** ⇒ 跨页保留了约 **590MB** 的活数据，不是垃圾（每页打点前都做过 full GC）。
- 单页最大瞬增是 **saves +520MB**，但下一页立刻 −501MB ⇒ 那是读 120MB 存档时的解析缓冲，**不是常驻**（与 §21 一致）。
- 真正的保留型增量：repos +124、changes +75、music +67、cache +62、flags +53、settings +48、downloadstats +37、modconfig +36。
- 另一次不开 MEM 的全量 sweep 用 `[mem]` 时间轴（2s 一格）看到峰值 **ws=1848.6MB / private=1823.3MB / heap=1378.9MB**，峰值出现在中段（前 6 页之后、saves 附近），随后回落并稳定在 **1259MB ws / 835MB heap**。

### 22.3 为什么「裁剪投影」压不下去（结论）

`IPageProjectionTrim` 只能断开 VM 对投影的引用，而 repos/changes/downloadstats 的数据实体（`CkanModule`、`ModuleRelationshipDescriptor`、仓库索引）挂在**服务层缓存**上，VM 裁剪后服务仍持有 ⇒ 活堆不降。§5.4 的 cache 页裁剪实验（剪了，ws 无变化）就是同一机制。要再往下压只有两条路，都需要改服务层语义：

1. 让仓库索引按需重建（丢掉 `IRepositoryService.Current` 的常驻，接受首次进页重新解析，秒级）；
2. 把 `CkanModule` 全量 DTO 换成只保留列表所需字段的窄投影（影响面大，跨页共享）。

### 22.4 4GB 的定位口径

三套实例我这边的活堆上限都是 0.7–0.9GB、ws 峰值 1.8–1.9GB；用户的 4GB 需要他自己的 `[mem]` 时间轴（`CKANUI_MEM_TRACE`）来定位：若 `private − heap` 一起涨 ⇒ 非托管（纹理/位图）；若只是 `heap` 阶梯上涨 ⇒ 页数据量与包规模成正比，走 22.3 的第 1 条。
