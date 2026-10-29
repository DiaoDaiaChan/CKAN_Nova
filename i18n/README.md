# i18n · 随包语言包

本目录存放新版界面的语言包与翻译词表，**不需要重新编译即可替换**（磁盘优先、内嵌兜底）。

## 目录

| 路径 | 用途 |
| --- | --- |
| `i18n/packs/<locale>.json` | 随包语言包，一种语言一个文件，文件名即 BCP-47 标识（如 `zh-CN.json`） |
| `i18n/glossary/` | 术语表：保证"模组/实例/依赖"等词在各语言里译法一致 |
| `i18n/tools/extract_ckan_meta.py` | CKAN-meta 元数据候选抽取（哈希规则与 C# 侧 `UI18n.Core` 一致） |
| `i18n/tools/contract-vectors.jsonl` | 跨语言哈希契约向量，由上面的脚本产出、由 `HashContractTests` 逐条校验 |

## 语言包格式

```json
{
  "locale": "zh-CN",
  "nativeName": "简体中文",
  "englishName": "Chinese, Simplified",
  "strings": {
    "Ui_NavRail_ManageMods": "模组",
    "Ui_Common_Status_StartupIncomplete": "启动未完成：{0}"
  }
}
```

- `locale`：BCP-47 标识；缺省时用文件名推断。
- `nativeName` / `englishName`：设置页语言下拉显示用，格式见 03 篇 §17.3
  （`简体中文 · Chinese, Simplified · 100%`，覆盖率由程序按 en-US 键集自动算出）。
- `strings`：键 → 译文。键名遵守 16 篇 §5.1（`Ui_<Area>_<Element>[_<Qualifier>]`，
  只用 ASCII 字母/数字/下划线；区域词表固定为 NavRail / ManageMods / ModInfo / Changeset /
  Settings / Instances / Repos / Log / Saves / ModConfig / Tray / Common，以及页面 VM 文案层的
  ModPacks / ModPackEditor / Content / DownloadStats / Dedup / InstallHistory / PlayTime /
  Plugins / ModText / Unmanaged）。
- 允许 `//` 注释与尾逗号（加载器按宽松模式解析）。
- 空字符串或 `null` 视为**缺译**，加载时被丢弃并记 Warn，随后走回退链。

## 键的分组约定

键 = `Ui_<Area>_<Thing>`：`<Area>` 说明这一条属于哪个界面区域，`<Thing>` 说明用在哪一句。同一个区域的键在
`src/CkanUI.Localization/LocalizationKeys.cs` 里连续排列，并带一行中文 XML 注释（写明页面与用途），
例如 `<summary>模组管理页（ModsPageViewModel）：状态栏中段的选择与变更摘要</summary>`。

| 区域 | 覆盖范围 | 例子 |
| --- | --- | --- |
| `NavRail_*` | 左侧导航栏；页面标题优先复用它，不另建重复文案键 | `Ui_NavRail_ManageMods` |
| `ManageMods_*` | 模组页运行期文案：状态栏、空态、筛选 chip、标签开关反馈 | `Ui_ManageMods_SelectionSummary` |
| `Instances_*` | 实例页运行期文案：启动阶段、切换/克隆/重命名反馈、对话框标题与正文 | `Ui_Instances_Count` |
| `ModInfo_* / Changeset_* / ModConfig_* / ModPacks_* / ModPackEditor_*` | 模组详情、变更集、配置、导入导出各页 | `Ui_Changeset_Title` |
| `Repos_* / Saves_* / Cache_* / Log_* / Content_* / DownloadStats_* / Dedup_* / InstallHistory_* / PlayTime_* / Plugins_* / ModText_* / Unmanaged_* / Settings_*` | 对应页面 | `Ui_Log_Title` |
| `Common_*` | 跨页面通用文案（分隔符、公共按钮词等） | `Ui_Common_ListSeparator` |

