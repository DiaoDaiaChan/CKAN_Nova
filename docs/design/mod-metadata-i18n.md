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
| UI | `src/CkanUI.ViewModels/Pages/ModTextPageViewModel.cs` + `src/CkanUI.App/Views/ModTextPageView.axaml` | 语言切换、按模组分组浏览（左栏分组栏按 `k` 的 identifier 归堆、条目数降序、首项「全部模组」）、单行表格（标识+字段 / 原文 / 译文 / 类型）、字段与类型双下拉、人工覆盖编辑、同步与打开目录 |
| UI | `src/CkanUI.ViewModels/Dialogs/ModDetailsViewModel.cs` + `src/CkanUI.App/Views/Dialogs/ModDetailsView.axaml` | 详情面板的摘要/描述先查 `Find(identifier, field, 原文)`，命中用译文并挂「已译」徽标，`Stale` 挂过期警告（见「详情面板接线与启动预热」） |
| 启动 | `src/CkanUI.App/App.axaml.cs` | bootstrap 完成后后台 `WarmUpModTextAsync`：预热语言包，失败降级英文、不挡首帧 |
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

## 全链路（编排与离线验收）

译文从上游 CKAN-meta 走到界面上要过四个仓/目录，任何一段断了都不该等到运行时才发现：

```
CKAN-meta（abstract / description）
   │  i18n/tools/extract_ckan_meta.py --export-pool
   ▼
packs/ckan_pool.jsonl                  # h / key / text / ctx / field / len（UniversalI18n）
   │  i18n_tools.translate --pool …      # --plan 分批 → api 门禁 → journal
   ▼
packs/zh-cn/CKANMeta.jsonl              # 源包：h / n / s / t / mod（可选 k）
packs/zh-cn/<Mod>.jsonl / <Mod>.il.jsonl   # 游戏内语言包：modui、部件描述等（98 个包 / 19837 行）
   │  i18n/tools/ckan_i18n_pipeline.py --stage sync   # 全量语言包：<dest>.tmp → 校验 → os.replace
   ▼
%LocalAppData%/CKAN/ckan-ui/i18n/packs/<lang>/*.jsonl（+ overrides.jsonl 覆盖层）
   │  CkanUI.ModText（覆盖层 → 语言包层 → 过期回退轮）
   ▼
ckan-ui 模组详情页译文 + i18n 页全量词条浏览（按模组分组）
```

编排器是 `i18n/tools/ckan_i18n_pipeline.py`（单文件、只用标准库、无硬编码路径：仓库根从 `__file__` 上两级推导，
UniversalI18n 根按 `--ui18n-root` → `UNIVERSAL_I18N_ROOT` → `<root>/external/UniversalI18n` → `<root>/../UniversalI18n` 依次解析）。

一条命令验收、一条命令产出：

```bash
python i18n/tools/ckan_i18n_pipeline.py --check            # 验收：零网络、零写盘
python i18n/tools/ckan_i18n_pipeline.py --check --json      # 一行 JSON，给 CI/工单
python i18n/tools/ckan_i18n_pipeline.py --check --strict    # warn 也判红
python i18n/tools/ckan_i18n_pipeline.py --stage all --jobs 4    # 产出：extract → translate → sync
```

`--check` 的判据（每条都带 `id`/`severity`/`ok`/`message`，`--json` 输出一行可粘贴摘要）：

