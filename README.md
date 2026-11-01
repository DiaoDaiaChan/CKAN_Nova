# ckan-ui

CKAN 的全新 Avalonia 界面（规格仓：`../CKAN/doc/uir/`）。

- 目标：完全特性对齐原版 GUI（55 条 F-xx），Core 零修改
- 技术栈：Avalonia 12.1.4 + `net10.0` 自包含发布
- 边界：`src/` 实现、`tests/` 测试、`external/CKAN` 只读上游、`docs/` 文档、`artifacts/` 产物（不入库）

```bash
dotnet build ckan-ui.sln                       # 0 警告是硬门禁
dotnet run --project tests/CkanUI.TestRunner    # 全部测试（走进程内 runner，见下）
dotnet run --project src/CkanUI.App             # 启动界面
```

一键编译并呼出界面（会先关掉正在运行的 CkanUI.exe，否则构建被输出文件锁挡住）：

```powershell
tools/run-gui/run-gui.cmd              # 双击或命令行；等价于同目录 build-and-run.ps1
tools/run-gui/run-gui.cmd -NoBuild     # 只启动，不编译
```

### 先决条件：两个 `external/` 依赖

本仓库不把上游源码入库（`external/` 在 `.gitignore` 里），构建前必须自己接上：

```bash
git clone --depth 1 --branch master https://github.com/KSP-CKAN/CKAN external/CKAN
```

- `external/CKAN`：上游 CKAN 核心（只读，`Core/CKAN-core.csproj`）。CI 的 `ci.yml` 有同样的兜底克隆。
- `external/UniversalI18n`：通用 i18n 运行时，需包含 `src/UI18n.Core/UI18n.Core.csproj`（本地做目录联接，无兜底）。

缺依赖 / restore 卡住 / 上游多 TFM 竞态的排查见 [docs/ops/build-environment.md](docs/ops/build-environment.md)。

> 沙箱里 `dotnet test`（VSTest testhost）不可用，统一用 `tests/CkanUI.TestRunner`；CI 上仍用 `dotnet test`。
> 命令与纪律详见 [docs/ops/engineering-conventions.md](docs/ops/engineering-conventions.md)。

---

### 测试与无头离屏套件

#### 1. 离屏全页面无头截屏工具 (UI Snapshot)
用于在无真实物理窗口弹出、无键鼠干扰的环境下，使用 Skia 纯内存渲染抓取全部 19 个 Tab 页面的高保真渲染图：

```bash
# 执行无头渲染并生成所有 19 个 Tab 截图到 artifacts/snapshots/
dotnet run --project tools/ui-snapshot/CkanUI.Tools.UiSnapshot.csproj -c Release
```

- **项目路径**：`tools/ui-snapshot/CkanUI.Tools.UiSnapshot.csproj`
- **输出产物**：`artifacts/snapshots/page_*.png`

#### 2. CLI 进程内测试运行器 (TestRunner)
避免 VSTest 在受限管道或沙箱环境下的通信中断，直接运行测试套件：

```bash
# 运行全部 ViewModel 状态机与业务逻辑测试
dotnet run --project tests/CkanUI.TestRunner/CkanUI.TestRunner.csproj -- --config Release ViewModels

# 运行 App 层界面与导航无头测试
dotnet run --project tests/CkanUI.TestRunner/CkanUI.TestRunner.csproj -- --config Release App

# 运行指定分类用例（例如仅测试 Mods 页面）
dotnet run --project tests/CkanUI.TestRunner/CkanUI.TestRunner.csproj -- --config Release ViewModels --where "cat == ModsPage"
```

> **沙箱里的临时目录**：`dotnet run --project` 拉起的测试进程会把 `TEMP` 落在 `%LOCALAPPDATA%\Temp`，该路径在沙箱内不可写，
> `ModConfig`/`BackgroundHost` 等「造临时目录」的用例会成片抛 `UnauthorizedAccessException`（看似回归，实为环境）。
> 直跑已构建的 dll 并把 `TEMP` 指到仓库内目录即可全绿：
>
> ```bash
> TEMP="$PWD/artifacts/tmp/temp" dotnet tests/CkanUI.TestRunner/bin/Release/net10.0/CkanUI.TestRunner.dll --config Release Services
> ```
