# F-31 模组列表上下文菜单（Context Menu，UI-18）

## §1 目标与架构定位

为模组主列表（`ModsPageView.axaml` 内的 `DataGrid`）提供右键快捷菜单，使用户能够对当前选中的模组直接执行高频操作：
1. **重新安装（Reinstall）**：将当前已安装的模组以重装动作加入待变更集。
2. **复制模组标识符（Copy Identifier）**：将选中模组的内部标识符（如 `ModuleManager`）复制到系统剪贴板。
3. **复制下载链接（Copy Download URL）**：将选中模组的安装包直接下载链接复制到系统剪贴板。
4. **打开主页（Open Homepage）**：在系统默认外部浏览器中直接导航到模组的主页地址（`resources.homepage`）。

依据系统架构规范，模组视图模型（`ModsPageViewModel`）绝不直接依赖平台特定的剪贴板或外壳启动 API，而是通过委托注入（`ClipboardWriter` 与 `UriLauncher`）实现解耦与测试友好。

## §2 契约与数据流

### 2.1 元数据投影扩充
- 在 `CkanUI.Services.Abstractions.Dtos.ModSearchFacts` 中补充模组级别的事实属性：
  - `string? Homepage { get; init; }`
  - `string? DownloadUrl { get; init; }`
- 在 `CkanUI.Services.Models.ModSearchFactsFactory` 中将上游 `CkanModule.resources.homepage` 及 `CkanModule.download` 首选地址提取并注入 `ModSearchFacts`。
- 在 `CkanUI.ViewModels.Pages.Mods.ModsRowView` 中暴露 `public ModSearchFacts Facts => Data.Facts;`，确保视图模型直接安全访问。

### 2.2 视图模型命令（`ModsPageViewModel`）
- `ReinstallSelectedCommand`：校验 `SelectedRow.IsInstalled`，将其添加入待变更集合 `_selected`，更新统计与应用状态。
- `CopySelectedIdentifierCommand`：若宿主已注入 `ClipboardWriter` 则写入，未注入时在状态栏进行友好降级提示。
- `CopySelectedDownloadUrlCommand`：提取 `SelectedRow.Facts.DownloadUrl` 并写入剪贴板。
- `OpenSelectedHomepageCommand`：提取 `SelectedRow.Facts.Homepage` 并调用 `UriLauncher` 打开系统默认浏览器。
- 可用性属性：
  - `bool CanReinstallSelected => SelectedRow is { IsInstalled: true };`
  - `bool CanOpenHomepage => !string.IsNullOrWhiteSpace(SelectedRow?.Facts.Homepage);`

### 2.3 视图挂接（`ModsPageView.axaml` 与 `.cs`）
- `DataGrid.ContextMenu` 绑定上述命令与可用性控制。
- 视图 Loaded 时调用 `WireContextMenuLaunchers()`，从 `TopLevel.GetTopLevel(this)` 自动绑定 `Clipboard.SetTextAsync` 与 `Launcher.LaunchUriAsync`。

## §3 测试覆盖与验证

测试位于 `tests/CkanUI.ViewModels.Tests/ModsContextMenuTests.cs`，包含 10 例高强度单元测试：
- `CanReinstallSelected_ReflectsWhetherSelectedRowIsInstalled`
- `CanOpenHomepage_ReflectsWhetherSelectedRowHasHomepage`
- `ReinstallSelected_WhenNotInstalled_ShowsStatusMessage`
- `ReinstallSelected_WhenInstalled_AddsToPendingChanges`
- `CopySelectedIdentifier_WithoutClipboard_SetsFallbackStatus`
- `CopySelectedIdentifier_WithClipboard_WritesIdentifier`
- `CopySelectedDownloadUrl_NoUrl_ShowsNoDownloadLinkStatus`
- `CopySelectedDownloadUrl_WithClipboard_WritesUrl`
- `OpenSelectedHomepage_NoHomepage_ShowsNoHomepageStatus`
- `OpenSelectedHomepage_WithLauncher_InvokesLauncher`
