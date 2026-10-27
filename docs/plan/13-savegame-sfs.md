# 13 存档工作台与 SFS 格式化工具

> 状态：**v1（计划与文档，未写代码）**。本文覆盖两块用户需求：①**存档管理与编辑（`.sfs`）** ②**SFS 格式化工具**。
> 与主计划的关系：主计划（00–12 篇，846→854h / 23 周）只做 **mod 管理**；本文是**新增能力域 E1**，独立里程碑 **M7**，不侵占主计划的工时与门禁。代码一行未写，等指令。

---

## 1. 目标与非目标

### 1.1 目标（用户可感知）

| 编号 | 能力 | 说明 |
|---|---|---|
| S-01 | 存档列表 | 扫 `<KSP>/saves/*`，列出每个存档的标题、模式、版本、UT、飞船数、资金/科研/声望、修改时间、体积 |
| S-02 | 只读查看 | 树形浏览 `GAME` 全文（`FLIGHTSTATE`/`SCENARIO`/`ROSTER`/`MESSAGESYSTEM`），大节点懒加载 |
| S-03 | 编辑 | 就地改键值（带类型校验）、重命名/删除节点、复制子树、增删节点 |
| S-04 | 格式化 | 一键规范化缩进/分隔符/空行，**不改变任何值文本**，预览 diff 后再落盘 |
| S-05 | 校验与修复 | 括号不平衡、重复键、非法 UTF-8、混合行尾、超深嵌套 → 报告并给出可修复项 |
| S-06 | 差异对比 | 两个存档 / 存档与其备份的按路径 diff（节点级折叠） |
| S-07 | 备份与回滚 | 写前自动快照、快照列表、一键回滚；保留 KSP 自带 `Backup/` 不动 |
| S-08 | CLI 工具 | `ckan-ui sfs format|validate|diff|backup` 可脱离 GUI 使用（CI/批量/脚本） |

### 1.2 非目标（明确不做）

- **不做**存档"作弊器"预设（改钱/改科技/解锁部件）——只提供通用编辑能力，不内置玩法捷径。
- **不做** `.principia_bin` / `.loadmeta` 之外的二进制副档编辑（实测 `saves/默认/` 下有 9 个 `*.principia_bin`）。
- **不做**飞船可视化（3D 预览、轨道图）——第一期只做数据视图。
- **不改** Core：`CKAN.Core` 至今没有任何 `.sfs` 代码（见 §2 取证），存档能力全部落在新 UI 仓库自己的库里。

---

## 2. 现状取证

### 2.1 CKAN 仓库里没有存档能力

```
$ grep -rn --include=*.cs -i "\.sfs\|savegame\|SaveFile" Core GUI ConsoleUI
GUI/Controls/EditModpack.cs:421:   var dlg = new SaveFileDialog()
GUI/Main/MainExport.cs:87:         var dlg = new SaveFileDialog()
```

两处都是"另存为"对话框，与 `.sfs` 无关。Core 里与存档沾边的只有三处，且都不解析内容：

| 位置 | 作用 |
|---|---|
| `Core/Games/KerbalSpaceProgram.cs:72` | 把 `saves` 列入"游戏自带目录"，mod 安装时不覆盖 |
| `Core/Games/KerbalSpaceProgram.cs:128-130` | 排除 `saves`、`saves/scenarios`、`saves/training` 三个前缀 |
| `Core/Types/TimeLog.cs:62` + `Core/GameInstance.cs:86` | `playtime.json` 记录运行时长（CKAN 自有文件，非存档） |

**结论**：存档解析器必须自研；这也意味着零兼容负担，但要自己承担格式细节的坑（§3）。

### 2.2 SFS 格式实测（本机真实存档，证据来自 `D:\Kerbal Space Program_newmod\saves\默认\`）

样本：`persistent.sfs` = 4,860,758 字节 / 270,706 行（其中末行以 CRLF 结束）；`quicksave.sfs` = 5,109,107 字节。

