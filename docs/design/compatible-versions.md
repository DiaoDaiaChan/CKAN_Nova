# F-14 兼容游戏版本管理设计文档（Compatible Game Versions, UI-11）

## 1. 概述与设计背景
在 Kerbal Space Program (KSP) 生态中，大量的模组并不严格依赖特定小版本补丁更新（例如为 KSP 1.12.3 构建的纯零件包或轻量插件在 1.12.5 下完全能够稳定运行）。然而，CKAN 官方元数据中通常会声明模组兼容的最高与最低版本范围。当玩家的游戏实例版本高于模组声明的版本时，CKAN 默认会将该模组标记为不兼容并隐藏或禁止直接安装。

为了满足玩家在确保自主可控的前提下安装此类模组的需求，CKAN Core 提供了游戏实例兼容版本（`GameInstance.CompatibleVersions`）配置机制。本项目（F-14 / UI-11）旨在为现代化 Avalonia 架构的 `ckan-ui` 交付一套清晰、易用且具备强风险防范的兼容游戏版本管理对话框与相关服务。

## 2. 需求与核心用例
1. **已知版本加载与呈现**：
   - 自动检测并展示当前所选游戏实例的真实原生版本（Real Version，例如 `1.12.5`）。原生版本作为游戏物理基础，在界面上始终勾选且只读锁定，不可取消。
   - 聚合呈现该游戏对应的全部已知发布版本（`Game.KnownVersions`）。
   - 自动推导并提供主/次版本通配符选项（例如根据 1.12 系列推导出 `1.12` 通配符）。
2. **通配与自由追加版本**：
   - 用户可勾选/取消勾选任意已知版本或通配符版本。
   - 提供自定义输入框，允许用户自由追加特定版本号（例如 `1.10` 或 `1.11.2`）。若追加的版本已存在，自动高亮并设置为勾选状态；若不存在则加入列表并立即勾选。
   - 提供“全选”、“重置”等快捷辅助操作，重置可恢复至对话框打开时的初始状态。
3. **变更风险警示机制**：
   - 跨版本运行模组存在潜在的游戏崩溃或存档损坏风险。
   - 当用户对兼容版本做出变更且保存时，必须触发显式的风险提示。支持宿主注入的交互弹窗确认，或者视图内部显式呈现醒目的警告横幅（Warning Banner），只有用户知悉风险并二次确认后方可提交持久化。
4. **状态持久化与兼容性重算**：
   - 调用 CKAN Core 的 `GameInstance.SetCompatibleVersions` 方法将所选版本持久化至当前游戏实例配置中。
   - 保存成功后，联动触发 `IRegistryService.RefreshAsync()` 与 `IRepositoryService.LoadCachedAsync()`，使模组列表、可用版本、安装兼容性判定即刻刷新生效。

## 3. 架构分层与工程实现
严格遵守 `ckan-ui` 架构契约与分层规范：

```
[ CkanUI.App (Avalonia UI 层) ]
  └── CompatibleVersionsDialogView.axaml (.cs)
        │ DataBinding
        ▼
[ CkanUI.ViewModels (MVVM 表现层) ]
  └── CompatibleVersionsDialogViewModel.cs
  └── CompatibleVersionItemViewModel.cs
        │ 依赖倒置（只引用 Abstractions）
        ▼
[ CkanUI.Services.Abstractions (契约层) ]
  └── ICompatibleVersionsService.cs
  └── CompatibleVersionsDtos.cs
        ▲
        │ 实现
[ CkanUI.Services (领域服务层) ]
  └── CkanCompatibleVersionsService.cs (位于 Services/Compatibility/)
        │ 调用
        ▼
[ CKAN Core & Registry ]
  └── GameInstance.SetCompatibleVersions / CompatibleVersions / Game.KnownVersions
```

