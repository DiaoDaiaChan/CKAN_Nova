# ckan-ui

CKAN 的全新 Avalonia 界面（规格仓：`../CKAN/doc/uir/`）。

- 目标：完全特性对齐原版 GUI（55 条 F-xx），Core 零修改
- 技术栈：Avalonia 12.1.4 + `net10.0` 自包含发布
- 边界：`src/` 实现、`tests/` 测试、`external/CKAN` 只读上游、`docs/` 文档、`artifacts/` 产物（不入库）

```bash
dotnet build ckan-ui.sln
dotnet test ckan-ui.sln
dotnet run --project src/CkanUI.App
```

> 本地开发时 `external/CKAN` 是指向规格仓 `../CKAN` 的目录联接；CI 使用 git submodule。
