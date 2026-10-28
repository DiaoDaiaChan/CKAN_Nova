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
- `strings`：键 → 译文。键名遵守 16 篇 §5.1（`Ui.<Area>_<Element>[_<Qualifier>]`，
  只用 ASCII 字母/数字/下划线；区域词表固定为 NavRail / ManageMods / ModInfo / Changeset /
  Settings / Instances / Repos / Log / Saves / ModConfig / Tray / Common）。
- 允许 `//` 注释与尾逗号（加载器按宽松模式解析）。
- 空字符串或 `null` 视为**缺译**，加载时被丢弃并记 Warn，随后走回退链。

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