| 观测项 | 实测结果 | 对实现的约束 |
|---|---|---|
| 编码 | UTF-8，**无 BOM**（首字节 `G A M E`）；中文标题 `默认 (SANDBOX)` 为 UTF-8 三字节 | 读写 UTF-8 无 BOM；检测到 BOM 时警告并可选移除 |
| 行尾 | **CRLF**（`\r\n`），全文件一致；文件末尾 `}\r\n` 后无空行 | 默认输出 CRLF；混合行尾时按多数派统一并报告 |
| 缩进 | **1 个 `\t` 每层**（非空格）；最大深度 **8 层** | 规范缩进 = `\t`；深度由括号栈计算，不信任缩进 |
| 花括号 | **独占一行**，与所属节点名同缩进（`\tGAME` 的子节点 `\t\t{`？否：`{\r\n` 与节点名同级），实测 0 处 `name {` 同行 | 解析器仍需容忍同行写法；格式化器输出独占一行 |
| 键值分隔 | 全部 **` = `**（空格-等号-空格）：110,682/110,682 行 | 规范形式 `key = value` |
| 空值 | `linkURL = ` —— 等号后有 **1 个空格**再换行，共 **7,333** 处 | 空值输出为 `key = `（保留尾空格），不折叠为 `key =` |
| 注释 | 0 处 `//` 或 `#` | 解析器支持并保留；格式化器原样保留注释行 |
| 反斜杠 | 1 处：`TechTreeUrl = GameData\ModuleManager.TechTree`（第 99 行） | **禁止**把反斜杠当转义 |
| 引号 | 值中 0 处 `"`（本样本） | 不引入引号语义；编辑含 `"` 的值时按字面处理 |
| 值内制表符 | 0 处 | 值内出现 `\t` 时按字面保留 |
| 值内空格 | 大量（`envInfo =  - Environment Info - ...`、`versionFull = 1.12.5.3190 (WindowsPlayer x64)`） | 只裁剪首尾空格，内部不动 |
| 值内等号 | 有：`_modVersions = Scale_Redist=3.3.2|ModuleManager=4.2.3|...`（第 22 行，**2,771 字符**，全文件最长行） | **只按第一个 ` = ` 分割**，`=` 之后的全部属于值 |
| 数字 | `UT = 5005129.6190400701`（16 位有效数字）、`Seed = 412091690` | **禁止** double→string 往返：会丢精度并改变存档语义 |
| 布尔/枚举 | `modded = True`、`gameMode = SANDBOX`、`mode = FLIGHT` | 保持原字面（`True` 非 `true`） |
| 根节点 | 唯一根 `GAME` | 解析结果为单根树 |
| 根下子节点 | `RemovedROCs`、`CometNames`、`PARAMETERS`、`SCENARIO`×**31**、`FLIGHTSTATE`、`LoaderInfo`、`ROSTER`、`MESSAGESYSTEM` | 编辑器顶部按此分栏 |
| 兄弟同名节点 | 31 个节点都叫 `SCENARIO`，靠子键 `name = XxxScenario` 区分 | **禁止**用"节点名"做唯一键；路径键 = `name` 索引 + 出现序号 |
| 兄弟文件 | `persistent.loadmeta`（平铺键值、同语法、379 字节）、`Backup/persistent (2026_10_26_23_50_59).sfs`（KSP 自带 5 份滚动备份）、`*.principia_bin`、`Ships/`、`KCT_Settings.cfg` | 存档目录是"一个包"而不是一个文件（§7.2） |

`.loadmeta` 全文（实测，`persistent.loadmeta`）：`vesselCount / UT / gameMode / gameNull / kspVersion / funds / science / reputationPercent / ongoingContracts / missionCurrentScore / missionIsStarted / missionIsEnded / missionHistoryId / missionExpansionVersion / saveMD5 / lastWriteTime`。

**关键推论**：主菜单显示的"飞船数/资金/UT"读的是 `.loadmeta`，不是 `.sfs`。编辑 `.sfs` 后若不同步 `.loadmeta`，UI 会显示旧数据 → 这是本模块最容易漏的一致性点（§7.3）。

---

## 3. 格式与语义铁律（写代码前必须先定，实现时逐条写进单测）

