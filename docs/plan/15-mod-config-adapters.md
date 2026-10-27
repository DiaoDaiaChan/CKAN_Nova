# 15 · 社区 Mod 配置适配层（E3）

> 状态：v1.0（2026-10-27）｜ 本文只做**计划与设计**，不含实现代码；实现须等用户指令。
> 归属域：**E3 Mod 配置域**（与 E1 存档、E2 可观测性并列，独立可交付）。
> 上游依据：`01-architecture.md` 分层、`13-savegame-sfs.md` 的 ConfigNode 解析/写盘安全网、`02-core-integration.md` 的注册表读取契约。

---

## §1 目标与非目标

### 1.1 目标

| ID | 目标 | 验收方式 |
| --- | --- | --- |
| M-01 | 为**常见社区 mod** 提供表单化的配置界面（首批：KSP Community Fixes、Kerbalism、TUFX、Scatterer、B9PartSwitch、KSP Performance Profiler） | 6 个 adapter 的 fixtures 全部往返一致 |
| M-02 | **模块化到"加一个目录就加一个 mod"**：不写代码、不重编译即可新增支持 | 在用户目录放一个 adapter 目录，重启 UI 即出现该 mod 的配置卡 |
| M-03 | 写入**保字、可预览、可回滚**（复用 E1 的原子写 + 快照 + 自校验） | 写盘前后 diff 只含用户改动的那几行 |
| M-04 | 无 adapter 的 mod **不会变成"不可见"**：提供只读原始视图 + 一键导出 adapter 骨架 | 任意 `.cfg` 都能只读查看 |
| M-05 | 与 CKAN 注册表联动：知道 mod 是否安装、装的是哪个版本、adapter 是否仍在支持区间 | 版本越界时**强制只读**并说明原因 |

### 1.2 非目标

- **不做通用 `.cfg` 编辑器**（见 §2.2 的实测理由：GameData 里有 **3,598 个** ModuleManager 补丁文件，乱改必炸）。
- 不做 ModuleManager 补丁的生成/求值（那是 MM 的领域；我们最多**检测**用户是否已经在用 MM 覆盖）。
- 不改 Core（D5），不碰 `registry.json` 的模块内容（只读）。
- 不做游戏内实时调参（KSP 里那些 mod 自带设置窗口，重复造轮子无意义）。

---

## §2 现状取证（本机真实安装，只读）

### 2.1 KSP Community Fixes：典型的"设置文件"

`D:\Kerbal Space Program_newmod\GameData\KSPCommunityFixes\Settings.cfg`（29,850 字节 / 618 行）：

- 根节点 `KSP_COMMUNITY_FIXES`，其下 **138 条** `Key = value`，**2 空格缩进**（无 Tab）。
- 值分布：`true` **118**、`false` **17**、`en-us` 1（语言）、数值 2（如 `2.5`、`20`）。
- **325 行 `//` 注释 + 144 行空行** —— 注释就是每个补丁的**人类可读说明**（例如 `// Fix the whole kerbal inventory persistence system being inactive in 1.12.2`），且用 `// ####...` 分节（`Major bugfixes` 等）。
- 目录里同时存在 `Settings.cfg` 与 `settings.cfg` 两个名字 → **Windows 下是同一文件**，adapter 匹配必须**大小写不敏感**。
- 官方推荐改法是 ModuleManager patch（`Extras/KSPCF_UserSettings.cfg.extra` 给了示例）——这与"直接改 `Settings.cfg`"是**两条互相覆盖的路径**，产品必须说清（§7 Q2）。

### 2.2 GameData 的两类 `.cfg` 必须分开对待

| 类别 | 数量/证据 | 处理策略 |
| --- | --- | --- |
| **设置文件**（纯 ConfigNode，用户可改） | `KSPCommunityFixes/Settings.cfg`、`KerbalismConfig/Settings.cfg`、`KSPPerformanceProfiler/PluginData/window_layout.cfg` 等 | adapter 支持读写 + 表单 |
| **MM 补丁文件**（`@`/`+`/`%`/`!` 开头，由 ModuleManager 解释） | **3,598 个** `.cfg` 含该语法（如 `AJE/*.cfg`、`AdvancedPQSTools/Patches/*.cfg`） | **只读**，且明确标注"这是补丁，不是设置" |

> 这是本文档最重要的一个决定：**通用编辑器会把用户的游戏搞坏**。所以本域的设计中心是"adapter 白名单 + 只读兜底"，而不是"什么都能改"。

### 2.3 格式可复用性

设置文件是 **KSP ConfigNode** 语法，与 `.sfs` 存档**同族**（花括号嵌套 + `key = value` + `//` 注释）。`13-savegame-sfs.md` §5/§6 的解析器、词法令牌、保字写回、幂等/往返不变量**可直接复用**，差异只有三点：

