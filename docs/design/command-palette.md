# 命令面板与快捷键目录

## 目标与范围

给 CKAN UI 加一层键盘入口：`Ctrl+K` 唤起居中命令面板，输入即搜（页面、动作、模块名都能命中），回车执行；`Ctrl+/` 打开可搜索、可复制的快捷键目录。
范围只到「面板 + 目录 + 纯函数匹配 + 视图模型层」，不含外壳按键接线、不含命令参数（第二段输入）、不含最近使用排序。

**做**

- `Ctrl+K` 打开/关闭居中浮层；`Esc` 关闭；点遮罩空白处关闭。
- 输入即搜：标题/分类/副标题/关键词四类字段加权打分，子序列兜底；结果最多 50 条，超出时显式提示截断。
- `↑`/`↓` 循环选择，`Enter` 执行选中命令；禁用命令回车无效。
- 标题命中高亮：连续命中显示一段，非连续命中逐字符显示。
- 命令目录可注册（`ICommandCatalog.Register`），内置 19 条导航 + 10 条界面动作 = 29 条。
- 快捷键目录：按分类分组、输入过滤、一键复制为纯文本、`Esc` 关闭。

**不做**

- 不做最近使用/使用频率排序：先保证打分确定性，方便写断言。
- 不做命令参数、不做内联预览、不做命令历史。
- 不接管全局键：`Ctrl+K` / `Ctrl+/` 注册到窗口是外壳的事，面板只管状态。

## 分层与文件表

视图模型层不认识 Avalonia，服务层不认识界面：两侧靠 `CommandDescriptor`（数据）+ `ICommandCatalog`（窄接口）对接。

| 层 | 文件 | 职责 |
| --- | --- | --- |
| 契约 | `src/CkanUI.Services.Abstractions/Commands/CommandDescriptor.cs` | 命令描述：`Id`/`Title` 必填，`Category`/`Subtitle`/`Keywords`/`ShortcutText`/`IsEnabled`/`RequiresSelectedInstance` 可选；`HasShortcut`/`HasSubtitle` 派生 |
| 契约 | `src/CkanUI.Services.Abstractions/Commands/TextMatchRange.cs`、`Commands/CommandMatch.cs` | 高亮区间（`Start`/`Length`/`End`）与匹配结果（`Command`/`Score`/`TitleHighlights`） |
| 契约 | `src/CkanUI.Services.Abstractions/Commands/IFuzzyMatcher.cs` | `Search(commands, query, limit)` / `Match(command, query)` |
| 契约 | `src/CkanUI.Services.Abstractions/Commands/ICommandCatalog.cs` | `Snapshot()` / `Register(descriptor)` / `Register(commands)` / `Search(query, limit)`；注册失败返回 `OperationResult` |
| 匹配 | `src/CkanUI.Services/Commands/FuzzyMatcher.cs` | 纯函数打分：精确/前缀/子串/子序列 + 位置加成 + 间隙罚分；`DefaultLimit = 50` |
| 目录 | `src/CkanUI.Services/Commands/CommandCatalog.cs` | 线程安全注册表：Id 唯一、批量注册 all-or-nothing |
| 内置 | `src/CkanUI.Services/Commands/DefaultCommands.cs` | 19 条导航 + 10 条界面动作；`nav:` / `action:` 前缀与 `TryGetNavigationPageKey` |
| VM | `src/CkanUI.ViewModels/CommandPalette/CommandPaletteViewModel.cs` | `IsOpen`/`Query`/`Results`/`SelectedIndex`/`ExecuteSelected`/`CommandInvoked`，上限 50 |
| VM | `src/CkanUI.ViewModels/CommandPalette/CommandPaletteItemViewModel.cs`、`CommandTitleSegment.cs` | 行视图模型 + 高亮分段（`FromRanges`） |
| VM | `src/CkanUI.ViewModels/CommandPalette/ShortcutsCatalogViewModel.cs`、`ShortcutEntryDto.cs`、`ShortcutGroupViewModel.cs` | 快捷键目录：条目采集、过滤、分组、纯文本导出 |
| 视图 | `src/CkanUI.App/Views/CommandPaletteView.axaml` | 遮罩 + 居中卡片 + 搜索框 + 结果列表 + 底部提示；code-behind 处理隧道键与遮罩点击 |
| 视图 | `src/CkanUI.App/Views/Dialogs/ShortcutsDialogView.axaml` | `DialogWindow` 子类：过滤框 + 分组列表 + 复制/关闭按钮 |

