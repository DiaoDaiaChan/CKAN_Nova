# 17 Mod 源信息（仓库元数据）的 i18n 翻译与仓库同步

> 定位：**E5 域**。把 CKAN 仓库元数据里的**人类可读文本**（`abstract` 为主、`description` 次之、`name` 默认不译）翻译成用户语言，并**跟着仓库同步**自动增量更新。
> 关键决定：**不自建翻译存储与门禁，直接复用 `UniversalI18n/tools/i18n_tools` 的数据契约与工具链**（`h = FNV-1a 64(normalize(s))`、G2–G9 门禁、journal 审计、`--root` 隔离），运行时复用 **`UI18n.Core`**（`Fnv1a` / `DictPack` / `PackLocator` / `UpdateVerify`）。
> 前置阅读：[16 篇](16-i18n.md)（UI 文案 i18n，D11）、[14 篇](14-log-and-ai-debug.md)（`IAiClient`）、[09 篇](09-migration-packaging.md)（数据位置与共存铁律）、[12 篇](12-feature-parity.md)（搜索与列表基线）。
> 一句话：**UI 文案走 resx（16 篇）；Mod 元数据走 i18n_tools 格式的译文包（本篇）；两者共用语言选择，互不阻塞。**

---

## 1. 目标与非目标

### 1.1 目标

| ID | 目标 | 可验收 |
|---|---|---|
| M-01 | 仓库元数据的 `abstract` 在用户语言下可读 | 3,799 个 identifier 的摘要覆盖率 ≥95%（无人译时用机翻补齐） |
| M-02 | `description`（长描述，仅 57 个 mod 有）同样处理 | 57 条全部可译；无描述的不显示空块 |
| M-03 | **跟着仓库同步**：仓库更新后只译"变了的" | 连续两次同步，第二次请求条数 = 实际变更条数（±0） |
| M-04 | 内容寻址，跨版本复用 | 同一 identifier 的 8.19 个版本共享一条译文，去重率 ≥95% |
| M-05 | 译文来源可辨 | 每条落 `mod`/`ctx`/`h` 三元组，UI 有徽标（人工 / 机翻 / 已过期） |
| M-06 | 原文永远可达 | 一键看英文原文；原文变了而译文未更新时明确标注 |
| M-07 | 搜索同时命中译文与原文 | 中文词能搜到英文摘要的 mod，英文词结果集**与老 GUI 完全一致** |
| M-08 | 可离线、可零外发 | 只用已有译文包即可工作；机翻可选，可指向本地模型 |
| M-09 | 名字默认不译 | `name` 保持原文；仅人工覆盖显式提供时才显示译名 |
| M-10 | **与 UniversalI18n 生态同契约** | 译文行的 `h` 由 `fnv1a64(normalize(s))` 计算，C# 侧用 `UI18n.Core.Hashing.Fnv1a` 校验一致 |

### 1.2 非目标

- **不做** UI 文案翻译（[16 篇](16-i18n.md)）。
- **不做** `identifier`、版本号、许可、依赖标识、标签名的翻译（标识符是主键）。
- **不改** 仓库 JSON 本身，**不回写**上游。
- **不新增**哈希/门禁/审计的第二套实现——凡是 `i18n_tools` 已有的（归一化、FNV、G2–G9、journal、revision 锁、manifest 校验）一律复用。
- **不污染** UniversalI18n 仓库的 `packs/pool.jsonl` 与覆盖率统计（用 `--root` 指到独立 root，见 §5.2）。

---

## 2. 现状取证

### 2.1 仓库缓存（本机真实数据，只读）

`%LocalAppData%\CKAN\repos\4A85405A-KSP-默认数据库.json` = **37,933,412 字节**。
顶层键：`available_modules`、`download_counts`、`known_game_versions`、`repositories`。

```
available_modules: {
  "<identifier>": {                    // 3,799 个
    "module_version": {
      "<version>": { spec_version, identifier, name, abstract, author, version,
                     ksp_version_min, ksp_version_max, license, resources, ... }
    }
  }
}
```

### 2.2 体量实测（决定"全量翻译"是否可行）

| 指标 | 实测值 |
|---|---|
| identifier 数 | **3,799** |
| module_version 条目总数 | **31,132**（**8.19 个版本/identifier**） |
| `name` 字节 | 84,925 |
| `abstract` 字节 | **325,026**（3,794 个 identifier 有值，99.9%） |
| `description` 字节 | **13,076**（**仅 57 个 identifier 有值**，1.5%） |
| 摘要字符数分布 | 均值 **229**，中位 **156**，p90 **594**，p99 **845**，最大 **845** |
| 去重后不同 `abstract+description` 块 | **3,639**（重复 160 个 identifier） |
| 含 Markdown 形态的摘要 | **1** 个 |
| 声明 `localizations` 的版本条目 | **4,598** 处 |
| 已含非 ASCII 文本的 mod | 63 个 |

