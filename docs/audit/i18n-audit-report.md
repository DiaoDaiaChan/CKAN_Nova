# CKAN-UI 国际化 (i18n) 穿透性审计报告

## 1. 总体健康度与核心指标

| 维度 | 统计数值 | 说明 |
| --- | --- | --- |
| 已标准化国际化站点 | 1904 | 包含 AXAML DynamicResource 与 C# Text(...) |
| 硬编码中文 (待标准化) | 2815 | 视图层与逻辑层待抽取的中文文案 |
| 硬编码英文 (待标准化) | 72 | 视图层硬编码自然英文标签/提示 |
| 规范排除项 (非本地化) | 622 | 遵循规范排除的异常/日志/诊断信息 |
| 语言包键总数 | 1851 | zh-CN 与 en-US 共享键基准 |
| 双语包对称性 | ✅ 对称 | zh-CN vs en-US 键集合比对 |
| 死键 (Dead Keys) 数量 | 11 | 语言包中定义但未被代码/视图引用的键 |

## 2. 分层穿透统计

| 架构分层 | 已标准化 (Localized) | 硬编码中文 (CJK) | 硬编码英文 (EN) | 规范排除 (Excluded) |
| --- | --- | --- | --- | --- |
| AXAML | 933 | 6 | 0 | 0 |
| ViewModel | 971 | 1376 | 10 | 133 |
| Service | 0 | 1224 | 22 | 401 |
| App | 0 | 209 | 40 | 88 |

## 3. 语言包与契约完整性审计

✅ **语言包键集完整且高度对称**：`zh-CN.json`、`en-US.json` 与 `LocalizationKeys.cs` 保持严格 1:1 镜像。

**未被引用的死键 (共 11 个)**：
```text
Ui_Assets_Column_Modified, Ui_Assets_Column_Size, Ui_Assets_Status_Ready, Ui_Common_Ascending, Ui_Common_Descending, Ui_Home_ErrorNoGameFolder, Ui_MainWindow_PerfMonitor, Ui_MainWindow_PerfMonitor_Tip, Ui_ModsPage_Header_Tip, Ui_ModsPage_Subtitle, Ui_NavRail_Assets
```

## 4. 重点未标准化文件 Top 20

| 文件路径 | 所属层 | 硬编码中文 | 硬编码英文 | 合计待处理 |
| --- | --- | --- | --- | --- |
| `src/CkanUI.Services/Commands/DefaultCommands.cs` | Service | 114 | 0 | 114 |
| `src/CkanUI.ViewModels/Dialogs/FirstRunWizardViewModel.cs` | ViewModel | 83 | 1 | 84 |
| `src/CkanUI.ViewModels/Dialogs/ModDetailsViewModel.cs` | ViewModel | 69 | 0 | 69 |
| `src/CkanUI.ViewModels/Pages/ModConfigPageViewModel.cs` | ViewModel | 68 | 0 | 68 |
| `src/CkanUI.ViewModels/Relations/RelationTreeViewModel.cs` | ViewModel | 56 | 0 | 56 |
| `src/CkanUI.Services/Plugins/PluginService.cs` | Service | 55 | 0 | 55 |
| `src/CkanUI.Services/Instances/InstanceRepairService.cs` | Service | 53 | 0 | 53 |
| `src/CkanUI.ModText/ModTaxonomyEngine.cs` | App | 16 | 36 | 52 |
| `src/CkanUI.Services/ModConfig/AdapterJsonReader.cs` | Service | 52 | 0 | 52 |
| `src/CkanUI.ViewModels/Pages/InstancesPageViewModel.cs` | ViewModel | 49 | 0 | 49 |
| `src/CkanUI.Services/Logs/BuiltinLogRules.cs` | Service | 40 | 5 | 45 |
| `src/CkanUI.ViewModels/Pages/ModPacks/ModPackEditorText.cs` | ViewModel | 41 | 3 | 44 |
| `src/CkanUI.ViewModels/ShellViewModel.cs` | ViewModel | 42 | 1 | 43 |
| `src/CkanUI.Services/ModPacks/ModPackEditorValidator.cs` | Service | 41 | 0 | 41 |
| `src/CkanUI.ViewModels/Pages/Changes/ChangesFormatter.cs` | ViewModel | 40 | 0 | 40 |
| `src/CkanUI.Services/ModPacks/ModPackCatalog.cs` | Service | 38 | 0 | 38 |
| `src/CkanUI.Services/ModConfig/AdapterWriter.cs` | Service | 37 | 0 | 37 |
| `src/CkanUI.Services/Update/UpdateInstaller.cs` | Service | 36 | 0 | 36 |
| `src/CkanUI.ViewModels/Pages/ModPacksPageViewModel.cs` | ViewModel | 32 | 0 | 32 |
| `src/CkanUI.Services/ModConfig/AdapterInstaller.cs` | Service | 30 | 0 | 30 |
