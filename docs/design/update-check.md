# F-52 自动更新与新版本检查器设计与实现 (UI-27)

## §1. 架构定位与契约抽象

自动更新模块严格遵循 MVVM 模式与分层隔离守卫，定义于 `src/CkanUI.Services.Abstractions`：
- `UpdateCheckResult` (DTO)：承载更新检查结果：
  - `HasUpdate`：是否存在更高版本
  - `CurrentVersion`：当前本地运行版本
  - `LatestVersion`：远端最新版本
  - `ReleaseNotes`：版本更新日志文本
  - `ReleaseUrl`：对应版本的发布页面或下载地址
  - `IsDevBuild`：是否为开发预览构建 (Dev Build)
- `IUpdateCheckService` (服务契约)：
  - `CheckForUpdatesAsync(bool includeDevBuilds = false, CancellationToken ct = default)`：无异常泄露的异步检查方法，统一返回 `OperationResult<UpdateCheckResult>`。

## §2. 服务实现层 (`CkanUI.Services/Update/UpdateCheckService.cs`)

1. **多数据源协同**：
   - **GitHub Releases API** (`https://api.github.com/repos/KSP-CKAN/CKAN/releases/latest`)：获取正式稳定版。
   - **S3 Dev Builds Feed** (`https://ksp-ckan.s3-us-west-2.amazonaws.com/version.json`)：当开启 `DevBuilds` 开关时优先比对开发版本。
2. **版本比对引擎**：
   - 提取语义版本号与修订号，支持带前缀的 tag（如 `v1.32.0` vs `1.32.0`）。
   - 本地版本等于或高于远端时平滑识别为 `HasUpdate: false`。
3. **错误与网络容灾**：
   - 所有 HTTP 请求异常、超时与 JSON 畸变均收口并返回友好的 `OperationResult.Fail`，不会向表现层抛出未捕获异常。

## §3. 表现层与交互 (`UpdateAvailableDialogViewModel` / `UpdateAvailableDialogView`)

- **弹窗交互**：
  - 继承 `DialogViewModelBase`，提供 `OpenReleaseUrlCommand`（跳转浏览器）与 `CloseCommand`（稍后提醒）。
  - 对话框视图完全遵循 `DynamicResource Ckan*` 主题调色板，展示当前版本 vs 最新版本、构建类型与格式化日志。
- **设置页集成**：
  - `SettingsPageViewModel` 注入 `IUpdateCheckService`。
  - 在常规设置组提供「立即检查更新」命令（`CheckForUpdatesCommand`），检查成功若有新版本则异步调起弹窗；无新版本则就地反馈状态栏。

## §4. 测试验证与质量保证

1. **服务单测** (`UpdateCheckServiceTests.cs`，7 场景)：
   - 包含稳定版更新、开发版检测、本地已最新、本地更高、网络异常、JSON 损坏安全降级等测试。
2. **表现层单测** (`UpdateDialogViewModelTests.cs`，7 场景)：
   - 包含属性对齐、外部跳转委托唤起、无跳转委托降级、关闭对话框、设置页命令联动等测试。