| 检查 | 内容 | 失败级别 |
| --- | --- | --- |
| `vectors` | 调 `extract_ckan_meta.py --verify-vectors`，官方向量逐条相等 | error |
| `pool.*` | pool 可解析、每行 `h == fnv1a64(normalize(text))`、无重复、`key` 形如 `ckan:<identifier>:<field>` | error |
| `pack.hash` / `pack.normalized` / `pack.text` / `pack.duplicate-h` | 包内 `h == fnv1a64(normalize(s))`、`n == normalize(s)`、`s`/`t` 非空、同包 `h` 唯一 | error |
| `pack.k-shape` / `pack.k-orphan` / `pack.k-hash` | 有 `k` 时必须形如 `ckan:<identifier>:<field>`、能在 pool 里找到该 `key`、且 pool 里**存在同 `key` 同 `h` 的行**（`key` 不是主键：4833 行 pool 只有 3785 个 `key`，同一字段可能留多条历史文本）；**无 `k` 只记 info** | error / info |
| `index.registered` / `index.rows` | `packs/index.json` 是否登记 `CKANMeta`，登记后行数/字节与磁盘是否一致 | 未登记 warn、不一致 error |
| `runtime.sha256` | 运行时 CKANMeta 包与源包 sha256 是否一致 | warn（`--strict` 判红） |
| `runtime.packs` | 运行时语言目录是否缺包（对比 `packs/<lang>/*.jsonl` 文件名集合，全量同步后应一致） | warn（`--strict` 判红） |
| `pool.gap` | pool distinct `h`、已译条数、缺口条数（信息项，供进度统计） | info |

| 码 | 含义 |
| --- | --- |
| 0 | 成功；`--check` 通过 |
| 2 | 参数错误（argparse） |
| 10 | 环境路径解析失败（ckan-ui 根 / 抽取器 / UI18n 根 / CKAN 数据源） |
| 11 / 12 / 13 | `extract` / `translate` / `sync` 阶段失败（13 含「源包契约不合格，拒绝同步」） |
| 14 | 可选 `--dotnet-test` 失败 |
| 20 | `--check` 不合格（error 级；`--strict` 下含 warn 级） |
| 21 | `--selftest` 断言失败 |

同步（`--stage sync`）搬**全量语言包**：`packs/<lang>/*.jsonl` 逐个复制（`--langs all` 默认覆盖所有语言目录，也可 `--langs zh-cn,ja` 限定），
根级 `glossary.jsonl` / `blocklist.json` 复制，`overrides.jsonl` 只做**合并**
（目标已有的 `(h, ctx)` 键一律保留原位，只追加新条目），所以同步不会把用户手工覆盖冲掉；每个文件走 `<dest>.tmp` →
sha256 核对 → `os.replace` 原子替换，源包契约不合格（哈希/空译文/重复 `h`）直接拒绝同步并给非 0 退出码。

**跨仓 `k` 契约（一句话）**：包行若带 `k`，它必须等于 pool 里同 `h` 行的 `key`（`ckan:<identifier>:<field>`）；
读取侧只把 `k` 当作 `(identifier, field)` 过期判定的辅助索引——带 `k` 才能对文本改过的条目报 `Stale`，
而 `key` 在 pool 里不唯一（同一字段的旧文本各占一行），所以「对齐」的判据是**存在同 `key` 同 `h` 的行**，不是「pool 里该 key 的第一行」，
不带 `k` 的旧包照常装载、只是不报过期，因此 `--check` 对缺 `k` 只记 info 不判红。

**装到游戏实例（生效那一步）**：游戏侧只认 `GameData/UI18n/Packs/<lang>/*.jsonl`（`UI18n.Runtime/Update/UpdateWorkflow.cs:261`），
而 ckan-ui 自己的运行时目录是 `%LocalAppData%/CKAN/ckan-ui/i18n/packs/<lang>`——两份是**不同目录**，所以「浏览得到」不等于「游戏里生效」。
把同一套语言包装进实例只要把 `--dest` 指向实例的 Packs 根：

```bash
python i18n/tools/ckan_i18n_pipeline.py --stage sync --dest "D:/Kerbal Space Program_newmod/GameData/UI18n/Packs"
python i18n/tools/ckan_i18n_pipeline.py --check --dest "D:/Kerbal Space Program_newmod/GameData/UI18n/Packs"
```

