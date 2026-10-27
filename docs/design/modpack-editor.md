# modpack 编辑器（F-46 / UI-24 编辑部分）

## 目标与范围

让用户在不离开界面的前提下**创建 / 编辑一个 `.ckan` 元数据包**，并保证写出来的文件能被 CKAN 与启动器直接消费。

覆盖字段：标识、名称、摘要、描述、作者、版本、许可证、发布状态、包类型（kind）、游戏版本与上下限、
下载地址、规范版本，以及 `depends` / `recommends` / `suggests` / `conflicts` / `supports` 五类关系行
（每行可选「单选 / 多选 / 任意一个」三种语义）。

不在范围内：下载与安装（属于导入流水线 F-40/F-41）、从实例反向生成 modpack（属于 F-39 导出）、
模组搜索与挑选器（编辑器只认标识字符串，不内嵌 mod 列表）。

## 分层与文件表

| 层 | 文件 | 行数 | 职责 |
| --- | --- | --- | --- |
| 契约 | `src/CkanUI.Services.Abstractions/Dtos/ModPackEditor.cs` | 157 | `ModPackEditorDocument` / `ModPackRelation` / `ModPackEditorIssue` / `ModPackRelationModes` / `ModPackRelationTypes`，零 CKAN 类型 |
| 契约 | `src/CkanUI.Services.Abstractions/IModPackService.cs`（追加 DIM） | +21 | `LoadEditorAsync` / `SaveEditorAsync` / `ValidateEditor`，默认实现一律返回"不可用" |
| 服务 | `src/CkanUI.Services/ModPacks/ModPackEditorStore.cs` | 339 | 读：`new CkanModule(json, StrictGameComparator)`；写：`CkanModule.ToJson()` + `.tmp` + `File.Move(overwrite: true)` |
| 服务 | `src/CkanUI.Services/ModPacks/ModPackEditorValidator.cs` | 396 | 纯函数校验，返回带 `Code` / `Field` / `RowIndex` 的中文问题列表 |
| 服务 | `src/CkanUI.Services/ModPacks/ModPackService.Editor.cs` | 30 | `partial class ModPackService` 接上三个 DIM 成员，复用既有的 `RunAsync` 取消/异常包装 |
| VM | `src/CkanUI.ViewModels/Pages/ModPackEditorPageViewModel.cs` | 502 | 表单状态、加载/保存/校验命令、脏标记、问题列表 |
| VM | `src/CkanUI.ViewModels/Pages/ModPacks/ModPackEditorRowViewModel.cs` | 53 | 作者 / 许可证的单行编辑 |
| VM | `src/CkanUI.ViewModels/Pages/ModPacks/ModPackRelationRowViewModel.cs` | 134 | 一行关系：类型、语义模式、目标、版本上下限、候选列表切分 |
| VM | `src/CkanUI.ViewModels/Pages/ModPacks/ModPackEditorText.cs` | 99 | 全部中文文案与下拉选项，VM 不散落字符串 |
| 视图 | `src/CkanUI.App/Views/ModPackEditorPageView.axaml`(+ `.cs`) | 307 / 20 | 表单 + 关系表 + 问题栏 + 状态栏，只用 `Ckan*` 资源键 |
| 测试 | `tests/CkanUI.Services.Tests/ModPackEditorStoreTests.cs` | 480 | 20 例：写盘字段名、往返保真、any_of / 多选 / 精确版本、ksp 互斥、拒绝写盘、覆盖写、外部文件读取 |
| 测试 | `tests/CkanUI.Services.Tests/ModPackEditorValidatorTests.cs` | 309 | 26 例：标识、版本字符集、许可证、枚举、ksp 互斥、download 联动、关系行、排序、纯函数性 |
| 测试 | `tests/CkanUI.ViewModels.Tests/ModPackEditorPageViewModelTests.cs` | 475 | 24 例：投影、加载、保存闸门、校验、脏标记、关系行增删与模式切换 |

依赖方向：视图 → VM → `IModPackService`（Abstractions）→ 服务层 → 上游 CKAN。
VM 与视图**不出现任何 CKAN 类型**，因此页面可以脱离 Core 单测（架构测试 NamespaceRulesTests 约束）。

## 关键口径

### 1. 序列化不发明字段

写盘完全走 `CkanModule.ToJson()`，读盘完全走 `new CkanModule(json, comparator)`。因此：

- 字段名、顺序、缩进（4 空格）、`license` 的单值/数组两种写法、`author` 的单值/数组两种写法，
  都是 CKAN 自己的行为，编辑器不复制一份映射表。
- `spec_version` 一律由 `SpecVersionAnalyzer.MinimumSpecVersion(module)` 按实际用到的特性反推
  （与 `RegistryManager.GenerateModpack` 同一口径）；用户手填的 `spec_version` 只作为**构造初值**，
  保存时会被反推结果覆盖。`stable + metapackage + MIT` 推导结果是 `v1.6`，`development` 会推到 `v1.36`。
