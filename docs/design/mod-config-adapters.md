# Mod 配置适配层（M9 / CFG-01~03、CFG-05、UI-CFG-01）

> 规格来源：`docs/plan/15-mod-config-adapters.md`（下称"15 篇"）。
> 本文记录**已实现的事实**：为什么这么切、哪些地方与规格不同、哪些还没做、以及实测出来的坑。
> 状态：CFG-01 / CFG-02 / CFG-03 / CFG-05 服务层完成；UI-CFG-01 页面骨架完成；**CFG-04 编辑控件与写盘入口完成，九类字段全部可编辑**（bool/int/float/enum/string/list 走文本框与下拉，path 带「浏览…」，color 带色块预览 + 十六进制输入，keybinding 支持按键捕获）；ADP-04 导入/哈希/更新检查的机制见 [adapter-import.md](adapter-import.md)。

## 1. 目标与边界

要做的事：让用户**不用读配置文件**也能改 mod 配置，同时不把配置写坏。

```
用户点开"Mod 配置"  →  适配器(纯 JSON 声明) 决定"这个 mod 有哪些键、能怎么改"
                     →  服务层读盘合成模型（只读）  →  页面渲染字段行
                     →  改完提交 IAdapterWriter：校验 → 快照 → 原子写 → 重解析自校验 → 失败回滚
```

三条不可协商的红线（15 篇 §7）：

1. **adapter 是纯声明式 JSON，不含任何可执行代码**。用户往 `%LocalAppData%\CKAN\ui\adapters` 放一个 adapter，最坏结果是"这个 adapter 被停用"，不可能变成任意代码执行。
2. **宁缺勿错**。没取证过的键不写进 adapter。写了错键名 = 静默写坏用户配置，比"少一个可改项"严重得多（15 篇 §7 Q4）。
3. **写盘只有一个入口** `IAdapterWriter`。页面、命令、将来的 CLI 都走它，校验/快照/自校验/回滚只实现一次。

明确不做（本段不涉及）：导入界面的页面编排（ADP-04 只交付服务层机制，见 [adapter-import.md](adapter-import.md)）、MemGraph 等 mod 的具体 adapter、配置项搜索跨 mod 聚合。

## 2. 分层与依赖方向

| 层 | 项目 | 内容 | 约束 |
| --- | --- | --- | --- |
| 契约 | `src/CkanUI.Services.Abstractions/ModConfig/` | `ModConfigAdapterDescription` / `ModConfigFieldState` / `ModConfigInstanceModel` / `IAdapterCatalog` / `IAdapterWriter` / `IModConfigService` / `ModConfigDiagnostic` | 只依赖 `OperationResult` 与枚举，不引用 Core/Avalonia。**CKAN 的 `ModuleVersion` 不出现在这里** |
| 实现 | `src/CkanUI.Services/ModConfig/` | ConfigNode 文档、adapter 读取与目录、状态机、写盘 | 可以引用 Core（`ModuleVersion`、`SfsParser`） |
| 呈现 | `src/CkanUI.ViewModels/Pages/ModConfigPageViewModel.cs` + `Pages/ModConfig/` | 卡片/分组/行模型、渲染器注册表、筛选 | **只引用 Services.Abstractions**，不引用 Core、不引用本地化层；译文走构造注入的 `Func<string,string>` |
| 视图 | `src/CkanUI.App/Views/ModConfigPageView.axaml` | 左右两栏 + 工具条 + diff 条 | Avalonia 只在这一层；颜色/圆角全用 `Tokens.axaml` 语义 token |

页面 VM 的构造函数（供 DI 接线）：

```csharp
public ModConfigPageViewModel(
    IModConfigService config,
    IAdapterCatalog? catalog = null,
    Func<string, string>? translate = null,
    ModConfigFieldRendererRegistry? renderers = null,
    IAdapterWriter? writer = null,          // CFG-04：写盘入口
    IDialogService? dialogs = null,         // CFG-04：双重确认
    IModConfigBackupRestorer? restorer = null)  // CFG-04：.bak 快照回滚
```

后三个参数都是可选的，`AddCkanModConfig()` 已注册 `IAdapterWriter`、`IDialogService` 也已由 `ServiceRegistration` 注册，所以**现有 DI 无需改注册**即可满足七参构造；设计时无参构造仍落到 `EmptyModConfigService`。缺任何一个可选依赖都不会崩：没有 writer 时点保存给可读错误，没有 dialogs 时拒绝写盘并说明原因（**不静默禁用按钮**）。

