# F-36 推荐与建议审计对话框设计文档（Audit Recommendations / Suggestions，UI-17）

## 1. 概述与背景
在 CKAN 生态中，模组元数据支持定义 `recommends`（推荐）与 `suggests`（建议）关系：
- `recommends`：与当前模组高度协同或提供关键游戏体验增强的模组。
- `suggests`：具有补充性质的可选增强模组。

随着用户安装的模组增多，已有模组的新推荐与建议往往散落在各个模组定义中。用户难以直观了解所有已安装模组推荐了哪些尚未安装的新模组。
F-36 推荐与建议审计功能（UI-17）通过遍历当前实例所有已安装模组，提取并汇总所有推荐与建议项，按原发起模组分组呈现，并支持用户多选后一键批量将选中的模组加入当前待变更集（`ChangeRequest`）。

## 2. 核心架构设计

### 2.1 契约分层与职责划分
- **数据流与事实来源**：
  - `RegistrySnapshot`：读取当前实例中已安装模组（`InstalledModuleInfo`）及其搜索元数据事实（`ModSearchFacts.Recommends` 和 `ModSearchFacts.Suggests`）。
  - `RepositorySnapshot`：从仓库可用模组清单中查询候选推荐项的真实显示名称、最新版本号、描述与摘要。
  - `pendingInstallIdentifiers`：当前变更集中已计划安装的模组集合，扫描时予以过滤，防止重复推荐。
  - `IChangeRequestSink`：解耦的变更落点接口，确认采纳后生成 `ChangeRequest(Identifier, ChangeAction.Install, TargetVersion)` 批量灌入。
- **视图模型层（`CkanUI.ViewModels`）**：
  - `AuditRecommendationsDialogViewModel`：对话框主 VM，继承 `DialogViewModelBase`，负责扫描聚合、分组构建、全局全选/全不选与校验提交。
  - `AuditRecommendationGroupViewModel`：发起模组分组 VM，代表一个已安装模组所贡献的推荐候选集合，提供组级全选与组级清空命令。
  - `AuditRecommendationItemViewModel`：候选模组条目 VM，封装模组标识、名称、版本、简介、推荐类别（Recommended / Suggested）与多来源归属，支持跨组实例共享与选择联动。
- **视图层（`CkanUI.App`）**：
  - `AuditRecommendationsDialogView.axaml (.cs)`：基于 Avalonia `DialogWindow`，采用语义化 Design Tokens 构建分层卡片列表与操作栏。

### 2.2 扫描与过滤算法
1. **已安装与待安装过滤**：
   - 收集已安装模组集合 `installedIds` 与待安装集合 `pendingIds`。
   - 若候选模组属于上述任一集合，则自动跳过，确保审计结果纯净聚焦于“新需求”。
2. **分组呈现与跨来源合并**：
   - 遍历每个已安装模组，提取其 `recommends` 与 `suggests` 列表。
   - 若目标模组被多个已安装模组共同推荐/建议，条目 VM 在内存中唯一保持单例，跨组共享实例，保证用户在任意组勾选或取消时全局与各组统计实时联动。
   - 最终按原发起模组构建 `AuditRecommendationGroupViewModel`，组头清晰标识来源模组名称与标识。
3. **元数据丰富化**：
   - 从仓库索引中补全模组名称（Name）、最新可用版本（LatestVersion）及摘要简介（Abstract/Description）。

## 3. 用户交互流程
1. **展示与导航**：
   - 顶部提供说明与实时统计文案（如：“已扫描已安装模组，发现 X 个模组提供了 Y 项未安装的推荐/建议，已选 Z 项。”）。
   - 提供“全选”与“全不选”全局操作按钮。
2. **分组浏览与快速勾选**：
   - 用户可展开/滚动浏览按发起模组归类的推荐项。
   - 每个分组卡片头部支持“本组全选”与“本组清空”。
   - 列表项清晰展示模组名、版本、标识、类别以及简介。
3. **批量采纳与提交**：
   - 用户勾选需要安装的模组后，点击“加入待变更集”主按钮。
   - 校验逻辑确保至少有一项被勾选（无勾选时抛出友好校验提示阻止空提交）。
   - 校验通过后，自动构造 `ChangeRequest` 列表（去重后）通过 `IChangeRequestSink.SetRequests` 发送到变更集页面，并关闭对话框。
   - 点击“关闭”取消时安全退出，不产生副作用。

## 4. UI 语义 Token 规范
界面完全遵循 Avalonia 与 CkanUI 设计规范，严禁硬编码颜色与字体大小：
- 边框与背景：`CkanBorderBrush`、`CkanSurfaceBackgroundBrush`、`CkanCornerRadiusMedium`
- 文字与层级：`CkanFontSizeBody`、`CkanFontSizeCaption`、`CkanTextSecondaryBrush`、`CkanAccentBrush`
- 状态与告警：`CkanDangerBrush`（校验错误）、`CkanWarningBrush`（不可选原因）

## 5. 自动化测试验证
测试套件位于 `tests/CkanUI.ViewModels.Tests/AuditRecommendationsDialogTests.cs`，包含 10 项严密测试用例：
1. `用例1_空已安装列表时无推荐候选`：验证空注册表时的空状态文案与按钮禁用。
2. `用例2_提取推荐与建议并按原发起模组分组呈现`：验证多发起模组的分组构建与组头元数据。
3. `用例3_过滤已安装模组`：验证已安装模组被正确剔除。
4. `用例4_过滤待安装队列中的模组`：验证待安装列表中的模组被正确剔除。
5. `用例5_模组名称版本与简介从仓库快照正确映射`：验证仓库元数据准确注入行 VM。
6. `用例6_全局全选与全局全不选行为正确`：验证主视图全局选择命令。
7. `用例7_组级全选与组级清空行为正确`：验证分组维度的独立选择与状态联动。
8. `用例8_未勾选时点击主按钮触发校验错误且不导出变更请求`：验证无勾选时的表单拦截。
9. `用例9_勾选后确认一键批量加入待变更集并导出请求`：验证确认提交时变更集请求的正确构造与注入。
10. `用例10_多来源指向同一推荐模组时勾选状态同步且去重导出`：验证跨组同模组的实例同步与最终导出请求去重。