已实测：1.7.0 更新包装出来的是 99 个包（无 `CKANMeta`），同步后 100 个包、`CKANMeta.jsonl` 4807 行 / 1,780,151 字节，
`--check --dest <实例>` 16 项全绿。同步是逐文件原子替换（`<dest>.tmp` → sha256 → `os.replace`），覆盖层只合并不冲掉，
装之前把原 `Packs/zh-cn` 整目录备份到实例外即可回滚。

本地自测：`python i18n/tools/ckan_i18n_pipeline.py --selftest`（临时目录造假 pool + 假包，把 `--check` 正常/异常、
`--check` 零写盘、sync 的原子替换/幂等/拒绝坏包/覆盖层保留各断言一遍，不联网也不依赖真实 CKAN 仓库）。

## 详情面板接线与启动预热

**接线点**（视图与 VM 各一处，都不引入新依赖）：`ModDetailsViewModel` 构造末位新增可选 `IModTextService? modText = null`，
并提供 `TextProvider` 属性注入（getter 读注入服务的**当前** `Provider`，setter 覆盖并立即 `Refresh`，显式 set null 表示关闭译文查找）；
`AbstractText`/`DescriptionText` 改成 `原文 → Find(identifier, "abstract"|"description", 原文) → 命中且译文非空白用译文，否则回落原文`。
派生属性 `IsAbstractTranslated`/`IsDescriptionTranslated`/`IsAbstractStale`/`IsDescriptionStale`/`HasStaleTranslation`/`StaleTranslationNotice`
全部现查不缓存（`Find` 是纯内存函数）——缓存一旦失效就会显示上一份 provider 的译文。

**可见三态**：未命中 = 英文原文、无徽标；命中 = 译文 + `CkanBadge Success`「已译」；命中但 `Stale` = 旧译文 + 徽标 + `CkanBadge Warning`「译文对应的是旧版原文，英文内容已更新」。
徽标文案放在 VM（`TranslationBadgeText`），XAML 只绑 `Classes="CkanBadge Success|Warning"`，颜色走主题 token。

**为什么必须有启动预热**：`ModTextService` 构造期只建空包，`Provider` 要到 `ReloadPacksAsync` 之后才有内容，
而此前全仓唯一调用点是 `ModTextPageViewModel`（用户点开 i18n 页面才会载入）——不预热等于「有包但永远不生效」，
详情面板会一直显示英文。`App.StartAsync` 在 bootstrap 完成后 `_ = WarmUpModTextAsync(services)` 后台预热一次：
不挡首帧，失败只降级为英文原文（`ReloadPacksAsync` 内部已 catch，外层再兜一层防意外异常冒到启动序列）。
实测运行时目录（`%LOCALAPPDATA%/CKAN/ckan-ui/i18n`）一次载入 4807 条，`Find("RestockRigidLegs", "abstract", …)` 返回中文、
把原文改成不存在的文本后同一条返回中文且 `Stale=true`（不误配）。
## 关键决策