- 没有关系行时五个关系键**不出现**（上游 `NullValueHandling.Ignore`），不写空数组。

### 2. 三种关系语义

| 编辑器模式 | 上游形状 | 说明 |
| --- | --- | --- |
| `single` | `{"name": "x", "min_version": "1.0", "max_version": "2.0"}` | 单选一个模组，可带版本范围 |
| `anyOf` | `{"any_of": [{"name": "x"}, {"name": "y"}]}` | 任意一个候选（上游 `AnyOfRelationshipDescriptor`） |
| `multi` | 展开成 N 条 `{"name": ...}` | 纯界面便利，磁盘上没有对应字段 |

版本折叠规则：`ModuleRelationshipDescriptor` 有 `version`（精确）/ `min_version` / `max_version` 三种写法，
编辑器只暴露上下限。读到 `version` 时上下限都填成该值（语义等价）；写回时若上下限相等则折叠成 `version`，
否则写 `min_version` / `max_version`。这样"打开→保存"不会把精确版本悄悄降级成范围。

### 3. 两条来自上游的硬约束（都是实测撞出来的）

**`ksp_version` 与 `ksp_version_min` / `ksp_version_max` 互斥。** `Spec.md:376` 写着"It is an error to include both"，
上游 `CkanModule.CheckHealth()` 也确实会抛 `InvalidModuleAttributesKraken: ksp_version 不能与 ksp_version_(min|max) 混用`。
这种文件**写得出去、读不回来**，所以：校验阶段判 Error（`ksp_version.conflict`），
`BuildModule` 里只要有上下限就**绝不写**精确版本（双保险）。

**`ModuleVersion` 不能用来判断"版本能否解析"。** 上游 `ModuleVersion` 的构造函数与比较器完全按字符串走，
`new ModuleVersion("一.零")`、`new ModuleVersion("")` 都不抛异常。因此版本号改用 `Spec.md:217` 与 `Spec.md:241` 的口径
（`[epoch:]mod_version`，主体只允许 ASCII 字母、数字与 `. + - _`）做正则校验：
`^([0-9]+:)?[0-9A-Za-z.+\-_]+$`。游戏版本仍然交给 `GameVersion.TryParse`（它对 `"一.零"` / `"abc"` 确实返回 false）。

### 4. 校验分级

只有 `Error` 阻止写盘，`Warning` 照样保存并提示：

- **Error**：路径为空、标识为空/含大写/字符集不合法、名称为空、摘要为空、版本字符集不合法、`spec_version` 字符集不合法、
  `ksp_version` 与上下限混写、
  游戏版本不可解析、许可证为空或不在 `License.valid_licenses` 白名单、发布状态或包类型不在白名单、
  `kind=package` 缺下载地址、下载地址不是 http(s) 绝对地址、关系行类型/模式非法、关系名为空或含大写、
  候选列表为空、关系版本不可解析。
- **Warning**：路径缺 `.ckan` 后缀、没有作者、游戏版本下限 > 上限、关系版本下限 > 上限、
  `any_of` 只有 1 个候选、同一类型下重复的关系。

标识字符集沿用上游 `CKAN.Identifier.ValidIdentifierPattern`（`^[A-Za-z0-9][A-Za-z0-9-]+$`，至少 2 字符），
再叠加"必须全小写"——上游正则本身不要求小写，但 CKAN 标识大小写敏感，大写会让依赖解析找不到包。

### 5. 脏标记

`OnPropertyChanged` 统一拦截：除基础设施属性（`IsDirty` / `IsBusy` / `BusyText` / `StatusText` /
`ErrorText` 与派生只读属性）外，任何表单字段变化都置脏；三个集合的 `CollectionChanged` 同样置脏。
`ApplyDocument` 期间用 `_suppressDirty` 抑制，因此"加载完成"与"新建文档"之后是干净的。

## 安全与失败语义

- **不写半个文件**：先写 `<目标>.ckan.tmp`（UTF-8 无 BOM），再 `File.Move(tmp, target, overwrite: true)`。
  写盘失败时原文件保持不变，`.tmp` 也不残留（成功路径上 `Move` 已消费掉它）。
- **校验不通过一个字节都不写**：`Save` 先跑 `ModPackEditorValidator`，有 Error 就直接返回
  `校验未通过，没有写入文件：` + 逐条 `· 原因`，连 `Directory.CreateDirectory` 都不会执行。
- **不抛异常给界面**：`Load` / `Save` 把 IO 异常、`JsonException`、`BadMetadataKraken` 都转成
  `OperationResult.Fail`；`ModPackService` 再套一层 `RunAsync` 把 `OperationCanceledException` 映射成
  "操作已取消。"（可重试）。页面侧 `RunAsync` 兜住任何漏网异常写进错误栏。
