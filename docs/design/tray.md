# 系统托盘图标（F-51 / UI-26）

## 目标与范围

给 CKAN UI 加一个常驻托盘图标：关掉窗口也能继续刷仓库/看进度，右键菜单直接跳日志、缓存、实例目录。
范围只到「图标 + 菜单 + 两个开关的生效口径」，不含开机自启、不含托盘气泡通知、不含托盘内嵌进度条。

**做**

- 一个托盘图标，左键单击 = 显示主窗口。
- 7 项右键菜单（顺序固定，见下表）。
- 设置页「高级与诊断」组两个开关：最小化到托盘、关闭到托盘，跟随 `UiSettings` 持久化。
- 无托盘环境（远程会话、精简桌面、平台不支持）安全降级：窗口回到「最小化留在任务栏、关闭即退出」。

**不做**

- 托盘图标不显示下载进度、不弹气泡。
- 不接管 `ShutdownMode`；退出只走菜单「退出」或窗口关闭（且关闭到托盘关掉时）。

## 分层与文件表

视图模型层不认识 Avalonia，App 层不认识「菜单该长什么样」—— 两侧靠两个窄接口对接。

| 层 | 文件 | 职责 |
| --- | --- | --- |
| 契约 | `src/CkanUI.Services.Abstractions/IUiSettingsStore.cs` | `UiSettings` 追加 `MinimizeToTray` / `CloseToTray`（`bool`，默认 `false`），手写 `Equals`/`GetHashCode` 同步 |
| VM | `src/CkanUI.ViewModels/Shell/ITrayHostActions.cs` | 宿主窄接口：`ShowWindow` / `HideWindow` / `OpenLogFolder` / `OpenCacheFolder` / `OpenInstanceFolder` / `RefreshRepositories` / `ExitAsync` |
| VM | `src/CkanUI.ViewModels/Shell/TrayMenuItemViewModel.cs` | 单项：`Id` / `Header` / `StartsGroup` / `Command`（`IAsyncRelayCommand`）/ `IsBusy` / `LastError` |
| VM | `src/CkanUI.ViewModels/Shell/TrayMenuViewModel.cs` | 7 项集合 + 7 条命令 + `StatusText` + `HideOnMinimize` / `HideOnClose` + `ApplySettings` |
| App | `src/CkanUI.App/Tray/TrayHostActions.cs` | `ITrayHostActions` 的真实实现：窗口显隐、开目录、刷仓库、退出 |
| App | `src/CkanUI.App/Tray/TrayHost.cs` | 装/卸 `TrayIcon` + `NativeMenu`，订阅 `UiSettingsStore.Changed` |
| 设置页 | `src/CkanUI.ViewModels/Pages/SettingsPageViewModel.cs`、`Pages/Settings/SettingsKeyNames.cs` | 「高级与诊断」组追加两个布尔项，键名 `MinimizeToTray` / `CloseToTray` |

## 关键口径

### 菜单项

| # | 文案 | 动作 | 分组线 | 失败语义 |
| --- | --- | --- | --- | --- |
| 1 | 显示主窗口 | `Show()` + `WindowState=Normal` + `Activate()` | — | 无窗口时静默（进程尚未建窗） |
| 2 | 隐藏到托盘 | `Window.Hide()` | — | 同上 |
| 3 | 打开日志目录 | `Process.Start(UseShellExecute)` | 有 | `日志目录打开失败：<原因>` |
| 4 | 打开缓存目录 | 同上，目录来自缓存设置 | — | `缓存目录打开失败：<原因>` |
| 5 | 打开当前实例目录 | 同上，目录来自当前实例 | — | `实例目录打开失败：<原因>` |
| 6 | 刷新仓库 | `IRepositoryService.RefreshAsync` | 有 | `刷新仓库失败：<原因>`，保留 `Retryable` |
| 7 | 退出 | `Application.Shutdown` | 有 | 无（退出即终态） |

- 文案是中文常量，**不进语言包**：原生菜单由 OS 绘制，拿不到 Avalonia 的语言切换重绘时机；等原生菜单支持重建时再迁移。
- 「隐藏到托盘」用 `Hide()` 而**不是** `Close()`：App 的 `ShutdownMode` 是 `OnMainWindowClose`，`Close()` 会直接退进程。
- 菜单项不可执行时（无宿主），`Command.CanExecute` 为 `false`，原生菜单自动置灰；点击不会进方法体。
- 动作抛异常一律在 `TrayMenuItemViewModel.ExecuteAsync` 收口成 `LastError = "执行「<文案>」失败：<message>"`，不让异常逸出到 OS 回调线程（逸出会静默杀掉菜单响应）。

### 最小化/关闭到托盘

判定只有一条式子，两个开关的投影都要求托盘真的可用：