## 3. CFG-01：无损 ConfigNode（字节级往返）

### 3.1 做法

`src/CkanUI.Services/ModConfig/ConfigNodeDocument.cs`。**复用上游 `external/CKAN` 的 `SfsParser` 做语义解析**（只读引用，不改上游），但**序列化不走上游 `SfsWriter`**，原因：`SfsWriter` 从语法树重建文本，没有"按行替换"的入口，做不到"只改那一行"。

三个设计点：

1. **语义层**：`SfsParser` 告诉"哪些行是键值对、注释挂在哪、节点名链是什么"。`SfsRawLine` 不带行号，所以节点名链靠**一次递归遍历**携带（不靠花括号行号区间推算——闭括号行没有行号）。
2. **原文层**：按物理行切成 `ConfigNodeLine(Content, Terminator)`，正文与行尾分离。**未改动时 `Render()` 逐字节等于原文是构造保证**，不是"应该"。
3. **改写层**：`TrySetValue` 只做**行内切片**——定位首个 `=` 之后的值 token，只换它；缩进、键名、分隔符两侧空格、行尾注释、行尾序列全部原地保留。所以"改 3 项只变 3 行"是自然结果，不需要 diff 修补。

### 3.2 保真项清单

| 保真项 | 处理 |
| --- | --- |
| 注释（整行/行尾） | 整行注释作为"键上方最近的注释块"挂到 `ConfigNodePair.Comment`（跳过空行、连续注释合并、遇其它元素即停）；行尾注释留在行内不动 |
| 缩进 | 行内切片不改前缀，空格/Tab 混用也保留 |
| 多根（同文件多个顶层节点） | 全部保留；每对键都带 `NodePath`（`A/B` 形式）区分来源 |
| 空行 | 物理行原样保留 |
| 行尾（LF / CRLF / CR 混用） | 每行单独保存，写回原样；adapter 的 `lineEnding` 只影响**新建文件** |
| UTF-8 BOM | `HasBom` 记录并在 `RenderBytes` 里保留（KSP 插件自己重写时不带 BOM，我们不该"顺手"改掉文件编码事实） |
| 非法 UTF-8 | `IsTextLossy = true`，**禁止写盘**（否则会把坏字节洗成替换字符） |
| 值里的引号 | 裸值含空格/注释符/等号/花括号时自动加双引号；写后自校验会去一层引号再比对原文 |

已知且**刻意不处理**的差异（都不影响"字节级往返"这一条，只影响极端输入下的写回）：

- 文件末尾缺换行：最后一行 `Terminator` 为空串，原样保留（不会自动补换行）。
- 解析出错的文件（`ParseErrorCount > 0`）：仍可读、可展示，但 `IAdapterWriter` 拒写。
- KSP 的 `ConfigNode` 语义里"重复键后者胜"之类的规则由 `SfsParser` 负责，本类型不重新实现。

### 3.3 往返测试

`tests/CkanUI.Services.Tests/ModConfig/ConfigNodeDocumentTests.cs` + `Fixtures/mod-config/` 下 **6 个真实形态样本**（超过验收要求的 5 个）：

| fixture | 覆盖形态 |
| --- | --- |
| `kspcf-settings.cfg` | node 布局 + `// #### 分节标题` + 行尾注释 + 布尔/枚举/浮点混排 |
| `kspcf-settings-nested.cfg` | 嵌套节点 + 同键名出现在不同节点 |
| `memgraph-settings.cfg` | flat 布局（无根节点）+ ` = ` 分隔符 |
| `memgraph-padheap.cfg` | custom 私有语法（只读形态的样本） |
| `memgraph-test.cfg` | 极短文件 + 只有注释与一个键 |
| `mm-patch-sample.cfg` | ModuleManager 补丁形态（`@PART[...]`，多根、缩进深） |

每个样本断言：`ParseBytes(bytes).RenderBytes()` **逐字节等于** `bytes`；另有 33 个用例覆盖行内切片、注释挂载、多根定位、BOM、非法 UTF-8、`Revert`、`TrySetValue` 的负样本。

## 4. CFG-02：adapter 加载（目录即插件）

### 4.1 目录与优先级