| 编号 | 铁律 | 违反后果 |
|---|---|---|
| L1 | **保字不保义**：格式化只重写"缩进 + 分隔符 + 行尾"，值的字节原样保留 | 数字精度/路径/枚举一旦重排就改了存档语义 |
| L2 | **幂等**：`format(format(x)) == format(x)`（字节级） | 反复保存导致文件持续变化、diff 噪声、用户不敢用 |
| L3 | **往返一致**：`serialize(parse(x)) == x`（字节级）用于"未修改"路径 | 打开即改会毁掉用户的存档 |
| L4 | **不猜类型**：解析层只有字符串值；`float/int/bool` 只在上层校验时投影 | 写回时精度丢失 |
| L5 | **重复键不合并**：同一节点内重复键原样保留（顺序不变），只警告 | SFS 合法但语义依赖顺序，合并会改语义（**待 SP-16 验证 KSP 取值规则**） |
| L6 | **不信任缩进**：结构由 `{`/`}` 栈决定；缩进只用于人工可读与格式化输出 | 缩进错乱的真实存档仍能正确解析 |
| L7 | **值内不转义**：`\`、`"`、`=`、`\t`、空格均为字面 | 反斜杠被当转义会破坏 Windows 路径 |
| L8 | **原子写**：临时文件 + `File.Replace`，失败则原文件不变 | 半写文件 = 存档报废 |
| L9 | **写前快照**：任何落盘前先在 `Backup/` 同级生成 `<name>.pre-edit-<ts>.sfs` | 用户误改无退路 |
| L10 | **时戳策略显式**：默认保留原 `LastWriteTime`（否则存档在主菜单里"跳到最新"） | 存档排序被污染 |

---

## 4. 架构

```
新仓库（UI）
├─ CkanUI.Sfs/                    ← 纯库，无 UI 依赖，可被 CLI 复用
│   ├─ SfsLexer.cs                行导向分词：Indent / Key / Sep / Value / Comment / Brace / Blank
│   ├─ SfsParser.cs               括号栈 → SfsNode 树，保留 Trivia
│   ├─ SfsDocument.cs             树 + 原始字节 + 行索引（原始 ↔ 树 双向定位）
│   ├─ SfsWriter.cs               保字序列化（Canonical / AsIs 两种模式）
│   ├─ SfsFormatter.cs            缩进/分隔符/空行规范化（L1/L2）
│   ├─ SfsValidator.cs            括号平衡/重复键/编码/行尾/深度/超长行
│   ├─ SfsDiff.cs                 按节点路径的 LCS diff（节点级折叠）
│   └─ Semantics/                 GAME/FLIGHTSTATE/VESSEL/PART/SCENARIO 类型化投影（只读视图）
├─ CkanUI.Sfs.Cli/                `sfs format|validate|diff|backup|stats`
└─ CkanUI.App/
    ├─ Services/Saves/SaveIndex.cs        扫 saves/*、读 .loadmeta、算体积/时戳
    ├─ Services/Saves/SaveWriter.cs       快照 + 原子写 + .loadmeta 同步 + 进程检测
    └─ Presentation/Saves/                列表页、树编辑页、原始文本页、diff 页、格式预览
```

依赖规则沿用 01 篇：`Sfs` 库不得引用 Core（它是纯文本工具），`Services` 层才引用 Core 的 `IUser`/日志/事务。

---

## 5. 解析器设计

- **令牌模型**（每行一个令牌，值不拆）：
  `Blank | Comment(text) | NodeOpen(name) | NodeClose | KeyValue(key, sepRaw, valueRaw, trailingSpace)`
  `sepRaw` 保存原始的 ` = ` / `=` / `  =  ` 以支持 AsIs 模式；`valueRaw` 保存 `=` 之后（含前导空格？不含，单独记 `trailingSpace` 标志）。
- **节点**：`SfsNode { Name, HeaderTrivia, Children[], Pairs[], FooterTrivia }`；`Pairs` 保持插入顺序（L5）。
- **行索引**：每个令牌记录 `byteOffset`/`lineNo`，供编辑后就地重写单行（避免整树序列化）。
- **内存**（实测 4.86MB / 27 万行）：字符串池化（key 名去重，实测重复率极高），目标 **≤ 3× 文件大小**；1 个存档的树 ≈ 15–25MB，列表页只读 `.loadmeta`（379 字节/个）不加载全文。
- **性能目标**：解析 4.8MB ≤ 1.2s（单线程，`ReadOnlySpan<char>` 切行）；格式化 ≤ 2s；diff 两份 5MB ≤ 3s（节点级 LCS，先按路径哈希对齐再递归）。
- **容错**：单行无法解析（既非 `k = v` 也非 `{`/`}`）→ 记为 `OpaqueLine` 原样保留并在校验里报位置，绝不静默丢弃（真实存档里第三方 mod 可能写出怪行）。

