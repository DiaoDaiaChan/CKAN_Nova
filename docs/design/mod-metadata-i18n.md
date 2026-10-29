# 元数据本地化（M11 MTI）

模组元数据（abstract / description / name）的译文与界面语言包是两套东西：界面文案有稳定键，元数据文本是上游 CKAN-meta 的自由文本，只能按**文本内容**索引。本篇记录本项目的落地实现与契约边界。

## 落地范围

| 层 | 文件 | 说明 |
| --- | --- | --- |
| 契约 | `src/CkanUI.ModText/IModTextProvider.cs` | `ModText? Find(identifier, field, sourceText)` + `ModText(Text, Source, Stale)` + `ModTextSource{None,Machine,Human}` |
| 契约 | `src/CkanUI.ModText/IModTextService.cs` | 载入/热重载、语言列表、文件与条目浏览、覆盖项增删、JSONL 导入导出、UniversalI18n 同步；导出与同步返回 `OperationResult`（`CkanUI.Services.Abstractions`），失败不抛 |
| 实现 | `src/CkanUI.ModText/ModTextProvider.cs` | 两层 `DictPack`（覆盖层 → 语言包层）+ 策略 + 黑名单 |
| 实现 | `src/CkanUI.ModText/ModTextPolicy.cs` | `Off` / `ExistingOnly`（默认）/ `AllowMachine` |
| 实现 | `src/CkanUI.ModText/ModTextBlocklist.cs` | `blocklist.json`：`["A"]`、`{"blocked":[…]} `、` {"identifiers":[…]}` 三种形状 |
| 实现 | `src/CkanUI.ModText/ModTextPackVerifier.cs` | 复用 `UI18n.Core.Update`（`UpdateManifest` / `UpdateVerify` / `ZipSafety`）做包与清单校验 |
| 实现 | `src/CkanUI.Services/ModText/ModTextService.cs` | 存储布局、装载、原子写、同步、包校验入口 |
| UI | `src/CkanUI.ViewModels/Pages/ModTextPageViewModel.cs` + `src/CkanUI.App/Views/ModTextPageView.axaml` | 语言切换、词条浏览、人工覆盖编辑、同步与打开目录 |
| 工具链 | `external/UniversalI18n/src/UI18n.Core` | `Fnv1a` / `DictPack` / `KeyLevel` / `PackLocator` / `TextNormalizer` / `UpdateVerify` |

## 哈希与归一化契约（跨语言，不许各写一套）

`h = fnv1a64(Normalize(sourceText))` 的 16 位小写十六进制；`Normalize` = NFKC → 换行折成空格 → 空白折叠（含 NBSP、全角空格）→ trim，不做大小写折叠。官方向量：空串 `cbf29ce484222325`、`a` `af63dc4c8601ec8c`、`foobar` `85944171f73967e8`。

- C# 侧唯一实现是 `UI18n.Core.Hashing.Fnv1a`（按 UTF-8 手工逐码点，零分配，孤立代理替换成 U+FFFD，永不抛）；Python 侧脚本必须与它逐条对齐（`T-MTI-01`：100 条抽样逐条相等 + 官方向量）。
- 归一化用 `UI18n.Core.Text.TextNormalizer`，已归一化时返回原引用（零分配）。

## 存储布局与查找链

`%LocalAppData%/CKAN/ckan-ui/i18n/`：

- `packs/<lang>/CKANMeta.jsonl`（机器/人工译文，按文件名序载入，后载入者覆盖同名 h）
- `packs/overrides.jsonl`（用户的单条人工覆盖，独立成一个 `DictPack`）
- `blocklist.json`（缺省回落到 `packs/blocklist.json`）
- `state.json`（增量比对用，见 MTI-02）

`Find` 的查找链：**覆盖层 → 语言包层 → 过期回退轮（覆盖层 → 语言包层）→ null（调用方用英文原文）**，命中即返回。第一轮按 `h` 桶精确命中；第一轮全落空时，第二轮按 `ckan:<identifier>:<field>` 键索引找「同一字段的旧条目」，命中则返回旧译文并把 `Stale` 置 `true`（见关键决策 9）。命中层级与 `src` 字段决定 `ModTextSource`，界面上「人工/机翻」徽标由此而来。

## 关键决策