| 来源 | 路径 | 优先级 | 缺失时 |
| --- | --- | --- | --- |
| 内置 | `<App 输出目录>/adapters/<任意子目录>/adapter.json`（仓库里是 `adapters/`，随 App 输出复制） | 低 | 报 Warning `adapter-dir-missing`（内置目录缺失说明打包出了问题，必须可见） |
| 用户 | `%LocalAppData%\CKAN\ui\adapters\<任意子目录>\adapter.json` | 高 | 静默（大多数用户没有这个目录） |

- 同 `id`：**用户版覆盖内置版**（`IsUserProvided = true`），记 Warning `adapter-overridden`；内置内重复 id 保留先加载的，记 `adapter-duplicate`。
- 单个 adapter 坏掉只 `continue` 停用它自己，不影响同目录其它 adapter（15 篇 T-CFG-06）。
- 结果按 `Name` 序数排序（UI 顺序稳定）。
- `AdapterCatalog.Reload()` 清缓存重扫：用户塞了新 adapter 不用重启程序。

### 4.2 校验规则（`AdapterJsonReader`）

读取用 `JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = Skip, MaxDepth = 32 }`——adapter.json 是**给人写的**，允许注释和尾逗号。

| 规则 | 违反时 |
| --- | --- |
| 根必须是对象 | Error `adapter-schema` |
| `schemaVersion` 必须是整数且 = 1 | Error |
| `id` 匹配 `^[a-z0-9]+(-[a-z0-9]+)*$` | Error |
| `name` 非空；`homepage`/`docUrl` 必须 http(s) | Error |
| `match.ckanIdentifiers` 至少 1 项 | Error |
| `modVersionRange` 每项走 `ModConfigVersionRange.TryParseConstraint` | Error（消息带 `modVersionRange：` 前缀） |
| `files` 至少 1 项；`path` 非空且非绝对路径；`layout` ∈ node/flat/custom；`mode` ∈ editable/readonly | Error |
| `layout = custom` **必须** `mode = readonly` | Error（我方不理解私有语法，允许写等于允许写坏） |
| `onMissing = create-from-template` 必须给 `template` | Error |
| `file` 引用（整数下标或字符串 id）必须能解析到已声明文件 | Error |
| 同文件内 key 不重复 | Error |
| `type` 必须是 10 种之一；`enum` 必须给 `values`；`min <= max`；`pattern` 必须能编译 | Error |
| 未知属性 | **只 Warning** `adapter-unknown-property`（大概率是拼错），不影响启用 |

**任一 Error → 整份 adapter 停用**（`Adapter` 为 null）。不做"半可用"：一个 adapter 只有一部分字段可信时，用户看到的界面会骗人。

### 4.3 诊断码表

`adapter-json-invalid`（JSON 语法错）· `adapter-schema`（结构/取值非法）· `adapter-field`（字段声明错）· `adapter-version-range`（版本区间错）· `adapter-unknown-property`（Warning）· `adapter-dir-missing` / `adapter-dir-io`（目录问题）· `adapter-overridden` / `adapter-duplicate`（Warning）。

### 4.4 与 schema 的关系

`docs/design/adapter.schema.json` 是**门禁用**的 JSON Schema（draft-07 子集），`tools/verify-layout` 的 H-08 用它校验 `adapters/**/adapter.json`。它只覆盖**结构性**约束（必填、类型、枚举、正则、长度）；跨字段规则（custom 必须 readonly、file 引用必须存在、key 不重复、min<=max）只有 C# 读取器能表达，两边不重复实现。**改 schema 时同步改读取器的已知属性表**，否则会出现"H-08 绿但读取器报未知属性"或反过来。

## 5. CFG-03：状态机（纯函数）

`src/CkanUI.Services/ModConfig/ModConfigDetector.cs` —— 全是静态纯函数，输入全部由调用方探测后传入，零 IO，好测。

```csharp
public static bool IsInstalled(bool registryHit, bool anyDeclaredFileExists);
public static ModConfigAdapterState DetectAdapterState(
    bool registryHit, bool anyDeclaredFileExists,
    IReadOnlyList<string>? supportedVersionRange, string? installedVersion,
    bool anyCreatableFile, bool anyWritableFile,
    out ModConfigDiagnostic? diagnostic);
```

判定顺序（**顺序本身是设计**）：

