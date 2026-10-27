# 模组详情多页签架构（F-26 & F-30 / UI-03）

## 目标与范围
重构模组详情面板（Mod Details View），由原先的单视口平铺滚动结构升级为标准的 4 页签（TabControl / TabStrip）多维度浏览架构：
1. **元数据与简介页（Metadata / Overview）**：展示模组基础元数据（名称、作者、摘要、许可证、描述、版本号、主页与仓库等资源外链）。
2. **关系树页（Relationships）**：分类展示模组依赖（Depends）、推荐（Recommends）、建议（Suggests）与冲突（Conflicts）关系，带有色彩区分的状态标签。
3. **全部版本页（Versions，对齐 F-30）**：列表呈现模组的所有历史与最新版本，标识版本号、KSP 游戏兼容范围、发布日期、下载体积、当前已装标记及兼容性/稳定性状态徽章。
4. **内容预览引导/已装文件页（Contents，对齐 F-26 & F-29）**：展示已装模组的本地文件路径清单；未安装或需要查看完整压缩包时，提供直接跳转至全局 ZIP 内容预览页（F-29）的引导能力。

---

## 架构设计与文件清单

### 1. DTO 与模型扩展（CkanUI.ViewModels）
- **`Dialogs/ModVersionItemDto.cs`**：
  - 定义版本列表数据传输模型：`Version`、`GameCompatibility`、`ReleaseDate`、`DownloadSizeBytes`、`DownloadUrl`、`IsInstalled`、`IsCompatible`、`IsUnstable`。
  - 提供只读格式化与展示辅助计算属性：
    - `CompatibilityBadgeText` / `CompatibilityBadgeClasses`：例如「当前已装」（成功色 Badge）、「兼容」（成功色）、「不兼容」（警告色）。
    - `StabilityBadgeText` / `StabilityBadgeClasses`：例如「不稳定/测试版」（警告色）。
    - `ReleaseDateText`：格式化日期（yyyy-MM-dd）或「未知」。
    - `DownloadSizeText`：格式化文件大小（B / KB / MB / GB）或「未知」。
- **`Dialogs/ModDetailsSnapshot.cs`**：
  - 扩展 `IReadOnlyList<ModVersionItemDto>? Versions`。
  - 扩展 `IReadOnlyList<string>? InstalledFiles`。
- **`Dialogs/ModDetailsViewModel.cs`**：
  - 增加页签切换与状态控制：`SelectedTabIndex`（0=元数据, 1=关系, 2=版本, 3=内容）。
  - 增加版本列表投影：`Versions`、`HasVersions`、`VersionsCountText`。
  - 增加内容/已装文件列表投影：`InstalledFiles`、`HasInstalledFiles`、`InstalledFilesCountText`。
  - 增加内容预览导航命令与外联跳转委托：`NavigateToContentPreviewCommand`、`ExternalNavigateAction`。
  - `ShowDetails` / `Select` 具备容错防空与重置机制，切换模组或清空时自动同步页签计数与状态。

### 2. 视图升级与 Token 规范（CkanUI.App）
- **`Views/Dialogs/ModDetailsView.axaml`**：
  - 将根容器升级为 Avalonia `TabControl`，包含 4 个语义清晰的 `TabItem`：
    - **页签一（基本信息）**：元数据网格、许可证与描述、资源超链接（主页、代码仓库、Bug 追踪、Bug 赏金等）。
    - **页签二（依赖关系）**：四类关系分组卡片，每组包含图标、计数角标与流式标签列表，空状态友好提示。
    - **页签三（全部版本）**：带有轻量表格头（版本号、兼容 KSP、发布日期、体积、状态）的滚动列表，卡片带悬浮高亮与彩色 Badge。
    - **页签四（文件与内容）**：若模组已安装，列出所有本地已装文件相对路径；同时提供直达 ZIP 内容预览（F-29）的引导卡片与按钮。
  - 全面使用 `{DynamicResource Ckan*}` 语义 Token（`CkanBackgroundCard`、`CkanTextPrimary`、`CkanTextSecondary`、`CkanPrimaryBrush`、`CkanSuccessBrush`、`CkanWarningBrush` 等），杜绝硬编码颜色。

### 3. 测试覆盖（CkanUI.ViewModels.Tests）
- **`DialogsModDetailsTests.cs`**：
  - `ModDetails_版本页列表投影与标记`：验证所有版本列表字段、已装状态、兼容状态、不稳定标记、格式化日期与体积。
  - `ModDetails_内容页已装文件列表与跳转命令`：验证已装文件清单投影、文件计数文本以及点击跳转全局内容预览的命令触发。
  - `ModDetails_页签索引切换与重置`：验证 `SelectedTabIndex` 的切换、初始值与模组重选时的重置状态。

---

## 交互设计与状态反馈

1. **页签标题计数角标**：
   - 依赖关系页签：显示总关系数，如「依赖关系 (5)」。
   - 全部版本页签：显示版本总数，如「全部版本 (12)」。
   - 文件与内容页签：已安装时显示文件数，如「文件与内容 (38)」，未安装时显示默认文本。
2. **状态徽章（Badges）**：
   - 当前版本已安装：使用绿色主题徽章并高亮边框。
   - 兼容版本：以自然标签呈现 KSP 兼容跨度（如 `1.12.0 - 1.12.5`）。
   - 不兼容或不稳定版本：显式标注黄色警告徽章。
3. **内容导航互通**：
   - 模组详情页与全局内容预览页面解耦，通过 `INavigationService` 或 `ExternalNavigateAction` 传递跳转意图，实现跨功能模块平滑联动。
