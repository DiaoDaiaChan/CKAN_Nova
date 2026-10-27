# 本地化（i18n）实现与接线说明

对应规格：`16-i18n.md`（字符串目录与切语言）、`03-ui-spec.md` §17（文案/排版/语言下拉）、
`06-wbs-milestones.md` UI-13（判据：**切语言无需改代码**）。

## 1. 职责边界

| 谁 | 做什么 |
| --- | --- |
| `src/CkanUI.Localization` | 字符串目录、回退链、占位符格式化、缺译统计、语言包加载（**不引用 Avalonia**） |
| `i18n/packs/*.json` | 随包语言包（数据，可替换，不必重新编译） |
| `CkanUI.App`（Lead 负责） | DI 注册、把快照灌进 Avalonia `ResourceDictionary`、`LanguageChanged` 时热替换、XAML 用 `DynamicResource` |
| `CkanUI.ViewModels` | 只通过 `ILocalizer` 取文案（含 `Format`），不写中文字面量 |

本层**不依赖 Avalonia**（架构测试 `Only_presentation_projects_reference_Avalonia` 会拦），
所以 DynamicResource 的接线必须由 App 侧完成，见 §6。

## 2. 目录

```
i18n/
  packs/zh-CN.json      随包语言包（内嵌进 CkanUI.Localization.dll，磁盘版本优先）
  packs/en-US.json      回退语言，必须 100% 完整
  glossary/             术语表
src/CkanUI.Localization/
  ILocalizer.cs         对外契约（Get/Format/TryGet/SetCulture/LanguageChanged/AvailableLocales/MissingKeys/Snapshot）
  Localizer.cs          实现：快照式解析 + 原子切语言
  LocalizerFactory.cs   装配：磁盘优先、内嵌兜底
  LanguagePackLoader.cs JSON 加载（宽松解析，坏包不抛）
  LanguagePack.cs / LocaleInfo.cs / LocalizationOptions.cs
  LocalizationKeys.cs   键常量（App/VM 引用常量，避免手写字符串）
tests/CkanUI.Localization.Tests/
```

## 3. 键规范（16 篇 §5.1）

`Ui_<Area>_<Element>[_<Qualifier>]`，只用 ASCII 字母/数字/下划线。
区域词表固定：`NavRail` `ManageMods` `ModInfo` `Changeset` `Settings` `Instances` `Repos` `Log`
`Saves` `ModConfig` `Tray` `Common`。例：`Ui_NavRail_ManageMods`、`Ui_Common_Status_StartupIncomplete`。

- 新增键先加到 `LocalizationKeys`，再补 zh-CN / en-US 两套包（`ShippedPacksTests` 会校验一致性）。
- 键本身不翻译、不改名；改文案只改语言包。

## 4. 解析与回退链

`当前语言 → 当前语言的父语言（zh-CN→zh-Hans→zh）→ en-US（及其父语言）→ key 本身`。

- 回退到 key 本身时：`MissingKeys` 里该 key 计数 +1，并**每个 key 只记一次 Warn**（防刷日志）。
- 占位符与参数不匹配时：返回原文，同样每个 key 只 Warn 一次，绝不抛异常。
- 语言包 JSON 损坏 / 目录不存在 / 配置里写了未随包的语言（如 `es-ES`）：记 Warn 后走回退链，不抛、不崩。
- `TryGet` 是纯探测，不计数、不记日志。

> 与规格的差异（已与 Lead 对齐口径）：16 篇 §4.2 只允许"语言链内回退、不跨语言"，
> 而 M1 任务要求 `当前语言 → en-US → key`。本实现按任务口径做，en-US 兜底是唯一跨语言回退。

## 5. 占位符

- 模板用 `{0}`、`{1}`…，禁止复数变位（16 篇 §8.3），一律"标签：值"写法。
- 格式化跟随 `CultureInfo.CurrentCulture`（数字/日期按系统区域）；**存盘一律 Invariant**。
- 各语言同一 key 的占位符数量与序号必须一致，`ShippedPacksTests.Placeholders_AreConsistentAcrossLanguages` 校验。

## 6. Avalonia 接线契约（App 侧必须做的事，本层不实现）

### 6.1 DI 注册（`src/CkanUI.App/ServiceRegistration.cs`）

```csharp
// 语言包目录优先取随包的 i18n/packs（可替换），内嵌资源兜底。
var i18nDir = Path.Combine(AppContext.BaseDirectory, "i18n", "packs");
var localizer = LocalizerFactory.CreateFromDirectory(i18nDir, new LocalizationOptions
{
    // App 侧用 log4net 适配一个 ILocalizationLog（Localization 层不引日志包）
    Log = new Log4NetLocalizationLog(logger),
    DefaultLocale = coreConfig.Language,   // Core 的 config.json 里存的 Language
    FallbackLocale = "en-US",
    ApplyThreadCulture = true,             // 只设 CurrentUICulture，不改 CurrentCulture
});
services.AddSingleton<ILocalizer>(localizer);
```

要求：
1. `ILocalizer` 必须是**单例**，且注入到需要文案的 VM 构造函数（页面 VM 已是单例，容器可解析）。
2. `LocalizationOptions.ApplyThreadCulture = true` 时只改 `CultureInfo.CurrentUICulture` 与
   `DefaultThreadCurrentUICulture`，**不得**改 `CurrentCulture`（数字/日期跟随系统，16 篇 §2.3 / Q3）。