相关用例在 `tests/CkanUI.ViewModels.Tests/CommandPalette/`（4 个契约/算法文件 + 2 个视图模型文件，共 73 个 `[Test]`）。

## 关键口径

### 打分

| 维度 | 取值 |
| --- | --- |
| 字段基础分 | 标题 1000 / 分类 400 / 关键词 300 / 副标题 200 |
| 命中方式加成 | 精确 500 / 前缀 300 / 子串 150 / 子序列 50 |
| 位置加成 | `max(0, 16 - start)`（起始越靠前越高，超过 16 字符不再扣） |
| 间隙罚分 | `min(gaps, 8)`（子序列命中时首末字符之间没命中的字符数，最多扣 8） |

- 同一 token 只取**最佳字段**的分，所以标题命中永远压过关键词命中；多 token 累加。
- AND 语义：每个 token 都要命中任一字段，命令才进结果（`搜不到 = 少一个 token`）。
- 空查询 = 浏览态：按 `Category` 的 `OrdinalIgnoreCase` 排序、分数 0、无高亮。
- 查询超过 `MaxQueryLength`（256）先截断再匹配，不抛；`limit <= 0` 退回 `DefaultLimit`（50），不返回空。
- 同分保持注册顺序（`OrderByDescending` 是稳定排序）。
- 可核对的三个例子：标题「日志」+ 关键词 `log` + 查询 `log` → `300 + 300 + 16 = 616`（关键词前缀命中，标题无高亮）；查询「日志」→ `1000 + 500 + 16 = 1516`，高亮 `(0,2)`；标题「打开日志目录」查「日志」→ `1000 + 150 + 14 = 1164`，高亮 `(2,2)`。

### 高亮分段

- 匹配器里相邻/重叠的命中区间已合并（`MergeRanges`，`next.Start <= current.End` 即合并）。
- `CommandTitleSegment.FromRanges`：过滤越界与零长区间 → 按 `Start` 排序 → 游标推进，把窗口外的部分补成 `IsHighlighted = false` 的段。
- `FromRanges` **不**合并相邻的高亮段：`(0,2) + (2,1)` 得到两段高亮而非一段，拼起来仍是完整标题，代价只是多一个 `TextBlock`。
- 高亮不走 Avalonia 的 `Inline` 转换器（`Converters/` 不在本分支范围）：视图用横向 `ItemsControl` + `TextBlock.segment.highlight` 类上色。

### 面板状态

- 打开：`Query` 置空 → 立即 `Refresh()` → `SelectedIndex = 0`（结果非空时）。
- 关闭：清空 `Query` 并复位结果；`Close()` 幂等（已关且查询已空时直接返回）。
- `Results` 上限 50；达到上限时 `IsCapped = true`，计数文案追加「（结果过多，已截断）」。
- 执行：`ExecuteSelected` **先 `Close()` 再**抛 `CommandInvoked(Id)`，宿主处理命令时面板已经收起；禁用命令（`IsEnabled == false`）不执行、不抛事件。
- `ShowEmptyHint` 只在「已打开 + 查询非空 + 结果为空」时为真，避免刚打开就闪一行空态。
- 选择回绕：`↑` 在第一条回到最后一条，`↓` 在最后一条回到第一条；结果为空时 `SelectedIndex` 保持 `-1`。

### 快捷键目录