1. `.cfg` 有 `//` 注释需要**挂到具体键上**（`.sfs` 实测 0 注释，未做此设计）；
2. `.cfg` 用空格缩进而非 Tab（不能假设）；
3. `.cfg` 根节点名是 mod 自有（`KSP_COMMUNITY_FIXES`），且可能多根节点/多文件。
4. 行尾与编码：`KSPCommunityFixes/Settings.cfg` 实测 **LF、无 BOM**（618 行、0 个 CR），与 `.sfs` 的 CRLF 相反 → **本域不得假设行尾**，必须保留原样（13 篇 L3 同一约束）。

### 2.4 CKAN 侧可复用的事实

- 已装 mod 与版本：`RegistryManager.Instance(inst, repoData)` → `registry.InstalledModules`（`Core/Registry/RegistryManager.cs:265` 入口；单文件 `registry.json`）。
- 文件归属：`registry.installed_files` 能告诉我们"这个 `.cfg` 是哪个 mod 装的"，用于"设置文件属于谁"的推断与**卸载前警告**。
- 下载/缓存与本域无关；不产生新的锁。

### 2.5 MemGraph 取证（2026-10-27 完成，第 7 个 adapter 就位）

用户按 §7 Q4 现场装了一份 **MemGraph**（CKAN 标识 `MemGraph`，版本 `1:1.2.0.1`，MIT，`ksp_version_min` 1.12.0），以下是真实文件取证结果——**这是本域唯一的"flat 形态"样本**，比 KSPCF 更有代表性。

**① 文件清单（`GameData/MemGraph/`）**

| 文件 | 大小 | 行数 | 形态 |
| --- | --- | --- | --- |
| `Plugins/PluginData/MemGraph/settings.cfg` | 227 B | 10 | **flat**：无根节点、无缩进、无注释，10 条 `key = value` |
| `Plugins/PluginData/MemGraph/padheap.cfg` | 201 B | 23 | **冒号表**：`<size>\t\t: <count>` ×22 + `total\t: 1024` |
| `Plugins/PluginData/MemGraph/test.cfg` | 20 B | 2 | flat：`size = 984` / `num = 20`（仅 Mod-KeypadDivide 的测试代码读） |

> **路径陷阱**：README 写 `MemGraph\PluginData\test.cfg`，实际在 `Plugins/PluginData/MemGraph/`；DLL 内有报错串「Incorrect MemGraph Installation: … All files should be located in KSP/GameData/MemGraph/Plugins」。→ adapter 必须**经 `registry.installed_files` 反查真实路径**，不能照抄文档路径（`install_time` 2026-10-27T10:00:57+08:00，16 个文件已登记）。

**② `settings.cfg` 全部 10 个键（逐字）**

| 键 | 实测值 | 类型 |
| --- | --- | --- |
| `visible` | `true` | `bool` |
| `applyPadding` | `false` | `bool` |
| `enableLogging` | `false` | `bool` |
| `keyToggleWindow` | `KeypadMultiply` | `keybinding` |
| `keyScaleUp` | `KeypadPlus` | `keybinding` |
| `keyScaleDown` | `KeypadMinus` | `keybinding` |
| `keyRunTests` | `KeypadDivide` | `keybinding` |
| `keyPadHeap` | `End` | `keybinding` |
| `keyMark` | `Home` | `keybinding` |
| `keyToggleLogging` | `PageUp` | `keybinding` |

- 分隔符恒为 ` = `（空格等号空格），行尾 **LF**、无 BOM、末行即 `keyToggleLogging = PageUp\n`；全文件 **0 注释、0 缩进、0 空行**。
- DLL 字符串表里**恰好**是这 10 个键，并带两条容错：`Ignoring invalid key in settings: '…'`、`Ignoring invalid line in settings: '…'` → **未知键被忽略**（前向兼容）；另有 `Unrecognised KeyCode: …`（非法键位名可容忍）。
- 读 = `ReadAllLines`、写 = `WriteAllText` → **插件会整份重写此文件**；这是 §5 与 `CFG-05` 幂等性要求的来源（我方写完后若格式与插件 writer 不一致，用户下次退出游戏就被改回）。

**③ `padheap.cfg` 是第三种语法（不是 `key = value`）**

```
8\t\t: 1
16\t\t: 1
…
2032\t: 0
total\t: 1024
```