- **页面标题**：优先用已有的 `Ui_NavRail_*`；只有页眉文案与导航项不同（例如模组翻译页）才新建 `Ui_<Page>_Title`。
- **整句一个键**：带占位符的长句（如 `已选 {0} · 变更 +{1}/-{2}`）整体作为一个键，`{0}`/`{1}` 的数量与顺序在两套包里必须一致；不要把一句话拆成多个键再拼接，别的语言语序不同。
- **同一句复用**：同一文案出现在多个页面时复用同一个键，不要按页面复制。
- **fallback 与 zh-CN 逐字相同**：页面 VM 里写 `Text("中文原文", LocalizationKeys.Xxx)` 时，第一个参数就是 `zh-CN.json` 里那条译文（它是没有解析器时的显示值，也是既有单测的期望值）。
- **不放进语言包**：开发者异常消息、日志、`IErrorReporter.Report` 的上下文串、诊断码、字段名、文件名、单位符号。

## 回退链（本实现的硬约定）

`当前语言 → 当前语言的父语言（zh-CN→zh-Hans→zh）→ en-US（及其父语言）→ 补充来源层 → key 本身`。

父语言由 `CultureInfo.Parent` 逐级求得，链上每一级都去查对应语言包；`LocalizationOptions.Sources`
里的补充来源（元数据译文、上游 Core 资源）只在语言包全链未命中后才会被问到。

回退到 key 本身时：`ILocalizer.MissingKeys` 里该 key 计数 +1，并**每个 key 只记一次 Warn**
（避免刷日志）。

## 新增一种语言

1. 复制 `en-US.json` 为 `<locale>.json`，改 `locale`/`nativeName`/`englishName`。
2. 逐条翻译 `strings`；**不要删键**——删键会走回退链，覆盖率随之下降（下拉里会显示成 `93%`）。
3. 占位符（`{0}`、`{1}`…）必须与 en-US 完全一致，数量和序号都不能变（测试会校验）。
4. 跑 `dotnet build ckan-ui.sln -v q --nologo` 后执行
   `dotnet run --project tests/CkanUI.TestRunner -- Localization`，测试会校验：
   两套语言包键集一致（`ShippedPacksTests`）、`LocalizationKeys` 里的常量全部存在
   （`ShippedPacksTests`）、占位符一致（`ShippedPacksTests`）、包内不得出现常量之外的死键
   （`LocalizationKeyDriftTests`）。

## 元数据译文（与界面语言包分开）

模组摘要/说明的译文不是「键 → 译文」，而是按**文本哈希**索引，落在用户目录：

```
%LocalAppData%/CKAN/ckan-ui/i18n/packs/<lang>/CKANMeta.jsonl   # 机器/人工译文
%LocalAppData%/CKAN/ckan-ui/i18n/packs/overrides.jsonl         # 用户的单条人工覆盖（优先级最高）
%LocalAppData%/CKAN/ckan-ui/i18n/blocklist.json                # 不翻译的 mod 名单
```

每行一个 JSON 对象：`h`（`fnv1a64(Normalize(s))` 的 16 位小写十六进制）、`n`（`ckan:<identifier>:<field>`）、
`s`（英文原文）、`t`（译文）、`mod`（`CKANMeta`）、`ctx`（identifier）、`src`（`human`/`machine`）。
查找链与三档策略（关 / 仅已有 / 允许机翻）见 `docs/design/mod-metadata-i18n.md`。

抽取候选：

```bash
python i18n/tools/extract_ckan_meta.py --verify-vectors          # 先自检官方向量
python i18n/tools/extract_ckan_meta.py --meta-root <CKAN-meta> --out CKANMeta.jsonl --state state.json
```

## 伪本地化（截图验收用）

设 `CKAN_UI_PSEUDO_LOCALE=1` 后再启动，语言包文案会被重音化、外包 `[!! … !!]` 并加长 30%，
占位符保持原样（`string.Format` 不会炸），CJK 不变形以便一眼看出漏译。发布默认关闭。

## 注意

- 语言包是**数据**，解析失败绝不抛异常：坏掉一个语言只会让界面回退到 en-US，不会崩。
- 数字与日期的显示跟随系统区域（`CurrentCulture`），语言切换只改 `CurrentUICulture`；
  存盘一律 Invariant（16 篇 §8）。
- 不要在本目录放日志、临时文件或本机绝对路径。