- **不联网**：读写的只有本地文件；`download` 只做格式校验，不发起请求。
- **不依赖 DI 容器**：`ModPackEditorStore` 显式传入 `StrictGameComparator`。上游 `CkanModule` 构造函数在
  comparator 为 `null` 时会去 `ServiceLocator.Container`（Autofac）解析，测试环境没有容器会直接抛。
- **路径只做规整不做限制**：`NormalizeTargetPath` 补 `.ckan` 后缀并转绝对路径；用户可以写任意可写位置
  （与导出功能一致），越权由文件系统自身拒绝并转成错误栏文案。

## Lead 需要落地的接线

1. `src/CkanUI.App/ServiceRegistration.cs:97` 之后追加：

   ```csharp
   services.AddSingleton<ModPackEditorPageViewModel>();
   ```

2. `src/CkanUI.App/ServiceRegistration.cs:170`（`PageTypes` 表）之后追加：

   ```csharp
   ["modpack-editor"] = typeof(ModPackEditorPageViewModel),
   ```

3. `src/CkanUI.ViewModels/ShellViewModel.cs:54` 之后追加导航项：

   ```csharp
   new("modpack-editor", _translate("Ui_NavRail_ModPackEditor"), "Edit"),
   ```

   并在 `FallbackTexts`（`ShellViewModel.cs:151` 附近）加 `["Ui_NavRail_ModPackEditor"] = "modpack 编辑器",`，
   同时在 `src/CkanUI.Localization/LocalizationKeys.cs` 与 `i18n/packs/*.json` 里补同名键。

4. `tests/CkanUI.App.Tests/ViewLocatorTests.cs:21` 之后追加：

   ```csharp
   Assert.That(ViewLocator.ResolveViewType(typeof(ModPackEditorPageViewModel))?.FullName, Is.EqualTo("CkanUI.App.Views.ModPackEditorPageView"));
   ```

5. `docs/README.md` 的设计文档索引里加一行 `docs/design/modpack-editor.md`（否则 `verify-layout` H-05 一直提醒）。

无需改 DI 服务注册：编辑器能力是 `IModPackService` 上的 DIM 成员，`ModPackService` 已是单例，
内部 `ModPackEditorStore` 无依赖，直接 `new` 即可。

## 验收实测

`dotnet build ckan-ui.sln -v q --nologo` → `0 个警告 / 0 个错误`。

- `CkanUI.Services.Tests`：`Test Count: 422, Passed: 421, Failed: 0, Warnings: 0, Inconclusive: 0, Skipped: 1`
  （跳过的是既有用例，非本次新增；本包 20 + 26 = 46 例全绿）
- `CkanUI.ViewModels.Tests`：`Test Count: 675, Passed: 669, Failed: 6` —— 6 个失败全部在
  `ChangesGameRunningTests` / `DownloadStatsPageViewModelTests` / `ShellViewModelTests`（并行 agent 的改动），
  本包 24 例全绿。
- `CkanUI.Architecture.Tests`：`Test Count: 9, Passed: 9`（DTO 无 CKAN 类型、VM 无 Avalonia/CKAN 引用均通过）。
- `CkanUI.App.Tests`：`Test Count: 45, Passed: 45`（ViewLocator 断言待 Lead 补，见接线第 4 条）。
- `tools/verify-layout`：阻断级 0 项；H-05 提醒本文件未登记 `docs/README.md` 索引（Lead 落地）。

开发期另用一次性 NUnitLite 宿主单独跑过这 70 例（不依赖整份 solution 编译），确认 70/70 通过后才合并回主线。

## 已知缺口

1. **没有资源（resources）/ 本地化（localizations）/ provides 字段**：编辑器只覆盖 modpack 常用的那批字段。
   打开一个含 `resources` 或 `provides` 的文件再保存，这些键会丢失（上游 `CkanModule` 会解析它们，但
   `BuildModule` 没有把它们写回去）。修法：在 `ModPackEditorDocument` 上加对应字段并在 `BuildModule` 里回填。
2. **没有安装信息（install / download_hash / download_size）**：同理，编辑 `kind=package` 的完整模组会丢字段。
   当前定位是 modpack（`metapackage`）编辑器。
3. **`multi` 模式读回是 N 条 `single`**：磁盘上没有"多选"概念，往返后行数会变多。这是有意的取舍
   （不发明字段），但用户会看到"我写的一行变成了三行"。
4. **作者/许可证是单行文本**：没有做去重与合法性即时提示，错误要等点"校验"或"保存"才出现。
5. **没有模组标识自动补全**：编辑器不查注册表，拼错标识只能靠"校验"发现字符集问题，不能发现"包不存在"。
6. **`spec_version` 手填值不生效**：保存时总被 `SpecVersionAnalyzer` 覆盖。若将来需要精确控制规范版本，
   得在 `BuildModule` 里加开关。