- 22 行固定尺寸（8,16,24,32,40,48,64,80,96,112,144,176,208,240,296,352,432,664,800,1008,1344,2032）+ 末行 `total`；**尺寸列只作说明**（README 明写这些尺寸 hardwired，程序只吃每行的 count），`total` 是注解而非字段。
- 上面代码块里的 `\t` 是**真实制表符**：2 位尺寸用 2 个 tab（`8\t\t: 1`）、4 位尺寸只用 1 个（`1008\t: 0`），即**靠 tab 对齐**、tab 数随列宽变化 → 又一个「不可假设格式」的铁证（与 13 篇 L6 同理）。
- 容错串：`Ignoring invalid line in padheap.cfg: '…'`、`Can't find padheap.cfg`；调试日志 `Pad(8): …`。
- 语义：count 之和决定 Mod-End 预先分配的堆垫片（默认约 1024 MB）→ **写错就是给游戏多要/少要几百 MB 内存**，收益低、风险高，故 v1 定为 **`ReadOnlyRaw`**（见 §5）。

**④ 版本来源**：`MemGraph.version` 是 AVC JSON（`VERSION` 1.2.0.1 / `KSP_VERSION` 1.12.2 / `KSP_VERSION_MIN` 1.12.0）；CKAN 侧版本为 `1:1.2.0.1`（epoch 前缀）。→ `modVersionRange` 用**CKAN 版本**（注册表现成，含 epoch），`MemGraph.version` 只作交叉校验。

**⑤ 结论**：MemGraph 的**可编辑面 = `settings.cfg` 的 10 个键**（3 个 `bool` + 7 个 `keybinding`），`padheap.cfg`/`test.cfg` 只读展示。这同时验证了 §3「目录即插件」的一个隐含假设——**同一 mod 的不同文件可以有不同形态**，因此 `files[]` 每项必须能独立声明 `layout` 与 `mode`（见 §4.1、§4.5）。

---

## §3 架构（三层 + 目录即插件）

```
adapters/                     ← 目录即插件（内置，随包发布）
  ksp-community-fixes/
    adapter.json              ← 元数据 + 匹配规则 + 字段 schema
    fixtures/                 ← 黄金测试文件（before.cfg / expected.json）
    presets/*.json            ← 可选预设
    README.md                 ← 人读说明
  kerbalism/  tufx/  scatterer/  b9partswitch/  kspprofiler/
%LocalAppData%\CKAN\ui\adapters/   ← 用户目录（优先于内置，可覆盖/草稿）
```

| 层 | 内容 | 依赖规则 |
| --- | --- | --- |
| `CkanUI.Core` | `ModConfigAdapter`、`ConfigField`、`FieldType`、`PresetSpec`、`AdapterMatch` 记录类型 | 不依赖 Avalonia、不依赖 HTTP |
| `CkanUI.Services` | `AdapterLoader`（内置 + 用户 + 导入）、`ModConfigDetector`（注册表 + 文件 + 版本区间）、`ConfigNodeDocument`（复用 E1 解析器）、`IAdapterWriter`（快照 + 原子写 + 自校验）、`AdapterDraftExporter` | 可依赖 `CKAN`；不可依赖 `Presentation` |
| `CkanUI.Presentation` | 表单渲染器、分组/搜索、diff 条、只读原始视图 | 只依赖 VM 与服务接口 |

**核心约束（架构测试）**：

1. `Presentation` **不得**直接读文件——所有读写经 `IConfigDocumentService`；
2. 写盘**只有一个入口** `IAdapterWriter.WriteAsync(adapter, changes)`，内部做"校验 → 快照 → 原子替换 → 重新解析自校验"，与 E1 的 `ISaveWriter` 同一套安全网；
3. adapter 的 JSON **必须在加载时做 schema 校验**，校验失败即"该 adapter 不启用"（不是崩溃、不是半可用）。

---

## §4 Adapter 描述格式（这是"方便添加"的关键）

### 4.1 `adapter.json` 结构

```jsonc
{
  "schemaVersion": 1,
  "id": "ksp-community-fixes",
  "name": "KSP Community Fixes",
  "homepage": "https://github.com/KSPModdingLibs/KSPCommunityFixes",
  "docUrl": "https://github.com/KSPModdingLibs/KSPCommunityFixes#readme",

  // 何时启用：CKAN 标识（用于显示已装版本）+ 文件探测（用于手工安装的场景）
  "match": {
    "ckanIdentifiers": ["KSPCommunityFixes"],
    "paths": ["GameData/KSPCommunityFixes/Settings.cfg"],
    "caseInsensitive": true
  },

  // adapter 自身支持哪些 mod 版本；超出则只读
  "modVersionRange": [">=1.30", "<2.0"],

  // 要处理的文件（可多个）
  "files": [
    {
      "path": "GameData/KSPCommunityFixes/Settings.cfg",
      "layout": "node",                      // node（有根节点+缩进）| flat（无根节点、无缩进、无注释，见 §4.5 MemGraph）
      "rootNode": "KSP_COMMUNITY_FIXES",     // layout=node 时必填；layout=flat 时省略
      "mode": "editable",                    // editable | readonly（readonly 只展示 + 校验，如 padheap.cfg）
      "onMissing": "create-from-template",   // create-from-template | error | readonly
      "template": "fixtures/settings.default.cfg"
    }
  ],

  // 字段：这里用"从注释提取说明"来把 325 行注释直接变成 UI 文案
  "descriptionFromComment": true,
  "groups": { "fromCommentHeadings": true },

  "fields": [
    { "file": 0, "key": "RefundingOnRecovery", "type": "bool", "default": true,
      "risk": "low", "requiresRestart": true },
    { "file": 0, "key": "Language", "type": "enum",
      "values": ["en-us", "de", "fr", "ja", "ru", "zh-cn"], "default": "en-us" },
    { "file": 0, "key": "MaxPhysicsDelta", "type": "float", "min": 0.01, "max": 0.2,
      "unit": "s", "risk": "medium", "requiresRestart": true }
  ],

  "unlisted": "preserve"   // 未声明的键：preserve（默认）| hide | mark-unknown
}
```