1. **覆盖层与语言包层拆成两个 `DictPack`。** 早先把 `overrides.jsonl` 载入同一个包，靠文件顺序保证优先级——一旦用户重命名或新增包文件，顺序就变了，人工覆盖会被语言包悄悄盖掉。拆成两层后优先级由代码固定，与文件名无关。`TotalEntries` 与 `GetAllEntries` 仍是两层合计/合并视图，保持既有可观察行为。
2. **默认 `ExistingOnly`。** 机翻覆盖率虽高，但错译出现在模组摘要处最刺眼；默认只用人工译文，想提高覆盖率的人自己抬到 `AllowMachine`，责任边界清楚（17 篇 MTI-08 三档）。
3. **`ExistingOnly` 遇到机翻条目是「继续往下一层找」而不是返回 null。** 否则覆盖层里一条历史机翻会挡住语言包里的人工条目——这是分层查找最容易被写错的一行，`ModTextLayerTests.ExistingOnly_skips_machine_override_and_falls_back_to_pack` 专门守它。
4. **`field` 不参与哈希。** 哈希只由文本决定，同一句话既出现在 abstract 又出现在 description 时只存一条；`field` 只体现在 `n=ckan:<identifier>:<field>` 与覆盖项命名上。
5. **黑名单优先于一切。** 作者声明「不要翻这个 mod」是意愿问题，连人工覆盖也不该绕开。
6. **缺包不是错误。** `PackLocator.Resolve` 返回 `PackDirStatus`，`LanguageDirMissing` 只记一行 info 并降级为英文原文，首帧照画；载入过程整体包在 try/catch 里，异常时降级空词库而不是让页面崩。
7. **目录名大小写不影响定位。** 用户手工解包出来的目录常是 `zh-CN`，服务内部语言标签全是小写 `zh-cn`；`PackLocator` 先 `zh-cn` 再 `zh`、先 Ordinal 再 OrdinalIgnoreCase，两处都兼容。
8. **包校验复用 `UI18n.Core.Update`，不重写清单解析。** 清单形状与 zip-slip 判据是跨语言契约，各写一套必然出现「脚本觉得对、读取器觉得错」；`ModTextPackVerifier` 只是把 `UpdateManifest.TryParse` / `UpdateVerify.CheckFile` / `CheckZip` / `ZipSafety` 包成本项目的返回类型（失败给 `size-mismatch`/`sha256-mismatch`/`unreadable`/`no-manifest` 原因码，全部不抛）。
9. **过期判定靠 `(identifier, field)` 二级索引，不靠 `DictPack` 的哈希桶。** `DictPack.TryGetEntry(hash, …)` 只在 `entry.Hash == hash` 的桶里找，而 `hash` 由当前文本算出，所以「桶命中 ⇒ 哈希相等」恒成立、`Stale` 结构性恒为 `false`。`ModTextProvider` 因此在装配期用 `pack.AllEntries` 另建一层 `n → DictEntry` 索引（只收 `n` 形如 `ckan:<identifier>:<field>` 的人工覆盖条目；上游管线生成的包 `n` 是归一化原文，拿它做键会把 A 字段的译文贴到 B 字段，宁可漏报不可错报）：哈希桶先照旧查原文，查不到再查键索引，命中且条目 `h` 与当前文本哈希不同即返回「过期译文 + `Stale=true`」。键命中分支上文本一定已变，条目没记 `h` 也报过期（`forceStale`）。`ExistingOnly` 对过期条目同样不下沉机翻——宁可回退英文也不拿旧机翻顶替，`ModTextLayerTests.ExistingOnly_does_not_substitute_machine_translation_for_changed_text` 守这一条。
10. **策略与黑名单不进 `IModTextService`。** 加接口成员会波及 `ModTextPageViewModel` 的内嵌空服务替身与他人测试假服务，而设置页还没有三档入口；先以 `ModTextService` 的公开属性暴露（`Policy`/`Blocklist` 改完立即生效），等设置页要接线时再提升到契约。

## 线程与性能

- `ReloadPacksAsync` 在 `Task.Run` 里装配 `DictPack`（`DictPack` 非线程安全，只在装配期写），装配完成后在锁内一次性替换 `_provider` 引用；查找路径纯内存、无锁、只读快照。
- 3,639 条（约 1.5 MB）载入在数十毫秒量级；启动时后台载入，未完成前一律英文原文，不阻塞首帧。
- 覆盖项写入走 `.tmp` + `File.Move(overwrite: true)`，避免半截文件；写完立即重载并触发 `PackChanged`，页面与详情页同时刷新。