### 2.3 四条结论

1. **要翻的量远小于直觉**：全目录人类可读文本 ≈ **423 KB**（≈106k tokens），不是"几万 mod × 长文"。
2. **`description` 几乎是空字段**：仅 57 个、最长 845 字符——所以不存在长文档排版问题（§1.2 砍掉 Markdown 重排有数据依据）。
3. **必须按内容寻址**：8.19 版本/identifier，按版本键请求量 ×8；实测去重后 3,639 条。
4. **`name` 是专名**（`4kSP_Expanded`、`MechJeb2`）→ 默认不译（M-09）。

### 2.4 字段来源与同步钩子

| 用途 | 位置 |
|---|---|
| `abstract` / `description` / `name` 定义 | `Core/Types/CkanModule.cs:38-42`（`@abstract`、`description?`）、`:110-111`（`name`） |
| mod 自带本地化声明（非译文） | `Core/Types/CkanModule.cs:153-154` `public string[]? localizations` |
| 仓库定义（名字/地址/优先级） | `Core/Repositories/Repository.cs:15-17` |
| 仓库刷新与事件 | `Core/Repositories/RepositoryDataManager.cs:144` `Update()`、`:253` `Updated` |
| 仓库缓存目录 | `Core/Repositories/RepositoryDataManager.cs:25`（默认 `%LocalAppData%\CKAN\repos`） |
| 旧 GUI 搜索索引字段（要扩展的对象） | `GUI/Model/GUIMod.cs:100-106`（`SearchableName/Identifier/Abstract/Description/Authors`）+ `:256-289` 归一化；`GUI/Model/ModSearch.cs:575-579` `MatchesDescription` |

### 2.5 复用源的实测契约（`C:/Users/43701/Documents/github/UniversalI18n`）

| 契约 | 位置 | 内容 |
|---|---|---|
| 哈希 | `tools/i18n_tools/fnv.py:14-41` | `OFFSET_BASIS=0xCBF29CE484222325`、`PRIME=0x100000001B3`、UTF-8 逐字节、输出 16 位小写 hex；测试向量 `""`→`cbf29ce484222325`、`"a"`→`af63dc4c8601ec8c`、`"foobar"`→`85944171f73967e8` |
| 归一化 | `tools/i18n_tools/textnorm.py:59-66` | **NFKC → CRLF/CR 折成 LF → 空白折叠（含 NBSP U+00A0、U+3000 等）→ trim**；**不做**大小写折叠、**不**统一引号 |
| 过滤判据 | `tools/i18n_tools/textnorm.py:69-104` | already-cjk / key-like / pure-number / url-only 等（元数据场景多数不适用，见 §4.4） |
| 门禁 | `tools/i18n_tools/api.py:131` `GATE_ORDER=("G2"…"G9")`、`:840-895` | G2 占位符数、G3 格式符、G4 富文本、G5 哨兵、G6 长度比、G7 锁定术语、G8 echo_keep、G9 骨架 `#`；人工入口只跑 G2–G5 |
| 写入口 | `tools/i18n_tools/api.py` | `entry put` / `entry put-batch --stdin`（NDJSON，≤60 条/批，原子事务，自动算 h、revision 乐观锁、journal 审计）/ `override put` / `glossary put` / `validate [--strict]` |
| 根隔离 | `tools/i18n_tools/api.py:189-243` | `--root` 把全部读写关到另一个根（`<root>/packs/<lang>/*.jsonl`、`<root>/packs/journal`、`<root>/packs/overrides.jsonl`、`glossary.jsonl`、`conventions.jsonl`、`terms.jsonl`、`mods.json`） |
| 包身份 | `tools/i18n_tools/packindex.py:29-30,168-170` | `mod = 文件名去掉 .jsonl`（IL 包再剥 `.il`），与包内行 `mod` 字段同一套 |
| 行格式 | `packs/zh-cn/Squad.jsonl` 实测 | `{"h":"3ff885233413de88","n":"#autoLOC_500100","s":"…","t":"…","mod":"Squad"}` |
| C# 运行时 | `src/UI18n.Core/Hashing/Fnv1a.cs:20-108` | `OffsetBasis`/`Prime`/`Hash(string)`/`Hash(byte[])`；csproj 双目标 **net472 + net10.0**、**零第三方依赖、零游戏依赖**（有编译期红线守卫） |
| C# 字典 | `src/UI18n.Core/Dict/DictPack.cs:93-143` | `LoadJsonLines` / `AddJsonLine` / `TryGet(ulong hash, string source, out string translation)` + 命中/未命中/保留计数 |
| C# 层级 | `src/UI18n.Core/Dict/KeyLevel.cs` | 有 `ctx` → `Ctx(1)`；有 `mod` → `Site(2)`；都无 → `Global(3)`；数值越小优先级越高 |
| C# 定位 | `src/UI18n.Core/Dict/PackLocator.cs` | packs root / 语言目录 / 候选文件解析 + `PackDirStatus` |
| C# 校验 | `src/UI18n.Core/Update/{UpdateManifest,UpdateVerify,ZipSafety,Sha256Hex}.cs` | `CheckBytes`/`CheckFile`/`CheckZip`/`CheckEntry`，失败原因常量 `size-mismatch`/`sha256-mismatch`/`unreadable`/`no-manifest` |

