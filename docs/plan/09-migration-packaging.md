# 09 迁移、共存、打包与发布

## 1. 迁移策略

| 策略 | 含义 | 采用 |
|---|---|---|
| A 替换 | 新 UI 成为唯一 GUI，老 `ckan.exe` 的 GUI 入口废弃 | **最终目标**，但不在本项目内执行（本项目只交付新 exe） |
| B 并存 | 新 UI 与老 GUI 长期共存，用户二选一 | **过渡期形态**（发布后至少 2 个版本） |
| C 并行运行 | 两者同时操作同一实例 | **禁止**（实例锁，见 §3） |

**阶段**：

```
v0.x 预览版   → 并存，新 UI 自述"实验性"，不接管 ckan://
v1.0          → 并存，推荐新 UI；文档明确"不要同时开"
v1.x+（上游决策）→ 老 GUI 标记为 legacy / 移除
```

## 2. 部署形态

### 2.1 首选：独立 exe（决策 D2）

```
C:\Program Files\CKAN UI\     （或用户目录下的便携目录）
├── CkanUI.exe
├── *.dll（Avalonia、CKAN.dll、log4net 等）
├── log4net.xml
└── assets\
```

- 与 `ckan.exe` **完全独立的目录**，互不覆盖；
- 共享的只有 `%LocalAppData%\CKAN\`（配置、注册表、缓存）—— 这正是跨工具兼容的基础；
- 不注册到 CKAN 的 ILRepack 单文件产物中（`build/BuildPaths.cs` + ILRepack 那条链路不动）。

### 2.2 降级形态：仓库内项目（若不想维护 submodule）

在 CKAN fork 内新增 `AvaloniaUI/CKAN-AvaloniaUI.csproj`，`ProjectReference` 到 `Core/CKAN-core.csproj`，并：

| 需要改的 | 文件 | 改动 |
|---|---|---|
| 解决方案 | `CKAN.sln` | 加项目 |
| 构建脚本 | `build/BuildPaths.cs` | 决定是否进 `_build/out` |
| 打包 | ILRepack 参数 | **不要**把新 exe 合进 `ckan.exe`（避免入口冲突） |
| CI | `.github/workflows/build.yml:15-20` | 加一步 `dotnet build` 新项目 |

代价：fork 与上游 diff 变大，每次 rebase 有冲突。**只在无法使用 submodule 时采用。**

## 3. 与老 GUI / CLI 共存

### 3.1 铁律：不要同时操作同一实例

`RegistryManager` 对每个实例持锁（`Core/Registry/RegistryManager.cs:265`），第二个进程拿不到。表现可能是：新 UI 卡在"正在读取注册表"，或老 GUI 报错。

**产品处理**：

| 场景 | UI 行为 |
|---|---|
| 启动时目标实例被占用 | 顶部红色横幅："该实例已被另一个 CKAN 进程（PID xxxx）占用。可浏览但无法安装。" + [以只读方式继续] [关闭] |
| 操作时才发现被占用 | 操作失败 → 明确文案 + "请在另一个窗口关闭后重试" |
| 用户主动同意"抢占" | **不提供**（会损坏数据） |

### 3.2 共享 `%LocalAppData%\CKAN\` 的写冲突

| 文件 | 冲突可能 | 处理 |
|---|---|---|
| `config.json` | 两进程同时写 → 可能丢字段（`JsonConfiguration` 落盘非原子） | `ISettingsService` 串行 + 改后读回校验 + 大面积改动（如重置）前提示"请关闭其他 CKAN 进程" |
| `registry.json`（**单文件**，含 `installed_modules` / `installed_files` / `sorted_repositories` / `registry_version`） | 被 `registry.locked` 锁机制保护；两进程同时写 = 后写覆盖 | 只通过 `RegistryManager` 访问（`Core/Registry/RegistryManager.cs:61` 路径、`:46` 锁），不额外处理 |
| `registry.locked` | 陈旧锁（进程崩溃留下） | 继承老 GUI 的处理：提示"检测到锁文件"，提供"删除锁并回滚"（12 篇 F-02 的 Kraken 处理） |
| `downloads/` | 缓存文件同名 | 天然幂等（相同 URL 相同 hash）；下载中文件用 `.tmp` 后缀，读取时忽略 |

> **兼容三条硬约束（v1.5 补充，针对"直接复用玩家现有数据"）**：
> 1. **整份重写语义**：`JsonConfiguration.SaveConfig()` 是 `JsonConvert.SerializeObject(config)` 全量写盘（`Core/Configuration/JsonConfiguration.cs:291-295`），`Config` 类只有 10 个已知键且**没有 `[JsonExtensionData]`** → 用户手工加过的顶层键会在任何一次设置保存时丢失。行为与 `ckan.exe` 一致，但我们的 UI 应：首次写前备份 `config.json`，并在"诊断"页提示。
> 2. **前向兼容闸门**：`registry_version > LATEST_REGISTRY_VERSION` 会抛 `RegistryVersionNotSupportedKraken`（`Core/Registry/Registry.cs:166-172`）→ 说明玩家用过更新版 CKAN。UI 必须捕获并给话术"该实例的注册表由更新版 CKAN 写入，请升级本程序"，**不得**尝试改写降级。
> 3. **缓存账目**：`CacheSizeLimit` / `DownloadCacheDir` 与老 GUI 共享，清理缓存必须走 Core 的缓存管理器（记账在配置与注册表侧），禁止直接删目录。
> 4. **不迁移**：玩家的实例列表（`config.json` 的 `GameInstances`）、仓库源（`sorted_repositories`）、已装 mod（`installed_modules`）**天然可见**，不需要任何导入步骤——这是 D1（源码引用 Core）的直接收益。
| `CKAN.log` | 两进程同时写同一 appender | log4net 的 `FileAppender` 默认不共享 → 新 UI 用 `CKAN-ui.log`（**唯一推荐的日志分叉**），并在"诊断"页同时列出两个日志 |

### 3.3 陈旧锁（stale lock）

进程崩溃后锁文件可能残留。检测规则（三条全部满足才认定为 stale）：

1. 锁文件中记录的 PID 不存在或已被重用（校验进程名）；
2. 该目录没有被系统句柄占用（尝试以独占方式打开）；
3. 用户明确点击"修复"。

修复动作：把锁文件重命名为 `*.stale-<timestamp>`（**不删除**，便于事后诊断）。

### 3.4 启动时一致性检查（防 R-03 半装）

```
启动 → 读注册表 → 抽样/全量校验 installed 条目声明的文件是否存在
  ├─ 全部存在 → 正常
  ├─ 少量缺失 → 横幅："检测到 N 个模组的文件不完整，[查看] [全部重装]"
  └─ 注册表无法解析 → 错误页："注册表损坏" + 备份路径 + [从备份恢复] [重建注册表(危险)]