---

## 6. 格式化器算法（本文的核心交付物）

### 6.1 输入输出

```
sfs format <file> [--write] [--eol=crlf|lf|auto] [--indent=tab|2|4]
                  [--blank=keep|collapse|strip] [--preserve-timestamp]
                  [--check] [--diff]
```

- 默认**不写盘**，只输出统计与 diff（`--check` 模式给 CI 用：有差异则退出码 1）。
- `--eol=auto`（默认）：若全文件 CRLF → CRLF；若混合 → 按多数派统一并在报告里列出不一致行号。
- `--indent=tab`（默认，与 KSP 实测一致）。
- `--blank=keep`（默认）：保留所有空行；`collapse`：连续空行折叠为 1；`strip`：删除空行（**非默认，因为会改变可读性布局**）。

### 6.2 规范形式

```
<depth × "\t">NAME
<depth × "\t">{
<depth+1 × "\t">key = value
<depth × "\t">}
```

- 空值：`key = `（等号后 1 个空格，`\r\n` 结尾）—— 与 KSP 实测一致（7,333 处）。
- 注释：`// ...` 按当前深度缩进，内容不动。
- 值：**一个字节都不动**（含首尾空格裁剪到"KSP 写出的形态"：实测值前无多余空格、后有 0 空格；`envInfo` 的 `  - Environment` 是值内容的一部分，不动）。

### 6.3 处理流程

```
读字节 → 检测 BOM/编码/行尾分布
      → 分词（逐行）
      → 括号栈建树 + 深度标注（不信任缩进）
      → 校验前置检查（不平衡则拒绝格式化，报位置）
      → 生成新文本（StringBuilder，容量预估 = 原长度 × 1.05）
      → 自校验：parse(新文本) 得到同构树（节点数/键值对/值哈希，深度对齐）  ← 拿到 L1 的形式化证明
      → 差异统计（缩进行数、分隔符规范化数、行尾统一数、空行变化数）
      → （--write）快照 → 原子替换 → 同步时戳
```

**自校验是本设计的关键**：写盘前把新文本重新解析，与旧树做"结构同构 + 值多重集相等"比较；不等则放弃写盘并报错。这样即使实现有 bug，也不会产出语义被改的存档。

### 6.4 校验项（`sfs validate`）

| ID | 检查 | 级别 |
|---|---|---|
| V-01 | 括号平衡（`{`/`}` 数量与嵌套） | 错误 |
| V-02 | 根节点唯一且为 `GAME` | 错误 |
| V-03 | 每行可分词（无 `OpaqueLine`） | 错误 |
| V-04 | 同节点内重复键 | 警告（列路径与次数，L5） |
| V-05 | 行尾混合 / BOM / UTF-8 非法字节 | 警告 |
| V-06 | 缩进与括号深度不一致的行 | 警告（可 `--fix`） |
| V-07 | 嵌套深度 > 16 / 单行 > 64KB | 警告（性能与解析风险） |
| V-08 | `SCENARIO` 缺 `name` 键 | 警告 |
| V-09 | `.loadmeta` 与 `.sfs` 不一致（`vesselCount` vs `FLIGHTSTATE` 内 `VESSEL` 数、`UT` vs `GAME.UT`） | 警告 + 一键修复（§7.3） |

---

## 7. 存档管理

### 7.1 索引（`SaveIndex`）

扫 `<GameDir>/saves/*`：
- 目录 = 存档槽；主文件 `persistent.sfs`；附加 `quicksave.sfs`、`*.loadmeta`、`Backup/`、`Ships/`、`*.principia_bin`、`KCT_Settings.cfg`。
- 列表字段取自 `.loadmeta`（379 字节，秒读）；无 `.loadmeta` 时降级：只读文件头 200 行取 `GAME` 直接子键（`Title/Mode/version/scene`），**不加载全文**。
- 排序默认按 `lastWriteTime`（`.loadmeta` 内字段），可切文件名/体积/飞船数。

### 7.2 危险操作与并发