### 3.1 契约与 DTO (`CkanUI.Services.Abstractions`)
- `CompatibleVersionItemDto`：描述单个版本的版本字符串、选中状态、原生版本标记、通配符标记。
- `CompatibleVersionsSnapshot`：聚合实例名、真实原生版本及版本列表的不可变只读快照。
- `ICompatibleVersionsService`：提供 `LoadAsync` 与 `SaveAsync` 两个核心异步操作。

### 3.2 领域服务实现 (`CkanUI.Services/Compatibility`)
- 类：`CkanCompatibleVersionsService`
- 职责：
  1. 通过 `Lazy<GameInstanceManager>` 定位当前活动实例或指定实例。
  2. 提取原生版本 `instance.Version()`、已知版本集合 `instance.Game.KnownVersions` 与当前已配置列表 `instance.CompatibleVersions`。
  3. 基于版本号主次字段推导 `${Major}.${Minor}` 通配版本。
  4. 将版本项格式化为 DTO 快照返回。
  5. 接收选中的版本字符串列表，调用 `instance.SetCompatibleVersions(gameVersions)` 持久化，并自动通知注册表与仓库数据刷新。

### 3.3 表现层 (`CkanUI.ViewModels/Dialogs`)
- `CompatibleVersionItemViewModel`：包装每个版本行项的勾选状态、展示标签（原生版本/主版本通配/已知版本）及只读控制。
- `CompatibleVersionsDialogViewModel`：
  - 继承自 `DialogViewModelBase`，标准化对话框声明周期与按钮操作。
  - 支持设计器/无服务模式回退演示数据。
  - 管理输入框文本、校验提示 `ValidationError`、保存中状态 `IsSaving` 以及风险确认横幅状态 `IsWarningPromptVisible`。
  - 支持通过委托 `WarningConfirmationHandler` 接入外壳通用弹窗服务，或纯本地就地警示交互。

### 3.4 视图层 (`CkanUI.App/Views/Dialogs`)
- `CompatibleVersionsDialogView.axaml`：
  - 严格采用语义 Design Token（如 `CkanSurfaceBackgroundBrush`, `CkanBorderBrush`, `CkanPrimaryBrush`, `CkanWarningBackgroundBrush`, `CkanDangerBrush` 等），无硬编码颜色。
  - 使用 `DialogWindow` 基类与标准网格布局，包含顶部标题说明、实例与原生版本卡片、自定义输入与操作栏、可滚动的版本选择器、动态警示/错误区及底部操作按钮。

## 4. 测试与验证策略
针对 `CompatibleVersionsDialogViewModel` 编写完整的单元测试套件（位于 `tests/CkanUI.ViewModels.Tests/CompatibleVersionsDialogTests.cs`），覆盖 13 组测试用例：
1. **版本数据加载与解析**：验证快照反序列化后原生版本、通配版本、已知版本的正确分类与展示标签。
2. **原生版本保护**：验证真实版本只读锁定且不可被用户取消勾选。
3. **无服务与异常恢复**：验证设计器兜底数据的呈现，以及服务抛出异常时的 `ValidationError` 友好提示。
4. **自定义版本录入**：
   - 验证有效版本号的追加（新增插入列表并勾选）。
   - 验证输入已存在版本时的高亮与选中联动。
   - 验证空白字符或非法输入的即时校验阻止。
5. **全选与重置功能**：
   - 验证一键全选所有非原生版本。
   - 验证一键取消全选时原生版本不受影响。
   - 验证“重置”按钮精准还原至初始加载快照。
6. **风险提示与二次确认流程**：
   - 验证未做任何变更时直接保存，不触发警示。
   - 验证发生版本变更但未提供外部处理器时，自动激活内置警示横幅。
   - 验证外部确认委托取消时不保存，确认后正常保存并传递版本集合。
   - 验证保存服务异常时的错误提示与界面维持。

## 5. 验收结果
- 解决方案编译：`dotnet build ckan-ui.sln` 0 错误 0 警告成功。
- 完整测试验证：`dotnet run --project tests/CkanUI.TestRunner --no-build` 全部 1354 个测试通过（覆盖 ViewModels, App, Services, Core, Localization, Sfs 等全部模块）。