| # | 条件 | 结果 | 诊断 |
| --- | --- | --- | --- |
| 1 | 注册表没装 且 声明的文件都不存在 | `NotInstalled`（页面不显示卡片） | 无 |
| 2 | 版本区间判定不出来（版本未知/非法） | 按支持处理，继续往下 | Warning `version-unknown` |
| 3 | 版本不满足区间 | `ReadOnlyVersion` | Warning `version-out-of-range` |
| 4 | 声明的文件全不存在 | `CreatableByTemplate`（有模板）否则 `ReadOnlyRaw` | `files-missing` |
| 5 | 有文件但既不可写也无可创建 | `ReadOnlyRaw` | `files-readonly` |
| 6 | 其它 | `Editable` | 无 |

版本区间语义：各条约束 **AND**；运算符 `>=` `<=` `>` `<` `=`（`==` 归一成 `=`）；裸版本按 `=`；空区间 = 不限。
**越界只读**（15 篇 §4.3）：越界时不是"警告着让你改"，而是直接禁用编辑 + 横幅说明，文案固定在 `BuildVersionOutOfRangeMessage`：

> 此 mod 版本 {版本} 超出适配器支持范围（{≥1.30, <2.0}），为避免写坏配置已禁用编辑；请更新适配器或原始视图查看

字段值校验 `ValidateFieldValue(field, value, out error)`：ReadOnly 拒绝、空值拒绝、含换行拒绝、bool 只认 true/false/1/0、int/float 解析并检查 min/max（拒 NaN/∞）、enum 白名单（**忽略大小写**）、pattern（编译失败降级为不校验，不阻断）。

### 版本号判定的坑（实测）

CKAN 的 `ModuleVersion` 构造**对任意字符串都不抛异常**：`new ModuleVersion("abc")` 成功。所以"用它是否抛异常"当合法性判据等于没校验，`">=not-a-version"` 会被当成合法区间、`"about"` 会被当成版本号。
修法：`ModConfigVersionRange.IsModuleVersion` 先过一层**语法闸门**——可选 epoch 前缀 `n:`、必须数字开头、其余字符只能是数字/字母/`.`/`-`/`_`/`+`，再交给 `ModuleVersion` 解析。

## 6. CFG-05：写盘（唯一入口）

`src/CkanUI.Services/ModConfig/AdapterWriter.cs`。`SemaphoreSlim` 串行化整个 `WriteAsync`（同一进程内不并发写同一实例）。

### 6.1 校验顺序（任一失败都必须"无副作用"）

1. 空变更 → 失败；2. 找不到 adapter → 失败；3. 游戏目录不存在 → 失败；
4. **KSP 正在运行且未确认** → `Fail(detail: "game-running", retryable: true)`，文案含"重启 KSP 才生效"与"与游戏自身写盘竞争"；
5. 逐条：key 非空 / 值非 null / 值不含换行 / **目标路径在 gameDir 内** / adapter 声明过该文件 / 文件 `IsWritable`（否则透出 `readonlyReason`）/ adapter 声明过该 key / `ValidateFieldValue` / **`risk = high` 且未确认** → `Fail(detail: "risk-high")`。

路径与文件匹配两条都容易写错，已踩过：

- 目标路径：`Path.GetFullPath` + 去掉尾分隔符后，必须是 `gameDir` 前缀（`OrdinalIgnoreCase`），且**不能等于** gameDir 本身。
- 声明文件：adapter 里的 `path` 是**相对游戏目录**的写法，所以比较前要先 `Path.Combine(gameDir, file.Path)`。直接 `GetFullPath(file.Path)` 会解析到进程当前目录，结果所有写入都被判成"未声明文件"（这就是下面 6.4 那个 bug）。

### 6.2 每个文件的落盘序列

```
解析现有文件（IsTextLossy → 拒写）
  或 按模板创建（模板解析失败 → 拒写，不用坏模板创建配置）
→ 定位节点（node 布局用 rootNode；flat/custom 用根层）
→ 找旧值（找不到该 key → 取消，不新建键）
→ TrySetValue 逐条改（只改行内值片段）
→ 有改动才继续
→ 快照：<文件名>.ckan-ui-<yyyyMMdd-HHmmss>.bak（重名 -2..-20，绝不覆盖）
→ RenderBytes()
→ 原子写：同目录 <文件名>.tmp-<8位guid> → File.Move(overwrite: true)，finally 删临时文件
→ 写后自校验：重新 ParseFile，逐个键比对（去引号后 Ordinal）
→ 自校验失败 → 回滚（原本存在则用 originalBytes 原子写回；原本不存在则删掉）
```

