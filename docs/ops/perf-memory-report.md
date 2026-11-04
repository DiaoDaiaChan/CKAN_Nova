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

验证这两条只需要把 StyleInclude 移到 App 级、把密度/详情窗格属性改成「与当前值不同才赋值」，但两个文件都是别人的 WIP。

## 11. 已落地

- `7f2149a` 存档头按字节封顶（`HeaderByteBudget=256KB`），不再把 ArrayPool 顶到 512MB
- `905de7b` 缩略图缓存 64MB 字节预算 LRU（旗帜页滚动掉帧 9→3→0）
- `a9f5dfc` ZIP 预览与条目投影移出 UI 线程（内容页 480ms 同步块清零）
- `9eae410` 大存档校验后压缩 LOH
- `da11ccb`/`9734f2c`/`3c62845` 探针：每页 ws/private/heap、页内页签、按钮清点