### 2.6 现在完全没有的东西

- 仓库里**没有任何元数据译文**：CKAN-meta / netkan 只产出英文（`description` 甚至基本不产出）。
- Core 内 `translat*` 零命中；无"下载译文包"路径。
- 老 GUI 的"语言"只换界面文案，**摘要永远英文**——这就是 §3 要补的缺口，也是 `03-ui-spec.md` §17.4 原写"Mod 元数据始终英文"必须改写的原因（§14 修订点）。

---

## 3. 契约对齐：用 i18n_tools 的键，不发明新哈希

### 3.1 键（与 `fnv.py` / `Fnv1a.cs` 完全一致）

```
h = format(fnv1a64(normalize(sourceText)), "016x")     // 16 位小写 hex
```

- **`sourceText` 是原始英文文本**（`abstract` 或 `description` 原样），`normalize` 只在算 `h` 时使用（NFKC + 空白折叠 + trim）。
- 同文本不同版本 → 同一 `h`（M-04）；改一个字 → 新 `h`。
- **不引入第二套哈希**：我早先草案里的 `sha256(id|field|text)[:16]` **作废**（v1.1 变更），因为它会让 C# 侧必须自己再实现一遍，且与 `DictPack.TryGet` 不兼容。

### 3.2 行格式（沿用 `packs/<lang>/*.jsonl`，只加两个字段约定）

```json
{"h":"a1b2c3d4e5f60718","n":"ckan:MechJeb2:abstract","s":"MechJeb2 is a plugin for KSP that provides...","t":"MechJeb2 是一个……","mod":"CKANMeta","ctx":"MechJeb2"}
```

| 字段 | 取值 | 理由 |
|---|---|---|
| `mod` | 固定 `CKANMeta` | `mod = 文件名去 .jsonl`（`packindex.py:29`），所以整包一个文件 `packs/zh-cn/CKANMeta.jsonl`，避免 3,799 个小文件 |
| `ctx` | CKAN `identifier` | 有 `ctx` → `KeyLevel.Ctx`（优先级最高，`KeyLevel.cs`）；同时天然保留"这条属于哪个 mod"的归属信息 |
| `n` | `ckan:<identifier>:<field>` | 保留人类可读定位；`field ∈ {abstract, description, name}` |
| `s` / `t` | 英文原文 / 译文 | 与既有包一致 |

> **为什么不用"每 mod 一个文件"**：`packindex.py:49` 只扫 `<packs>/<lang>/*.jsonl` 一层，3,799 个文件会把包索引、覆盖率报表、分发包体积全部弄坏；单包 3,639 行 ≈ 1.5 MB，正好是一个合理的分发单位。

### 3.3 查找链（映射到既有结构，不新造）

| 层 | 载体 | 实现 |
|---|---|---|
| L1 人工覆盖 | `<root>/packs/overrides.jsonl`（`override put` 写入） | 独立的 `DictPack`，**先试** |
| L2 人工译文 | `packs/<lang>/CKANMeta.jsonl` 中 `src` 为人工的行（人工审核后由 `entry put` 写入） | 同一个 `DictPack`（同文件内天然优先由重叠覆盖实现） |
| L3 机翻译文 | 同上文件的机翻行（`--actor agent:translate` 写入，journal 可溯） | 同上 |
| L4 英文原文 | `CkanModule.abstract` / `description` | 永不修改 |

- 运行时是**两个 `DictPack` 依次 `TryGet`**（override 包 → 主包 → 原文），命中即返回；`DictPack` 已有 Hits/Misses/Keeps 计数，可直接喂给 UI 的"覆盖率"显示。
- **过期判定**：仓库里该 identifier 的 `normalize(abstract)` 算出的 `h` 在包内查不到，而 `n` 里能查到同名键 → 判"原文已更新"，显示英文 + 黄色徽标（M-06）。

### 3.4 存储与载入

```
%LocalAppData%\CKAN\ckan-ui\i18n\
  packs\<lang>\CKANMeta.jsonl      ← 下载/导入的译文包（一份或多份，按来源分目录可叠加）
  packs\<lang>\overrides.jsonl     ← 用户本机人工覆盖
  state.json                       ← 已载入包清单 + 上次同步的仓库指纹 + 预算用量
```

