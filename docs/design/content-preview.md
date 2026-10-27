# 内容预览页（F-29 / UI-03）

## 目标与范围
提供模组 ZIP 包内的目录与文件树形结构查看功能；对尚未缓存的模组提供一键下载到本地缓存的能力。仅解析归档中央目录（Central Directory），不解压完整文件内容至磁盘，以保障内存与 I/O 效率。

---

## 架构分层与文件清单

### 1. 契约与 DTO 层（CkanUI.Services.Abstractions）
- **`IContentService.cs`**：
  - `PreviewAsync(string identifier, CancellationToken ct = default)`：读取指定模组 ZIP 并返回文件/目录条目列表。
  - `EnsureCachedAsync(string identifier, IProgress<OperationProgress>? progress = null, CancellationToken ct = default)`：未缓存时拉取下载并存入本地缓存。
- **`IContentTargetProvider.cs`**：
  - 宿主注入点，向内容预览页提供当前选中的模组标识符（`SelectedId`）以及变更事件（`SelectedIdChanged`）。
- **`Dtos/ContentPreview.cs`**：
  - `ContentPreview(string Identifier, bool IsCached, string? CachedPath, long TotalBytes, IReadOnlyList<ContentEntry> Entries)`
  - `ContentEntry(string RelativePath, bool IsDirectory, long SizeBytes, int Depth)`
  - 零项目引用、零包依赖、零第三方类型。

### 2. 服务实现层（CkanUI.Services）
- **`Content/IContentZipStore.cs`**：
  - 隔离 ZIP 缓存查询与网络拉取，便于离线测试与单元测试注入。
- **`Content/NetModuleCacheZipStore.cs`**：
  - 对接 CKAN `GameInstanceManager.Cache`（`NetModuleCache`）与 `IDownloadService`。
  - 负责模组元数据匹配、缓存路径推断与下载落地。
- **`Content/ContentService.cs`**：
  - 通过 `System.IO.Compression.ZipArchive` 遍历 ZIP 中央目录。
  - 自动补全中间隐式目录，统一斜杠分隔符（`/`），安全阻断 `.` / `..` 路径遍历。
  - 限制最大条目数 20,000，限制单文件展示大小 1 PiB，汇总未压缩总大小。

### 3. 视图模型层（CkanUI.ViewModels）
- **`Pages/Content/ContentNodeViewModel.cs`**：
  - 树形节点 ViewModel，支持 `Name`、`RelativePath`、`IsDirectory`、`SizeBytes`、`SizeText`、`IsExpanded`、`Children`。
- **`Pages/Content/ContentTreeBuilder.cs`**：
  - 将服务层返回的扁平 `ContentEntry` 集合转换为层次树结构；目录在前、同类型按名称排序；默认展开第一级。
- **`Pages/Content/IContentShellLauncher.cs`**：
  - 剪贴板与操作系统文件管理器操作的解耦接口，提供无宿主时的安全降级。
- **`Pages/ContentPageViewModel.cs`**：
  - 继承 `PageViewModel`，实现 `IActivatable`，Title 为中文「内容预览」。
  - 包含 `PreviewAsync`、`DownloadAsync`、`RetryAsync`、`Cancel`、`ClearError`、`CopyPathAsync`、`OpenDirectoryAsync` 等命令与状态属性。
  - 提供完善的中文空状态、加载中状态、未缓存状态、错误横条提示与命令降级反馈。

### 4. 视图层（CkanUI.App）
- **`Views/ContentPageView.axaml`**：
  - 采用 Avalonia `TreeView` 与 `HierarchicalDataTemplate` 呈现可折叠树形结构。
  - 严格使用 `{DynamicResource Ckan*}` 语义 Token，杜绝硬编码颜色。
- **`Views/ContentPageView.axaml.cs`**：
  - 代码隐藏，挂载平台原生剪贴板（`TopLevel.Clipboard`）与进程管理器打开目录。

---

## 条目树语义

1. **扁平条目收集与规范化**：
   - `ContentService` 读取 ZIP 中央目录中的条目名称，统一正斜杠 `/`，去除首尾斜杠。
   - 文件路径中的父级路径自动解析并补齐中间目录条目（例如 `GameData/Mod/a.dll` 自动生成 `GameData` 与 `GameData/Mod` 目录）。
   - 目录条目的 `IsDirectory` 为 `true`，大小计为 0。文件条目记录真实未压缩字节数。
