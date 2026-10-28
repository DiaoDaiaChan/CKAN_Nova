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
