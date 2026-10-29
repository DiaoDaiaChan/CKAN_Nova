# I18N 国际化运行时（M10 收口）

## 落地范围

| 层 | 文件 | 说明 |
| --- | --- | --- |
| 契约 | `src/CkanUI.Localization/ILocalizer.cs` | `Get`/`Format`/`TryGet` + `CurrentLocale`/`CurrentCulture`/`SetCulture`/`LanguageChanged`/`AvailableLocales`/`MissingKeys`/`Snapshot`/`ClearDiagnostics` |
| 契约 | `src/CkanUI.Localization/IStringSource.cs` | 语言包之外的补充字符串来源（元数据层、上游 Core 资源层） |
| 契约 | `src/CkanUI.Localization/ILocalizationDiagnostics.cs` | 诊断面：`LayerHits`、`IsPseudoLocalized`；与 `ILocalizer` 分开，避免给只读替身加必须实现的成员 |
| 实现 | `src/CkanUI.Localization/Localizer.cs` | 解析链 + 快照 + 缺键计数 + 层命中计数 + 来源结果缓存 + 伪本地化入口 |
| 实现 | `src/CkanUI.Localization/DictionaryStringSource.cs` | 扁平 JSON 或带 `strings` 子对象的语言包形状都能读；坏 JSON 记警告后返回 null，不抛 |
| 实现 | `src/CkanUI.Localization/CoreResourceStringSource.cs` | 上游 `CKAN.Properties.Resources` 反射层，只应答 `Core.` 前缀键 |
| 实现 | `src/CkanUI.Localization/PseudoLocalization.cs` | 伪本地化变形（`qps-ploc`） |
| 装配 | `src/CkanUI.Localization/LocalizerFactory.cs` | 内嵌包 / 内嵌+磁盘包两种装配；`CKAN_UI_PSEUDO_LOCALE` 环境变量开关 |
| 数据 | `i18n/packs/zh-CN.json`、`i18n/packs/en-US.json` | 各 930 键（76 条界面键 + 854 条页面 VM 运行期文案键），键集完全相同，并由 `ShippedPacksTests` + `LocalizationKeyDriftTests` 双向校验 |

## 解析链（一次 `Get` 的完整路径）

1. **界面语言包**（`pack`）：当前语言链 `zh-CN → zh` 优先，其后是回退语言链 `en-US → en`。两侧在 `BuildSnapshot` 时反向写入同一个扁平字典，高优先级覆盖低优先级，因此一次查表就能得到「当前语言 → 父语言 → 回退语言」的结果，热切换只替换快照引用。
2. **补充来源**（`source:<层名>`）：仅在语言包全链未命中后才按 `LocalizationOptions.Sources` 顺序探测。语义上这就是任务口径的「元数据包」层——元数据译文条目量大（实测 3,639 条）且按文本哈希索引，不能整表塞进 `DynamicResource` 快照，所以做成按 key 惰性探测的 `IStringSource`。
3. **回退键**（`missing`）：返回 key 本身并计数，绝不返回空串——空串在界面上等于「控件消失」，比看到 key 更难排查。

补充来源的探测结果按 `(locale, key)` 缓存（未命中缓存 `null`），因此「一次界面上有 200 个缺键」只会探测 200 次，不会因为绑定重算而反复问同一层。来源抛异常时只记一次警告并跳过该层，不中断整次解析——层是外部插件式扩展点，一个坏层不该让界面白屏。

`LayerHits` 的键约定：`pack`、`source:<层名>`、`missing`。`ClearDiagnostics()` 的返回值仍是「缺键 + 告警」条数（保持既有测试语义），同时清空 `LayerHits` 与来源缓存。

## 热切换

- `SetCulture` 在同一把锁内重建快照并原子替换 `_snapshot` 引用，锁外触发 `LanguageChanged`，处理器里读到的已经是新语言文案（`LanguageSwitchTests.Handler_already_sees_new_language_strings` 断言这条）。
- 语言未变时直接返回，**不触发事件**——否则设置页每点一次下拉就会整屏重算绑定。
- 切换后按新 locale 重新探测补充来源（缓存键含 locale），旧语言文案不会残留；`FallbackKeyCount` 暴露「靠回退语言撑住的键数」，供设置页显示覆盖率。
- 请求的语言没有随包时：不抛异常，记一次警告后按回退链工作，`CurrentLocale` 仍是用户选的语言（设置页显示「简体中文 99%」这类覆盖率，而不是偷偷把选择改回去）。
- 视图侧当前走 `LocalizationHost` 生成的 `ResourceDictionary` + `{DynamicResource Ui_NavRail_ManageMods}` 这类绑定的整表替换（`src/CkanUI.App/Localization/LocalizationHost.cs`）。16 篇 §4.3 描述的 `{loc:T}` 标记扩展尚未实现，见「已知缺口」。

## 伪本地化