2. **层次树结构构建（ContentTreeBuilder）**：
   - 逐级拆分路径片段，自根至叶逐级插入 `ContentNodeViewModel`。
   - **排序规则**：每一层级中，目录始终排在文件之前；同为目录或文件时按名称字母序不区分大小写排序。
   - **展开策略**：根节点默认 `IsExpanded = true`，方便用户快速纵览一级目录；深层级由用户按需折叠/展开。
   - **大小格式化**：叶子文件展示格式化大小（如 `1.2 MB`、`45 KB`）；目录节点保留原始条目语义。

---

## 下载链路与缓存原子存储

### 为什么先下到临时文件再 Store？
在 `NetModuleCacheZipStore.EnsureCachedAsync` 中：
1. **目标文件名未知**：CKAN 的 `NetModuleCache` 仅向外公开了 `GetCachedFilename(CkanModule)`，该方法只对**已存在于缓存中**的文件返回有效路径；在下载前无法获知最终带有 SHA 散列标记的标准缓存文件名。
2. **校验与防损坏**：CKAN `NetModuleCache.Store(module, path, ..., move: true, validate: true)` 负责执行 ZIP 散列（SHA-1/SHA-256）与完整性校验。如果直接下载到目标缓存文件，中途断网或文件损坏会留下残缺的脏文件，破坏缓存一致性。
3. **安全下载流程**：
   - 先调用 `IDownloadService.DownloadAsync` 下载至缓存目录下的临时分卷文件：`<DownloadCacheDir>/<identifier>-<guid>.zip.part`。
   - 下载成功且完成后，调用 `cache.Store(..., move: true, validate: true)` 校验并原子移动进缓存库。
   - 若下载失败或校验抛出异常，主动在 `finally/catch` 中清理临时 `.zip.part` 文件，确保零脏数据残留。

---

## 关闭项与验证

- [x] **契约与 DTO 纯净性**：`CkanUI.Services.Abstractions` 零包依赖、零项目引用；DTO 无任何 CKAN 类型泄露。
- [x] **服务层单元测试**：`tests/CkanUI.Services.Tests/ContentServiceTests.cs` 共 11 例（≥10 例），覆盖：
  - 空 ZIP
  - 嵌套目录层级与父级补齐
  - 文件名包含空格与非 ASCII（如中日文字符）
  - 根级 `.ckan` 清单文件
  - 损坏与无效数据 ZIP
  - 文件大小与汇总计算
  - 路径遍历防护（`..`、`.` 攻击路径拒绝）
  - 未缓存模组提示拒绝
  - 取消令牌响应（`OperationCanceledException`）
  - 超过最大 20,000 项安全限制
  - 真实临时文件 ZIP 测试（采用 `CkanUI.TestSupport.TempWorkspace`，避免沙箱 `Path.GetTempPath()` 不可写问题）
- [x] **视图模型单元测试**：`tests/CkanUI.ViewModels.Tests/ContentPageViewModelTests.cs` 共 12 例（≥10 例），覆盖：
  - 无目标/未选中模组时的中文空状态
  - 正常加载建立多层级树结构与大小汇总
  - 加载失败展示错误横条并支持重试
  - 未缓存模组下载成功后自动刷新内容
  - 下载失败时恢复界面状态并展示错误
  - 耗时预览支持一键取消操作
  - 复制路径命令在无宿主时的中文降级提示与有宿主时的正常调用
  - 打开目录命令在无宿主时的中文降级提示与有宿主时的正常调用
  - 激活生命周期（`IActivatable.Activate / Deactivate`）事件监听
- [x] **Avalonia 树形视图**：`ContentPageView.axaml` 采用 `TreeView` 折叠树，100% 绑定 `{DynamicResource Ckan*}` Token，支持文件大小右对齐、空状态卡片、未缓存快速下载引导横条与错误重试横条。
- [x] **PageViewModel 规范**：`ContentPageViewModel` 命名与位置符合规范，继承 `PageViewModel`，实现 `IActivatable`，标题为「内容预览」。