### 4.2 字段类型（覆盖实测样本）

| 类型 | 渲染 | 校验 | 实测来源 |
| --- | --- | --- | --- |
| `bool` | 开关 | — | KSPCF 135 条 |
| `int` / `float` | 数字框 + 步进 | `min`/`max`/`unit` | KSPCF 数值项 |
| `enum` | 下拉 | `values` 白名单 | KSPCF `Language`（`en-us`） |
| `string` | 单行文本 | 可选正则 | 各 mod |
| `path` | 文件/目录选择 + 校验存在性 | — | Scatterer/TUFX 类 |
| `color` | 颜色选择 → 写回 `r,g,b,a` | 0–1 或 0–255（按 `scale`） | TUFX/Scatterer |
| `keybinding` | 按键捕获 → `KeyCode` 名 | 必须是 Unity `KeyCode` 成员 | 各 mod 快捷键 |
| `list` | 增删行的字符串列表 | — | 白名单类配置 |
| `readonly` | 灰显 + 原因 | — | 版本越界/临时项 |

### 4.3 版本区间与"越界即只读"

- `modVersionRange` 用 `[min, max)` 语义（复用 Core 的 `ModuleVersion` 比较，不自己写）；
- 已装版本超出区间 → **强制只读**，顶部横幅："此 mod 版本 1.31 超出适配器支持范围（≥1.30, <2.0），为避免写坏配置已禁用编辑；请更新适配器或原始视图查看"。
- 这种降级是**默认行为**，不需要用户确认——安全优先。

### 4.4 信任模型（三层来源）

| 来源 | 优先级 | 校验 |
| --- | --- | --- |
| 内置（随包） | 低 | 构建时 CI 跑 fixtures |
| 用户目录 `%LocalAppData%\CKAN\ui\adapters` | **高**（可覆盖内置） | 加载时 JSON schema 校验；不执行任何代码（纯声明，无脚本） |
| 远程导入（用户手动下载 zip 后导入） | 中（导入到用户目录） | 显示来源 URL 与哈希；**不做自动更新检查**（D9：零遥测） |

> **这是本域最关键的安全设计**：adapter 是**纯声明式 JSON**，不含可执行代码。因此"加一个 mod 支持"不扩大攻击面——最坏情况是写错文件，而写盘有快照与自校验兜底。

---

### 4.5 示例：MemGraph（flat 形态 + 同 mod 混形态）

`adapters/memgraph/adapter.json`（字段全部来自 §2.5 实测，无一项推测）：