- 开关：环境变量 `CKAN_UI_PSEUDO_LOCALE` = `1`/`true`/`yes`/`on`（`LocalizerFactory.PseudoLocaleEnvironmentVariable`），或代码里显式置 `LocalizationOptions.PseudoLocalize`。发布默认关。
- 变形：ASCII 字母换成带重音的近似字符、外包 `[!! ` / ` !!]`、用 `·` 补到 `ceil(len * 1.3)`。花括号内的占位符逐字符跟踪深度后原样保留，因此 `string.Format` 不会因为伪本地化抛异常；CJK 不变形，漏译的中文一眼可辨（变形后的英文里夹一段汉字）。
- 作用范围：语言包快照（含 `Snapshot()`）与经补充来源命中的文案；**缺键返回的 key 不加壳**——被加壳的 key 说明「这条本该翻译」，会让缺键探针看起来像通过。
- 不改覆盖率与缺译计数：伪本地化只是显示层滤镜。

## 缺译可见性

- `MissingKeys` 是 `key → 命中次数` 的并发字典，首次未见过的 key 额外走一次 `ILocalizationLog.Warn`（默认 `TraceLocalizationLog` 写 `System.Diagnostics.Trace`，App 层换成 log4net）。
- 探针路径：删掉 `zh-CN.json` 里任意一条译文 → 界面显示 key 且 `MissingKeys` 计数 +1，界面不崩（`T-I18N-09`）。

## 键规范与语言列表

- 键格式 `Ui.<Area>_<Element>[_<Qualifier>]`（`LocalizationKeyDriftTests.Constant_names_follow_the_key_convention` 静态校验；`LocalizationKeys` 的字段名是 PascalCase 缩写，如 `NavRailManageMods = Ui_NavRail_ManageMods`）。
- 两份语言包不得出现 `LocalizationKeys` 之外的键（死键守卫），也不得缺少任何常量（缺键守卫）。
- 语言列表取官方资源目录并集 13 项，`en-GB` 作为 `en-US` 别名；`LocaleInfo` 已带 `Coverage`/`CoveragePercent`/`DisplayText`，设置页直接显示「简体中文 (Chinese, Simplified) 99%」。

## 与上游的对应

| 上游 | 本项目 |
| --- | --- |
| `GUI/SingleAssemblyComponentResourceManager`（13 个语言子目录打进 exe） | 内嵌 `i18n/packs/*.json`（`EmbeddedResource` + `LogicalName=CkanUI.Localization.Packs.%(Filename)%(Extension)`），加磁盘覆盖目录便于热修 |
| `Core/Properties/Resources.resx`（209 条） | `CoreResourceStringSource` 反射 `CKAN.Properties.Resources.ResourceManager`，键加 `Core.` 前缀 |
| 只设 `CurrentUICulture`，改语言要重启（`SettingsDialog.cs:795`） | `SetCulture` 立即生效，`LanguageChanged` 驱动绑定重算 |
| 13 项语言、es-ES 有文件但不可选 | 并集 13 项，es-ES 可选（16 篇 Q1/Q4） |

## 页面 VM 文案层

视图层的文案走 `{DynamicResource}`（上面的键表），但页面**运行时**生成的文案——状态栏、空态提示、对话框标题与正文、命令执行反馈——在 XAML 里没有位置，只能由 ViewModel 拼字符串。缺口 ⑧ 收口后，这部分统一走 `PageViewModel` 的文案层，不再硬编码中文：

| 成员 | 位置 | 语义 |
| --- | --- | --- |
| `TextResolver`（`Func<string, string>?`） | `src/CkanUI.ViewModels/PageViewModel.cs:20` | 键 → 译文；为 null 时页面吐中文原文 |
| `TextFormatter`（`Func<string, object?[], string>?`） | `src/CkanUI.ViewModels/PageViewModel.cs:23` | （键, 实参）→ 译文；为 null 时用 `string.Format(CurrentCulture, 中文模板, 实参)` |
| `protected string Text(fallback, key)` | `src/CkanUI.ViewModels/PageViewModel.cs:28-29` | 无占位符文案：`TextResolver is null ? fallback : TextResolver(key)` |
| `protected string Text(fallback, key, args)` | `src/CkanUI.ViewModels/PageViewModel.cs:35-56` | 带 `{0}` 占位符文案；`FormatException` 时回落 fallback 原文 |

页面里的写法：

```csharp
public override string Title => Text("模组", LocalizationKeys.NavRailManageMods);
StatusText = Text("已启动「{0}」", LocalizationKeys.InstancesStatusLaunched, name);
```