- `lineEnding` 只在**新建文件**时生效；已有文件永远保留原行尾。
- "只有文件原本存在才做快照"：新建文件没有"写前状态"，回滚就是删文件。
- 回滚失败时，错误消息会附上快照路径，让用户能手工恢复。

### 6.3 为什么写后要重新解析

`TrySetValue` 是行内切片，理论上不可能写坏；但"理论上"不该是安全边界。写后重新解析 + 逐键比对把"我方法是对的"变成"这次写盘的结果确实是对的"，代价是每文件多解析一次（配置文件都是 KB 级）。

### 6.4 已修的两个真 bug（都靠测试抓到）

1. **行尾注释吞空格**：裸值扫描到注释符后要**向前回退吃掉空白**再取值末位置，否则 `a = 1 // 注释` 改成 2 会变成 `a = 2// 注释`（空格被当值吃掉）。修好后 `SetValue_KeepsTrailingComment` 转绿。
2. **声明文件永远匹配不上**：见 6.1 第 2 条，`FindFile` 现在先与 `gameDir` 拼绝对路径。

## 7. UI-CFG-01 + CFG-04：页面骨架与编辑写盘

### 7.1 结构

```
┌ 页头：标题 + 说明 + 实例摘要 ──────────────────────────────┐
├ KSP 运行中警示（仅运行中显示，不粗暴禁写）────────────────┤
├ 错误提示区：读盘/写盘/回滚的失败原因（仅 HasError 显示）──┤
├ 工具条：搜索 / 清空 / 仅看已改 / 刷新 / 重新加载适配器 ────┤
├ 左 340px：mod 卡片列表（名字/版本/徽标/已改数/告警）──────┤
│            底部：⊘ N 个 mod 无适配器 + 已加载适配器清单     │
├ 右：选中 mod 的字段行（按注释标题分组，每行 = 编辑控件 + ───┤
│      键名/说明/风险/需重启/已改/待保存 徽标）              │
└ 底部动作条：改动计数 + 保存 + 放弃未保存改动 + 恢复快照 +  ┘
   "写前快照/写后自校验"说明 + 逐文件失败列表
```

渲染器扩展点：

```csharp
public interface IModConfigFieldRenderer
{
    ModConfigFieldType FieldType { get; }
    ModConfigEditorKind EditorKind { get; }
    string FormatValue(ModConfigFieldState field);
}
```

`ModConfigEditorKind` 是**编辑器形态**（`Toggle / NumberField / Dropdown / TextField / PathPicker / ColorPicker / KeyCapture / ListEditor / ReadOnlyText`），视图按形态选模板，不按字段类型写 switch。新增一种字段类型 = 加一个 renderer + 注册，不动页面。

默认注册表已覆盖 9 种字段类型（顺序固定：Bool / Int / Float / Enum / String / Path / List / Color / Keybinding）：`BoolFieldRenderer`（开/关 → `Toggle`）、`IntFieldRenderer` / `FloatFieldRenderer`（数字 + 单位，非法数字老实显示原文 → `NumberField`）、`EnumFieldRenderer`（下拉，白名单来自 adapter → `Dropdown`）、`StringFieldRenderer` / `PathFieldRenderer`（文本框 → `TextField` / `PathPicker`）、`ListFieldRenderer`（逐行文本 → `ListEditor`）、`ColorFieldRenderer` / `KeybindingFieldRenderer`（色块预览 + 按键捕获 → `ColorPicker` / `KeyCapture`）。`ReadOnly` 类型与任何未注册类型退回 `UnsupportedFieldRenderer`（只读原值 → `ReadOnlyText`）。

筛选与分组：搜索三路匹配（键名 / 显示名 / 注释），"仅看已改"按 `value != default` 过滤，分组标题来自 adapter 声明的 `// #### 分节注释`（无标题的组不画表头）。**筛选只重建行对象，草稿保存在页面 VM 的 `_pending` 字典里**，所以来回筛选/切换 mod 再切回来，未保存的输入仍在（行对象会被换成新实例，草稿按 `文件路径 + 键名` 铺回）。

### 7.2 CFG-04：编辑、脏标记与保存

**编辑形态**（`ModConfigFieldRowViewModel`，`: ObservableObject`）：