```jsonc
{
  "schemaVersion": 1,
  "id": "memgraph",
  "name": "MemGraph",
  "homepage": "https://github.com/linuxgurugamer/MemGraph",

  "match": {
    "ckanIdentifiers": ["MemGraph"],
    "paths": ["GameData/MemGraph/Plugins/PluginData/MemGraph/settings.cfg"],
    "resolveByInstalledFiles": true      // 见 §2.5 路径陷阱：优先用注册表 installed_files 反查
  },

  "modVersionRange": [">=1.2.0", "<2.0"],

  "files": [
    {
      "id": "settings",
      "path": "GameData/MemGraph/Plugins/PluginData/MemGraph/settings.cfg",
      "layout": "flat",
      "mode": "editable",
      "onMissing": "create-from-template",
      "template": "fixtures/settings.default.cfg",
      "lineEnding": "preserve",          // 实测 LF；插件用 WriteAllText 重写，必须与之逐字节一致
      "separator": " = "
    },
    {
      "id": "padheap",
      "path": "GameData/MemGraph/Plugins/PluginData/MemGraph/padheap.cfg",
      "layout": "custom",                // 第三种语法：<size>\t\t: <count> + total
      "mode": "readonly",
      "readonlyReason": "该文件控制堆垫片总量（默认约 1024 MB），写错会直接影响游戏内存；v1 只读展示"
    },
    {
      "id": "test",
      "path": "GameData/MemGraph/Plugins/PluginData/MemGraph/test.cfg",
      "layout": "flat",
      "mode": "readonly",
      "readonlyReason": "仅供 Mod-KeypadDivide 的开发者测试代码读取"
    }
  ],

  "fields": [
    { "file": "settings", "key": "visible",          "type": "bool", "default": true,
      "label": "显示内存图表窗口", "requiresRestart": false },
    { "file": "settings", "key": "applyPadding",     "type": "bool", "default": false,
      "label": "启动即应用堆垫片", "risk": "medium", "requiresRestart": true },
    { "file": "settings", "key": "enableLogging",    "type": "bool", "default": false,
      "label": "启用内存日志", "risk": "low", "requiresRestart": false },
    { "file": "settings", "key": "keyToggleWindow",  "type": "keybinding", "default": "KeypadMultiply",
      "label": "开/关窗口" },
    { "file": "settings", "key": "keyScaleUp",       "type": "keybinding", "default": "KeypadPlus",
      "label": "增大纵轴比例" },
    { "file": "settings", "key": "keyScaleDown",     "type": "keybinding", "default": "KeypadMinus",
      "label": "减小纵轴比例" },
    { "file": "settings", "key": "keyRunTests",      "type": "keybinding", "default": "KeypadDivide",
      "label": "运行 test.cfg 测试" },
    { "file": "settings", "key": "keyPadHeap",       "type": "keybinding", "default": "End",
      "label": "立即分配堆垫片", "risk": "medium" },
    { "file": "settings", "key": "keyMark",          "type": "keybinding", "default": "Home",
      "label": "在日志写标记" },
    { "file": "settings", "key": "keyToggleLogging", "type": "keybinding", "default": "PageUp",
      "label": "开/关日志记录" }
  ],

  "unlisted": "preserve"                 // 实测插件对未知键"忽略但不报错"，我方更不能删
}
```

要点：

1. **`layout` 三值**：`node`（KSPCF 这类）/ `flat`（MemGraph settings.cfg）/ `custom`（padheap 这类私有语法，仅只读）。加值时不需要改代码，只需 adapter 声明 + 对应 reader；
2. **`mode` 与 `layout` 正交**：`padheap.cfg` 是 flat 邻近的 `custom` 且只读，`test.cfg` 是 flat 也只读，只有 `settings.cfg` 可编辑；
3. **`resolveByInstalledFiles`** 解决"文档路径与实际路径不一致"（§2.5 路径陷阱）：先按 `paths` 探测，命中失败再查 `registry.installed_files`；
4. **零推测**：上面 10 个字段的键名与默认值全部来自本机文件与 DLL 字符串表；README 未提到的键（`keyScaleUp` 等）也是从文件里读出来的。

---

## §5 检测与状态机

```
                ┌─────────────┐
   mod 未安装 → │  NotInstalled │ 不显示配置卡（或显示"未安装"灰卡）
                └─────────────┘
   已装 + 有 adapter + 版本在区间 + 文件可读 → Editable
   已装 + 有 adapter + 版本越界           → ReadOnlyVersion
   已装 + 有 adapter + 文件缺失           → CreatableByTemplate（可一键生成默认配置）
   已装 + 无 adapter                       → ReadOnlyRaw（原始只读 + 导出 adapter 骨架）
   KSP 正在运行                            → 顶部警示 + 写盘需二次确认（§7 Q1）
```

- 已装判定 = **CKAN 注册表命中** OR **`paths` 文件存在**（后者覆盖"手工装的 mod"）；
- 状态机是**纯函数**（输入：注册表快照 + 文件探测结果 + adapter），因此可 headless 单测（`07` 篇风格）。
- **文件级只读**：`mode: "readonly"` 的文件不进编辑面（MemGraph 的 `padheap.cfg`/`test.cfg` 即此例）。理由分两类——语义危险（`padheap` 直接决定几百 MB 堆垫片）与用途特殊（`test.cfg` 只服务开发者测试）；只读视图仍要做**解析校验**，坏行要标红（插件本身也只是"忽略无效行"）。

---

## §6 UI 设计

### 6.1 页面形态

左侧 **Mod 配置列表**（卡片：图标、名字、已装版本、状态徽标、`N 项已改`），右侧 **表单 + 底部 diff 条**。