- 多源叠加：`PackLocator` 已有"候选文件解析"能力，多个文件按文件名序载入，后载入者覆盖先载入者（同 `h`）。
- 载入：3,639 行 ≈ 1.5 MB，`DictPack.LoadJsonLines` 实测应在数十毫秒级（SP-32 验）。

---

## 4. 机翻管线（复用 `i18n_tools`，不重写门禁）

### 4.1 两条可走的路（按是否需要人工介入区分）

| 路径 | 做法 | 适用 |
|---|---|---|
| **A. 工具链离线生产**（推荐，保质量） | CKAN 元数据 → 候选 NDJSON → `python -m i18n_tools.api --root <独立root> entry put-batch --actor agent:translate --stdin` → 门禁 G2–G9 预检 → journal 落账 → 打包发布 | 社区包制作、我们自己做全量首翻 |
| **B. 应用内按需机翻**（保体验） | 复用 E2 的 `IAiClient`，直接产出 `{h,s,t,mod,ctx}` 行，写入本机 `packs/<lang>/CKANMeta.jsonl`（**本机缓存，不进社区包**） | 用户打开未译 mod 时的即时补译 |

两条路产出**同一行格式**，因此可以互相补齐、也可以互相覆盖。

### 4.2 批处理与预算（实测数字）

- 全量 3,639 条 ÷ 60 条/批（工具的切片上限）= **61 批**。
- 输入 ≈ 106k tokens，输出 ≈ 110k tokens；廉价档成本 **< $0.5**（一次性）；仓库日更增量（几十条）≈ **< $0.01/天**。
- 切片规则（skill §4）：**一行一个 `h` 不可拆**；长段落按句号/分号切段翻译再拼回；占位符 `<<1>>`、`{0}`、`%s` 与 KSP 富文本标签**原样保留**——元数据里这些极少（实测含占位符的摘要近乎为 0），但门禁照样跑。

### 4.3 门禁（直接用，不改）

| 门 | 在元数据场景的含义 | 处理 |
|---|---|---|
| G2/G3/G5 | 占位符/格式符/哨兵数量一致 | 原样执行 |
| G4 | 富文本标签成对保留 | 原样执行（元数据里几乎不出现） |
| **G6 长度比** | 中文/英文长度比 | **需要适配**：长文本（229 字符均值）与 UI 短串的合理区间不同，需按长度分档放宽（SP-35 定阈值） |
| **G7 锁定术语** | 官方术语（`Oxidizer→氧化剂` 等 12 条约定 + 短语级官方术语）**不得改** | **原样执行，这是本方案最大收益**：mod 摘要会自动与原版官方汉化用词一致（`packs/zh-cn/Squad.jsonl` 是权威译名库） |
| G8 echo_keep | 原文照抄 | 短专名允许 keep（`t==s` 落库），长句照抄拒绝 |
| G9 骨架 `#` | 模板行 | 元数据无骨架行，不触发 |

### 4.4 需要跳过的过滤判据

`textnorm.py` 的过滤（`is_key_like`/`is_pure_number`/`is_url_only`/`already-cjk`）是为 **UI 提取**设计的；元数据场景只保留两条：`empty` 与 `already-cjk`（后者不适用，因为源恒为英文）。

### 4.5 幂等与冷却

- 相同 `h` 已存在 → 零请求。
- 门禁不过或请求失败 → **不写入**，记 `failed` 计数，同 `h` 7 天内不重试（`state.json` 记时间戳）。
- 写入走"临时文件 + 原子替换"；`journal` 由工具链保证（手工路 A），本机路 B 记录 `state.json` 版本号。

---

## 5. 译文包：发布、订阅与仓库同步

### 5.1 包结构（独立仓库 `ckan-ui-translations`，格式与 UniversalI18n 一致）

```
packs/zh-cn/CKANMeta.jsonl        ← 3,639 行（单文件、单 mod 身份）
packs/overrides.jsonl             ← 社区人工校准
packs/glossary.jsonl              ← 从 UniversalI18n 同步的术语表副本
packs/journal/                    ← 工具链自动产生的审计日志
manifest.json                     ← {schemaVersion, repoFingerprint, generatedAt, files:[{path,bytes,sha256}]}
blocklist.json                    ← 按 identifier 拉黑（takedown）
```

- `repoFingerprint = fnv1a64(sorted("identifier|h" 列表).join("\n"))`：与本地算出的当前仓库指纹比对，不一致 → 提示"译文包落后 N 条"并补齐。
- **`manifest.json` 直接对齐 `UI18n.Core/Update/UpdateManifest.cs` 的字段语义**（`Path` / `Bytes` / `Sha256`），校验直接调 `UpdateVerify.CheckZip` / `CheckFile`，不必自写。

### 5.2 与 UniversalI18n 仓库的边界（**必须用 `--root` 隔离**）

