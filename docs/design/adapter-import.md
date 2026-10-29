# Adapter 导入与更新检查（ADP-04）

> 规格来源：本段任务书 ADP-04。与 `docs/design/mod-config-adapters.md` §1「明确不做」互补：本文补上「远程 adapter 导入 / 哈希展示 / 检查更新」的**可离线验证**机制。
> 状态：服务层完成（契约 + `AdapterInstaller` + `HttpAdapterPackageFetcher` + 29 个用例）；UI 与 DI 接线未做（见 §9）。
> 边界：本机无 KSP、无外网，因此**不写真实 mod 的字段表**、不做真实远端联调；HTTP 全部用注入 `HttpMessageHandler` 的假实现验证。
## 1. 要解决的问题
`adapters/` 目前只有 KSPCF 三个键与 Kerbalism 空骨架，而且没有「把第三方 adapter 装进来」的路径：用户只能自己往 `%LocalAppData%\CKAN\ui\adapters\<id>\` 手抄文件，抄坏了也看不出抄的是哪一版、有没有被改过。这一段只做三件可离线验证的事：
1. **导入**：从本地文件或 https 地址，把 `.json`（单个 adapter）或 `.zip`（adapter + 附带文件）装进用户 adapter 目录。
2. **留痕**：算 SHA-256 写进 `installed.json`，界面能显示「装的是哪份字节」。
3. **查更新**：带 ETag 条件请求远端，回答「没更新 / 有更新」。
## 2. 来源类型
`AdapterPackageSource`（`src/CkanUI.Services.Abstractions/ModConfig/AdapterPackageModels.cs`）只提供两条工厂方法，避免造出 Kind 与内容不一致的对象：

| 工厂 | Kind | Location | 说明 |
| --- | --- | --- | --- |
| `FromFile(path, entryPoint?)` | `LocalFile` | 绝对或相对路径 | 直接读盘，不走网络 |
| `FromUrl(uri, entryPoint?)` | `RemoteUrl` | `uri.ToString()` | 只接受绝对 http/https；其余抛 `ArgumentException`（构造期错误，不是服务层失败） |

`entryPoint` 是 zip 内的入口相对路径，`.json` 来源时忽略；为 null 表示「自己找唯一的 `adapter.json`」。格式不靠扩展名判断：`AdapterPackageReader.DetectFormat` 跳过 BOM 与空白后看首字节——`{` 判 JSON，`PK` 魔数判 ZIP，都不像才回落到 `.zip` 扩展名。
## 3. 校验顺序（自外向内，任一步失败即返回、绝不落盘）
1. `adapter-source-missing` / `adapter-source-unreadable`：本地文件不存在或读不动。
2. `adapter-package-empty`、`adapter-package-too-large`：空包，或包体超过 `MaxPackageBytes`（默认 16 MiB；远端先看 `Content-Length` 再边读边数，超了中断）。
3. ZIP 专用（`AdapterPackageReader.ReadZip`）：`adapter-zip-invalid`（非法 zip / 压缩方法不支持）、`adapter-zip-empty` 与 `adapter-zip-too-many-entries`（上限 `MaxEntryCount` 默认 512）、`adapter-zip-slip`（条目名归一化失败：绝对路径、含 `..` 段、含盘符或流名冒号——**整包拒绝**，不跳过不重命名）、`adapter-zip-too-large`（按 zip 头声明的 `Length` 累加超过 `MaxExtractedBytes` 默认 32 MiB，**解压之前**就拒绝，防解压炸弹）、`adapter-zip-entry-missing` / `adapter-zip-no-adapter-json` / `adapter-zip-ambiguous-adapter-json`（入口选择）、`adapter-zip-entry-outside-root`（条目落在入口目录之外，见 §7-1）。
4. 入口文件本身超过 `MaxAdapterJsonBytes`（默认 1 MiB）→ `adapter-package-too-large`。
5. `adapter-schema-invalid`：把入口 JSON 交给既有 `AdapterJsonReader.Read` 校验 schema v1。这条**复用**已有用例覆盖的校验器，导入路径不另写一套规则。
6. `adapter-id-invalid`：schema 通过后再验 id 能否当目录名（只允许 `[a-z0-9-]`、长度 ≤ 64、首尾不是横线）。放在 schema 之后，是因为 kebab-case 已由 schema 保证，这里只防目录穿越。
7. 写盘阶段的第二道 `IsInside`（`AdapterPackageInstaller.Write`，`src/CkanUI.Services/ModConfig/AdapterPackageInstaller.cs`）：落盘前逐条目 `Path.GetFullPath` 再确认仍在目标目录内，越界同样返回 `adapter-zip-slip`。
## 4. 哈希与清单格式
`sha256` 是对**收到的原始字节流**（json 整体或 zip 整体）算的，不是解压后内容——所以同一份 zip 重新打包（时间戳变了）会被判成「有更新」，这是刻意的：宁可多提示一次，也不假装字节相同。算法 `Convert.ToHexStringLower(SHA256.HashData(payload))`，小写 64 位 hex。

清单落在 `<root>\<id>\installed.json`，UTF-8 无 BOM、缩进 JSON，字段固定：

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `manifestVersion` / `schemaVersion` | int | 清单结构版本（当前 1）/ adapter schema 版本 |
| `id` / `name` | string | 来自 adapter 声明 |
| `declaredVersion` | string? | adapter 顶层 `version`（schema v1 没有该字段，通常 null） |
| `sourceKind` / `source` | string | `local-file` / `remote-url`；来源路径或 URL 原文 |
| `sha256` / `sizeBytes` | string / long | 原始字节的哈希与长度 |
| `installedAt` | string | ISO-8601 UTC（`O` 格式） |
| `sourceEtag` / `entryPoint` | string? | 远端 ETag（查更新默认用它）/ zip 内入口相对路径 |

读清单的容错是「降级而不是抛」：没有 `installed.json` 视为**手工放入的 adapter**（`adapter-not-installed`，不算损坏）；有但字段缺失、对不上、JSON 坏掉，则记一条 `ModConfigDiagnosticLevel.Warning` + `adapter-manifest-corrupt`，同样按未安装处理。这样用户手抄的文件不会因为清单缺失就整个目录被隐藏。
## 5. 原子替换
```
<root>/.staging-<id>-<8hex>/   写 adapter.json + 附带文件 + installed.json
<root>/.backup-<id>-<8hex>/    旧版本临时挪到这里
<root>/<id>/                   最终 Move 进来
```
顺序：写 staging → 目标存在则 `Directory.Move(target, backup)` → `Directory.Move(staging, target)` → 删 backup。第二步之后任何失败都回滚 `backup → target` 并返回 `adapter-install-failed`（`retryable: true`）；`finally` 清 staging。因此**不存在「目录在但文件少一半」的中间态**：要么旧版本、要么新版本。以 `.` 开头的目录被 `ListInstalledAsync` 与 `AdapterCatalog` 的扫描跳过，残留的 staging/backup 不会被当成 adapter。进程内用一把 `Lock` 串行化清单读写，跨进程互斥没做（§7-4）。
## 6. 更新检查语义
`CheckForUpdateAsync(adapterId, remoteUri, etag?)` 只回答状态，**不改清单、不落盘**：

| 远端结果 | Status | 说明 |
| --- | --- | --- |
| 304 | `NotModified` | 远端没动过；`RemoteSha256` 为 null，ETag 沿用 |
| 200 且 sha256 相同 | `UpToDate` | 字节一致 |
| 200 且 sha256 不同 | `UpdateAvailable` | `Message` 带「短哈希 → 短哈希」，界面可直接显示 |
| 200 空体 / 超限 / 非 2xx / 超时 / 连接失败 | 失败 | `OperationResult.Fail`，永不抛 |

条件请求的 ETag 优先级：显式参数 > 清单 `sourceEtag`。5xx / 408 / 429 / 超时 / 连接失败标 `retryable: true`，4xx（404 认作 adapter 已下线）不标。发现更新后由调用方决定是否 `InstallAsync(FromUrl(remoteUri, entryPoint))`——重装是幂等的（同 id 走 §5 替换路径），所以「查更新」与「装」不必合成一个方法。
## 7. 已知限制
1. **入口目录必须唯一**：一个 zip 只能装一个 adapter，所有条目必须落在入口所在目录之下；兄弟目录里再放一份 `adapter.json`（常见于「一个压缩包塞多个 adapter」）会被 `adapter-zip-entry-outside-root` 整包拒绝。支持多 adapter 需要上层先拆分，或在契约里引入「装哪一个」的显式选择。
2. **只支持 `.json` 与 `.zip`**：没有 tar.gz / 7z；https 重定向到别的 https 允许（`HttpClient` 默认），重定向到 `file:` 不允许。
3. **不做签名与证书钉扎**：安全性靠 https + 用户能看见 sha256；adapter 本身是不可执行的声明式 JSON，最坏结果是被停用。
4. **没有跨进程锁**：两个实例同时装同一 id 会有一方 `Move` 失败，返回 `adapter-install-failed`，重试即可，不会写坏目录。
5. **不比版本号**：schema v1 没有 `version` 字段，更新判定只看哈希，「远端旧版本覆盖本地新版本」拦不住，需要 `declaredVersion` + 版本比较语义。
6. **不做差分更新**，每次整包替换，只有一次回滚窗口；`UninstallAsync` 只删 `<root>\<id>`，用户手工塞进去的同名目录会被一起删掉。
## 8. 诊断码表
`OperationResult.Detail` 一律以稳定码开头（`code: detail`），UI 可按码本地化：

| 码 | 触发点 |
| --- | --- |
| `adapter-source-missing` / `adapter-source-unreadable` | 本地来源不可用 |
| `adapter-package-empty` / `adapter-package-too-large` | 包体问题（包体与入口 JSON 两处体积） |
| `adapter-zip-invalid` / `adapter-zip-empty` / `adapter-zip-too-many-entries` / `adapter-zip-too-large` | zip 结构或上限 |
| `adapter-zip-slip` / `adapter-zip-entry-outside-root` | 路径安全（两道检查共用前者） |
| `adapter-zip-entry-missing` / `adapter-zip-no-adapter-json` / `adapter-zip-ambiguous-adapter-json` / `adapter-entry-invalid` | 入口选择 |
| `adapter-schema-invalid` / `adapter-id-invalid` | adapter 声明不合格 |
| `adapter-manifest-corrupt` / `adapter-manifest-id-mismatch` | 清单损坏 / 与目录名不符（降级用 Warning） |
| `adapter-not-installed` | 目录或清单不存在 |
| `adapter-fetcher-missing` / `adapter-url-invalid` / `adapter-http-status` / `adapter-http-empty` / `adapter-http-error` / `adapter-http-timeout` | HTTP 端口 |
| `adapter-cancelled` / `adapter-io` / `adapter-install-failed` / `adapter-update-check-failed` | 取消、落盘与未预期错误（后三者 `retryable`） |
## 9. 接线待办（留给 Lead）
1. `docs/README.md` 文档索引表加一行 `adapter-import.md`（本文不改共享文件）。
2. `AddCkanAdapterImport()`（`src/CkanUI.Services/ModConfig/AdapterImportServiceCollectionExtensions.cs`）已存在但**没被调用**：在 `ServiceRegistration` 加一行即可，`TryAddSingleton` 保证与既有 `AddCkanModConfig()` 不冲突。
3. 页面要「装一个 adapter」时，建议在 `IModConfigService` 上暴露 `Task<OperationResult<AdapterInstallResult>> InstallAdapterAsync(AdapterPackageSource, CancellationToken)` 转发给 `IAdapterInstaller`，而不是让 ViewModel 直接拿 `IAdapterInstaller`（保持「页面只认一个服务门面」）。
4. 界面文案（哈希短串、更新状态）需要新增本地化键；`LocalizationKeys.cs` 与 `i18n/packs/*` 属共享文件，本文不改。