```
┌ Mod 配置 ──────────────┬ KSP Community Fixes   v1.31.0   [已安装] ────────┐
│ ▸ KSP Community Fixes  │ 🔍 搜索 138 项…        [全部展开] [仅看已改]      │
│   Kerbalism            │ ── Major bugfixes ───────────────────────────────│
│   TUFX                 │  RefundingOnRecovery          [ ●] on            │
│   Scatterer            │    // Vessel recovery funds properly account…    │
│   B9PartSwitch         │  KerbalInventoryPersistence   [ ●] on            │
│   KSP Perf Profiler    │ ── Minor bugfixes ───────────────────────────────│
│ ──────────────────     │  …                                               │
│ ⊘ 3 个 mod 无适配器    │ ─────────────────────────────────────────────────│
│                        │ 改动 2 项 ▸ [查看差异]        [放弃] [写入(2)]    │
└────────────────────────┴──────────────────────────────────────────────────┘
```

### 6.2 关键交互

- **注释即说明**：`descriptionFromComment: true` 时，键上方灰字显示该键前最近的注释块（多行注释合并）。这直接把 KSPCF 的 325 行注释变成 138 条帮助文本，**零人工成本**。
- **分组**：从 `// ####` 分节注释生成折叠组（`fromCommentHeadings`）。
- **风险徽标**：`risk: medium/high` 的项显示黄色小三角，hover 显示"可能影响存档/性能"。
- **搜索**：按键名与说明文本双路匹配。
- **"仅看已改"**：一键过滤出与磁盘上不同的项。
- **diff 条**：写盘前必看；显示 `键 旧值 → 新值`，可逐项放弃。
- **写入后**：提示"已写入，`requiresRestart` 的 3 项需重启 KSP 生效"，并提供"打开日志"（联动 E2）。
- **只读兜底**：无 adapter 的 mod 点开是**原始 ConfigNode 树 + 文本**（只读），底部有醒目按钮 **"此 mod 没有适配器 → 导出适配器骨架"**。

### 6.3 "导出适配器骨架"（贡献闭环）