| 场景 | 处理 |
|---|---|
| KSP 正在运行（进程 `KSP_x64.exe`） | 列表页顶部警示条；写盘前二次确认（KSP 退出时会覆盖存档，编辑必丢） |
| 文件被占用 | 捕获 `IOException` → 提示"可能被其他程序占用"，不重试 |
| 同目录被新旧 UI 同时操作 | 仅本程序内加锁（`<save>.ckanui.lock`），跨程序靠"存在他人的 `.pre-edit-*` 快照 + mtime 变化"提示 |
| 存档正在被格式化 | 页级忙碌态，禁止并发写同一文件 |

### 7.3 `.loadmeta` 同步（易漏点）

编辑落盘后按需重建 `.loadmeta`（键序与实测一致）：

```
vesselCount      = count(FLIGHTSTATE/VESSEL)
UT               = GAME.UT（原字面）
gameMode         = GAME.Mode
gameNull         = False
kspVersion       = GAME.version
funds/science/reputationPercent = GAME.Funding...（存在时）
lastWriteTime    = <新文件 mtime 的 .NET ticks>（实测 639286582044873794 = ticks）
saveMD5          = 空（KSP 自己留空，实测为空）
```

策略：默认**只同步被编辑影响的字段**（用户改了 `vesselCount` 相关的东西才动 `vesselCount`），并始终提供"不重建 .loadmeta"开关。**待 SP-17 验证**：KSP 是否在加载时校验 `saveMD5`（为空则跳过？）。

### 7.4 备份与回滚

- 写前快照：`<dir>/<name>.pre-edit-<yyyyMMdd-HHmmss>.sfs`（与我们自己的命名区分 KSP 的 `Backup/persistent (2026_10_26_23_50_59).sfs`）。
- 快照列表：按时间倒序，显示体积与 diff 摘要（改动行数），一键回滚（同样先快照当前）。
- 保留策略：默认保留最近 10 份，可配置。

---

## 8. 编辑器 UI（页面规格，落地到 03 篇 §14）

三视图同一个 `SfsDocument`：

| 视图 | 内容 | 关键交互 |
|---|---|---|
| **树视图**（默认） | `GAME` 分栏：`FLIGHTSTATE`（飞船树：`VESSEL` → `PART` → `MODULE`）、`SCENARIO`（31 个，按 `name`）、`ROSTER`（乘员）、`MESSAGESYSTEM`、其余。虚拟化树，节点懒展开 | 就地改值（按类型提示：数字/布尔/字符串）、新增/删除键、复制子树、右键"跳到原始行" |
| **原始文本** | 全文 + 行号 + 语法高亮（节点名/键/值/花括号/注释），只读或可编辑（可编辑时保存前必过 `validate`） | Ctrl+F 定位、`跳转到树中节点`、超大文件按需分块渲染（27 万行必须虚拟化） |
| **差异** | 编辑前后 / 两个存档 / 存档 vs 快照 | 节点级折叠、只显示变化、逐项"采用左/采用右" |

编辑保护（安全网，逐条列进 07 篇验收）：
- 任何写盘前：`validate` 必过（错误级） → 结构自校验 → 快照 → 原子写。
- "格式化"是**独立动作**：`Ctrl+Shift+F` 打开预览 diff，用户确认后才写（不做保存时静默格式化）。
- 编辑后标题栏显示 `●` 未保存；关闭拦截。
- 数字字段编辑：原字面高亮显示（`5005129.6190400701`），输入不做千分位/科学计数法转换。

---

## 9. WBS（E1 存档工作台，250h）