1. **覆盖层与语言包层拆成两个 `DictPack`。** 早先把 `overrides.jsonl` 载入同一个包，靠文件顺序保证优先级——一旦用户重命名或新增包文件，顺序就变了，人工覆盖会被语言包悄悄盖掉。拆成两层后优先级由代码固定，与文件名无关。`TotalEntries` 与 `GetAllEntries` 仍是两层合计/合并视图，保持既有可观察行为。
2. **默认 `ExistingOnly`。** 机翻覆盖率虽高，但错译出现在模组摘要处最刺眼；默认只用人工译文，想提高覆盖率的人自己抬到 `AllowMachine`，责任边界清楚（17 篇 MTI-08 三档）。
3. **`ExistingOnly` 遇到机翻条目是「继续往下一层找」而不是返回 null。** 否则覆盖层里一条历史机翻会挡住语言包里的人工条目——这是分层查找最容易被写错的一行，`ModTextLayerTests.ExistingOnly_skips_machine_override_and_falls_back_to_pack` 专门守它。
4. **`field` 不参与哈希。** 哈希只由文本决定，同一句话既出现在 abstract 又出现在 description 时只存一条；`field` 只体现在 `n=ckan:<identifier>:<field>` 与覆盖项命名上。
5. **黑名单优先于一切。** 作者声明「不要翻这个 mod」是意愿问题，连人工覆盖也不该绕开。
6. **缺包不是错误。** `PackLocator.Resolve` 返回 `PackDirStatus`，`LanguageDirMissing` 只记一行 info 并降级为英文原文，首帧照画；载入过程整体包在 try/catch 里，异常时降级空词库而不是让页面崩。
7. **目录名大小写不影响定位。** 用户手工解包出来的目录常是 `zh-CN`，服务内部语言标签全是小写 `zh-cn`；`PackLocator` 先 `zh-cn` 再 `zh`、先 Ordinal 再 OrdinalIgnoreCase，两处都兼容。
8. **包校验复用 `UI18n.Core.Update`，不重写清单解析。** 清单形状与 zip-slip 判据是跨语言契约，各写一套必然出现「脚本觉得对、读取器觉得错」；`ModTextPackVerifier` 只是把 `UpdateManifest.TryParse` / `UpdateVerify.CheckFile` / `CheckZip` / `ZipSafety` 包成本项目的返回类型（失败给 `size-mismatch`/`sha256-mismatch`/`unreadable`/`no-manifest` 原因码，全部不抛）。
9. **过期判定靠 `(identifier, field)` 二级索引，不靠 `DictPack` 的哈希桶。** `DictPack.TryGetEntry(hash, …)` 只在 `entry.Hash == hash` 的桶里找，而 `hash` 由当前文本算出，所以「桶命中 ⇒ 哈希相等」恒成立、`Stale` 结构性恒为 `false`。`ModTextProvider` 因此在装配期用 `pack.AllEntries` 另建一层 `键 → DictEntry` 索引，键的取值由 `ResolveIndexKey` 一条规则决定：行级可选字段 `k` 非空即用（形状 `ckan:<identifier>:<field>`，`api.py` 的 `check_key` 与 `validate` 校验形状并在同包内验唯一）；没有 `k` 时才回退到 `n` 形如 `ckan:<identifier>:<field>` 的历史覆盖条目；其余行（`n` 是归一化原文且无 `k`）不建键——拿归一化原文当键会把 A 字段的译文贴到 B 字段，宁可漏报不可错报。管线产出的包行现在都带 `k`，见「全链路」：哈希桶先照旧查原文，查不到再查键索引，命中且条目 `h` 与当前文本哈希不同即返回「过期译文 + `Stale=true`」。键命中分支上文本一定已变，条目没记 `h` 也报过期（`forceStale`）。`ExistingOnly` 对过期条目同样不下沉机翻——宁可回退英文也不拿旧机翻顶替，`ModTextLayerTests.ExistingOnly_does_not_substitute_machine_translation_for_changed_text` 守这一条。
10. **策略与黑名单不进 `IModTextService`。** 加接口成员会波及 `ModTextPageViewModel` 的内嵌空服务替身与他人测试假服务，而设置页还没有三档入口；先以 `ModTextService` 的公开属性暴露（`Policy`/`Blocklist` 改完立即生效），等设置页要接线时再提升到契约。

## 线程与性能

- `ReloadPacksAsync` 在 `Task.Run` 里装配 `DictPack`（`DictPack` 非线程安全，只在装配期写），装配完成后在锁内一次性替换 `_provider` 引用；查找路径纯内存、无锁、只读快照。
- 3,639 条（约 1.5 MB）载入在数十毫秒量级；启动时后台载入，未完成前一律英文原文，不阻塞首帧。
- 覆盖项写入走 `.tmp` + `File.Move(overwrite: true)`，避免半截文件；写完立即重载并触发 `PackChanged`，页面与详情页同时刷新。

## 测试

