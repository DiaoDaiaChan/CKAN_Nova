# 游戏时长与版本显示格式

## 范围

本文记录 F-48（UI-10）实例累计游戏时长与 F-54（UI-07）版本文本格式能力的落地口径。
截至本次落地，页面、存储、设置项、全局导航与多语言接线均已完成；剩下的只有「真实计时来源」（见 §5）。

## 1. 分层与文件

| 层 | 文件 | 职责 |
| --- | --- | --- |
| 契约 | `src/CkanUI.Services.Abstractions/IPlayTimeService.cs` | `Load()` / `Save(instanceName, hours)`，两个成员都是 DIM，既有替身零改动 |
| 契约 | `src/CkanUI.Services.Abstractions/Dtos/PlayTimeEntry.cs` | `record PlayTimeEntry(string InstanceName, double Hours)` |
| 服务 | `src/CkanUI.Services/PlayTime/PlayTimeService.cs` | 原子 JSON 文件读写；默认 `%LocalAppData%/CKAN/play-time.json`，另有可注入路径的 ctor |
| VM | `src/CkanUI.ViewModels/Pages/PlayTimePageViewModel.cs` + `Pages/PlayTime/` | 列表、行编辑（F2/Enter/Esc）、空状态与错误栏 |
| VM | `src/CkanUI.ViewModels/Pages/Settings/VersionDisplayFormat.cs` | 版本号剥离的纯函数，两个开关独立 |
| 视图 | `src/CkanUI.App/Views/PlayTimePageView.axaml` | 行内 TextBox 编辑；命令经 `$parent[ListBox].DataContext` 上溯 |
| 设置 | `src/CkanUI.ViewModels/Pages/SettingsPageViewModel.cs` | 「高级与诊断」组四项布尔设置（见 §4） |

## 2. 游戏时长存储

## 游戏时长存储

### 2.1 位置与格式

游戏时长使用 UI 自有 JSON 文件 `%LocalAppData%/CKAN/play-time.json`，不写入 CKAN 配置或实例文件（与 `ckan-ui.json` 同目录、不同文件，避免界面设置被计时写盘反复重写）。文件内容是实例名与小时数记录数组。实例名按 ordinal 区分；同名保存覆盖已有小时数。损坏、缺失 JSON 视为空数据。

### 2.2 写入与容错

写入先创建同目录唯一 `.tmp-<guid>` 临时文件，再用 `File.Move(overwrite: true)` 替换目标；成功后清理临时文件。读写失败不向 UI 抛出异常，保存失败返回 false。测试可注入临时文件路径。

### 2.3 取值与输入

小时值只接受有限且非负的数；写入取整到整小时，中点 away-from-zero。UI 输入按当前文化解析，非法输入显示中文提示并保持编辑状态，不触发写盘。实例行可通过 F2 开始编辑；Enter 提交持久化，Esc 取消。

## 版本显示

### 3.1 纯函数

纯函数 `VersionDisplayFormat.Format(string? version, bool hideEpoch, bool hideVPrefix)` 可独立开关 epoch 与 v 前缀剥离。
epoch 只匹配开头「数字 + 冒号」；v 前缀只移除 v/V 后紧跟 ASCII 数字的形式（`version-2` 不会被削成 `ersion-2`）。
空串与无法识别格式原样保留，避免猜测性修正。

### 3.2 接线点

- `src/CkanUI.ViewModels/Pages/Mods/ModsRowView.cs`：新增 `HideVersionEpoch` / `HideVersionVPrefix` 两个 `init` 属性，`InstalledVersionText` 与 `LatestVersionText` 统一走 `FormatVersion`；缺失值仍显示 `—`。
- `ModsRowView.From(data, hideVersionEpoch = false, hideVersionVPrefix = false)` 保留原单参调用的语义（默认不剥）。
- `src/CkanUI.ViewModels/Pages/ModsPageViewModel.cs` 的 `ReadDisplayFlags()` 每次重建列表时读 `IUiSettingsStore.Current`；无设置服务或读盘抛异常时三个开关全部按 false 处理并记一次错误，列表照常可用。

## 4. 设置项与导航

四项布尔设置落在设置页「高级与诊断」组（`SettingGroupKey.Advanced`，`SettingPersistence.UiSettings`，`SettingItemKind.Boolean`），键名在 `SettingsKeyNames` 与 `UiSettings` 两处同名：

| 键 | 作用 |
| --- | --- |
| `HideVersionEpoch` | 隐藏 `1:2.0.0` 的 `1:` |
| `HideVersionVPrefix` | 隐藏 `v2.0.0` 的 `v` |
| `AutoSortUpdates` | 模组列表在**按名称排序**时把可升级项整体置顶 |
| `SuppressRecommendations` | 安装时跳过推荐模组选择框（F-35 的「不再提示」） |

导航：`playtime` 排在 `instances` 与 `saves` 之间（`ShellViewModel.NavItems` 第 6 项，图标 `Clock`），语言键 `Ui_NavRail_PlayTime`（zh-CN「游戏时长」/ en-US "Play time"）；页面视图由 `ViewLocator` 映射到 `CkanUI.App.Views.PlayTimePageView`，`tests/CkanUI.App.Tests/ViewLocatorTests.cs` 已钉住该映射。
DI：`src/CkanUI.Services/ServiceCollectionExtensions.cs` 注册 `IPlayTimeService` → `PlayTimeService`；`src/CkanUI.App/ServiceRegistration.cs` 注册 `PlayTimePageViewModel` 单例并登记 `PageTypes["playtime"]`。

## 5. 已知缺口

- **计时来源未接**：`PlayTimeService.Save` 目前只由页面手工编辑调用；实例启动/退出路径（`GameLaunchReleaseCoordinator` 的 `GameExited` 信号）还没有累计时长的写入方。
- 写入按整小时取整（中点 away-from-zero），不足一小时的会话记 0；如需精度应改为存分钟并只在显示时取整。
- 未做「清空/重置单个实例时长」的界面入口。
- 无真实实例集成测试：服务测试用注入路径的临时目录，页面测试用 `FakePlayTimeService`。
