# F-16 / UI-31 安装文件过滤器编辑器 (Install Filters Dialog / Editor)

## 1. 概述与背景
在 CKAN 安装/更新 Mod 过程中，部分发布包可能夹带开发残留文件（如 `.pdb`）、版本控制元数据（如 `.git`）、系统缩略图缓存（如 `Thumbs.db`、`.DS_Store`）、或非游戏必须文档（如 `README.md`、`*.craft`）。
CKAN Core 支持针对当前游戏实例配置全局或实例级过滤器规则集合，在 `ModuleInstaller` 解包部署文件时，通过匹配目标相对路径若包含过滤规则即自动跳过，从而保证安装目录的纯净与安全性。

本模块提供现代化的独立可视化过滤规则管理弹窗（UI-31），支持用户查看、添加、删除自定义规则、恢复预置默认规则，并持久化保存。

## 2. 架构设计与契约

### 2.1 契约层 (src/CkanUI.Services.Abstractions)
`IInstallFiltersService.cs`：
- `Task<IReadOnlyList<string>> LoadAsync(CancellationToken cancellationToken = default)`：加载配置的规则，若无则返回默认内置规则。
- `Task SaveAsync(IReadOnlyCollection<string> filters, CancellationToken cancellationToken = default)`：持久化过滤规则（规范化、去重、去除空串）。
- `IReadOnlyList<string> GetDefaultFilters()`：获取系统内置安全预置项（`.pdb`, `.git`, `.gitignore`, `Thumbs.db`, `.DS_Store`, `*.craft`, `README.md`, `LICENSE.txt`）。

### 2.2 服务实现层 (src/CkanUI.Services/Settings)
`InstallFiltersService.cs`：
- 结合 `CKAN.IConfiguration` 与 `ISettingsService`：
  - 优先读取/写入 Core 的 `IConfiguration.GetGlobalInstallFilters(game)` / `SetGlobalInstallFilters(game, filters)`。
  - 同时同步序列化为 JSON 存入 `ISettingsService`（Key: `"GlobalInstallFilters"`），支持解耦运行。
- 提供内置默认规则预置与字符串清洗（Trim 与不区分大小写去重）。

### 2.3 视图模型层 (src/CkanUI.ViewModels/Dialogs)
`InstallFiltersDialogViewModel.cs` 继承自 `DialogViewModelBase`：
- `Title`: "安装文件过滤器"
- `PrimaryButtonText`: "保存"
- `SecondaryButtonText`: "取消"
- `NewFilterText`: 供用户输入新规则的文本属性。
- `AddFilterCommand`: 添加规则，校验非空与重复性。
- `RemoveFilterCommand`: 删除单条规则。
- `ResetDefaultsCommand`: 恢复默认规则预置。
- `OnPrimaryAsync()`: 提交保存并关闭弹窗（`Close(true)`）。
- `FilterItemViewModel`: 封装单条规则对象及其行内删除动作。

### 2.4 视图层 (src/CkanUI.App/Views/Dialogs)
`InstallFiltersDialogView.axaml`：
- 采用 4 行 Grid 结构：Header 与说明 + 输入与添加按钮、规则列表 Border + ListBox、验证信息 TextBlock、底部操作栏（恢复默认规则、取消、保存）。
- 100% 绑定 `{DynamicResource Ckan*}` 语义 Token（`CkanDialogBackgroundBrush`, `CkanCardBackgroundBrush`, `CkanBorderBrush`, `CkanTextPrimaryBrush`, `CkanTextSecondaryBrush`, `CkanDangerBrush` 等）。
- 支持 Enter 快捷键快速添加规则。

## 3. 测试覆盖
- `InstallFiltersServiceTests.cs`：
  - 默认规则有效性校验。
  - Core 配置读取与规范化去重。
  - SettingsService JSON 格式解析与去重。
  - 保存时双写校验及参数空异常验证。
- `InstallFiltersDialogViewModelTests.cs`：
  - 构造函数空参数检查及属性初始化测试。
  - `InitializeAsync` 数据加载与异常处理测试。
  - `AddFilter` 空值校验、重复值忽略/错误提示测试。
  - `RemoveFilter` 删除指定条目测试。
  - `ResetDefaults` 恢复默认规则测试。
  - `PrimaryCommand` 保存数据并关闭对话框，以及保存异常时的错误提示测试。