- **解析器由导航统一挂**：`NavigationService.NavigateCore`（`src/CkanUI.ViewModels/Navigation/NavigationService.cs:153-157`）在页面被工厂解析出来之后执行 `page.TextResolver ??= _translate; page.TextFormatter ??= _format;`，其中 `_translate`/`_format` 由宿主注入（通常是 `ILocalizer.Get` / `ILocalizer.Format`）。页面因此不依赖任何本地化实现；`??=` 又允许页面或测试钉住自己的解析器。
- **null 即中文原文兜底**：两个解析器都为 null 时 `Text(...)` 直接返回源码里的中文 fallback（带占位符的自己 `string.Format`），所以既有断言中文的用例天然保持绿。只挂 `TextResolver` 不挂 `TextFormatter` 时，带占位符的文案仍按中文模板格式化——这条由 `tests/CkanUI.ViewModels.Tests/PageTextLocalizationTests.cs` 里 `ProbePage_ResolverOnly_StillFormatsChineseTemplate` 钉住，也是宿主必须同时注册 `Get` 与 `Format` 的原因。
- **键必须成套**：每处 `Text(...)` 的键既要在 `LocalizationKeys` 里有常量，也要在两套 `i18n/packs/*.json` 里都有译文；漏一边 `ShippedPacksTests`/`LocalizationKeyDriftTests` 会红。运行期若回退到键本身（`missing` 层），说明有人漏了译文。
- **整句一个键**：带占位符的长句（如「已选 {0} · 变更 +{1}/-{2}」）整体作为一个键，不拆成多段再拼接——别的语言语序不同，拼接会碎成沙拉句。
- **不搬的东西**：开发者异常消息（`throw new ArgumentException(...)`）、`IErrorReporter.Report(ex, "上下文")` 的上下文串、日志文案、诊断码、字段名、文件名、单位符号都不进文案层——它们不是给用户读的。XAML 里的文案仍走 `DynamicResource`。
- **覆盖范围**：17 个页面 ViewModel 的正文与标题已全部迁完，共 **852 处** `Text(...)`：Settings 197、Instances 105、Mods 71、InstallHistory 62、Unmanaged 55、Cache 51、Changes 43、ModPacks 42、Repos 38、Saves 38、Dedup 32、ModText 26、Plugins 20、Log 18、ModPackEditor 18、Content 15、PlayTime 12、DownloadStats 9。`ModConfigPageViewModel` 自带的旧式「中文即键」适配器已统一成 `T(fallback, key, args)`（内部转调 `Text`）。
- **尚未覆盖**（子 VM / 静态格式化器，它们不是 `PageViewModel`、拿不到 `protected Text()`）：`Pages/Changes/ChangesFormatter.cs`（39 处）、`ChangesLabels.cs`（19）、`Pages/ModPacks/ModPackEditorText.cs`（39）、`Pages/Mods/ModsSearchPanelViewModel.cs`（30）、`Pages/Logs/LogGlyphs.cs`（24）、`Pages/Settings/SettingsDiagnosticsViewModel.cs`（14）、`StabilityToleranceFile.cs`（14）、`Pages/InstallHistory/InstallHistoryText.cs`（17）、`SettingValidator.cs`（10）、`SaveRowViewModel.cs`（10）等。收口办法是给它们加可选 `Func<string,string>`/`Func<string,string,string>` 参数、由页面把方法组传下去；这层改造会影响它们的构造点（`SettingItemViewModel` 有 30 处），因此留作单独一轮。
- **错误栏消息**：`_errors.Report(ex, ctx, msg)` 的第三个参数（用户可见消息）已迁；第二个参数（上下文串）与 `InstancesErrorText.Compose(..., "xxx失败")` 的兜底句按 §10.3 的规矩保留中文。

## 已知缺口

1. **`{loc:T}` 标记扩展未实现**（`src/CkanUI.App/Localization/` 下只有 `LocalizationHost`/`Log4NetLocalizationLog`）：当前靠 `DynamicResource` 整表替换实现热切换，效果等价但每次切换重建 ResourceDictionary；16 篇 §4.3 的 `IObservable<string>` 方案需要在 App 层新增 `MarkupExtension`，不在本任务写作用域。
2. **字体回退链（16 篇 §7.1）未落到代码**：需要 `src/CkanUI.App/Presentation/Themes/Typography.axaml` 统一定义 `ThemeFontFamily`，属视图层。
3. **构建期 `missing.<locale>.txt` 未实现**：运行期已有 `MissingKeys`，构建期报表需要新增 MSBuild 目标。
4. **设置页语言下拉尚未接 `AvailableLocales`**：`IUiSettingsStore.Locale` 是真源（conventions §12.1），接线在 Lead 手里。
5. **Core 资源层依赖反射**：上游 `external/CKAN/Core/CKAN-core.csproj:91-93` 用 `UseOurResourceManager` 目标把 `Resources.Designer.cs` 的 internal 改写成 public，直接引用会与上游构建脚本强绑死，故取反射；上游若改名，该层退化为「只记一次警告 + 回退 key」。
6. **页面 VM 文案层还剩子 VM 与静态格式化器**：17 个页面 ViewModel 已覆盖（852 处），但 `ChangesFormatter`、`ChangesLabels`、`ModPackEditorText`、`LogGlyphs`、`InstallHistoryText`、`SaveRowViewModel`、`SettingValidator` 等子 VM / 静态合成器仍在硬编码中文——它们不是 `PageViewModel`，需要先接一个 `Func<string,string,…>` 适配器，见上面「尚未覆盖」清单。