| 事项 | 结论 |
|---|---|
| 是否把 `CKANMeta.jsonl` 放进 UniversalI18n 的 `packs/` | **否**。它的 `packs/pool.jsonl` 覆盖率分母来自 GameData 抽取（12,792 行），塞进元数据行会污染覆盖率与 `selftest` 断言（`selftest.py:446-459` 逐字节比对 `pool.jsonl`） |
| 怎么复用工具链 | `python -m i18n_tools.api --root <ckan-ui-translations 根>`，读写全部落在独立 root（`api.py:189-243`） |
| 怎么复用术语与官方译名 | 把 `packs/glossary.jsonl` + `packs/conventions.jsonl` + `packs/zh-cn/Squad.jsonl` 复制为独立 root 的对应文件（只读同步），G7 立刻生效 |
| 代码复用 | 我们的 UI 项目 `ProjectReference` **`src/UI18n.Core/UI18n.Core.csproj`**（net10.0 目标，零依赖零游戏依赖，编译期红线守卫保证不会引入 UnityEngine） |

### 5.3 与 CKAN 仓库的关系

| 事项 | 结论 |
|---|---|
| 是否注册为 CKAN `Repository` | **否**（会污染 `registry.json` 与仓库优先级，且会被误认为 mod 源） |
| 是否写 `config.json` | **否**，只写 `ckan-ui.json` / `state.json`（[09 篇](09-migration-packaging.md) §3.2 铁律） |
| 下载触发 | 仓库 `Updated` 后检查一次远端 `manifest.json`（ETag/304），**≤1 次/小时** |
| 离线 | 支持导入 zip/单文件；与在线包同格式、同校验 |

### 5.4 同步流程（M-03）

```
RepositoryDataManager.Updated
  → 遍历 available_modules（3,799）取每个 identifier 的"最相关版本"abstract/description
  → 算 h（Fnv1a.Hash(normalize(text))），与 state.json 里的集合求差
  → 新增/变更 → 入队（预算内自动机翻；超预算转"按需"）
  → 消失 → 标记孤儿（保留 30 天）
  → 更新 state.json（仓库 ETag + 集合摘要 + 预算用量）
```

- 全程旁路、不阻塞仓库刷新（[05 篇](05-threading.md) 线程纪律）。
- 孤儿清理与多仓库共享（同文本 → 同 `h` → 天然复用）。

### 5.5 许可与归属

- 元数据文本属各 mod 作者；译文行带 `mod`/`ctx` 归属，`src` 通过 journal 与 actor 可溯。
- 社区包默认 CC-BY-NC-SA 风格，并在「关于」页与 `manifest.json` 写明来源。
- `blocklist.json` 命中 → 该 identifier 一律显示英文。

---

## 6. UI 设计

### 6.1 ModInfo 元数据页

```
┌ 摘要 ────────────────────────────────────────────┐
│ 4kSP 按与原版 UI 缩放设置对应的比例缩放地图元素。   │ ← 译文（主）
│ [机器翻译 · 2026-10-26 · 原文已更新]              │ ← 徽标行（可点开原文）
│ ▸ 显示英文原文                                     │
└──────────────────────────────────────────────────┘
```

- 徽标三态：`人工译文`（绿）/ `机器翻译`（灰）/ `原文已更新`（黄，同时显示英文）。
- 无译文 → 英文 + 「翻译此 mod」按钮（按需触发，不自动烧预算）。

### 6.2 列表与搜索

- `Description` 列显示译文，可切回原文（对应 [12 篇](12-feature-parity.md) §2 的 15 列）。
- 搜索：`ModSearch.MatchesDescription` 改为 `SearchableAbstract ∪ 译文字段`（**并集**，M-07）；命中译文时结果行右侧显示小标记。
- 不做"只搜中文"模式（会让人找不到自己装过的 mod）。

### 6.3 其他出场位置

- 安装确认对话框的摘要、依赖冲突提示里引用的摘要、通知文案。
- 「设置 → 语言」新增一行：**「Mod 简介翻译：关 / 仅用已有译文 / 允许机翻（含预算）」**，默认"仅用已有译文"（与 [14 篇](14-log-and-ai-debug.md) §7 的零遥测默认一致）。

---

## 7. 线程与性能

| 关注点 | 结论 |
|---|---|
| 取文本 | `IModTextProvider.Get(identifier, field) → ModText { Text, Src, Stale }`，纯内存 `DictPack.TryGet`，无锁（快照引用替换） |
| 载入 | 启动后台载入（1.5 MB / 3,639 行），未完成前一律英文，**不阻塞首帧** |
| 同步计算 | 3,799 × 2 次 `Fnv1a.Hash`（UTF-8 逐字节）≈ 数十毫秒，放线程池，绝不占 UI 线程 |
| 内存 | 3,639 条 ×（键 ~40B + 译文 ~400B）≈ **1.6 MB**；多语言按需载入 |
| 列表 | 译文替换不改变行高计算方式，无新增测量开销 |