- `tests/CkanUI.ModText.Tests/ModTextLayerTests.cs`（19 用例，套件 25 条）：覆盖层优先、`ExistingOnly` 跳过机翻回落、`AllowMachine`、`Off`、黑名单、`field` 不影响查找、黑名单三种形状与坏 JSON 降级、清单必需、按目录校验成功/尺寸不符/文件缺失、`TryMapToLocal` 拒绝 `..`；过期判定五条——文本改过时键索引命中并报 `Stale=true`、哈希相同不报过期、`ExistingOnly` 不用机翻旧译文顶替改过的文本（`AllowMachine` 下允许但标过期）、`k` 压过 `n`（同一句原文跨字段不串条目）、无 `k` 且 `n` 是原文时不建索引（宁可漏报）；以及「同 `k` 多行」两条取舍规则——人工压过机翻（与载入顺序无关）、同类则后载入的胜。
- `tests/CkanUI.Services.Tests/ModTextServiceTests.cs`（9 用例）：重载、覆盖项写入/删除、条目过滤、JSONL 导入后再导出、UniversalI18n 同步合并；失败路径三条——导出文件不存在、导出路径为空、同步目录不存在，都断言「返回失败结果且不抛」。
- `tests/CkanUI.ViewModels.Tests/ModTextPageViewModelTests.cs`（19 用例）：载入与统计、投影裁剪、搜索过滤（源文/译文/标识/归一化原文/模组/字段/上下文七个维度）、覆盖新增/删除；同步三条——命令成功刷新状态栏、成功文案逐字断言、失败走错误出口（错误中心记 `ModTextPage.SyncUniversalI18n` + 状态栏「同步失败: …」+ `IsLoading` 复位）；分组六条——分组按 identifier 归堆且首项「全部模组」计数正确、选组过滤并置 `HasActiveFilters`、`ClearFiltersCommand` 连分组一起复位、`TrimProjection` 清空分组、`k` 压过 `n` 决定分组键（`n` 是归一化原文时仍能按 identifier 分组）、字段下拉含「无字段」兜底。
- `tests/CkanUI.App.Tests/ModTextPageDensityTests.cs`（2 用例）：单行表格行高断言（首容器 < 36，实测 30）+ 1280x800 快照。
- `tests/CkanUI.Services.Tests/ModTextServiceLayeringTests.cs`（6 用例）：两层合计与合并视图、目录名大小写回退、缺包降级不抛、默认策略与热改档、`blocklist.json` 载入生效、`VerifyInstalledPacks` 的 no-manifest/size-mismatch/成功三条路径。
- `tests/CkanUI.ViewModels.Tests/DialogsModDetailsTests.cs`（新增 6 条，套件 1647）：摘要/描述命中译文、未命中回落原文、过期标记、未接线（`TextProvider` 为 null）与接线前逐字节一致、`Find` 的 identifier/field/原文入参、换绑 provider 后的属性通知。
- 跨语言契约对齐（`T-MTI-01`）与增量比对（`T-MTI-04`）需要 Python 侧脚本，见「已知缺口」。

## 已知缺口

0. **「装进实例」只有命令行一条路，界面里还没有按钮。** `--stage sync --dest <实例>/GameData/UI18n/Packs` 已验证可用（见「全链路」），
   但 i18n 页的「同步」按钮调的是 `IModTextService.SyncFromUniversalI18nAsync`，目标固定是 ckan-ui 自己的运行时目录。
   要补的话：实例选择器 + 一个「安装到本实例」动作（写 `Packs/<lang>`，走同样的临时文件 + 原子替换 + 备份），属于新功能不算修缺陷。