## 测试

- `tests/CkanUI.ModText.Tests/ModTextLayerTests.cs`（13 用例）：覆盖层优先、`ExistingOnly` 跳过机翻回落、`AllowMachine`、`Off`、黑名单、`field` 不影响查找、黑名单三种形状与坏 JSON 降级、清单必需、按目录校验成功/尺寸不符/文件缺失、`TryMapToLocal` 拒绝 `..`；以及过期判定三条——文本改过时键索引命中并报 `Stale=true`、哈希相同不报过期、`ExistingOnly` 不用机翻旧译文顶替改过的文本（`AllowMachine` 下允许但标过期）。
- `tests/CkanUI.Services.Tests/ModTextServiceTests.cs`（9 用例）：重载、覆盖项写入/删除、条目过滤、JSONL 导入后再导出、UniversalI18n 同步合并；失败路径三条——导出文件不存在、导出路径为空、同步目录不存在，都断言「返回失败结果且不抛」。
- `tests/CkanUI.ViewModels.Tests/ModTextPageViewModelTests.cs`（9 用例）：载入与统计、投影裁剪、搜索过滤、覆盖新增/删除；同步三条——命令成功刷新状态栏、成功文案逐字断言、失败走错误出口（错误中心记 `ModTextPage.SyncUniversalI18n` + 状态栏「同步失败: …」+ `IsLoading` 复位）。
- `tests/CkanUI.Services.Tests/ModTextServiceLayeringTests.cs`（6 用例）：两层合计与合并视图、目录名大小写回退、缺包降级不抛、默认策略与热改档、`blocklist.json` 载入生效、`VerifyInstalledPacks` 的 no-manifest/size-mismatch/成功三条路径。
- 跨语言契约对齐（`T-MTI-01`）与增量比对（`T-MTI-04`）需要 Python 侧脚本，见「已知缺口」。

## 已知缺口

1. **过期判定只覆盖带 `ckan:<identifier>:<field>` 键的条目。** 键索引只收 `n` 以 `ckan:` 开头的人工覆盖条目（本仓自己的写入格式），上游管线产出的语言包 `n` 是归一化原文，不做键匹配——那类条目文本一改就只是「查不到」，不会报 `Stale`。要覆盖它们需要 `state.json` 记录 `(identifier, field) → 旧 h`（MTI-02 的增量比对），属于后续任务。另外 `Stale` 目前只有测试消费，界面还没有「译文已过期」提示，接线由 Lead 决定。
2. **抽取与增量比对脚本未落地。** MTI-01/02/09 要求从 CKAN-meta 抽取候选、写 NDJSON、做仓库指纹与集合差（含孤儿 30 天），需要 Python 侧工具链；本任务只交付了运行时读取侧与包校验。
3. **三档开关与预算（MTI-08）没有设置页入口**，只有 `ModTextService.Policy` 这一层实现。
4. **`SyncFromUniversalI18nAsync` 的默认路径写死** `%UserProfile%/Documents/github/UniversalI18n`（可通过参数或 `UNIVERSAL_I18N_ROOT` 覆盖）。失败已改为返回 `OperationResult`（见下条），但默认路径本身仍写死，设置页还没有「选择仓库目录」入口。
5. **导出的 `OperationResult` 契约与失败语义。** `IModTextService.ExportJsonlAsync` 返回 `OperationResult<string>`、`SyncFromUniversalI18nAsync` 返回 `OperationResult<int>`，两者都不再抛异常：文件/目录不存在给不可重试失败，`IOException`/`UnauthorizedAccessException` 分别给「可重试」与「无权限」失败；显式传入的 `sourceDir` 不存在时**直接失败、不再回落到用户主目录里的真实仓库**（以前会静默同步另一份数据）。`OperationCanceledException` 仍按取消语义冒泡。页面在失败分支走既有错误出口（`IErrorReporter` + 状态栏中文文案），`catch` 只兜底真正的意外异常。
6. **`IModTextService` 没有版本/清单字段**，`VerifyInstalledPacks` 由调用方传清单 JSON；下载与 304/ETag（MTI-06）尚未接 `IAiClient`/`HttpClient`。
7. **页面文案尚未走 `ILocalizer`**：`ModTextPageViewModel` 的状态文案仍是中文硬编码，等 `LocalizationKeys` 补 `Ui_ModText_*` 键后由 Lead 接线。