3. 切语言后由 App 把 `localizer.CurrentLocale` 写回 Core 配置的 `Language`（持久化用 Invariant 标识）。

### 6.2 ResourceDictionary 热替换

```csharp
// 用快照一次性建字典：键名就是字符串 key，值就是译文（已应用回退链）。
static ResourceDictionary BuildResources(ILocalizer loc)
{
    var dict = new ResourceDictionary();
    foreach (var (key, value) in loc.Snapshot())
    {
        dict[key] = value;          // key 是 string，XAML 里可直接 {DynamicResource Ui_Common_Status_Ready}
    }
    return dict;
}

// LanguageChanged 可能在任意线程触发：必须封送到 UI 线程（IDispatcher）后再替换，
// 且要整表替换（先建新表，再换 MergedDictionaries 里的那一项），否则控件会短暂读到空值。
localizer.LanguageChanged += (_, _) => dispatcher.Post(() =>
{
    Application.Current!.Resources.MergedDictionaries[LocalizationDictionaryIndex] = BuildResources(localizer);
});
```

约束：
- 只支持"整表替换"，不支持逐 key 更新（逐 key 会在中间态出现混语言）。
- `x:Key`/`DynamicResource` 的键必须与 `LocalizationKeys` 常量完全一致（本实现的键不含点号与空格，
  可直接写 `{DynamicResource Ui_NavRail_ManageMods}`）。
- 必须在窗口首次显示前完成一次灌入，避免首帧显示裸 key。

### 6.3 XAML 用法

```xml
<TextBlock Text="{DynamicResource Ui_Common_Status_Ready}" />
```

**`DynamicResource` 不能带格式化参数**：带占位符的文案（如 `Ui_Common_Status_StartupIncomplete`）
必须在 VM 里用 `ILocalizer.Format(key, args)` 算好再暴露成属性，XAML 绑定该属性。

### 6.4 现有中文字面量的替换清单

| 位置 | 键 |
| --- | --- |
| `ShellViewModel` 导航项 `模组/实例/设置` | `Ui_NavRail_ManageMods` / `Ui_NavRail_Instances` / `Ui_NavRail_Settings` |
| `ShellViewModel._statusText = "就绪"` | `Ui_Common_Status_Ready` |
| 三个页面 VM 的 `Title` | `Ui_ManageMods_Title` / `Ui_Instances_Title` / `Ui_Settings_Title` |
| 页面 VM `Placeholder = "待 M1 实现"` | `Ui_Common_Page_Pending` |
| `PagePlaceholderView.axaml` 文案 | `Ui_Common_Page_ImplNote` |
| `App.axaml.cs:61` `"启动未完成：{...}"` | `Ui_Common_Status_StartupIncomplete`（用 `Format`） |
| `App.axaml.cs:69` `"启动失败，请查看日志。"` | `Ui_Common_Status_StartupFailed` |
| `Program.cs:83` 崩溃窗标题 | `Ui_Common_Error_Unhandled_Title` |
| `Program.cs:91` 崩溃窗正文 | `Ui_Common_Error_Unhandled_Body`（用 `Format`） |
| `Program.cs` `"未知错误"` | `Ui_Common_Error_Unknown` |
| 设置页语言下拉 | `LocaleInfo.DisplayText`（`简体中文 · Chinese, Simplified · 100%`，03 篇 §17.3） |
| 选到旧版不支持的语言 | `Ui_Settings_Language_LegacyWarning` |

> 日志文本（`Log.Info/Warn/Error`）属于诊断信息，不入字符串目录。

## 7. 运行时切语言（调用方视角）

```csharp
localizer.SetCulture("en-US");   // 立即生效：快照整体替换 + 触发 LanguageChanged
```

- 不重启、不重建 VM、不改代码（UI-13 判据）。
- 语言未变时是空操作，**不触发** `LanguageChanged`（避免无谓重刷）。
- 未随包的语言不抛异常，只 Warn 并走回退链（兼容 Core 配置里存了 es-ES 的场景）。

## 8. 测试

`dotnet run --project tests/CkanUI.TestRunner -- Localization`（runner 不重建测试项目，改测试源码要先 build sln）。

42 个用例，覆盖：切语言与事件、回退链（父语言/en-US/不跨语言）、缺 key 计数与 Warn 去重、
占位符格式化与坏模板降级、并发读写、JSON 损坏降级、语言包完整性（键集一致、键规范、占位符一致、100% 覆盖率）、
语言下拉显示格式。

## 9. 待办与已知限制

- 语言包目前只覆盖 M1 已存在的界面文案；M2 起新页面要同步补键（缺键会走回退链并进 `MissingKeys`，不会崩）。
- 尚未接入 Core 官方 `resx` 快照（16 篇 §4.2 的 L2）与 `Core.` 前缀的 L3 解析——M1 范围内不需要，
  但 `Localizer` 的链式解析结构已预留（新增一层只需在 `BuildSnapshot` 里多叠一个字典）。
- 字体回退链（03 篇 §17.2 `Inter, Microsoft YaHei UI, …`）属于 App 主题资源，不在本层。