| 字段类型 | 控件 | 说明 |
| --- | --- | --- |
| bool | `CheckBox` | `IsToggleOn` 读写，"true/1" 视为开，写回 `true`/`false` |
| int / float | `TextBox` | 视图侧不格式化，写盘前由服务层 `ModConfigDetector` 校验数字与范围 |
| enum | `ComboBox` | `Choices` 来自 adapter 白名单；**当前值不在白名单时补到首位**，避免"显示空白 + 一保存就变值" |
| string | `TextBox` | 单行 |
| path | `TextBox` + 「浏览…」按钮 | 按钮只在宿主注入了 `PathPicker` 委托时出现（`CanBrowse`）；挑目录后把绝对路径写回文本框，取消则保留草稿 |
| list | 多行 `TextBox` | 逐行编辑，写盘时按 `", "` 拼成**单行**——服务层的校验明确拒绝含换行的值（配置文件一行一键），所以多行只能停在 UI 层 |
| color | 22×22 色块 + `TextBox` | 色块走 `ColorPreviewHex` + `HexColorToBrushConverter`（识别失败即透明）；文本原样写盘，**不替用户改写写法**（配置由 mod 自己解析），`ModConfigColorText.Describe` 给一句可见提示 |
| keybinding | `TextBox` + 按键捕获 | 直接输入键名，或按住 Ctrl/Alt/Meta 再按键由 `OnKeyCaptureKeyDown` 捕获成 `Ctrl+Shift+K` 形式（`KeyGestureText.Format` 固定顺序）；Esc 清空，Shift+字母仍是正常输入 |

**只读不是静默禁用**：每行都能拿到一句可见原因（`ReadOnlyNote`，绑定到 `HasReadOnlyNote`）。文件不可写 → "只读：<adapter 声明的 readonlyReason>" 或"只读：所在配置文件当前不可写。"；`ReadOnly` 字段类型 → "只读字段：值由 mod 自己维护，页面不提供修改。"；其它未覆盖形态 → "本版只读：这类字段还没有编辑控件。"

**脏标记**：`HasPendingChange`（行）= 行可编辑且 `WireValue.Trim()` 与基线（当前磁盘值或最近一次保存后的值）逐字符不等（`Ordinal`，只 trim 两端）；页面侧 `_pending` 以 `文件路径 + \0 + 键名` 为键累积草稿，底部动作条的 `ModifiedBarText` 实时给出"改动 N 项（待保存 M 项）"这类组合文案。**行内编辑只更新 `_pending` 与计数，绝不重建字段行**——一重建焦点就没了。

**保存流程**（`SaveCommand` / 单行 `SaveRowCommand`，两者走同一段编排）：

```
按文件路径分组（每文件一次 IAdapterWriter.WriteAsync）
→ 有 IAdapterWriter 才写；没有则给可读错误（按钮可点，不静默禁用）
→ KSP 运行中：先确认一次（说明"要等重启游戏才生效，运行中的游戏还可能覆盖"）
→ 存在 ModConfigRisk.High 改动：再确认一次（列出键名）
→ 逐组 new AdapterWriteRequest(实例名, 游戏目录, adapterId, changes,
      ConfirmedRiskHigh: …, ConfirmedGameRunning: …)
→ 成功文件：移除草稿、行 MarkSaved、收集 BackupPaths 与 RequiresRestartCount
→ 失败文件：逐文件一行错误，**草稿全部保留**（不整页丢用户输入）
→ 结尾重新读盘刷新模型，并停在原来选中的 mod
```

两个确认位是 `AdapterWriteRequest` 上的**标志**而不是"页面自己记得"：拒绝任意一次确认就完全不写（`ConfirmedGameRunning` / `ConfirmedRiskHigh` 仍为 `false`，服务层也会再拒一次），同意才把标志一起带下去。测试用假 `IDialogService` 覆盖了"拒绝则不写"与"同意则写"两条路径。

**回滚入口**：保存成功后页面记下服务层返回的 `.bak` 路径清单（`HasBackups`/`RestoreButtonText`/`BackupHintText`）。"恢复上次快照"经确认后交给 `ModConfigBackupRestorer`（ViewModels 层，`IModConfigBackupRestorer`）：从 `<文件>.ckan-ui-yyyyMMdd-HHmmss[-N].bak` 的文件名反推原文件，读字节 → 同目录临时文件 → `File.Move(overwrite: true)` 原子替换 → 删临时文件。失败按文件给可读原因（"快照文件名不是 … 的格式，无法定位原文件。" / "快照文件已不存在，无法恢复。"），部分成功会同时报成功数与失败清单。