1. **过期判定的键来源已由行级 `k` 承载，剩余边界是「没有 `k` 的历史行」。** 管线与覆盖项现在都写 `k`（`ckan:<identifier>:<field>`，`api.py` 校验形状；同一个 `k` 在同一包里允许多行——上游原文改版后的历史译文并存，读取侧按「人工 > 机翻、同类后写胜」取舍，见 `ModTextProvider.BuildKeyIndex`），`ModTextProvider.ResolveIndexKey` 优先取 `k`，因此上游管线产出的包也能做过期判定；只有早于本次改动、`n` 为归一化原文又没有 `k` 的包行仍只能漏报（不建键）。另一处缺口是 `Stale` 还只有测试消费，界面没有「译文已过期」徽标，接线由 Lead 决定（需要 `ModTextPageViewModel` 增加一列投影，且页面文案还没走 `ILocalizer`，见缺口 7）。
2. **增量比对（MTI-02/09）仍未落地。** 抽取与编排已可用：`i18n/tools/extract_ckan_meta.py` 产出 pool/候选，`i18n/tools/ckan_i18n_pipeline.py` 做全链路编排与离线验收（见「全链路」）。缺的是 `state.json` 的仓库指纹与集合差（含孤儿 30 天），以及 `translate` 需要网络与 `.env.local` 才能在 CI 里重跑——所以 CI 里只跑 `--check`。
3. **三档开关与预算（MTI-08）没有设置页入口**，只有 `ModTextService.Policy` 这一层实现。
   另有一处口径要说清：`ModTextProvider.IsMachine` 读行内 `src == "machine"`，但**全仓没有 `machine` 的生产者**
   （只有 `ModTextService.PutOverrideAsync` 写 `src="human"`），机翻按需翻译（`IAiClient`）也尚未接线；
   所以现有数据上 `ExistingOnly` 与 `AllowMachine` 行为等价——管线产出的包按语义就算「已有的译文」，归 `ExistingOnly` 合理。
   将来接机翻时，机翻行**必须**写 `src=machine`，否则该策略仍然失效。
4. **`SyncFromUniversalI18nAsync` 的默认路径写死** `%UserProfile%/Documents/github/UniversalI18n`（可通过参数或 `UNIVERSAL_I18N_ROOT` 覆盖）。失败已改为返回 `OperationResult`（见下条），但默认路径本身仍写死，设置页还没有「选择仓库目录」入口。
5. **导出的 `OperationResult` 契约与失败语义。** `IModTextService.ExportJsonlAsync` 返回 `OperationResult<string>`、`SyncFromUniversalI18nAsync` 返回 `OperationResult<int>`，两者都不再抛异常：文件/目录不存在给不可重试失败，`IOException`/`UnauthorizedAccessException` 分别给「可重试」与「无权限」失败；显式传入的 `sourceDir` 不存在时**直接失败、不再回落到用户主目录里的真实仓库**（以前会静默同步另一份数据）。`OperationCanceledException` 仍按取消语义冒泡。页面在失败分支走既有错误出口（`IErrorReporter` + 状态栏中文文案），`catch` 只兜底真正的意外异常。
6. **`IModTextService` 没有版本/清单字段**，`VerifyInstalledPacks` 由调用方传清单 JSON；下载与 304/ETag（MTI-06）尚未接 `IAiClient`/`HttpClient`。
7. **页面文案只有一部分走了 `ILocalizer`**：筛选条、表头、分组栏、页脚摘要已改用 `LocalizationKeys.Ui_ModTextPage_*`（zh/en 双键集对齐），但状态栏与错误文案仍是中文硬编码，等补 `Ui_ModText_*` 键后由 Lead 接线。
   同理，详情面板的「已译」「译文对应的是旧版原文，英文内容已更新」也还是中文硬编码（在 `ModDetailsViewModel`），跟页面文案一起进 `ILocalizer`。
8. **已打开的详情面板不会因换包自动刷新。** `IModTextService.PackChanged` 目前只有 `ModTextPageViewModel` 订阅，
   `ModDetailsViewModel.Refresh` 是私有的，所以「详情开着 → 跑 `--stage sync` → 换包」这条路径要等下次打开详情才看到新译文。
   要补的话：公开 `RefreshTranslations()`（内部即 `Refresh`），在 VM 里订阅 `PackChanged` 并切到 UI 线程再调；对话框是单例，订阅不用退订，
   但必须经 `IDispatcher`（`PackChanged` 在线程池线程触发，直接改绑定属性会跨线程）。