```

**不自动修复、不自动删除**。任何破坏性动作都要用户点击。

## 4. 数据兼容性承诺

| 保证 | 说明 |
|---|---|
| 正向兼容 | 新 UI 读老 GUI/CLI 写的一切（配置、注册表、缓存、实例列表） |
| 反向兼容 | 老 GUI/CLI 能读新 UI 写的一切 —— **因此新 UI 不向 `config.json` 写任何自定义字段**；UI 专属状态全部进 `ckan-ui.json` |
| 无破坏性迁移 | 首次运行不"升级"任何已有文件格式 |
| 版本可回退 | 从新 UI 回到老 GUI，无需清理步骤 |

`ckan-ui.json` 结构（示例）：

```json
{
  "schemaVersion": 1,
  "window": { "width": 1440, "height": 900, "maximized": true },
  "theme": "system",
  "language": "zh-CN",
  "lastPage": "Mods",
  "columns": [ { "id": "name", "width": 320 }, { "id": "installed", "width": 90 } ],
  "suppressedPrompts": [ "confirm-install-unsupported" ]
}
```

## 5. 打包

| 形态 | 命令 | 体积（估） | 适用 |
|---|---|---|---|
| framework-dependent | `dotnet publish -c Release -r win-x64 --self-contained false` | ~25 MB（需装 .NET 10 Desktop Runtime） | 主流分发（体积小、可共享运行时） |
| self-contained | `dotnet publish -c Release -r win-x64 --self-contained true` | ~90–110 MB | 免安装运行时 |
| 便携 zip | 上面任一 + `log4net.xml` + `assets` 打包 | 同上 | 主要分发方式（与 CKAN 现有习惯一致：无安装器） |
| ~~MSI~~ | — | — | **不做**（2026-10-27 决策：只发 EXE） |

**推荐发布矩阵**：

| 平台 | 产物 | 完整性 |
|---|---|---|
| win-x64 | 便携 zip（self-contained） | 全部功能 |
| win-x64 | framework-dependent zip | 全部功能 |
| linux-x64 | 便携 tar.gz | 浏览/搜索/只读（安装受限，见 R-07） |
| osx-arm64 | 便携 tar.gz | 同上 |

**图标与资源**：`assets/` 里有现成的 CKAN 图标可复用（沿用品牌），新增的 UI 图标走内嵌矢量。

## 6. 构建与发布脚本

```powershell
# scripts/build.ps1 —— 一条命令出全部产物
param([string]$Version = "0.1.0")
dotnet build -c Release -warnaserror
dotnet test  -c Release --no-build
dotnet publish src/CkanUI.App -c Release -r win-x64 --self-contained true  -o artifacts/win-x64-sc  -p:Version=$Version
dotnet publish src/CkanUI.App -c Release -r win-x64 --self-contained false -o artifacts/win-x64-fd  -p:Version=$Version
Copy-Item CKAN/log4net.xml artifacts/win-x64-* -Force
Compress-Archive artifacts/win-x64-sc/* artifacts/CkanUi-$Version-win-x64.zip
```

| 版本号规则 | 说明 |
|---|---|
| `CkanUi` 自己的版本 | 语义化 `0.x` 预览、`1.0` 正式 |
| 绑定的 Core 版本 | 从 submodule commit 读，显示在"关于"页 |
| 产物命名 | `CkanUi-<uiVer>-win-x64.zip`，**不带 Core 版本**（避免用户混淆两个版本号） |

## 7. 更新通道

| 方案 | 采用 | 理由 |
|---|---|---|
| 自更新（下载新 exe 替换） | ✅ 自建，不用 `Core/AutoUpdate` | Core 的实现会拉起 `ckan.exe`（`Core/AutoUpdate/AutoUpdate.cs:62`），语义不符 |
| 渠道 | 检查 GitHub Releases 的 latest tag（或 self-host 的 `version.json`） | 简单、无基础设施 |
| 行为 | **只提示，不自动替换**（v1.0 阶段）；用户点"下载"打开浏览器 | 避免"半更新"状态；替换运行中 exe 在 Windows 上不可靠但可行（先 rename 再写），复杂度不值 |
| 关闭 | 设置里可关 | 尊重用户 |

## 8. 回滚

| 层 | 回滚方式 |
|---|---|
| 应用版本 | 保留上一版 zip；`ckan-ui.json` 有 `schemaVersion`，旧版遇到更新的 schema 时忽略未知字段并备份 |
| 数据 | 新 UI 不改任何 Core 文件格式 → 卸载新 UI 即回到原状 |
| 安装操作 | `CkanTransaction` 保证单次操作原子；另有注册表备份（R-03 缓解①） |
| 完整回滚演练 | M5 门禁含"装 3 个模组 → 卸新 UI → 老 GUI 打开同一实例一切正常" |

## 9. 用户侧沟通（文案要点）

| 场景 | 要点 |
|---|---|
| 预览版公告 | 明确"实验性、可能与老 GUI 并存、**不要同时打开两个界面**"；给出反馈渠道 |
| 首次启动 | 自动探测实例；若 `%LocalAppData%/CKAN` 已有配置 → "已找到你现有的 CKAN 数据，将直接沿用" |
| 只读模式横幅 | 说明原因（别的进程占用 / 平台限制）与可做的操作 |
| 卸载说明 | 说明"卸载不会删除你的模组与配置" |

## 10. 卸载与清理

| 内容 | 是否随卸载删除 |
|---|---|
| 程序目录 | 是 |
| `%LocalAppData%\CKAN\ckan-ui.json` | 否（保留用户偏好，重新安装后继承） |
| `%LocalAppData%\CKAN\` 其它（Core 数据） | **绝不删除**（属于 CKAN 生态共享数据） |
| `ckan://` 注册表项 | 是 |
| 开始菜单/桌面快捷方式 | 是 |

## 11. 附：发布形态定稿（v1.1 回填，依据 [11 篇](11-tech-stack-decision.md)）

| 项 | 定稿 |
|---|---|
| 构建 | `dotnet publish -c Release -r win-x64 --self-contained -p:PublishReadyToRun=true` |
| 单文件 | **否**（`PublishSingleFile=false`）——单文件首启解压拖慢冷启动 |
| 分发 | zip（`ckan-ui-<ver>-win-x64.zip`），解压即用；`win-arm64` 后续评估 |
| 前置依赖 | **无**（运行时随包）——这是选择 Avalonia 自包含发布的核心动因 |
| 体积预算 | 60–90MB（主题资源 + Skia 原生库为主）；超出预算须在发布说明中解释。**v1.4 实测后口径修订见 §14**（zip 63.7MB 在预算内；解压体积另计） |
| 系统检查 | 启动读 `Environment.OSVersion`，低于 Win10 1607 时弹明确提示并退出，**不静默崩溃** |
| 与老 `ckan.exe` | 并存：不同 exe 名与进程，共享 Core 数据目录；同一实例不可同时操作（本文 §3 铁律不变） |

## §12 附：安装 / 卸载 / 注册定稿（v1.2 回填，依据 [19 篇](19-windows-install-flow.md)）

本文 §2（部署形态）、§5（打包）、§10（卸载与清理）由以下结论覆盖，冲突时**以 19 篇为准**：

| 项 | 定稿 |
| --- | --- |
| 分发形态 | **两产物**：`CkanUISetup.exe`（一键、per-user、免提权；静默 `-s`、`-t <DIR>`、`-l <LOG>`）、`ckan-ui-<ver>-win-x64-portable.zip`（便携/CI） |
| 安装目录 | `%LocalAppData%\CkanUI`（Velopack `packId = "CkanUI"`）——**绝不能是 `%LocalAppData%\CKAN`**，那是 Core 的数据目录（R-65） |
| 用户数据 | 仍全部落在原位置（`%LocalAppData%\CKAN` 与 `<GameDir>\CKAN`），安装与卸载都不碰 |
| 注册表 | **只写 HKCU**：`Software\Classes\ckan`、`.ckan` + `CKAN.ModPackage`、`Software\CKAN\CkanUI\Capabilities` + `RegisteredApplications`、`Uninstall\CkanUI`、`App Paths\CkanUI.exe`；不再走 `Registry.ClassesRoot`（旧行为见 `GUI/URLHandlers.cs:109-142`） |
| 提权 | 安装与注册都**不需要管理员**；只有"清理旧 ckan.exe 留下的 HKLM 协议键"这一可选动作需要 |
| 卸载 | 删程序目录 + 上述 HKCU 键 + 快捷方式；**保留** `config.json`/`registry.json`/`repos`/`ckan-ui.json`/日志，完成页列出位置 |
| 更新 | 由 Velopack `Update.exe` 执行（取代本文 §7 与 08 篇 R-21 的自建替换器） |
| 签名 | **本期不签**（0 成本）：文件名带 `-unsigned` + 发布页 SHA256 清单；v1.0 前可接 SignPath Foundation 免费 OSS 签名（见 19 篇 §17） |

另：本文 §11 的发布命令仍是唯一的构建入口，安装包只是在其产物之上再打一层。

## §13 附：外观资源与主题包的分发（v1.3 回填，依据 [20 篇](20-appearance-personalization.md) §12）

| 内容 | 落位 | 升级时行为 |
| --- | --- | --- |
| 内置主题（3 套） | 程序目录 `themes/<packId>/` | **整体替换**（随版本走） |
| 用户导入主题 | `%LocalAppData%\CKAN\themes\<packId>\`（**数据侧**） | 不碰（与 §12 的 "R-68 用户 adapter 放数据侧" 同理） |
| 用户背景图 | 用户选定的绝对路径，或拷贝到 `%LocalAppData%\CKAN\backgrounds\` | 不碰 |
| 外观配置 | `%LocalAppData%\CKAN\ckan-ui.json` 的 `appearance` 段 | 不碰；带 `schemaVersion` 便于迁移 |

打包约束：①主题包与背景**不进** exe 的"程序文件"清单以外位置；②安装器**不得**在卸载时删数据侧 `themes/`（同 §12 的"绝不删除"清单）；③便携版必须能把 `themes/` 相对化（便携模式下数据侧 = 便携目录）。

## §14 附：发布体积实测与预算修订（v1.4 回填，依据 docs/ops/release.md §3.2 / §3.5）

§11 的「体积预算 60–90MB」在实现期被实测修正，本附录为最终口径：

| 形态 | 解压后 | zip | 冷启动 |
| --- | --- | --- | --- |
| 自包含 + R2R（默认） | 152 MB / 277 文件 | 63.7 MB / 281 条目 | 0.81 s |
| 自包含 + 无 R2R（release.ps1 -NoReadyToRun） | 119 MB / 281 条目 | 49.7 MB | 1.6 s |
| 框架依赖 + R2R | 75.5 MB / 90 文件 | 29.7 MB | 0.80 s |
| 框架依赖 + 无 R2R | 42 MB / 94 条目 | 15.7 MB | 1.56 s |

- **下载包（zip）在预算内**：默认自包含 63.7 MB，落在 60–90MB 区间；框架依赖 29.7 MB。
- **解压体积另有地板**：.NET 自包含运行时约 95 MB（System.Private.CoreLib 15.3 MB、coreclr 4.4 MB、System.Private.Xml 7.4 MB 等）+ libSkiaSharp.dll 11.1 MB + av_libglesv2.dll 5.1 MB + 自研与 CKAN 程序集；任何自包含形态都做不到 60–90 MB 解压。
- **两个省体积杠杆**：release.ps1 -NoReadyToRun（152 → 119 MB，代价是冷启动 0.81 → 1.6 s）；框架依赖版（75.5 / 42 MB，代价是用户要自装 .NET 10 Desktop Runtime）。
- **被 external 仓库挡死的做法**：PublishSingleFile 与 PublishTrimmed 都会向所有工程注入 Microsoft.NET.ILLink.Tasks，而 external/UniversalI18n 的 src/UI18n.Core/UI18n.Core.csproj 有「必须零第三方依赖」的硬门禁，实测 publish 直接退出码 1；除非改上游仓库，否则本仓库不可能用裁剪换体积。
- **包内容已变化**：主程序名统一为 CkanUI.exe（AssemblyName=CkanUI，Avalonia 资源 URI 改 avares://CkanUI/…）；包根新增 LICENSE.md（本项目 GPL-3.0 全文）；§12 提到的 App Paths 键名同步改为 App Paths\CkanUI.exe。

> 本附录取代 §11 该行的判读方式；§11 原文保留以便追溯。