---

## 8. WBS（134h）

| ID | 任务 | 工时 | 依赖 | 产出 | 验收 |
|---|---|---|---|---|---|
| MTI-01 | 元数据候选抽取（3,799 → 去重 3,639）+ `normalize`/`Fnv1a` 对齐 + NDJSON 候选产出 | 12 | ARC-01 | 候选集 | 与 Python 版 `h` 逐条一致（交叉校验 100 条） |
| MTI-02 | 仓库指纹与增量比对（`state.json`、集合差、孤儿清理） | 12 | MTI-01, INT-04 | 同步器 | 连续两次同步第二次请求数 = 实际变更数 |
| MTI-03 | `IModTextProvider`：`DictPack` 叠加 + `KeyLevel` 策略 + 过期判定 | 8 | MTI-01, UI18n.Core | 取文本服务 | L1→L4 单测全绿 |
| MTI-04 | 机翻批处理（工具链 CLID 封装：切片 60、NDJSON 管道、`--root`、重试与冷却） | 12 | MTI-03, `IAiClient` | 翻译器 | 全量 61 批一次跑通 |
| MTI-05 | 门禁适配（G6 分档阈值、G7 术语副本同步、G8 keep 白名单） | 6 | MTI-04 | 门禁配置 | 20 条坏输出全拦、正常长文不误伤 |
| MTI-06 | 译文包发布/订阅（`UpdateManifest`+`UpdateVerify`+`ZipSafety`、ETag/304、离线导入） | 8 | MTI-03 | 包消费端 | 200/304/校验失败/断流 四路径通过 |
| MTI-07 | `blocklist.json` + 许可与来源声明 + 关于页展示 | 8 | MTI-06 | 合规层 | 拉黑条目一律英文 |
| MTI-08 | 预算与三档开关（关/仅已有/允许机翻）+ 用量统计 | 10 | MTI-04 | 设置页数据源 | 超预算自动降级为按需 |
| MTI-09 | 工具链包装脚本（`tools/meta-i18n.ps1`：harvest → slice → put-batch → packindex → 打包） | 8 | MTI-01..06 | 生产脚本 | 一条命令产出可发布包 |
| UI-MTI-01 | ModInfo 摘要块（译文/原文切换、徽标三态、按需翻译按钮） | 14 | UI-08, MTI-03 | 控件 | H-11 手测通过 |
| UI-MTI-02 | 列表 Description 列译文 + 搜索并集 + 命中标记 | 10 | UI-09, MTI-03 | 列表改造 | T-MTI-06/07 绿 |
| VM-MTI | `ModTextViewModel`（徽标状态、切换、按需请求、错误呈现） | 8 | UI-MTI-01 | VM | headless 可测 |
| TST-MTI | 单测 + 仓库快照黄金测试 + 跨语言哈希交叉校验 + 手工组 H-11 | 12 | 上述全部 | 自动化用例 | [07 篇](07-testing.md) §13 全绿 |
| DOC-MTI | 「译文包作者指南」（如何加语言、如何跑工具链）+ 隐私说明 | 6 | — | 文档 | 新人能产出一个语言包 |
| **合计** | | **134h** | | | |

**E5 可整段独立交付**：MTI-01/02/03 + UI-MTI-01（只消费已有包，不接机翻）= **46h ≈ 1.2 周**。

---

## 9. 里程碑（M11，W45–W48）

| 阶段 | 周 | 任务 | 门禁 |
|---|---|---|---|
| **M11a** 契约与取数 | W45–W46 | MTI-01/02/03 | ① C# 与 Python 的 `h` 交叉校验 100/100 ② 3,639 条载入 ≤80ms ③ 增量比对零误报 |
| **M11b** 生产与门禁 | W46–W47 | MTI-04/05/08/09 | ④ 61 批一次跑通 ⑤ 坏输出全拦、长文不误伤 ⑥ 超预算自动降级 ⑦ 脚本一条命令产出可发布包 |
| **M11c** 包与同步 | W47–W48 | MTI-06/07 | ⑧ 304/校验/离线导入通过 ⑨ 包落后 N 条能识别补齐 ⑩ 拉黑生效 |
| **M11d** UI 与收口 | W48 | UI-MTI-01/02, VM-MTI, TST-MTI, DOC-MTI | ⑪ 徽标三态正确 ⑫ **英文词搜索结果集与老 GUI 完全一致** ⑬ H-11 四语言×三 mod 截图 |

---

## 10. 测试