点一下，`AdapterDraftExporter` 产出到 `%LocalAppData%\CKAN\ui\adapters\draft-<id>\`：

- `adapter.json`（字段**全部推断为 `bool`/`string`，`id`/`match`/`files` 已自动填好，键按出现顺序列出，注释已抓成 description）；
- `fixtures/before.cfg`（当前文件副本，脱敏路径）；
- `README.md`（说明怎么改 `type`、怎么提交）。

用户改完丢回仓库即成一个 adapter——**这就是"方便添加"的完整闭环**（发现 → 生成骨架 → 人工标注类型 → fixtures 固化 → CI 验收）。

---

## §7 决策（2026-10-27 已拍板）

> **决策**：Q1 = **(b) 警示 + 二次确认后写**（写前必须显示"需要重启 KSP 才生效"）；Q2 = **(c) 默认改文件 + 提供"导出为 MM patch"按钮**（检测到 `Extras/KSPCF_UserSettings.cfg.extra` 已启用时横幅提示会被覆盖）；Q3 = **(a) 首批 6 个 adapter（24h）**；Q4 = **(a) 用户现场装一份 MemGraph 供取证** → `CFG-08b` 的 4h **由"不计入"改为计入**，本节合计 170h → **174h**。

（下表的"我的倾向"即最终决策依据。）

| # | 问题 | 选项 | 我的倾向 |
| --- | --- | --- | --- |
| Q1 | KSP 正在运行时是否允许写配置？ | (a) 完全禁止（b) 警示 + 二次确认后写（c) 写到临时文件由用户手动替换 | **(b)**：KSP 多数插件在启动时读一次，写入不会立刻生效但也不会坏；禁止反而让"改了没生效"变成谜。但写前必须显示"需要重启 KSP 才生效" |
| Q2 | KSPCF 官方推荐用 ModuleManager patch 覆盖，我们要不要**生成 MM patch** 而不是改 `Settings.cfg`？ | (a) 只改 `Settings.cfg`（b) 只生成 MM patch（c) 默认改文件，提供"导出为 MM patch"按钮 | **(c)**：这既尊重官方推荐路径，又不把用户推进 MM 语法里；若检测到 `Extras/KSPCF_UserSettings.cfg.extra` 已被启用，则横幅提示"你的 MM patch 会覆盖这些改动" |
| Q3 | 首批 adapter 就做这 6 个，还是先只做 KSPCF + Kerbalism 再观察？ | (a) 6 个（24h）(b) 2 个（8h） | **(a)**：6 个才能验证 schema 表达力（颜色/路径/枚举/数值各一个来源），否则 schema 容易设计偏 |
| Q4 | **MemGraph** 本机未安装（§2.5），怎么办？ | (a) 用户装一份供取证（最快，10 分钟）(b) 先交 6 个 adapter，MemGraph 留 `pending-fixture` (c) 让我按公开资料盲写（**不推荐**：键名/默认值错了会写坏配置） | **(a) 优先，(b) 可接受**：adapter 的价值全在"键名与默认值准确"，盲写等于给自己埋 R-35 |

---

## §8 WBS（174h）

| 任务 | 内容 | 工时 |
| --- | --- | --- |
| CFG-01 | ConfigNode 复用与差异适配（注释挂载、空格缩进、多根节点；复用 13 篇解析器） | 16h |
| CFG-02 | Adapter 描述模型 + 加载器（内置/用户/导入）+ JSON schema 校验 | 14h |
| CFG-03 | 状态机检测（注册表 + 文件探测 + 版本区间）纯函数 + 单测 | 10h |
| CFG-04 | 表单渲染器（9 种字段类型 + `layout: node/flat` 两种读写分支 + 分组 + 注释即说明 + 搜索 + 仅看已改） | 20h |
| CFG-05 | 差异与写盘（快照、原子写、重新解析自校验、KSP 运行检测、回滚） | 16h |
| CFG-06 | 预设（presets）与导入/导出 | 12h |
| CFG-07 | 原始只读视图 + `AdapterDraftExporter`（骨架导出） | 12h |
| CFG-08 | 首批 6 个 adapter（KSPCF / Kerbalism / TUFX / Scatterer / B9PartSwitch / Profiler，各 4h，含 fixtures） | 24h |
| CFG-08b | MemGraph adapter（**§2.5 取证已完成 2026-10-27**：10 键 flat 可编辑 + 2 只读文件；4h，含 fixture；**已计入合计**） | 4h |
| UI-CFG-01 | 页面（左卡片列表 + 右表单 + 底部 diff 条 + 状态横幅） | 16h |
| VM-CFG | `ModConfigListViewModel` / `ModConfigEditorViewModel`（2 个） | 8h |
| TST-CFG | fixtures 往返黄金测试 + schema 校验 + 权限/并发 + 只读降级 | 14h |
| DOC-CFG | adapter 作者指南（含"如何从骨架做到合并"） | 8h |
| **合计** | | **174h ≈ 22 人日 ≈ 4.4 周净**（含 CFG-08b 4h） |

### 里程碑 M9（W36–W40，5 周）

| 周 | 内容 | 出口 |
| --- | --- | --- |
| W36 | CFG-01/02/03（解析复用 + adapter 加载 + 状态机） | 能识别 6 个 mod 并显示状态 |
| W37 | CFG-04（表单渲染器） | KSPCF 138 项可用表单展示，注释变说明 |
| W38 | CFG-05/07（写盘安全网 + 只读视图 + 骨架导出） | 写盘 diff 精确、能回滚；任意 mod 可只读看 |
| W39 | CFG-06/08 + UI-CFG-01/VM-CFG（预设 + 首批 adapter + 页面） | 6 个 adapter 全绿 |
| W40 | TST-CFG + DOC-CFG + 打磨 | M9 收口 |

---

## §9 测试计划（接 `07-testing.md`）

| ID | 用例 | 判据 |
| --- | --- | --- |
| T-CFG-01 | 6 个 adapter 的 fixtures：解析 → 改 3 项 → 序列化 | 只改动目标 3 行，其余**逐字节不变**（含注释与空行） |
| T-CFG-02 | 幂等：未改动时 `write(read(x)) == x` | 字节相等 |
| T-CFG-03 | 未知键处理（`unlisted: preserve`） | 文件里多出的键在写回后仍在原位置 |
| T-CFG-04 | 版本越界 | 强制只读 + 横幅文案含实际版本与区间 |
| T-CFG-05 | 文件缺失 + `create-from-template` | 生成文件 == template（归一化后） |
| T-CFG-06 | adapter JSON 损坏/缺字段 | 该 adapter 不启用，其它不受影响，有明确错误 |
| T-CFG-07 | 写盘中 `kill -9` | 原文件完好或完整新文件（原子写），快照可回滚 |
| T-CFG-08 | KSP 运行中写入（Q1=(b)） | 二次确认后写入成功；未确认则不写 |
| T-CFG-09 | 大小写不敏感匹配（`Settings.cfg` vs `settings.cfg`） | 只识别为一个文件，不重复显示 |
| T-CFG-10 | 只读视图打开 3,598 个 MM 补丁中的任意一个 | 明确标注"这是补丁文件，不是设置"，不提供写入口 |
| T-CFG-11 | 骨架导出 | 产物可直接加载为 adapter（自校验通过） |

**手工组 H-9**：①KSPCF 关掉 3 个补丁 → 启动 KSP 确认日志里对应补丁不再加载 ②Kerbalism 改一项 → 游戏内确认生效 ③用 MM patch 覆盖后我们的 UI 给出横幅提示 ④中文路径下的 mod 目录。

---

## §10 风险（接 `08-risk-register.md`）

| ID | 风险 | 等级 | 缓解 |
| --- | --- | --- | --- |
| R-35 | **改坏配置**导致 mod 异常或存档受损 | P0 | 快照 + diff 预览 + 原子写 + 写盘后重新解析自校验；高风险项（`risk: high`）二次确认 |
| R-36 | adapter 与 mod 新版本不匹配（键改名/删除） | P1 | `modVersionRange` + 越界强制只读；未知键 `preserve`；写前校验"声明的键是否都存在"，缺失则只读 |
| R-37 | 与 ModuleManager patch 互相覆盖 | P1 | 检测 `*.extra`/MM 补丁存在 → 横幅提示；Q2 的"导出为 MM patch"逃生口 |
| R-38 | 写入时机（KSP 运行中 / 插件读一次就不再看） | P1 | `requiresRestart` 标记 + 写后提示；Q1 的二次确认 |
| R-39 | 远程 adapter 供应链 | P2 | 纯声明式（无可执行代码）+ 导入时显示 URL/哈希 + 不做自动更新 |
| R-40 | adapter 数量膨胀后维护跟不上 | P2 | 版本区间 + 只读降级让"过时 adapter"安全退化；骨架导出降低贡献成本；`unlisted: preserve` 保证新键不丢 |

---

## §11 待验证（SP-23..SP-26，接 `10-spike-plan.md`）

| ID | 问题 | 时间盒 | 失败后果 |
| --- | --- | --- | --- |
| SP-23 | ConfigNode 保字往返（注释/空行/顺序/末尾换行）在 5 个真实 `.cfg` 上成立吗？ | 3h | 若不能 → 关闭"原地编辑"，改为"生成覆盖块 + 提示用户自行合并" |
| SP-24 | 写盘后 KSP 启动是否按新值生效（KSPCF + Kerbalism 各一组） | 2h | 若某些项不生效 → 全部标 `requiresRestart`,并在 UI 明示"需重启" |
| SP-25 | adapter schema 能否表达 6 个 mod 的真实配置（颜色/路径/按键/枚举） | 3h | 若不能 → 扩展字段类型或允许 `custom: raw` 逃生 |
| SP-26 | 骨架导出产出的 adapter 能否被加载器接受（自举） | 1h | 修导出器格式 |

---

## §12 对其它篇的修订点

1. `00-index.md`：文档地图加 15 篇；§2 加 E3；§3 加 **S15**；§4 加 **D10**；§5 加 M9、合计 **40 周 / 1460h**；§6 加**第 9 条"配置写入安全"**。
2. `01-architecture.md`：加 §12（E3 分层 + `adapters/` 目录约定 + 三条硬约束）。
3. `03-ui-spec.md`：加 §16（Mod 配置页 IA、字段类型渲染规范、骨架导出入口）。
4. `04-mvvm-and-models.md`：加 §12（2 个 VM + 约定：VM 不做版本判定、不直接读文件）。
5. `06-wbs-milestones.md`：加 §1.12（170h）、头部改 **1460h / 183 人日 / 36.5 周净 / 排期 40 周**、加 M9、加 W36–W40、资源表。
6. `07-testing.md`：加 §11（T-CFG-01..11 + H-9）。
7. `08-risk-register.md`：加 §9（R-35..R-40）。
8. `10-spike-plan.md`：加 §12（SP-23..SP-26）。
9. `12/13/14` 篇的"全量口径"改为 1460h / 40 周。
10. `doc/ui-refactor-feasibility.md` §11：改"15 份"。
11. （v1.1，E4 落盘后）各篇"全量口径"再改为 **1602h / 44 周**；E5 落盘后为 **1736h / 48 周**；E6 落盘后为 **1846h / 51 周**；E7 落盘后为 **1990h / 55 周**；E8 落盘后最终为 **2196h / 60 周**（[20 篇](20-appearance-personalization.md) 206h / M14）。

---

## §13 修订记录

| 版本 | 日期 | 变更 |
| --- | --- | --- |
| v1.0 | 2026-10-27 | 首版：adapter 声明式格式、9 种字段类型、注释即说明、状态机、骨架导出闭环、170h / M9 |
| v1.1 | 2026-10-27 | **MemGraph 取证完成**（用户现场装包）：新增 §2.5 实测（3 文件 / 10 键 / 3 种语法 / LF / 无根节点 / 插件 `WriteAllText` 重写 / `installed_files` 反查路径）；§2.3 补"行尾不得假设"；§4.1 `files[]` 增 `layout`(node/flat/custom) 与 `mode`(editable/readonly)；新增 §4.5 MemGraph 完整示例；§5 增"文件级只读"两类理由；§8 标题改 174h、`CFG-04` 补 flat 分支、`CFG-08b` 标已取证。工时不变（174h，flat 支持在 `CFG-04` 20h 内吸收） |