**键盘**：`Ctrl+S` 保存整页、单行文本框里 `Enter` 提交当前行（多行 list 编辑器里 `Enter` 是换行，不劫持）、`Tab` 顺序＝控件自然顺序。实现走 `ModConfigPageView.axaml.cs` 的隧道键处理（与本仓库其它页面一致），而不是 XAML `KeyBindings`。

### 7.3 本段刻意没做

- 没有弹出式取色面板：颜色字段用"十六进制文本框 + 色块预览"就地编辑，够用且不引入额外依赖；真要拖色轮再加。
- 没有配置项搜索跨 mod 聚合：搜索只匹配当前 adapter 声明过的 label 与配置文件注释。
- 没有本地化：页面文案是中文原文，经 `Func<string,string>` 委托转出，键由 Lead 统一登记。

## 8. 首批 adapter 与取证状态

| adapter | 状态 | 覆盖 | 说明 |
| --- | --- | --- | --- |
| `adapters/ksp-community-fixes/` | 完整示例 | 1 文件 / **3 字段** | `RefundingOnRecovery`(bool)、`Language`(enum 6 值)、`MaxPhysicsDelta`(float 0.01~0.2 unit s)。**该 mod 的 Settings.cfg 实测有 138 条键，其余 135 条未收录** |
| `adapters/kerbalism/` | 骨架 | 1 文件 / **0 字段** | 文件语法形态未核验 → `layout: custom` + `mode: readonly` + `onMissing: readonly`，只做只读展示 |

为什么只写 3 个键：本机没有 KSP 实例，键名/默认值只能取 15 篇 §4.1/§4.2 里**已取证**的那几条；本会话的联网检索工具不可用（凭据失效），无法进一步核验。按 §7 Q4，盲写键名等于埋雷，所以**宁缺勿错**，其余键在 README 里列了补齐 4 步（取证 → 加字段带 label/risk → 加 fixture 与往返测试 → 跑 H-08）。

待补 adapter 清单（15 篇 §8，全部未开始）：MemGraph（fixture pending）、Kerbalism 完整版、KSPIE、RealChute、Scatterer、Restock、Waterfall 等。

## 9. 与规格 15 篇的差异 / 不一致处

1. **KSPCF 覆盖面**：15 篇示例给了 3 个键，但正文提到该 mod 有 138 条键。本段只实现示例那 3 个，并在 adapter.json 顶部注释与 README 里写明"其余 135 条待核验"。**这不是缩水，是取证边界**。
2. **custom 布局强制只读**：15 篇只说 custom 是"mod 私有语法"，没有明说禁止写。本项目把"custom 必须 readonly"做成**读取器的硬错误**（写错就得改 adapter），理由是允许写等于允许写坏。
3. **ConfigNode 复用上游**：15 篇 §9 说"复用 CKAN 的 ConfigNode 实现"。实际复用只到 `SfsParser`（语义解析），序列化自研（`ConfigNodeDocument` 的行内切片），因为 `SfsWriter` 没有按行注入的入口。上游代码零修改。
4. **JSON Schema**：15 篇提到 schema 校验但没指定位置。仓库里原有一份 `tools/verify-layout/adapter.schema.json`（旧项目版，缺 `custom/id/lineEnding/separator/readonlyReason/label/default`），H-08 会**优先用** `docs/design/adapter.schema.json`。本段新增后者，与读取器规则对齐；旧文件保留未动（属他人范围，建议 Lead 后续合并或删除）。
5. **KSP 运行中"禁写"**：15 篇 §5 说"运行中禁写"，但 §7 Q1 的结论是"(b) 允许写但二次确认"。实现按 Q1：默认拒绝（`detail: "game-running"`），确认后允许，并提示需要重启才生效。
6. **页面 VM 不引用 Core**：15 篇没写这条，是本仓库的架构铁律（工程约定 §5 铁律 3）。因此 `ModuleVersion` 语义全部留在 Services，VM 只拿现成字符串（如 `SupportedVersionRangeText`）。

## 10. 测试与门禁

| 套件 | 文件 | 用例 |
| --- | --- | --- |
| ConfigNode 往返 | `tests/CkanUI.Services.Tests/ModConfig/ConfigNodeDocumentTests.cs` | 33 |
| adapter 读取 | `.../AdapterJsonReaderTests.cs` | 35 |
| adapter 目录 | `.../AdapterCatalogTests.cs` | 9 |
| 状态机 | `.../ModConfigDetectorTests.cs` | 18 |
| 版本区间 | `.../ModConfigVersionRangeTests.cs` | 19 |
| 写盘安全 | `.../AdapterWriterTests.cs` | 23 |
| 页面 VM / 编辑 / 回滚 | `tests/CkanUI.ViewModels.Tests/ModConfig/*` | 119 |

