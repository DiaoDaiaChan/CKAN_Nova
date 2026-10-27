# F-15 / UI-30 首选下载主机可视化管理 (Preferred Download Hosts Dialog) 设计文档

## 1. 概述与背景
在 CKAN 生态中，模组通常挂载在多个分发平台（例如 GitHub Releases、SpaceDock、Internet Archive、GitLab 等）。当网络环境不稳定或访问特定平台较慢时，用户希望调整下载主机的优先级，使得模组优先尝试从首选平台获取。

原有 GUI 具备基本的首选主机设置与 Core 层的 `IConfiguration.PreferredHosts` 读取/持久化，但在全新 MVVM 客户端架构下，需要提供遵循响应式、无障碍与语义 Token 设计的可视化管理弹窗，支持主机的上下移动重排序、新增已知或自定义主机、删除、重复项校验及回写保存。

## 2. 架构设计与分层契约

### 2.1 契约与 DTO (`CkanUI.Services.Abstractions`)
- **零第三方包依赖**：纯 C# 抽象，仅引用基础 BCL。
- **DTO**: `HostPriorityItemDto(string Host, int Priority, bool IsCustom)`
  - 承载主机名、排序优先级序数、是否为自定义主机的元数据。
- **服务契约**: `IPreferredHostsService`
  - `Task<IReadOnlyList<HostPriorityItemDto>> GetPreferredHostsAsync(CancellationToken cancellationToken = default);`
  - `Task<IReadOnlyList<string>> GetKnownHostsAsync(CancellationToken cancellationToken = default);`
  - `Task<OperationResult> SavePreferredHostsAsync(IReadOnlyList<string> hosts, CancellationToken cancellationToken = default);`

### 2.2 服务层实现 (`CkanUI.Services`)
- **`PreferredHostsService`**:
  - 依赖 `ISettingsService`、`GameInstanceManager`（可选）与 `RepositoryDataManager`（可选）。
  - **默认预设已知主机**：包含 `github.com`, `spacedock.info`, `archive.org`, `gitlab.com`, `curseforge.com` 等主流模组托管平台。
  - **动态发现**：从当前实例的注册表或仓库数据中提取已收录的主机名，与内置预设去重合并。
  - **配置读写**：通过 `ISettingsService.GetPreferredHostsAsync()` 与 `SetPreferredHostsAsync()` 保证设置键的强类型序列化与持久化。

### 2.3 视图模型层 (`CkanUI.ViewModels`)
- **`PreferredHostItemViewModel`**:
  - 继承自 `ObservableObject`，包装主机名、当前优先级次序及是否为内置已知主机的标记。
- **`PreferredHostsDialogViewModel`**:
  - 继承自 `DialogViewModelBase`，复用通用弹窗基础设施。
  - 状态管理：
    - `Items`: `ObservableCollection<PreferredHostItemViewModel>`，维护实时有序首选主机列表。
    - `KnownHosts`: `ObservableCollection<string>`，提供候选已知主机列表。
    - `SelectedHost`: 当前选中的列表行。
    - `NewHostInput`: 用户新增主机输入框文本。
    - `ValidationError`: 校验错误信息反馈（如空主机、格式非法、重复主机）。
  - 命令交互：
    - `MoveUpCommand` / `MoveDownCommand`: 边界安全的次序调整（首项不可上移，末项不可下移）。
    - `RemoveCommand`: 移除指定或选中的主机。
    - `AddCommand`: 校验并新增主机（自动清除首尾空白与前后缀协议/斜杠，防止重复添加）。
    - `AddKnownHostCommand`: 快速从已知列表中选取并追加。
    - `SaveAsync()` / `PrimaryCommand`: 异步回写并关闭弹窗。

### 2.4 视图层 (`CkanUI.App`)
- **`PreferredHostsDialogView.axaml`**:
  - 继承 `DialogWindow`，窗口尺寸设计为 520x460，适配各种 DPI 缩放。
  - 100% 绑定 `{DynamicResource Ckan*}` 语义 Token（如 `CkanBrushText`, `CkanBrushSubtext`, `CkanBrushSurfaceCard`, `CkanBrushBorderDefault`, `CkanRadiusMd`, `CkanBrushDanger` 等）。
  - 双通道无障碍体验：列表每行均显示序号徽标（如 #1, #2）与主机域名，不依赖单一色彩表达层级。
  - 完整的上移、下移、移除快捷操作按钮与下部新增控制栏。

## 3. 测试覆盖与质量验证
- **服务层测试** (`PreferredHostsServiceTests.cs`):
  - 空配置与默认主机提取。
  - 仓库/注册表动态提取已知主机与去重。
  - 保存回写与空值过滤。
- **视图模型单测** (`PreferredHostsDialogViewModelTests.cs` - 11 例单测):
  - 初始化加载首选列表与已知列表。
  - 选中行并上移、下移操作及边界条件验证。
  - 移除主机行。
  - 新增主机合法性、清理协议前缀（如 `https://`）与路径尾缀。
  - 重复主机拒绝并设置校验错误信息。
  - 保存回写至服务层并触发弹窗关闭。
