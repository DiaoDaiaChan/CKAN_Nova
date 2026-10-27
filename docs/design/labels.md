# F-24 标签组（Label 属性）

## 落地范围

| 层 | 文件 | 说明 |
| --- | --- | --- |
| 契约 | `src/CkanUI.Services.Abstractions/ILabelService.cs` | 全部成员为 DIM（默认实现返回"不可用/空"），既有替身无需改动（`service-contracts.md` §12.9） |
| 契约 | `src/CkanUI.Services.Abstractions/Dtos/LabelInfo.cs` | 标签组只读投影，十项属性 + `IsGlobal` + `ModuleCount`，颜色以 `#RRGGBB` 跨界 |
| 契约 | `src/CkanUI.Services.Abstractions/Dtos/LabelEditRequest.cs` | 新建/编辑入参，按名称定位（不支持改名） |
| 实现 | `src/CkanUI.Services/Labels/LabelService.cs` | 经 `GameInstanceManager` 拿实例，读写上游 `ModuleLabelList` |
| VM | `src/CkanUI.ViewModels/Pages/Mods/ModsLabelFilter.cs` | 纯函数过滤（多选为**或**） |
| VM | `src/CkanUI.ViewModels/Pages/Mods/ModsLabelChip.cs` / `ModsLabelBadge.cs` | 筛选开关与详情标记的展示模型 |
| VM | `src/CkanUI.ViewModels/Pages/ModsPageViewModel.cs` | 加法接入：`ILabelService? labels = null`（第 10 个可选参数） |
| 视图 | `src/CkanUI.App/Views/ModsPageView.axaml` | 筛选区标签开关 + 详情面板标记与收藏/隐藏/锁定按钮 |

## 关键决策

1. **落盘一律交给 Core。** 服务只调用 `ModuleLabelList.Save(path)`，不写一行 JSON；成员关系（`module_identifiers_by_game`）由上游 `ModuleLabel` 自己序列化。
2. **默认路径复用上游静态单例** `ModuleLabelList.ModuleLabels`，这样服务写入与 `ModSearchFactsFactory` 投影出的行内 `label:` 事实是同一个内存对象，不会出现"改完标签、搜索还按旧成员关系命中"。测试/便携场景可传自定义路径，此时服务用 `ModuleLabelList.Load` 独立加载。
3. **多选为或**：选中 Favourites + Hidden 保留"属于任一"的行。与搜索语法 `label:`（同名条件之间按 AND 分组）刻意不同——筛选区是"给我看这些"，搜索是精确条件；差别写在这里以免被当成 bug。
4. **默认组按语义而非名字定位**：收藏 = 名字为 `Resources.ModuleLabelListFavourites`（回退 "Favourites"）的标签组；隐藏 = 第一个 `Hide == true` 的标签组；锁定 = 第一个 `HoldVersion == true` 的标签组。找不到时按默认名创建，因此本地化改名的用户也能正常用。
5. **失败不静默**：`SetLabelsAsync` 遇到未知标签组名直接失败并回显名称（不丢条件）；`Save` 返回 false 时给出"标签文件保存失败，请检查磁盘权限。"；无实例时所有写操作返回"当前没有选中的游戏实例，无法读写标签。"。
6. **读路径全部吞异常**：绑定 getter（`DetailLabels`/`DetailIsFavourite`…）异常时退化为"无标记"，读标签组失败经 `IErrorReporter` 进错误栏，界面照常可用。

## 与上游的对应

上游 `GUI/Dialogs/EditLabelsDialog` 的十项属性（名称、背景色、隐藏于其它过滤器、变更时通知、变更时移除、安装时提醒、安装时移除、锁定版本、忽略缺失文件、导出）全部在 `LabelEditRequest`/`LabelInfo` 里有对应字段；`ModuleLabelList.HeldIdentifiers`/`IgnoreMissingIdentifiers` 通过 `ILabelService.HeldIdentifiers`/`IgnoreMissingIdentifiers` 暴露给升级器与缺失文件检查。标签组编辑对话框本身（UI-24b 的另一半）不在本次交付内。

## 已知缺口

1. **颜色只到 ToolTip。** 仓库里没有任何 `IValueConverter`，`string` → `IBrush` 的绑定在本沙箱无法验证，因此 `ColorHex` 目前只作为 `ToolTip.Tip` 与数据字段存在，chip 背景仍用 `Ckan` 语义 brush。要真正上色需新增一个 `HexToBrushConverter`（App 层）并在 `ModsPageView.axaml` 里挂 `Background`。
2. **标签组编辑 UI 未做**（新建/删除/十项属性编辑的对话框）：服务侧 `CreateLabelAsync`/`UpdateLabelAsync`/`DeleteLabelAsync` 已就绪并有测试，缺 `ILabelService` 的对话框视图。
3. **行内 label 事实不随单次写操作刷新**：`ModSearchFactsFactory` 在仓库/注册表快照重建时投影，服务写完标签只重载本地列表；要让 `label:` 搜索立刻反映新成员关系需要触发一次快照重投影（目前靠 `ReloadFromSnapshots()` 与 `ModuleLabels` 共享对象间接生效，已在服务侧对齐，但 ViewModel 测试用 fake 服务覆盖不到这条链路）。
4. **`ModuleLabelList` 静态构造器会读 `%LocalAppData%\CKAN\labels.json`**：该文件损坏会让类型初始化器抛异常（上游既有行为，`ModSearchFactsFactory` 早已触发）。服务侧对该路径做了 try/catch，但静态构造失败后同进程内无法恢复。