| ID | 类型 | 内容 |
|---|---|---|
| T-MTI-01 | 跨语言契约 | C# `Fnv1a.Hash(normalize(s))` 与 Python `hash_text(normalize(s))` 对 100 条真实摘要逐条相等；含官方向量 `""`/`"a"`/`"foobar"` |
| T-MTI-02 | 单测 | `normalize` 行为：NFKC（全角→半角）、CRLF→LF、NBSP/`\u3000` 折叠、trim；**不做**大小写折叠 |
| T-MTI-03 | 单测 | 查找链优先级与过期回退（override → 主包 → 原文） |
| T-MTI-04 | 单测 | 增量比对：三份仓库快照（旧/新/删 mod）验证集合差与孤儿保留 |
| T-MTI-05 | 集成 | 门禁：G2–G9 各一条构造拒绝 + 长文不误伤（G6 分档） |
| T-MTI-06 | 集成 | 搜索并集：中文词命中；英文词结果集与老 GUI **零差异** |
| T-MTI-07 | 集成 | 徽标三态渲染截图对比 |
| T-MTI-08 | 集成 | 包下载 200/304/失败/断流；离线 zip 导入；`UpdateVerify` 四种失败原因各一条 |
| T-MTI-09 | 集成 | 预算超限后不再发请求、转按需、UI 提示准确 |
| T-MTI-10 | 安全 | 输出含 `<script>`/JSON 泄露/超长 → 丢弃并记事件；`blocklist` 生效 |
| H-11 | 手工 | zh/ru/de ×（MechJeb2、KSPCommunityFixes、Kopernicus）：看译文、切原文、制造过期、按需翻译、无网启动 |

**M11 出口规则**：T-MTI-01..10 全绿；H-11 截图留证；搜索对拍零差异；未测 = 不通过。

---

## 11. 风险

| ID | 风险 | 概率 | 影响 | 缓解 |
|---|---|---|---|---|
| **R-48** | 机翻质量差导致误解 mod 功能 | 中 | 中 | 强制徽标；原文一键可达；G2–G9 门禁；人工译文优先 |
| **R-49** | 译文过期仍显示旧译 | 中 | 中 | `h` 精确失效（内容变了必然换 `h`）+ 黄标 + 自动重译队列 |
| **R-50** | 成本失控 | 低 | 中 | 内容寻址 + 失败冷却 7 天 + 三档开关 + 月度上限 + 超限转按需 |
| **R-51** | 外发隐私争议 | 中 | 中 | 公开元数据；默认"仅用已有译文"；显式同意；可本地模型 |
| **R-52** | 译文包许可/归属纠纷 | 低 | 高 | 行内 `mod`/`ctx` 归属 + journal 可溯 + CC-BY-NC-SA 声明 + `blocklist` takedown |
| **R-53** | 搜索被译文污染 | 中 | 中 | 并集 + 命中标记 + 对拍零差异门禁 + 可切回仅原文搜索 |
| **R-54** | 仓库结构变化导致取数失败 | 低 | 高 | 只依赖 `available_modules.*.module_version.*.{name,abstract,description}`；解析失败降级为不翻译，绝不崩 |
| **R-55** | **污染 UniversalI18n 仓库**（把元数据行写进其 `packs/`） | 中 | 高 | 一律 `--root` 独立根；CI 校验该仓库 `selftest`/`pool.jsonl` 逐字节未变 |
| **R-56** | `UI18n.Core` 升级导致契约漂移（`h`/行格式/target 变更） | 低 | 高 | 固定引用版本 + T-MTI-01 跨语言契约测试作为门禁 + 变更需同步升 `manifest.schemaVersion` |
| **R-57** | G6 长度比对长文本误伤大量条目 | 中 | 中 | SP-35 先定分档阈值；误伤时宁可留英文，不放低质量译文 |

**观察指标**：待译条目数、失败冷却数、覆盖率（按语言）、过期条目数、每语言累计 tokens、用户"切回原文"次数（高 → 译文质量差）。

---

## 12. Spike（SP-31..SP-35）

| ID | 问题 | 做法 | 通过标准 | 不通过时 |
|---|---|---|---|---|
| **SP-31** | 200 条真实摘要的机翻质量与成本 | 抽 200 条（含长尾/含 URL/含专名）跑真实模型，人工抽检 50 条 | 可读率 ≥80%、成本 < $1 | 退化为"只消费人工包"（46h 版本） |
| **SP-32** | 3,799 个 identifier 的 `Fnv1a.Hash` + 比对耗时；3,639 行 `DictPack` 载入耗时 | 本机 37.9 MB 仓库缓存 + 生成包 | 哈希 ≤200ms；载入 ≤80ms | 移入线程池 + 结果缓存 |
| **SP-33** | 搜索并集是否破坏既有结果集 | 20 个英文查询对拍老 GUI | 结果集完全一致 | 译文不参与匹配（降级 M-07） |
| **SP-34** | 本地模型（Ollama）零外发可用 | 指向本地端点跑 50 条 | 质量可用且无外发 | 仅保留云端路径，默认关闭 |
| **SP-35** | `i18n_tools --root` 能否在独立根跑通 `entry put-batch` + `packindex`，且 G6 对长文本的合理阈值 | 建临时 root，灌 60 条真实摘要 + 术语副本 | 写入/journal/packindex 全通；G6 误伤率 <5% | 改用工具链的 `validate --strict` 子集（G2/G4/G9）并自定长度阈值 |

