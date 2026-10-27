# F-36 推荐与建议审计对话框设计与实现（UI-17）

## §1. 概念与范围
在 CKAN 使用过程中，用户可能在初次安装某模组时跳过了其推荐（recommends）或建议（suggests）模组，或者随时间推移仓库新增了相关推荐。
F-36 推荐审计功能提供集中扫描与交互入口：
1. **全量扫描**：遍历当前已安装的所有模组（`registry.Installed`），提取其元数据中的 `recommends` 与 `suggests` 依赖描述。
2. **候选去重与过滤**：
   - 过滤掉当前已经安装的模组；
   - 过滤掉当前已经处于待安装列表（`pendingInstallIdentifiers`）中的模组；
   - 对同向推荐进行来源归并（例如 ModA 与 ModB 均推荐 ModC，则 ModC 的来源列出 `ModA, ModB`）。
3. **交互与变更交接**：
   - 采用多选列表展示候选模组、推荐类型（推荐/建议）、来源模组、简介及最新版本；
   - 提供「全选」、「全不选」快捷操作；
   - 点击「加入待安装」，将勾选的模组批量转换为 `ChangeRequest` 并通过 `IChangeRequestSink` 写入变更集。

## §2. 架构与分层实现

| 层级 | 构件与位置 | 职责 |
|---|---|---|
| **ViewModels** | `src/CkanUI.ViewModels/Dialogs/AuditRecommendationsDialogViewModel.cs` | 扫描、去重、过滤、多选状态与批量加入待安装逻辑 |
| **ViewModels** | `src/CkanUI.ViewModels/Dialogs/Mods/AuditRecommendationItemViewModel.cs` | 审计项行 VM（模组名、来源、推荐类别、勾选双向绑定） |
| **App (View)** | `src/CkanUI.App/Views/Dialogs/AuditRecommendationsDialogView.axaml(.cs)` | Avalonia 模态对话框视图（DataGrid + Ckan* Token 语义样式） |
| **Tests** | `tests/CkanUI.ViewModels.Tests/AuditRecommendationsDialogTests.cs` | 9 例完整单元测试（覆盖空注册表、过滤已安装/待安装、来源归并、全选全不选、交接 Sink 等） |

## §3. 核心交互契约
- **ViewModel 继承基类**：继承 `DialogViewModelBase`，主按钮为「加入待安装」，副按钮为「关闭」；
- **响应式属性**：
  - `SelectableCount`：可勾选的候选总数；
  - `SelectedCount`：当前已勾选总数；
  - `CanApply`：有勾选项且存在可选候选时可用；
  - `SummaryText`：底部统计文案（如「已选 2 项 / 共 3 项推荐模组」）。