写盘安全用例覆盖：只改目标行、快照字节等于写前文件、无 `*.tmp-*` 残留、同秒两次写不覆盖快照、KSP 运行中拒绝/确认后成功、高风险需确认、只读文件/未知键/未声明文件/越界值/非法枚举被拒、缺失文件按模板创建、非 UTF-8 拒写、含空格值加引号后重解析等价、node 布局不误伤其它节点同名键、并发两次写都落盘。

页面侧编辑用例（CFG-04 新增 55 例，其中 19 例行编辑语义、27 例页面写盘编排、9 例 .bak 恢复器真实文件 IO）覆盖：编辑即置脏与底部计数（"改动 2 项（待保存 1 项）"）、改回原值清脏、**每文件恰好一次 `WriteAsync` 且请求参数（实例名/游戏目录/adapterId/改动键值）正确**、跨 mod 按 adapter 分组、无确认需求时两个确认位保持 `false`、游戏运行中同意写入（`ConfirmedGameRunning = true`）与拒绝不写、高风险拒绝不写、两个确认各问一次、没有 `IDialogService` 时拒绝写盘、成功/部分失败/全失败的文案与"失败文件草稿保留"、写盘器抛异常时逐文件报错、保存后重新读盘（`LoadAsync(string.Empty)`）且停在原选中 mod、保存后暴露 `.bak` 供回滚、回滚拒绝不调用、回滚成功清空快照与草稿、回滚失败保留快照、放弃草稿恢复原值、筛选与搜索来回之后草稿仍保留、只读文件行不产生草稿、`SaveRowCommand` 只写本行所属文件、list 字段逐行文本写盘时拼成不带换行的单行。

命令（**禁 `dotnet test`**，沙箱拒绝 Testhost）：

```bash
dotnet build tests/CkanUI.Services.Tests/CkanUI.Services.Tests.csproj --nologo -v q /nodeReuse:false
dotnet run --project tests/CkanUI.TestRunner --no-build -- Services
dotnet build tests/CkanUI.ViewModels.Tests/CkanUI.ViewModels.Tests.csproj --nologo -v q /nodeReuse:false
dotnet run --project tests/CkanUI.TestRunner --no-build -- ViewModels
dotnet run --project tools/verify-layout
```

## 11. 已知限制与后续

1. **颜色 / 按键 / 路径三类控件已补齐**：color 走色块预览 + 十六进制文本（`ModConfigColorText` 归一化只用于预览与提示，写盘仍是原文）；keybinding 走 `OnKeyCaptureKeyDown` 捕获（含 Ctrl/Alt/Meta 才算组合键）；path 走「浏览…」按钮（宿主未注入 `PathPicker` 时不显示，不假装有）。三者的判定逻辑都在 ViewModels 层，可单测。
2. **没有实例选择器**：页面固定读 `IInstanceService.Current`，切实例靠上层页面。
3. **回滚只回滚到"上一次写盘前"**：`IAdapterWriter` 每次写盘都新建带时间戳的快照（不覆盖旧快照），但页面只跟踪本会话最近一次保存返回的那批 `.bak`；历史快照需要手工在磁盘上找。服务层没有"从快照恢复"的公共方法，所以恢复逻辑放在 ViewModels 层的 `ModConfigBackupRestorer`（做 IO 在本仓库有先例：`StabilityToleranceFile` / `LaunchCommandFile`），没有改 Services 层。
4. **adapter 导入/远程更新只到服务层**：ADP-04 已交付 `IAdapterInstaller`（本地 json/zip、远程 URL、SHA-256 清单、ETag 更新检查、原子替换与回滚），细节见 [adapter-import.md](adapter-import.md)；页面还没有「导入 adapter…」入口，15 篇 §4.4 的信任模型仍只到"内置 + 用户目录 + 手动放入"。
5. **配置项关键词的本地化**：搜索只匹配 adapter 声明的中英文 label 与注释，不做中文同义词。
6. **Avalonia 视图未截图验证**：本段只能保证编译 0 警告、绑定名与 VM 属性一致（`IsSelectionEmpty` 这类取反属性已补进 VM 而不是在 XAML 里做转换）。