**SP-31 与 SP-35 是 M11 的 Go/No-Go**：SP-31 不过 → 只消费人工包；SP-35 不过 → 机翻改走应用内路径 B（不落社区包）。

---

## 13. 决策（2026-10-27 已拍板）

> **决策**：Q1 = **默认"仅用已有译文"**（机翻需显式打开）；Q2 = **本地导入 + 可选 URL**（暂不托管）；Q3 = **不译 `name`**；Q4 = **用 UniversalI18n 的 `Squad.jsonl` 做 G7 术语**（与官方汉化对齐）；Q5 = **`localizations` 做成筛选徽标**。工时不变（134h）。

（下表的"建议"即最终决策依据。）

| ID | 问题 | 我的建议 |
|---|---|---|
| Q1 | 默认三档中的哪一档 | **"仅用已有译文"**，机翻需显式打开 |
| Q2 | 译文包托管在哪（新仓库 / UniversalI18n 的 release / 本地导入） | 先做"本地导入 + 可选 URL"，托管后定 |
| Q3 | `name`（mod 原名）要不要译 | **不译**（M-09） |
| Q4 | 是否把 UniversalI18n 的 `Squad.jsonl` 官方译名一并用于 G7（强术语一致性） | **建议是**，代价是摘要里 KSP 专有名词与原版汉化完全对齐 |
| Q5 | `localizations`（4,598 处）是否做成筛选徽标 | 建议做（成本极低，属 C 族搜索字段增强） |

---

## 14. 与其他篇的修订点

1. `00-index.md`：文档地图加 17 篇；§2 加 E5；§3 加 **S17**；§4 加 **D12**（元数据译文走 i18n_tools 契约、旁路存储、不入 `config.json`/`registry.json`、复用 `UI18n.Core`）；§5 加 M11、合计改 **48 周 / 1736h**；§6 验收加**第 11 条"元数据译文不误导"**；v1.9。
2. `01-architecture.md`：加 §14（`CkanUI.ModText` + `Services/ModText`；硬约束：**译文永不写回 `CkanModule`**、哈希只用 `UI18n.Core.Hashing.Fnv1a`、外发唯一入口复用 `IAiClient`）。
3. `03-ui-spec.md`：加 §18（摘要块/徽标三态/列表译文列/搜索命中标记）；**改写 §17.4 第二条**（"Mod 元数据始终英文"作废 → 由 E5 提供，缺译英文兜底）。
4. `04-mvvm-and-models.md`：加 §14（`ModTextViewModel` + 三条约定：VM 只读 provider、切换原文是视图状态、按需翻译必须显式命令）。
5. `05-threading.md`：加 §14（启动后台载入、`Updated` 后线程池算差、`DictPack` 快照无锁、机翻后台可取消）。
6. `06-wbs-milestones.md`：加 §1.14（134h 表）+ M11 + W45–W48 + 资源表 + **DoD 第 11 条**。
7. `07-testing.md`：加 §13（T-MTI-01..10 + 跨语言契约 + H-11 + 搜索对拍门禁）。
8. `08-risk-register.md`：加 §11（R-48..R-57）。
9. `10-spike-plan.md`：加 §14（SP-31..SP-35，SP-31/SP-35 为 Go/No-Go）。
10. `12/13/14/15/16` 与 `doc/ui-refactor-feasibility.md`：全量口径改 **1736h / 48 周**（E6 落盘后为 **1846h / 51 周**；**E7 落盘后为 1990h / 55 周；E8 落盘后最终为 2196h / 60 周**）、文档数 **17 份**（E6 后 **18 份**，E7 后 **19 份**，E8 后 **20 份**，加布局篇后 **21 份**）。

---

## 15. 修订记录

| 版本 | 日期 | 变更 |
|---|---|---|
| v1.0 | 2026-10-26 | 首版：E5 域落盘（内容寻址译文库、仓库增量同步、机翻质量门、社区包、152h / M11） |
| v1.1 | 2026-10-26 | **改用 `i18n_tools` 契约**：`h=fnv1a64(normalize(s))`（作废自研 sha256 方案）、复用 G2–G9 门禁与 journal、`--root` 隔离、运行时引用 `UI18n.Core`（`Fnv1a`/`DictPack`/`PackLocator`/`UpdateVerify`）；行格式定为单文件 `CKANMeta.jsonl` + `ctx=identifier`；WBS 152h → **134h**；新增 R-55/R-56/R-57 与 SP-35 |