| ID | 任务 | 工时 | 依赖 | 验收 |
|---|---|---|---|---|
| SFS-01 | 分词器 + 解析器（容错、`OpaqueLine`、行索引） | 24 | — | 27 万行真实存档解析 ≤1.2s；树规模与实测一致（31 个 SCENARIO、110,682 键值对） |
| SFS-02 | 序列化器（AsIs 字节级往返） | 20 | SFS-01 | 未修改时 `serialize(parse(x))` 与原文件**逐字节相同**（10 个真实存档全过） |
| SFS-03 | 格式化器（缩进/分隔符/空行/EOL + 自校验） | 12 | SFS-02 | 幂等；格式化前后"值多重集"相等；`--check` 退出码正确 |
| SFS-04 | 内存与大文件策略（字符串池、分块读、行索引） | 10 | SFS-01 | 4.8MB 存档峰值内存 ≤ 3× 文件；列表页不加载全文 |
| SFS-05 | 语义层投影（GAME/FLIGHTSTATE/VESSEL/PART/SCENARIO/ROSTER） | 20 | SFS-01 | 能统计飞船数/部件数/乘员数，与 `.loadmeta` 的 `vesselCount` 一致 |
| SFS-06 | 存档索引（saves 扫描、`.loadmeta` 快读、降级路径） | 12 | SFS-05 | 50 个存档列表首屏 ≤300ms |
| SFS-07 | 写入层：快照 + 原子替换 + 时戳 + 进程检测 | 12 | SFS-02 | 断电模拟（kill）后原文件可用；时戳默认不变 |
| SFS-08 | 校验器 V-01..V-09 + 修复动作 | 10 | SFS-01 | 人工构造 9 类坏文件全部被识别并定位 |
| SFS-09 | 差异引擎（路径对齐、节点折叠、diff 视图数据） | 12 | SFS-01 | 两份 5MB 存档 diff ≤3s |
| SFS-10 | CLI（format/validate/diff/backup/stats） | 10 | SFS-03, SFS-08 | 退出码约定 + `--help` 完整；可进 CI |
| UI-SAVE-01 | 存档列表页（元信息列、排序、空态、KSP 运行警示） | 12 | SFS-06 | 与主菜单显示一致 |
| UI-SAVE-02 | 树视图 + 就地编辑（虚拟化、懒展开、类型提示） | 24 | SFS-05 | 27 万行树滚动 p99 ≤16.7ms；编辑 6 类值正确 |
| UI-SAVE-03 | 原始文本视图（虚拟化、高亮、行号、可选编辑） | 14 | SFS-01 | 5MB 文件打开 ≤1.5s，滚动不掉帧 |
| UI-SAVE-04 | 格式化预览与 diff 应用 | 8 | SFS-03, SFS-09 | 预览与最终落盘字节一致 |
| UI-SAVE-05 | 差异页（存档 vs 存档 / 存档 vs 快照） | 12 | SFS-09 | 三源对比可用 |
| UI-SAVE-06 | 安全网（快照、校验门、未保存拦截、锁、忙碌态） | 8 | SFS-07 | 07 篇 §9 全部通过 |
| VM-SAVE | 5 个 VM（见 04 篇 §10） | 8 | UI-SAVE-01..05 | headless 单测覆盖 |
| TST-SAVE | 往返/幂等/坏文件/大文件/真实存档集回归 | 16 | 全部 | 见 07 篇 §9 |
| DOC-SAVE | 格式规范 + CLI 手册 + 用户指南 | 6 | SFS-10 | 与实测表一致 |
| **合计** | | **250h** | | |

**可独立提前交付（84h ≈ 2.1 周）**：SFS-01/02/03/08/10 + TST-SAVE 的一半 = **格式化 CLI**。这是用户"需要编写一个格式化工具"的最小可用物，不需要 GUI，也不需要 Core。

---

## 10. 里程碑 M7（W24–W30，7 周）

| 里程碑 | 周次 | 任务 | 门禁 |
|---|---|---|---|
| **M7a** 格式化 CLI | W24–W25 | SFS-01/02/03/08/10 + TST-SAVE(半) | 10 个真实存档往返字节一致；幂等；9 类坏文件识别 |
| **M7b** 只读工作台 | W26–W27 | SFS-04/05/06 + UI-SAVE-01/03 | 列表 ≤300ms；文本视图 5MB ≤1.5s |
| **M7c** 编辑与安全 | W28–W29 | SFS-07 + UI-SAVE-02/06 + VM-SAVE | 编辑 6 类值 + 快照回滚 + 三次异常断电演练 |
| **M7d** 差异与收口 | W30 | SFS-09 + UI-SAVE-04/05 + DOC-SAVE | 三源 diff 可用；文档齐 |