- 条目来源（顺序固定）：① 命令目录里 `ShortcutText` 非空的命令（分类「全局」）② 面板自身手势 `↑`/`↓`/`Enter`/`Esc`（分类「命令面板」）③ 对话框自身 `Esc`（分类「快捷键目录」）。
- 默认只有 `Ctrl+K`、`Ctrl+/` 两条全局快捷键（其余 8 条界面动作为空快捷键）→ 默认 7 条 3 组（2 + 4 + 1）；未注入命令目录时 5 条 2 组。
- 过滤：对 `Gesture`/`Description`/`Category` 三处做 `OrdinalIgnoreCase` 子串匹配；纯空白等于不过滤；命中 0 条时显示空态文案而不是空白列表。
- 分组：按分类分组，分组顺序 = 首次出现顺序（不排序，保证「全局」总在最前）。
- 复制：`CopyCommand` → `CopyRequested(纯文本)`，剪贴板由视图写（视图模型不碰 Avalonia）；文本格式 = 标题行 + 计数行 + 每类 `[分类]` + 每行「手势 + 4 空格 + 说明」+ 空行。
- 关闭：继承 `DialogViewModelBase`，主按钮文案「关闭」、次按钮置空（于是 `HasSecondaryButton == false`），视图把主按钮标成 `IsDefault` + `IsCancel`，`Esc` 与回车都走它。

### 命令注册与执行

- Id 约定：导航 `nav:<pageKey>`、动作 `action:<verb>`；生成与解析都用 `DefaultCommands.NavigationId` / `ActionId` / `TryGetNavigationPageKey`。
- 重复 Id → `OperationResult.Fail("命令 Id 已被占用", "Id = …")`，既有命令不变。
- 批量注册先全量校验（空项、批内重复、与既有冲突）再一次性提交，校验失败整批不生效。
- 执行映射在宿主侧：`nav:` 前缀 → `INavigationService.NavigateAsync(pageKey)`；`action:` 前缀 → 宿主 `switch` 到具体动作（主题/密度/目录/诊断包等）。面板与目录都不认识导航服务，只发 Id。

### 新增文案键

面板 5 个：

- `Ui_Cmd_Palette_Placeholder` = 搜索命令：页面、动作或模块名…
- `Ui_Cmd_Palette_Empty` = 没有匹配的命令
- `Ui_Cmd_Palette_FooterHint` = ↑↓ 选择 · Enter 执行 · Esc 关闭
- `Ui_Cmd_Palette_ResultCountFormat` = 共 {0} 条
- `Ui_Cmd_Palette_TruncatedSuffix` = （结果过多，已截断）

快捷键目录 14 个：

- `Ui_Cmd_Shortcuts_Title` = 快捷键目录
- `Ui_Cmd_Shortcuts_FilterWatermark` = 搜索快捷键或动作…
- `Ui_Cmd_Shortcuts_Empty` = 没有匹配的快捷键
- `Ui_Cmd_Shortcuts_Copy` = 复制为文本
- `Ui_Cmd_Shortcuts_Close` = 关闭
- `Ui_Cmd_Shortcuts_TotalCountFormat` = 共 {0} 项
- `Ui_Cmd_Category_Global` = 全局
- `Ui_Cmd_Category_Palette` = 命令面板
- `Ui_Cmd_Category_ShortcutsDialog` = 快捷键目录
- `Ui_Cmd_Shortcut_MoveUp` = 上一条
- `Ui_Cmd_Shortcut_MoveDown` = 下一条
- `Ui_Cmd_Shortcut_Execute` = 执行选中命令
- `Ui_Cmd_Shortcut_Dismiss` = 关闭命令面板
- `Ui_Cmd_Shortcut_CloseDialog` = 关闭对话框

分类与动作标题 12 个：

- `Ui_Cmd_Category_Navigate` = 导航
- `Ui_Cmd_Category_Interface` = 界面
- `Ui_Cmd_Action_ShowPalette` = 命令面板
- `Ui_Cmd_Action_ShowShortcuts` = 快捷键目录
- `Ui_Cmd_Action_GoBack` = 后退
- `Ui_Cmd_Action_GoForward` = 前进
- `Ui_Cmd_Action_OpenInstanceFolder` = 打开游戏实例目录
- `Ui_Cmd_Action_OpenLogFolder` = 打开日志目录
- `Ui_Cmd_Action_ExportDiagnostics` = 导出诊断包
- `Ui_Cmd_Action_CheckUpdates` = 检查更新
- `Ui_Cmd_Action_ToggleTheme` = 切换主题
- `Ui_Cmd_Action_ToggleDensity` = 切换密度