```
HideOnMinimize = IsTrayAvailable && UiSettings.MinimizeToTray
HideOnClose    = IsTrayAvailable && UiSettings.CloseToTray
```

- `IsTrayAvailable` 由 `TrayHost` 在创建图标成功/失败时写入；默认 `true` 便于单测与设计时预览。
- 开关值本身不因托盘不可用而改写：设置页照常显示用户勾选的样子，只是不生效。理由是把用户的意图留在磁盘上，换个桌面环境（托盘恢复可用）就自动恢复行为。
- 两个开关都关时保持老行为：最小化留在任务栏，关闭退出进程。
- 设置保存后由 `IUiSettingsStore.Changed` → `TrayHost` → `TrayMenuViewModel.ApplySettings` 传到判定处，中间不缓存第二份真源。

### 图标

不新增二进制资源：`TrayHost.CreateIconImage()` 用 `RenderTargetBitmap`（32×32）+ `DrawingContext.DrawRectangle` 现画一个蓝色圆角方块加三道白色横杠，`Save(stream, PngBitmapEncoderOptions.Default)` 编码后塞进 `WindowIcon`。画不出来（没有渲染后端）就返回 `null`，托盘退回平台默认图标——图标丑一点不影响功能。

## 安全与失败语义

| 场景 | 行为 |
| --- | --- |
| 设置文件损坏 / `Load()` 抛异常 | `TrayHost.SafeLoad()` 吞掉，按默认值（两个开关都关）走，不阻断启动 |
| 创建 `TrayIcon` 抛异常（无托盘环境） | `Install()` 捕获 → 摘掉半成品图标 → `SetTrayAvailable(false)` → 两个开关不生效 |
| 平台已回收图标，摘除时抛异常 | `RemoveIcon()` 捕获后忽略，仍 `Dispose()` 释放 |
| 目录不存在 / 路径为空 | 返回 `OperationResult.Fail("目录不存在：<path>（不可重试）")`，不抛 |
| 缓存目录取不到值 | 先问注入的取值委托，再读核心设置 `DownloadCacheDir`（`ISettingsService`）；两条都空就报「服务层暂时给不出路径」，**不猜** `%LocalAppData%` 之类的默认位置 |
| 日志目录取不到值 | 同上口径；默认接线用 `SettingsPaths.DefaultConfigDirectory`（与 `LoggingSetup` 同源，见 `src/CkanUI.Services/Logging/LoggingSetup.cs:83`） |
| 刷新仓库失败 | 透传服务层 `OperationResult` 的 `Message` 与 `Retryable`，状态栏给中文前缀 |
| 刷新仓库抛异常 | 收口到菜单项 `LastError`，`IsBusy` 复位 |
| 设置保存发生在后台线程 | `Changed` 回调经 `IDispatcher.Post` 回到 UI 线程再改状态 |
| 重复 `Install()` | 幂等：第二次直接返回，不会出现两个图标 |

安全边界：三个「打开目录」只 `Process.Start` 已存在的目录，不拼命令行参数、不读用户输入拼路径，因此没有参数注入面。托盘菜单不暴露任何写操作（除「刷新仓库」这一条只读网络请求）。

## 已知缺口

1. **App 外壳接线由 Lead 落地**（本分支未改 `App.axaml.cs` / `MainWindow.axaml.cs`）：`TrayHost` 需要在主窗口建好后 `Install()`，并在 `MainWindow` 的 `Closing`/`StateChanged` 里读 `menu.HideOnClose` / `menu.HideOnMinimize` 决定是否 `e.Cancel = true` + `Hide()`。接线行见交付报告。
2. **DI 注册**：`TrayMenuViewModel` / `TrayHostActions` / `TrayHost` 需要进 `src/CkanUI.App/ServiceRegistration.cs`，本分支未改（Lead 独占）。
3. **文档登记**：`docs/README.md` 需要加本文件的相对链接（`tools/verify-layout` 的 H-05 检查，警告级）。
4. **没有 GUI 会话的自动化验证**：`TrayHost` 只做了「能编译 + VM 层单测」，真正的图标出现/菜单点击/开关联动需要在有桌面的机器上手测；`dotnet test` 在本沙箱不可用，测试走 `CkanUI.TestRunner`。
5. **菜单文案不走语言包**：切到英文界面时托盘菜单仍是中文，与设置页不一致（原因见上，等原生菜单可重建再补）。
6. **图标是现画的几何图形**，不是设计稿资产；视觉上只做到「能认出是 CKAN」，等设计给 SVG 再替换 `CreateIconImage()` 一处即可。
7. **开机自启 + 启动即最小化到托盘**未实现：需要「启动参数 + 首帧隐藏」的配套，属于后续条目。
8. **多显示器 / DPI 变化**时托盘图标不重绘：32×32 固定尺寸在高 DPI 下会被系统缩放，可能略糊。
