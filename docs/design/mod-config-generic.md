# Mod 配置的通用 cfg 模式（无适配器）

> 目标：让没有专属 adapter 的 mod 也能在界面里改配置，而不是只有"等作者写 adapter"这一条路。
> 本文记录已实现的扫描口径、宽松模式与写盘纪律；adapter 语义层见 [mod-config-adapters.md](mod-config-adapters.md)。

## 1. 两种模式

| 模式 | 回答的问题 | 依赖 | 代价 |
| --- | --- | --- | --- |
| 适配器配置 | 这个 mod 暴露了哪些**语义**配置项（类型/范围/风险） | adapter 包 | 没有 adapter 就没有内容 |
| 通用配置 | 这个 mod 的 cfg 里有哪些**键值** | 只读 ConfigNode | 没有类型与解释，只有键与值 |

通用配置不替换适配器：同一页里用分段控件切换，adapter 面板仍按原来的语义渲染。
两条路共用同一个写盘入口的纪律：先备份、同目录临时文件、原子替换、运行中拦截。

## 2. 扫描口径

入口：`GenericConfigScanner.Scan(gameDir, cancellationToken, maxFiles, maxDepth, options)`。

### 2.1 自动（保守）模式

扫描 `<gameDir>/GameData` 下全部 `*.cfg`（默认上限 20000 个文件、深度 12），然后按下面的规则筛：

1. 跳过 `Squad` / `SquadExpansion` 整棵目录（原版内容不是用户的 mod 配置）。
2. 跳过 `.bak` / `.bak-*` 备份文件。
3. 跳过 `/Parts/` 下根节点为 `PART` 的零件定义。
4. 跳过原版内容定义根节点（`PART`、`RESOURCE`、`EXPERIMENT_DEFINITION`、`PROP`、`INTERNAL`、`CONTRACT`、`AGENT`、`STRATEGY` 等）。
5. 跳过 ModuleManager 补丁：首 4 KB 行首出现 `@`、`!`、`$`、`%` 直接跳；解析后根节点仍带这些操作符前缀的也跳（畸形首行会骗过 4 KB 探测）。
6. 根节点按首个空格/Tab/`{` 截断，并去掉 ModuleManager 的 `:NEEDS[...]` 后缀；`PART {` 这种把花括号写在同一行的写法也要认成 `PART`。
7. 只收以下任一命中的文件：
   - 路径含 `/PluginData/`；
   - 文件名是 `settings.cfg`、`config.cfg`、`options.cfg`、`user.cfg`、`defaults.cfg`；
   - 根节点是 `SETTINGS`、`PARAMETERS`、`CONFIG`、`KSP_SETTINGS`、`PERSISTENT`。

命中后才做完整 `ConfigNodeDocument.ParseFile`，拿到真实键值数与可写性；没命中的只读首 4 KB，不解析全文。

### 2.2 宽松模式

用户勾选「宽松扫描」后，自动模式里"没命中但不是内容定义"的 cfg 也会列出来：

- 仍然排除：`DefinitionRootNodes`（零件/资源/实验/合同/效果模板/本地化/分类/纹理集等）、`/Localization/`、`/Patches/`、`/MMPatches/`、`/ModuleManagerPatches/`、ModuleManager 补丁。
- 因此会新增：自定义根节点（`MYMOD_SETTINGS` 一类）、扁平键值文件（没有根节点）、视觉/行为配置（`TUFX_PROFILE`、`EVE_CLOUDS`、Parallax 散布等）。
- 宽松模式不是"全收"：内容定义与补丁仍然不是用户配置，列出来只会让人误改。

### 2.3 自动兜底

保守模式一个文件都没扫到、但 `HiddenWideCandidateCount > 0` 时，页面会自动用宽松模式再扫一次；扫到文件就把「宽松扫描」开关置为开启并显示结果。
理由：用户不该先知道这个开关存在，才能看到一个本来就能编辑的文件。

保守模式下页面会显示「另有 N 个可宽松查看」，避免"扫不到 = 没有配置"的误解。

## 3. 真实基数（本机实测，用于校准预期）

Steam 实例 `D:/SteamLibrary/steamapps/common/Kerbal Space Program/GameData`：

| 口径 | 数量 |
| --- | --- |
| 全部 `*.cfg` | 16775 |
| `/PluginData/` 下 | 120 |
| `settings/config/options/user/defaults` 命名 | 30 |
| 深度 2 以内 | 472 |
| 自动模式实际列出 | 96（另有 607 个候选可宽松查看；宽松模式实际列出 666，差额是首 4 KB 像候选、解析后确认是定义/补丁而跳过的文件） |
| 宽松模式实际列出 | 666（跳过 15300 个内容定义/补丁/本地化） |

根节点频次里 `PART` 1734、`PROP` 1642、`Localization` 575、`EFFECTTEMPLATE` 435、`CONTRACT_TYPE` 312，这些都不该进通用配置列表；`TUFX_PROFILE` 41、`EVE_CLOUDS` 40 这类视觉配置会在宽松模式出现。

## 4. 页面行为

- 左栏：按 mod 归类的 cfg 卡片（相对路径、键值数、只读标记）+ 搜索框；顶部有「宽松扫描」勾选框。
- 右栏：按 `NodePath` 分组的键值行；bool 用勾选，数字/文本用输入框；行级脏标记与撤销。
- 底部：脏计数、保存/撤销/重新载入/打开所在文件夹；不可写时显示原因而不是禁用得莫名其妙。
- 保存走 `IGenericConfigEditService`：按行号贴值（`ConfigNodeDocument.TrySetValue` 只改值片段），服务层负责备份与原子替换。

## 5. 测试

- `tests/CkanUI.Services.Tests/ModConfig/GenericConfigScannerTests.cs`：自动模式不列自定义根节点但计数；宽松模式仍跳过零件/效果模板/本地化/补丁；宽松模式收扁平键值与 `TUFX_PROFILE`；`PART {` 同花括号写法仍被识别为内容定义。
- `tests/CkanUI.Services.Tests/ModConfig/GenericConfigServiceTests.cs`：发现/读取/保存与路径逃逸拒绝。
- `tests/CkanUI.Plugin.ModConfig.Tests/GenericConfigTabViewModelTests.cs`：隐藏计数文案、显式宽松选项、保守模式空手而归时自动兜底。

## 6. 已知缺口

- 扫描上限（20000 文件/深度 12）是硬编码默认值，超大整合包可能被截断并只留一条诊断。
- 宽松模式会列出视觉配置，但页面不解释"这个键改了会发生什么"；那是 adapter 的职责。
- 候选文件会读两遍（首 4 KB 判定 + 完整解析），大文件多时是额外 I/O。
- 保存暂不做 mtime/hash 并发改动检测；运行中拦截依赖 `IGameRunningProbe`。