**总量**：854h（主计划）+ 250h（E1）= **1104h ≈ 138 人日 ≈ 27.6 周净**，排期 **30 周**（M0–M6 = 23 周，M7 = 7 周）。若再加上 [14 篇](14-log-and-ai-debug.md) 的 E2 日志与 AI 排障（186h / 5 周）、[15 篇](15-mod-config-adapters.md) 的 E3 Mod 配置（174h / 5 周）、[16 篇](16-i18n.md) 的 E4 国际化（156h / 4 周）与 [17 篇](17-mod-metadata-i18n.md) 的 E5 元数据本地化（134h / 4 周）与 [18 篇](18-launch-resource-release.md) 的 E6 启动后资源释放（110h / 3 周），全量 = **2196h / 60 周**（含 E7 安装域 126h 净增 + E8 外观域 206h）。

---

## 11. 测试策略（落地到 07 篇 §9）

| 层 | 用例 |
|---|---|
| 单元（无 IO） | 分词器 20 例（含 `TechTreeUrl = GameData\ModuleManager.TechTree`、`_modVersions` 2,771 字符行、空值、同行花括号）；格式化 12 例；校验 9 例 |
| 往返/幂等（真实文件） | 取 `saves/` 下 10 个真实 `.sfs`（含 5.3MB 的 `快速存档.sfs`、中文名存档）：`parse→serialize` 字节一致；`format` 两次字节一致 |
| 语义等价 | 格式化前后：节点数、键值对数、每个 `(path,key)` 的值多重集相等；`UT`/`Seed`/`funds` 字面完全相同 |
| 性能 | 4.8MB 解析 ≤1.2s、格式化 ≤2s、diff ≤3s；5MB 文本视图首屏 ≤1.5s |
| 破坏性 | 截断文件、只删一个 `}`、BOM 注入、LF/CRLF 混合、超深嵌套 100 层、值里塞 `{`/`}` |
| 安全网 | kill 进程于写盘中途（用注入的写延迟）：原文件完好、快照存在 |

---

## 12. 风险

| ID | 风险 | 概率 | 影响 | 缓解 |
|---|---|---|---|---|
| R-23 | **格式语义误伤**：某 mod 依赖缩进/顺序/重复键的特殊行为 | 中 | 高 | L1/L2/L3 + 写盘前结构自校验；格式化默认不写盘；快照必留 |
| R-24 | **KSP 覆盖编辑**：用户在游戏运行时编辑，退出时被覆盖 | 高 | 高 | 进程检测 + 二次确认 + 页顶警示（§7.2） |
| R-25 | `.loadmeta` 不同步导致主菜单显示旧数据 | 中 | 中 | §7.3 同步 + V-09 校验 + 一键修复 |
| R-26 | 第三方 mod 的怪行（`OpaqueLine`）被格式化破坏 | 中 | 中 | 不识别即原样保留；格式化跳过含 `OpaqueLine` 的块并报位置 |
| R-27 | 大存档性能（27 万行 / 5MB）导致 UI 卡顿 | 中 | 中 | 列表不加载全文；文本视图虚拟化；行索引 |
| R-28 | 范围蔓延：从"格式化工具"长成"存档作弊器 + 3D 预览" | 中 | 中 | §1.2 非目标清单；新需求须改本文再排期 |

---

## 13. 待验证（进 10 篇 SP 表）

| ID | 问题 | 判据 | 耗时 |
|---|---|---|---|
| SP-15 | KSP 的 ConfigNode 解析器是否接受 `//` 注释、同行 `{`、空行、值尾空格 | 构造 6 个变体存档，KSP 能载入 | 1h |
| SP-16 | 同节点重复键时 KSP 取哪个（首个/末个） | 造 `funds = 1` + `funds = 2`，看载入值 | 1h |
| SP-17 | `.loadmeta` 的 `saveMD5` 为空是否被校验 | 手改 `funds` 不同步 `.loadmeta`，看主菜单 | 1h |
| SP-18 | 格式化后 KSP 是否照常载入（端到端） | 对 `quicksave.sfs` 格式化 → KSP 载入 → 飞船/资金/UT 不变 | 2h |

**SP-18 是本模块的 Go/No-Go**：若格式化后的存档 KSP 载入异常，则 CLI 只保留 `--check`（只读校验）形态，取消 `--write`。

---

## 14. 修订记录

| 日期 | 版本 | 变更 |
|---|---|---|
| 2026-10-26 | v1 | 新建。依据本机真实存档（`saves/默认/persistent.sfs` 4.86MB / 270,706 行）实测语法；给出格式化器保字/幂等/自校验算法、存档包一致性策略、250h / M7 7 周排期 |