- 19 条导航复用既有 `Ui_NavRail_*` 键，不新增。
- 每个视图模型自带一份中文 `FallbackTexts`：没注入 `translate` 时用中文（与 `ShellViewModel` 同口径），缺键时把键名原样显示，不抛。
- `Keywords`（检索同义词）**不进语言包**：中英混排只为「两种输入都能命中」。

## 安全与失败语义

| 场景 | 行为 |
| --- | --- |
| 注册重复 Id | `OperationResult.Fail("命令 Id 已被占用", "Id = …")`，不覆盖既有命令 |
| 批量注册有一项非法 | 整批不生效（先校验后提交），不会出现「注册了一半」 |
| `Id` / `Title` 空白 | 构造期 `ArgumentException.ThrowIfNullOrWhiteSpace`，属编程错误，不进运行期失败路径 |
| 查询为空或纯空白 | 浏览态（分类排序、0 分、无高亮），不抛 |
| 查询超长（> 256） | 截断后匹配，不抛 |
| `limit <= 0` | 退回 50，不返回空 |
| 结果超过上限 | 只显示前 50 条并显式提示「（结果过多，已截断）」，不静默截断 |
| 命令被禁用 | 行置灰（`Classes.disabled`），回车与点击都不执行、不抛事件 |
| 剪贴板不可用 | 视图 `try/catch` 静默失败，面板不崩、不弹错误框 |
| 宿主不认识命令 Id | `TryGetNavigationPageKey` 返回 `false` 时忽略该次执行，面板不猜 |
| 目录里没有任何带快捷键的命令 | 仍显示面板/对话框自身手势，绝不出现空白列表 |

安全边界：面板只发命令 Id，不执行任意字符串、没有 eval；注册表只接受 `CommandDescriptor`（纯数据），不接收回调或脚本。插件要执行自己的逻辑，得由宿主为该 Id 显式接线，注册本身拿不到执行权。

## 已知缺口

1. **App 外壳接线由 Lead 落地**（本分支未改 `src/CkanUI.App/MainWindow.axaml(.cs)` 与 `src/CkanUI.App/ServiceRegistration.cs`）：浮层要放进 `MainWindow` 最外层 `Panel`、`Panel.ZIndex` 高于 10；`Ctrl+K`/`Ctrl+/` 的 `KeyBinding` 挂在窗口；`CommandInvoked` 需要接到导航/动作。接线行见交付报告。
2. **DI 注册**：`FuzzyMatcher`、`CommandCatalog`、`CommandPaletteViewModel` 需要进 `ServiceRegistration.cs`（`ShortcutsCatalogViewModel` 每次打开新建即可）。本分支未改，那个文件是 Lead 独占。
3. **31 个 `Ui_Cmd_*` 键未进语言包**：需要加到 `src/CkanUI.Localization/LocalizationKeys.cs` 与 `i18n/packs/zh-CN.json`、`i18n/packs/en-US.json`（两处都是 Lead 独占）。未加时运行期走视图模型内置的中文兜底，英文界面下这部分仍是中文。
4. **文档登记**：`docs/README.md` 的索引需要加本文件的相对链接（`tools/verify-layout` 的 H-05 检查，警告级）。
5. **没有 GUI 自动化验证**：目前只做到「App 能编译 + 契约/算法/视图模型单测」，浮层自动聚焦、键盘隧道、遮罩点击、剪贴板写入需要真机手测；沙箱里 `dotnet test` 不可用，测试走 `CkanUI.TestRunner`。
6. **无拼音匹配**：中文只按字面匹配，输入拼音首字母（如 `rz` 命中「日志」）不会命中，要等有一张拼音表再说。
7. **快捷键目录只登记真实存在的键**：尚未接线的强绑定（例如 `Alt+←` 后退）不虚构，等外壳真挂上再加进 `DefaultCommands` 的 `ShortcutText`。
8. **没有命令参数**：`action:` 类命令目前都是无参动词，需要输入参数的场景（如「切到某个实例」）留给后续的二级面板。
