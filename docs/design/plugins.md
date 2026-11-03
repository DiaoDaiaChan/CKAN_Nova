# 插件系统（F-17 / UI-33）

## 1. 目标与范围

让第三方把 dll 放进插件目录后，能在「插件」页里**激活 / 停用 / 重载 / 卸载 / 新增**，并声明自己的页签或工具项。规格出处：doc/uir/12-feature-parity.md 的 F-17 行、doc/uir/06-wbs-milestones.md 的 UI-33；旧客户端口径参考
GUI/Plugins/PluginController.cs、GUI/Plugins/IGUIPlugin.cs、GUI/Dialogs/PluginsDialog.cs。沿用原版两栏模型
（Active / Dormant）与按钮启用规则（左栏选中 → 停用 + 重载；右栏选中 → 激活 + 卸载），但把原版四处**静默失败**
改成结构化结果：加载抛异常只写日志 → 带中文原因的 PluginLoadResult；Activate/Deactivate 在 catch 里 return → 失败且状态不变；
构造失败 = 类型消失 → PluginState.Failed 条目并在页面底部列出；重载永不释放程序集 → 可卸载 AssemblyLoadContext + Unload。
不做：插件商店/远程分发、插件间依赖解析、进程内沙箱。

## 2. 契约层（CkanUI.Services.Abstractions，纯 BCL）

ICkanUiPlugin（ICkanUiPlugin.cs）——元数据用属性而非方法：宿主必须在不构造实例的前提下显示插件自述，原版 IGUIPlugin.GetName() 得先构造，构造抛异常就什么都不剩。

| 成员 | 说明 |
| --- | --- |
| Id / Name / Version / Author / Description | 插件自述；Id 空串视为无效插件，Name 空串视为元数据不完整 |
| MinimumHostVersion | 空串或无法解析 = 不限制（1.2.0-beta 这类预发布串放行） |
| Pages（DIM，默认空） | PluginPageDescriptor(Key, Title, ViewModelTypeName)；纯后台插件不声明页签合法 |
| ToolItems（DIM，默认空） | PluginToolItemDescriptor(TargetPageKey, Key, Title, ToolTip)；声明往哪个内置页面工具条插一个按钮 |
| ExecuteToolItemAsync(key, ct) | 工具项被点击时的回调；默认实现抛 NotSupportedException，宿主把它转成可读失败 |
| Initialize() / Deinitialize() | 激活/停用钩子；Deinitialize 必须可反复调用（重载路径 Initialize → Deinitialize → Initialize） |

IPluginService（IPluginService.cs）：PluginsDirectory、Active、Dormant、Failures、event Changed、DiscoverAsync、LoadAsync(path,
activate)、ActivateAsync / DeactivateAsync / ReloadAsync / UnloadAsync(id)、AddFromPathAsync(sourcePath, activate)、InvokeToolItemAsync(pluginId,
itemKey, ct)（默认实现返回不可重试失败，替身不必跟着改）。
Failures 与 Activate/Deactivate 是相对任务书列表的追加：按钮按 Id 操作，失败条目要留痕给用户看。DTO：PluginState（Dormant=0/Active=1/Failed=2）、PluginInfo（AssemblyPath、State、
FailureReason、Pages、ToolItems，派生 IsActive / DisplayName = 「名字 - 版本」，对齐原版 IGUIPlugin.ToString()）、PluginLoadResult（Ok / Fail）、
PluginToolItemDescriptor（TargetPageKey / Key / Title / ToolTip）、PluginLoadFailure（None / NotFound / NotAnAssembly / MissingDependency / NoPluginType / HostVersionTooOld / InstantiationFailed /
MetadataFailed / AlreadyLoaded / DuplicateId / ActivationFailed / LoadFailed）。
契约层零依赖：不放 Type、不放视图类型、不收 CKAN 类型——页签用类型全名字符串，宿主按 ViewLocator 约定反射解析。

## 3. 安全边界（写死在服务层与页面文案里）

1. **默认不启用**：DiscoverAsync 一律 activate: false，扫描到的 dll 全部进 Dormant。原版 new PluginController(path, activate: true) 启动即激活，等于把「用户放了个文件」当成授权。
2. **必须显式激活**：只有用户点「激活」（或 ActivateAsync / LoadAsync(activate:true)）才执行插件代码的 Initialize。
   加载阶段只反射元数据，但**构造实例会执行插件的构造函数**——所以构造也放在 try/catch 里隔离。
3. **不做沙箱承诺**：插件与宿主同进程同权限，Initialize 能做的任何事都拦不住；页面页头直接写明，不暗示「停用 = 隔离」。
   卸载只释放引用并 Unload ALC，**不阻止**插件已写文件或已启线程。
4. **失败隔离**：缺依赖、版本不符、构造抛异常、元数据 getter 抛异常、类型不存在、非托管 PE——全部转成 PluginLoadResult 失败并释放 ALC，宿主不崩、不留半加载状态。
5. **插件目录只由 IPluginService 写**：AddFromPathAsync 是唯一入口，只接受 .dll，同名先卸旧再覆盖。

插件目录默认 %LocalAppData%/CKAN/plugins（PluginService.DefaultPluginsDirectory()：LocalApplicationData + "CKAN"
+ "plugins"），首次 DiscoverAsync / AddFromPathAsync 时按需创建。

## 4. 加载器（Services/Plugins/）

- PluginLoadContext（internal，AssemblyLoadContext，isCollectible: true，名 ckan-ui-plugin-<guid>）的 Load(AssemblyName) 对契约程序集与
  System/Microsoft/netstandard/mscorlib/WindowsBase/CkanUI/Avalonia 前缀**返回 null**（交回默认上下文）：否则插件加载到第二份
  CkanUI.Services.Abstractions，typeof(ICkanUiPlugin).IsAssignableFrom 判假、InvalidCastException——ALC 最容易踩的坑。其余依赖按插件目录探测 <简单名>.dll。
- AssemblyLoadContextPluginLoader（public，实现 public 窄接口 IPluginAssemblyLoader）：File.OpenRead + LoadFromStream，
  **不用** LoadFromAssemblyPath——后者在 Windows 上一直占着 dll 句柄，用户无法替换/删除（07 篇 H-5 验收会卡住）。
- 类型发现：assembly.GetTypes() 里取第一个 IsVisible && !IsAbstract && IsClass && typeof(ICkanUiPlugin).IsAssignableFrom 的类型，
  **取代**原版「文件名点两次」的 "{0}.{0}" 命名约定（那个约定强制重命名 dll 才能改类型名）。
- ReflectionTypeLoadException → MissingDependency（消息拼前 3 条 LoaderExceptions）；BadImageFormatException → NotAnAssembly；
  TargetInvocationException / 无公共无参构造 → InstantiationFailed。所有失败分支先 Release() 再返回。
- IPluginAssemblyLoader 是 public 接缝（同 IInstallerBackend 先例），测试注入假加载器做确定性失败；PluginAssemblyLoad
  带幂等 Release()（Interlocked.Exchange），服务层每个入口只释放一次。

## 5. 服务层流程（Services/Plugins/PluginService.cs）

状态：Dictionary<string, PluginEntry>（键 = 插件 Id，OrdinalIgnoreCase）+ List<PluginInfo> _failures。**插件代码（构造 / Initialize /
Deinitialize / 元数据 getter）一律在锁外调用，锁内只改表**，插件回调宿主不会自锁死。

- DiscoverAsync：确保目录存在 → 枚举 *.dll（顶层不递归）→ 排序 → 跳过已加载路径 → 逐个 LoadAsync(activate:false)；目录读不到时记日志返回空快照，不抛。
- LoadAsync：路径空 → NotFound；GetFullPath 失败 → LoadFailed；同路径已加载 → AlreadyLoaded（「该文件已经加载为插件
  「X」，请先卸载或重载。」）；加载器失败 → 记 Failures；Id 空 → LoadFailed、Name 空 → MetadataFailed；版本不符 →
  HostVersionTooOld（「要求宿主 X 或更高，当前为 Y」）；同 Id 已存在 → DuplicateId（并释放新加载的程序集）；否则入表。
- ActivateAsync / DeactivateAsync：Initialize / Deinitialize 抛异常 → 失败结果，状态不变。
- ReloadAsync：记原路径与原激活状态 → UnloadAsync（先 Deinitialize 再 Release）→ 重新加载并恢复激活状态。
- UnloadAsync：移除 → 已激活的先 Deinitialize（异常只记日志，继续卸载）→ Release()。
- AddFromPathAsync：存在性 + .dll 扩展名校验 → 复制进插件目录（源已在目录内则跳过）→ 目标已加载则先卸载 → 加载。
- Changed 在**锁外**按订阅者逐个 try/catch 触发（工程约定 §12.4）。

## 6. 页面层与视图

- src/CkanUI.ViewModels/Pages/PluginsPageViewModel.cs（PageViewModel 子类，Title => "插件"）：ActivePlugins / DormantPlugins
  （PluginRowViewModel）、SelectedActive / SelectedDormant（互斥）、PluginsDirectory、FailureTexts、StatusText / ErrorText /
  ErrorDetail / IsBusy / BusyText；按钮条件：CanActivate（选中右栏）、CanDeactivate（选中左栏）、CanReload / CanUnload（任选）、
  CanAddNew（注入了 IPluginFilePicker）、CanRefresh。
- 文件选择走注入窄接口 IPluginFilePicker（Pages/Plugins/IPluginFilePicker.cs，范式同 ICacheZipPicker），视图侧
  AvaloniaPluginFilePicker（Views/PluginsPageView.axaml.cs）用 TopLevel.StorageProvider；无选择器时（设计时预览、单测）
  AddNewAsync 给出「当前视图没有提供文件选择器」，不静默禁用。
- Views/PluginsPageView.axaml：页头（含「默认不启用 / 不沙箱」说明）+ 工具条 6 按钮 + 错误条 + 两栏 ListBox（ColumnDefinitions="*,16,*"）+ 状态栏（状态文案、失败清单、插件目录）+ 忙碌遮罩；颜色/圆角全部走 Ckan* token。
- **不订阅 Changed 而是拉取式**：服务层可能在后台线程回调，直接改绑定集合会踩跨线程集合异常；本页所有状态变化都
  源自本页命令，命令末尾统一 ApplyState() 重拉快照。事件留给 Shell 级监听者（刷新导航项，见下节）。

### 6.1 插件页签的渲染（F-17 的「页签」这一半）

插件声明页签只需要三步：在 `ICKANUIPlugin.Pages` 里返回 `PluginPageDescriptor(Key, Title, ViewModelTypeName)`，
视图模型实现 `IPluginPage`（`src/CkanUI.Services.Abstractions/IPluginPage.cs`），并把视图与视图模型放在**同程序集、
同命名空间**、只差类型名后缀（`XxxPageViewModel` → `XxxPageView`）。插件因此只引用契约程序集即可编译，
不需要引用宿主的视图模型层——但它自己要引用 Avalonia（视图是控件）。

- **导航项**：`ShellViewModel` 订阅 `IPluginService.Changed`，用 `PluginPageCatalog.Collect(Active, 内置键)` 取页签，
  插在「插件」页之后（不打断内置页签的分组顺序）。过滤口径集中在 `src/CkanUI.ViewModels/Navigation/PluginPageCatalog.cs`：
  只要 Active、按插件名排序稳定、键/标题/类型名任一为空即丢弃、与内置键或先到的插件键重名即丢弃（忽略大小写）。
  `Changed` 可能来自后台线程，改集合前用构造时捕获的 `SynchronizationContext` 封送（视图模型层不引用 Avalonia，架构铁律 1）。
- **集合是差分的不是换新**：`NavItems` 改成 `ObservableCollection<NavItem>` 后逐项比对、只插删必要项，插件页签的
  `NavItem` 按 key 缓存复用，否则整体换集合会把 ListBox 的选中项变成悬空引用、当前页签不高亮。
  当前页所属插件被卸载时退回「插件」页，避免停在一个没有入口的页面上。
- **页面实例**：页面工厂先查内置 `PageTypes`，查不到再交给 `src/CkanUI.App/Plugins/PluginPageFactory.cs`：
  在插件页签里按 key 找描述 → 在已加载程序集里解析类型（`Type.GetType` 先试，失败再逐程序集按全名找，
  可卸载 ALC 里的类型只能这样找）→ `ActivatorUtilities.CreateInstance` 构造（插件页可注入宿主服务）。
  **不做类型缓存**：缓存 `Type` 会钉住可回收 ALC，插件卸载后程序集再也回收不了。
- **视图解析**：`ViewLocator.Match` 现在接受 `PageViewModel` 或 `IPluginPage`；`ResolveViewType` 在内置命名空间改写
  查不到时，退回「视图模型自己的程序集里找同命名空间的 `XxxView`」。解析不到就回退占位视图，导航不会因插件缺视图而崩。

### 6.2 插件工具项（F-17 的「工具项」这一半）

工具项 = 往**已存在的**页面工具条插一个按钮。与页签相反，这条路**不走反射**：

- **为什么不用 ViewModelTypeName**：工具项只是「一个标题 + 一次回调」。若沿用页签的做法，插件就得为一个按钮写一个空视图模型，
  反射失败还会让按钮静默消失——插件作者很难看出是自己把类型名写错了。
- **契约**：`ICKANUIPlugin.ToolItems`（`PluginToolItemDescriptor(TargetPageKey, Key, Title, ToolTip)`）
  + `ExecuteToolItemAsync(key, ct)`。`TargetPageKey` 用内置页签键（当前只有 `mods`，见 `PluginToolCatalog.ModsPageKey`）；
  键不认识就当没声明，不会往不存在的页面投递。
- **执行**：`IPluginService.InvokeToolItemAsync(pluginId, itemKey, ct)`。宿主先校验「插件已加载 / 已激活 / 确实声明了这个键」，
  再在**锁外**调插件；插件抛异常只会变成可重试失败（`工具项「标题」执行失败：…`），不冒泡到 UI。
  声明了却没实现的插件拿到的是默认实现抛出的 `NotSupportedException`，宿主同样转成可读失败——用户看到「未实现」，而不是点了没反应。
- **收集**：`src/CkanUI.ViewModels/Navigation/PluginToolCatalog.cs`。只收 Active、按插件名再 Id 排序、
  `TargetPageKey`/`Key`/`Title` 任一为空即丢弃、同「页面 + 键」重名留排序在前的那个（与页签的重名规则一致）、
  空白 ToolTip 归一为 null。页面侧再用 `ForPage(entries, pageKey)` 过滤，忽略大小写。
- **分发**：`src/CkanUI.App/Plugins/PluginToolItemHost.cs`（App 层）。构造时拿 DI 里全部 `IPluginToolHost`（页面单例，与外壳同一实例）；
  `Install()` 先算一次——发现/激活往往发生在安装之前——之后只跟随 `IPluginService.Changed`，
  并经 `IDispatcher` 封送回 UI 线程（按钮集合是 `ObservableCollection`）。单个页面组装失败只记 Warn、不影响其它页面；
  `Active` 读失败整轮跳过（下次 Changed 再试）。
- **落点**：模组页工具条（`src/CkanUI.App/Views/ModsPageView.axaml`，`Grid.Column="10"`、`IsVisible="{Binding HasPluginTools}"`）。
  `ModsPageViewModel` 实现 `IPluginToolHost`（`ToolHostKey => "mods"`），`PluginTools` 是按钮集合；
  新页面要接工具项只需实现这个接口并在 DI 里注册别名，宿主代码不用改。
- **按钮**：`PluginToolButtonViewModel`。`IsBusy` 兼作重入保护（连点只执行一次）；`LastMessage` 记最后一次结果，
  并把 `ToolTipText` 从「插件名 · 键」换成结果文本——插件没有自己的状态栏，这就是它的反馈通道。

## 7. 测试


- tests/CkanUI.Services.Tests/PluginServiceTests.cs（23 个用例）：发现只加载不激活、目录不存在时自动创建、同路径重复加载被拒、
  加载器失败留痕且不抛、同 Id 拒绝并释放新程序集、最小宿主版本拒绝 / 不可解析视为不限、Id 空拒绝、元数据 getter 抛异常被隔离、
  Initialize 抛异常留在 Dormant、停用调 Deinitialize、卸载清空列表并释放、重载保激活状态、
  新增复制进目录 / 非 dll 拒绝 / 源缺失拒绝、Changed 订阅者抛异常不影响加载、取消被尊重；另有 3 个走**真实加载器**：
  非托管 PE（MZ + 垃圾字节）→ NotAnAssembly、文件缺失 → NotFound、把宿主程序集拷进插件目录 → NoPluginType（独立 ALC 里契约类型
  身份不同）。临时目录用 CkanUI.TestSupport.TempWorkspace（**不要用 Path.GetTempPath()，本沙箱下建目录会被拒**）。
- tests/CkanUI.ViewModels.Tests/PluginsPageViewModelTests.cs（15 个用例）：两栏分组、选中互斥、按钮启用条件、激活/停用/重载/卸载的
  调用与状态文案、无文件选择器的提示、失败清单投影、忙碌态保护、取消与错误上报。App.Tests/ViewLocatorTests.cs 另加 PluginsPageViewModel
  → CkanUI.App.Views.PluginsPageView 断言。

- tests/CkanUI.Services.Tests/PluginToolItemTests.cs（8 个用例）：元数据透传到 PluginInfo、按声明的键回调（忽略大小写与前后空白）、
  未知插件 / 未激活 / 未声明的键三种拒绝、插件抛异常转成可重试失败、只声明没实现时给出「未实现」提示。
- tests/CkanUI.ViewModels.Tests/PluginToolCatalogTests.cs（12 个用例）：只收 Active、空白字段丢弃、同页面同键去重留排序在前者、
  按页过滤忽略大小写；按钮的成功 / 失败 / 异常文案、忙碌时连点只执行一次、ToolTip 三级回退。
- tests/CkanUI.App.Tests/PluginToolItemHostTests.cs（7 个用例）：按目标页分发、未知页键不投递、Changed 后重算、
  Active 抛异常不崩、Dispose 后不再刷新、按钮回调带上声明者与键、构造参数校验。

## 8. 已知缺口

- **插件页签的视图必须自带**：宿主只认「同程序集同命名空间、后缀换 View」这一条约定，没有 XAML 文件名约定之外的兜底；
  插件视图还必须是 Avalonia 控件且有无参构造函数，否则该页签显示占位视图。
- **ALC 卸载是「尽力而为」**：Unload() 只是标记可回收，插件若持有静态引用、启动线程或注册全局事件，程序集不会被真正回收（宿主不阻止也不检测）；装载计数/回收验证未做。
- **不做签名校验**：任何能写进插件目录的 dll 都会被加载，信任边界是文件系统权限；
- **不递归子目录**：插件依赖必须与插件 dll 平铺在同一目录（否则 Load 探测不到 → MissingDependency）；MinimumHostVersion
  用宿主程序集版本比较，构建号不参与，Beta 里宿主版本号若未更新则版本校验形同虚设。
- 页面不监听 Changed：若未来 Shell 自己加载插件（例如启动时发现），本页需用户点一次「扫描目录」才看到。

## 9. 需要 Lead 落地（本文件禁止自行改动的位置；0.1 已全部落地，保留为登记口径）

- src/CkanUI.Services/ServiceCollectionExtensions.cs：第 98 行 services.TryAddSingleton<IAppBootstrapper, AppBootstrapper>(); 之后追加
  services.TryAddSingleton<IPluginService, PluginService>();（using CkanUI.Services.Plugins;）。无参构造即用默认目录。
- src/CkanUI.App/ServiceRegistration.cs：第 74 行附近的页面单例登记加 services.AddSingleton<PluginsPageViewModel>();；
  第 163-174 行 PageTypes 表加 ["plugins"] = typeof(PluginsPageViewModel),。
- src/CkanUI.ViewModels/ShellViewModel.cs：NavItems 里 settings 之前（现第 61 行；cache 第 59、dedup 第 60）加
  new("plugins", _translate("Ui_NavRail_Plugins"), "Puzzle"),。
- 本地化：src/CkanUI.Localization/LocalizationKeys.cs 第 38 行（NavRailCache）后加 public const string NavRailPlugins = "Ui_NavRail_Plugins";；
  i18n/packs/zh-CN.json 第 15 行后加 "Ui_NavRail_Plugins": "插件"，en-US.json 同位置加 "Ui_NavRail_Plugins": "Plugins"；
  ShellViewModel 第 154 行（["Ui_NavRail_Cache"] = "下载缓存"）后加 FallbackTexts 中文兜底。
- docs/README.md：登记 docs/design/plugins.md。页面内文案按仓库现状写在 VM/axaml 里，收口到语言包需另开 Plugins.* 键。

## 10. 最小示例插件（本轮冒烟用的那个）

拷进 `%LocalAppData%\CKAN\plugins` 就能被「插件」页发现；选中 + 激活后，模组页工具条右侧多出按钮。项目文件里 `Private=false` 是关键：契约程序集由宿主提供，插件自带副本会被独立 ALC 判成「另一份类型身份」，加载器报 NoPluginType（见 §7 里那条真实加载器用例）。

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <Reference Include="CkanUI.Services.Abstractions">
      <HintPath>…\src\CkanUI.Services.Abstractions\bin\Debug\net10.0\CkanUI.Services.Abstractions.dll</HintPath>
      <Private>false</Private>
    </Reference>
  </ItemGroup>
</Project>
```

```csharp
public sealed class DemoPlugin : ICkanUiPlugin
{
    public string Id => "demo.toolbar";
    public string Name => "工具条演示插件";
    public string Version => "1.0.0";
    public string Author => "CKAN UI";
    public string Description => "演示 F-17 的工具项。";
    public string MinimumHostVersion => string.Empty;

    public IReadOnlyList<PluginToolItemDescriptor> ToolItems { get; } =
        [new("mods", "hello", "打个招呼", "插件把结果写回按钮提示")];

    public Task ExecuteToolItemAsync(string key, CancellationToken ct) => Task.CompletedTask;

    public void Initialize() { }

    public void Deinitialize() { }
}
```

声明了 `ToolItems` 就必须实现 `ExecuteToolItemAsync`（默认实现抛 `NotSupportedException`，宿主把它转成「未实现」的不可重试失败）；纯后台插件或只声明页签的插件不实现它也合法。窗口侧的现象与截图见 `docs/design/milestones.md` §6.3。

## 11. 首方内置插件与「性能」页签试点（0.2）

用户机器上的插件原本只有一个目录（`%LocalAppData%/CKAN/plugins`），宿主自己带的插件也得先落到用户目录才生效——
于是「插件系统」在界面上永远只是一张空表。0.2 把「宿主随包携带的首方插件」建模成一等公民，并用「性能」页签做第一个真实试点。

### 11.1 两个目录、两个信任级

| 目录 | 路径 | 谁往里放 | 默认状态 |
| --- | --- | --- | --- |
| 内置（首方） | `<App>/plugins`（`PluginService.DefaultBundledDirectory()`） | 发布包（`CopyBundledPlugins`） | 引导时按 `activateBundled: true` 激活 |
| 用户 | `%LocalAppData%/CKAN/plugins` | 用户（插件页「新增」） | 一律 Dormant，只有用户显式激活过才恢复 |

`PluginInfo.IsBundled` 标明来源。同 Id 时内置优先，用户目录那份进 `Failures`（原因写「同一个标识……已跳过」），
被拒的那份 ALC 当场释放。

### 11.2 激活状态持久化

`PluginActivationStore` 落 `%LocalAppData%/CKAN/plugin-activation.json`：tmp → `File.Move(overwrite)` 原子替换，
文件读坏按空表处理（`Log.Warn`，不崩），写失败只告警且内存态先生效——插件页刚点的激活不会因为磁盘只读而"回弹"。
`PluginBootstrapper` 的顺序是「建目录 → `DiscoverAsync(activateBundled: true)` → 恢复持久化激活态」，
全程 try/catch，永不抛：插件是第三方代码，磁盘也可能只读，启动路径上任何失败都不该让主窗口打不开。

**`activateBundled` 是默认值，不是命令**（真机踩到后修）：`PluginService` 判定内置插件是否激活时先看
持久化表里有没有这个 Id 的显式记录，有就听它的（`activated: false` 就是停用），只有完全没有记录时才落回
`activateBundled`。原来的写法是 `activateBundled || IsActivated(id)`，引导固定传 `true` —— 于是在插件页
点一次「停用」，下次启动又被自动拉起来，入口周期性地出现又消失，比不能停用更让人摸不着头脑。
用例：`PluginBundledDiscoveryTests.DiscoverAsync_ActivateBundled_RespectsExplicitDeactivation`；
真机复验：把 `{"com.ckanui.assets":{"activated":false},"com.ckanui.saves":{"activated":false}}` 写进
`plugin-activation.json` 后重启，「实例」分节的存档/飞船/旗帜/加载图四个页签不再出现，KSP 设置等其余页签照常。

### 11.3 启动时机（性能红线）

`App.axaml.cs` 在 `_pluginTools.Install()` 之后把引导丢进后台任务（`StartPluginBootstrap`，`Task.Run` + 取消令牌，
`Shutdown` 时 `Cancel()`）。**不要在启动路径上同步跑**：982e93d 的教训是启动期同步重活把帧率从 60 打到 3，
插件扫描要读磁盘、反射类型、执行插件构造函数，正好属于那一类。引导完成时 `IPluginService.Changed` 触发，
`ShellViewModel` 在同步上下文里差分重建导航栏，页签自己就出现了，不需要用户点任何按钮。

### 11.4 内置插件清单

| 插件 | 目录 | Id | 图标 | 贡献 |
| --- | --- | --- | --- | --- |
| 性能 | `src/CkanUI.Plugin.Performance` | `com.ckanui.performance` | `Performance`（速度表） | 「性能」页签（`performance`），读 KPP 遥测、发命令，契约见 [performance-tab.md](performance-tab.md) |
| 模组翻译 | `src/CkanUI.Plugin.ModText` | `com.ckanui.modtext` | `Translate`（翻译） | 「模组翻译」页签（`modtext`），原来是宿主内置页，0.2 起整套视图移进插件；导航标题靠 `TitleKey` 取语言包 |
| 存档 | `src/CkanUI.Plugin.Saves` | `com.ckanui.saves` | `Archive`（归档盒） | 「存档」页签（`saves`，`Section=instances`、`Order=10`）+ SFS 工作台窗口，0.3 起整套视图移进插件 |
| 资产工作台 | `src/CkanUI.Plugin.Assets` | `com.ckanui.assets` | `Rocket` / `Flag` / `Image` | 「飞船」「旗帜」「加载图」三个页签（`ships`/`flags`/`loadingimages`，`Section=instances`、`Order=20/30/40`）；飞船与旗帜实现 `IPageThumbnailWarmup` 自己预热缩略图 |
| KSP 游戏设置 | `src/CkanUI.Plugin.KspConfig` | `com.ckanui.kspconfig` | `Settings`（齿轮） | 「KSP 游戏设置」页签（`kspconfig`，`Section=instances`、`Order=50`） |
| AI 助手 | `src/CkanUI.Plugin.Ai` | `com.ckanui.ai` | `Sparkle`（星芒） | 「AI 助手」页签（`ai-assistant`，`Section=plugins`、`Order=10`）+ 底栏常驻按钮（与后台任务胶囊同一行，点开右侧抽屉）：问答 + 8 个工具（7 个 `ckan_*`：查询/导航 + 一个必须过用户确认的安装，`web_search` 走 MCP），数据与写权限都经契约 `IAiCkanBridge` 由宿主实现；设计与实测见 [ai-assistant.md](ai-assistant.md) |
| 音乐 | `src/CkanUI.Plugin.Soundtrack` | `com.ckanui.soundtrack` | `Music`（音符） | 「音乐」页签（`music`，`Section=plugins`、`Order=20`）：读写 KSP 根目录 `Playlists/playlists.cfg`（增删曲目、排序、开关播放列表、备份后保存），乐曲库扫 `<游戏目录>/Music` 与 `GameData`；不做试听。文件契约与已知缺口见 [soundtrack.md](soundtrack.md) |
| Mod 配置 | `src/CkanUI.Plugin.ModConfig` | `com.ckanui.modconfig` | `Sliders`（滑块） | 「Mod 配置」页签（`modconfig`，`Section=mods`、`Order=50`）：解耦为内置插件，嵌入「模组」分节内；提供适配器高级配置模式与通用 ConfigNode（`*.cfg`）自动扫描、就地编辑与安全写回模式 |

宿主侧不改一行就能拿到这个页签：插件只引用 `CkanUI.Services.Abstractions` + `Avalonia`（视图里用到缩略图/骨架屏这类共享基元的，再引 `CkanUI.Ui`），
构造函数里声明 `IInstanceService` 之类的宿主服务，`PluginPageFactory` 用 `ActivatorUtilities` 注入。

### 11.5 再加一个首方插件

1. 新建 `src/CkanUI.Plugin.<名字>`（net10.0，引 `Avalonia`（无版本，走中央包管理）+ 契约层 `ProjectReference`，
   `AvaloniaUseCompiledBindingsByDefault=false`），实现 `ICkanUiPlugin` 并声明 `Pages`。
   页面视图模型要继承宿主的 `PageViewModel`（`Text(...)` 本地化、`IActivatable`、`IPageProjectionTrim` 三个契约都在
   `CkanUI.ViewModels`），这时才额外引 `CkanUI.ViewModels` 与领域库——首方插件可以，第三方插件不行（契约层之外不保证稳定）。
   要跟语言包走就在 `PluginPageDescriptor` 上填 `TitleKey`，宿主用当前翻译取标题；键不存在时回退 `Title`。
   侧栏图标靠 `Glyph`：填 `NavGlyphToGeometryConverter` 里已有的字形名（`Performance`、`Translate`、`Storage`…），
   不填或名字认不出来时退回通用「插件」七巧板——见 §11.10。
2. `dotnet sln add --solution-folder src <csproj>`；测试工程同理 `--solution-folder tests`。
3. `src/CkanUI.App/CkanUI.App.csproj` 的 `ProjectReference` 加一行，`ReferenceOutputAssembly=false` / `Private=false` /
   `SkipGetTargetFrameworkProperties=true`；`ItemGroup` 把那枚 csproj 加进 `_BundledPluginProject`。
4. 视图模型实现 `IPluginPage`；视图按「同程序集同命名空间、类名后缀换 View」命名（`FooViewModel` → `FooView`）。
5. 加 `tests/CkanUI.Plugin.<名字>.Tests`，并在 `PluginPerformanceIntegrationTests` 旁边补一条真 dll 集成用例。

**打包坑（踩过一次）**：拷贝目标必须写成 `DependsOnTargets="_CollectBundledPlugins"`，不能写成
`<CallTarget Targets="_CollectBundledPlugins" />`——`CallTarget` 会开新的属性/项作用域，里面产出的
`@(_BundledPluginFile)` 回到调用方是空集，构建照样"成功"，`plugins/` 目录里一个文件都没有。
构建产物验证点：`src/CkanUI.App/bin/<config>/net10.0/plugins/CkanUI.Plugin.Performance.dll` 存在。

**测试形态**：`tests/CkanUI.App.Tests/PluginPerformanceIntegrationTests.cs` 用**真实**插件 dll 跑「发现 → 激活 →
页签描述 → 跨 ALC 解析视图模型类型 → DI 构造 → 卸载」全链，并覆盖「同 Id 判重复」「未激活不产生页签」。
注意把插件工程输出目录里的依赖副本（`CkanUI.Services.Abstractions.dll` 等）过滤掉再当内置目录用，
否则扫描器会为每个依赖留一条"没有实现 ICkanUiPlugin"的失败记录。

### 11.6 仍然没解决的

- **真机未验证**：性能页签的读盘/命令往返只在样例与测试桩上验证过，没在跑着的 KSP 里走过一遍。
- **插件页仍是拉取模型**：`PluginsPageViewModel` 首次进入时扫一次，期间若引导还没完成会看到空表，需要点一次「扫描插件目录」；
  改成订阅 `Changed` 要先把跨线程改集合的问题处理掉（见该 VM 的 remarks）。
- **ALC 回收仍是尽力而为**：见 §8。

### 11.7 侧栏重建的订阅对象是 RailItems，不是 NavItems

插件页签在视图模型里"有"，侧栏上却"没有"——这是本次试点踩到的最贵的一个坑，记在这里免得下一个人再花两小时。

现象：引导完成、`ShellViewModel.NavItems` 与 `RailItems` 都含 `performance`（延迟 3 秒后可见），但
`FANavigationView.MenuItems` 仍然只有内置项。真实进程里加探针才看到：视图侧只收到一次变化通知，
那一刻 `RailItems.Count` 还是旧值（7），重建出来的菜单里当然没有新页面。

根因在 `src/CkanUI.ViewModels/ShellViewModel.cs` 的 `RebuildPluginNavItems()`：它先差分改 `NavItems`，
**最后**才 `RefreshRailItems()` 按 Sections + NavItems 重填 `RailItems`；而 `RebuildNavigation()` 渲染的是
`_shell.RailItems`。只订阅 `NavItems.CollectionChanged` 就等于永远早一步拿旧快照重建。

结论（写死在 `MainWindow.AttachShell` 的注释里）：`AttachShell` 必须同时订阅/退订
`NavItems.CollectionChanged` 与 `RailItems.CollectionChanged`，两者都走同一个 `OnNavItemsChanged`。
回归用例：`tests/CkanUI.App.Tests/PluginNavRailTests.cs` 的
「侧栏跟随RailItems重建并给插件页补上扩展分组」——它穿过真实 `MainWindow`，只断言视图模型是抓不到这个 bug 的。
这条用例做过变异验证：把 `AttachShell` 里的 `RailItems.CollectionChanged` 订阅注释掉，它立刻变红
（"RailItems 变了侧栏就必须重建"），恢复订阅后转绿——不是那种"删了被测代码也照样过"的假回归。

**写这类界面用例的两个坑（都真踩过）**：
1. **不要在 headless 里驱动帧**。`HeadlessAppHost` 用的是真 Skia 渲染（`UseHeadlessDrawing = false`），
   `AvaloniaHeadlessPlatform.ForceRenderTimerTick()` 与 `Dispatcher.UIThread.RunJobs()` 都会把渲染 job
   排进来、排不空，用例表现为挂死（CPU 空转，或 0.6 秒 CPU 静默等待），日志停在 Test Discovery 之后。
2. **不要在带 Avalonia `SynchronizationContext` 的测试线程上 `await ... ConfigureAwait(true)`**。
   NUnit 的 `NoMessagePumpStrategy` 不会替你泵消息，续体被 Post 回一个没人跑的 dispatcher，
   无限等待且没有异常。插件加载用 `Task.Run(() => service.DiscoverAsync(...)).GetAwaiter().GetResult()` 收口，
   界面断言全部走同步路径。

另一个同源结论：**点进插件页要验到视图，别只验到 VM**。`ViewLocator` 的插件分支走
`viewModelType.Assembly.GetType(...)`（插件在可回收 ALC 里，`Type.GetType` 按名字查不到），
断掉时页面会静默落到 `PagePlaceholderView`，看上去就是"插件只是个占位符"。
同一文件里的「性能页的视图模型能被视图解析器渲染成插件自带视图」把这条钉住。

### 11.8 关掉插件，资源真的放掉了吗

「停用插件」只是把状态位从 Active 改成 Dormant，插件占用的东西不会自动消失。这一节记录审计出的三个缺口与现在的口径，
排查顺序也按这个来：服务层 → 页面实例 → 视图。

**三个缺口（都在宿主的接线里，不在插件里）**

1. `PluginService.DeactivateAsync` / `UnloadAsync` 只调插件的 `Deinitialize()` 并释放 ALC，**不处置页面实例**——
   页面实例是宿主造出来的，谁也不该指望插件自己收拾。
2. 插件页的工厂每次导航都新建视图模型（`PluginPageFactory.Create` 用 `ActivatorUtilities.CreateInstance`，
   且刻意不缓存 `Type` 以免钉住可回收 ALC）。于是同一个页签进出三次就有三个视图模型，
   每个都带着自己的 1 Hz 轮询定时器与事件订阅。
3. `MainWindow.ShowPage` 按**页面实例**缓存视图（`_pageViews`，ReferenceEqualityComparer），并把视图常驻在
   `PageStage.Children` 里（换页只切 `IsVisible`）。视图不撤，视图模型就被钉住；而
   `PerformancePageView` 的轮询开关挂在 `AttachedToVisualTree` / `DetachedFromVisualTree` 上——
   视图永远不离开可视化树，这两个事件就再也不会发生。结果：`UnloadAsync` 之后 ALC 收不掉、插件 dll 一直映射着。

**现在的口径（三处，互相咬合）**

| 层 | 做法 | 位置 |
| --- | --- | --- |
| 页面实例 | 一个导航键一个实例，插件下线时按「还活着的页签目录」批量释放 | `src/CkanUI.App/Plugins/PluginPageCache.cs` |
| 服务接线 | `CreatePageFactory` 把插件分支接到缓存；订阅 `IPluginService.Changed`，用 `PluginPageCatalog.Collect(...)` 算活着的键 | `src/CkanUI.App/ServiceRegistration.cs` |
| 视图 | `_pageViewKeys` 记住视图属于哪个导航键，导航项消失时从舞台撤下并 Dispose | `MainWindow.PruneRemovedPageViews` |

三条必须一起成立：少了缓存，反复进出会累积实例；少了 Changed 订阅，插件停用后旧实例还在缓存里；
少了视图清理，视图钉着视图模型，前面两条都白做。

**证明（用例 + 变异）**

- `tests/CkanUI.App.Tests/PluginPageCacheTests.cs`：同键复用、工厂返回空不缓存、按活键集释放、键名忽略大小写、
  某个插件写坏 `Dispose` 不连累其它页面。
- `tests/CkanUI.App.Tests/PluginPageReleaseTests.cs`：拿真的插件 dll 走一遍激活 → 造页面 → 停用 → 卸载，
  断言页面实例被 `Dispose`（`_disposed` 为真）、视图模型与插件**程序集**都被 GC 回收（`WeakReference.IsAlive` 变假）、
  暂存目录能直接删掉（dll 句柄已放开）。
  变异验证：把 `cache.RetainOnly(live)` 注释掉，用例立刻红在「插件程序集还活着」——这不是「删了被测代码也照样过」的假回归。
- 两条用例都要求把局部变量关进 `[MethodImpl(MethodImplOptions.NoInlining)]` 的辅助方法里，只把 `WeakReference` 带出去；
  否则 JIT 可能让局部（页面实例、`Type`）活过卸载点，用例变成永远红的假阴性。

**协议外的坑：卸载没有完成之前，旧一份程序集还挂在 `AppDomain` 里**

`AssemblyLoadContext.Unload()` 只是开始回收，程序集从 `AppDomain.GetAssemblies()` 消失要等 GC。
于是"重载插件"这段窗口里，同一个类型名会有两份程序集，而 `PluginPageFactory.ResolveViewModelType`
是按名字遍历 `AppDomain` 找的——挑中旧那份的话，页面跑的是旧代码，旧 ALC 也被类型引用重新钉住，
等于卸载白做。.NET 没有公开"这个上下文正在卸载"的查询口子（`AssemblyLoadContext.IsCollecting` 不是公开 API），
所以由插件加载上下文自己记一笔：

- `src/CkanUI.Services.Abstractions/IPluginAssemblyContext.cs`：`bool IsUnloading { get; }`。
- `src/CkanUI.Services/Plugins/PluginLoadContext.cs` 实现它：`Unload()` 先置标志再交给基类
  （`new void Unload()` 隐藏基类方法，调用点 `AssemblyLoadContextPluginLoader.cs:55` 持有的是 `PluginLoadContext`，实际调到的就是这个）。
- `PluginPageFactory.ResolveViewModelType` 遍历时跳过 `IsUnloading` 的上下文。
- `PluginPageReleaseTests` 断言 `UnloadAsync` 之后该标志为真——只靠"页面被回收"验不出这一层。

顺带一条测试写法教训：插件加载器用 `LoadFromStream` 装 dll，**这种程序集的 `Assembly.Location` 是空串**，
按路径去 `AppDomain` 里认领"我这一份"永远找不到（症状：`staged 目录里的插件程序集没被加载进 AppDomain`）。
`PluginPageReleaseTests.DiscoverSingle` 改成"扫描前后程序集差集"认领，既避开空 `Location`，
也避开同进程里别的用例加载的同名副本。

**还没做的（诚实登记）**

- 插件页仍是拉取模型（§11.6），管理器不订阅 `Changed`：引导完成前进来会看到空表。
- 应用退出时没有统一「处置所有页面实例」的收口，靠进程结束；`_services.Dispose()` 只处理服务。
- 页面视图模型不实现 `IActivatable`，所以「切走页面」不会触发停轮询——目前靠插件页自己的
  `DetachedFromVisualTree`（视图被真正撤下时才发生）。

### 11.9 把宿主页面搬成插件：模组翻译（0.2）

第二个首方插件走的是另一条路：页面原本写死在宿主里，「插件化」等于把它**拆下来**，而不是并排新写一个。
顺序反过来做会留下两套实现（宿主一套、插件一套），谁也不知道界面上看到的是哪套。

**宿主侧要一起拆的东西（少拆一处就留一个死入口）**

| 位置 | 拆分前 | 拆分后 |
| --- | --- | --- |
| `src/CkanUI.ViewModels/ShellViewModel.cs` | `_baseNavItems` 有 `modtext`；`SectionSpecs` 有 `("modtext", null, ["modtext"])` | 两条都删；插件页由 `RebuildPluginNavItems` 插在「插件」之后、落「扩展」分组 |
| `src/CkanUI.App/ServiceRegistration.cs` | `AddSingleton<ModTextPageViewModel>()` + `IPageProjectionTrim` 映射 + `PageTypes["modtext"]` | 三处都删；插件页不在 DI 里，工厂按页签目录问 `PluginPageCache` |
| `src/CkanUI.App/MainWindow.axaml.cs` | `ActivationWarmups` / `WarmupOrder` 里的 `"modtext"` | 删掉（预热的是宿主页面实例，插件页由插件自己管） |
| 领域库 `src/CkanUI.ModText` | 服务 `IModTextService` 在宿主 | **不动**：插件只管界面，服务照旧由宿主注入 |

**`TitleKey`：插件页也要跟语言包走**

`PluginPageDescriptor` 加了可选尾参 `string? TitleKey = null`（3 参构造仍可用，向后兼容），
`PluginPageCatalog` 原样透传到 `PluginPageEntry`，`ShellViewModel.GetOrCreatePluginNavItem` 优先用
`_translate(TitleKey)`，**翻译委托把键原样返回**（语言包没这条键）时退回插件自述的 `Title`——
否则界面上会直接显示 `Ui_NavRail_ModText` 这种键名。两条用例钉住：透传一条、回退一条。

**L1 页面投影缓存要认得插件页**

`PageProjectionCacheParticipant` 原来只扫 DI 里的 `IPageProjectionTrim`，搬走之后模组翻译页不在 DI 里，
启动前那次释放就碰不到它。改法：`PluginPageCache` 提成 DI 单例（工厂与参与者共用同一个实例），
参与者额外遍历 `PluginPageCache.Instances`，对 `is IPageProjectionTrim` 的页面各调一次 `TrimProjection()`（逐个 try/catch）。
少了这一步，插件页的内存与投影缓存只能等卸载时才放。

**搬文件的 git 教训**

`git mv` 之后原路径又冒出来（并行流程还原了删除），`git status` 里目的地还是 `??`——说明 git 没把这当成重命名。
补救是先 `git rm` 五个原路径再 `git add` 新目录，然后**必须** `git status --short` 确认状态是 `R` 且原路径消失；
只 `ls` 看不出这种半搬状态，下一次构建就会因为一个程序集里定义了两份同名类型而炸。

**测试迁移**

- `tests/CkanUI.ViewModels.Tests/ModTextPageViewModelTests.cs` → `tests/CkanUI.Plugin.ModText.Tests/`（命名空间 `CkanUI.Plugin.ModText.Tests`），27 条照样绿。
- `ShellViewModelTests` / `PluginPageCatalogTests` / `ShellNavCollapseTests` 里写死的 `24` 全部降到 `23`（内置项少一条），
  按下标断言导航顺序的用例同步位移——这类断言脆，但正因为脆，漏改会立刻红。
- `tests/CkanUI.App.Tests` 直接 `new ModTextPageView()` 的三个文件改成引用插件工程（`using CkanUI.Plugin.ModText;`）。

### 11.10 插件页签的图标：契约里没有这个字段，所有插件就长同一张脸

试点刚上线时侧栏里「性能」和「模组翻译」都是一枚七巧板——不是画错了，是
`ShellViewModel.GetOrCreatePluginNavItem` 里硬编码了 `new NavItem(entry.Key, entry.Title, "Puzzle")`：
契约层没地方让插件说"我想用什么图标"，宿主只能给个通用占位。

改法（沿用 `TitleKey` 那套形状，向后兼容）：

- `PluginPageDescriptor` 加可选尾参 `string? Glyph = null`；`PluginPageCatalog` 透传到 `PluginPageEntry`；
  `ShellViewModel` 取 `entry.Glyph`（空白按未填处理），没填就还是 `Puzzle`。
- `Glyph` 是**宿主字形表里的名字**，不是路径也不是 emoji：`NavGlyphToGeometryConverter.PathData` 的键即白名单
  （`Performance` = Material speed、`Translate`、`Storage`、`Chart`…）。宿主不解释第三方图标的含义，只负责查表。
- 认不出来的名字**退回七巧板**（写进缓存），不返回 `null`：返回 null 导航栏会留一格空白，用户只会以为
  "这页本来就没图标"，看不出是插件把名字拼错了。
- 悬停动效同理：`IconHoverGlyph` 没配的字形走默认，新字形想加动效自己补一行。

**为什么把兜底做在转换器里，而不是让描述符校验失败**：插件 dll 是第三方的，宿主不能因为图标名写错就拒绝加载
整个插件——页签能用比图标好看重要。反过来，首方插件必须被用例钉住，不然"忘了填"会一直没人发现：

- `tests/CkanUI.App.Tests/PluginGlyphTests.cs`：加载两个真插件 dll，断言每个页签都声明了 `Glyph`、
  不是 `Puzzle`、`NavGlyphToGeometryConverter.Convert` 真能返回几何（名字拼错会红）、两个插件的图标不同、
  且 `ShellViewModel.NavItems` 里拿到的就是这一枚。
- `tests/CkanUI.ViewModels.Tests/PluginPageCatalogTests.cs`：条件用例各一条——透传（顺带钉住 `Glyph` 必须用
  命名参数，位置参数是 `TitleKey`）与未声明时的 `Puzzle` 兜底。


### 11.11 分节内嵌：插件页签进「实例」这一节，而不是侧栏顶格

**动机**：存档、飞船、旗帜、加载图、KSP 游戏设置这五页从来不是侧栏顶格入口——它们是「实例」分节内部的
页签（`ShellViewModel.SectionTabs`）。只把页面搬进插件、却只支持侧栏入口，等于把它们从原来的位置挪走。
所以契约得先长出「我属于哪一节」，否则插件化这条路对这五页根本不成立。

**契约**：`PluginPageDescriptor` 追加两个可选参数

| 参数 | 含义 |
| --- | --- |
| `string? Section` | 宿主分节根键（`instances` / `mods` / `cache` / `modpacks` / `plugins`）。填了就进该分节顶部页签条 |
| `int Order` | 同分节内的排序位次，默认 0，小的在前 |

两条兜底，都是「宁可放错位置，也别把页丢掉」：

- `Section` 为空、或宿主认不出这个根键（`SectionRoot` 只认 `SectionSpecs` 里的 Root），退回侧栏入口——
  第三方插件写错一个键不应该导致页面消失，用户还以为插件坏了。
- 分节已存在但插件没声明 `Section`：仍是侧栏入口，不会被吸进任何一节。

**定序：槽位优先，插件补位**。`SectionSpecs` 保留这五页的键作为「槽位」
（`("instances", null, ["instances","saves","ships","flags","loadingimages","kspconfig","unmanaged","installhistory","playtime"])`），
`BuildSections` 逐槽先找 `_baseNavItems`，找不到再从已激活插件的条目里按 key 摘一个出来；槽位都没命中才跳过。
于是「插件装了没装」不影响已有页签的相对顺序，五个插件页回到它们历史上的位置，而插件额外声明的同分节页面
按 `Order → Title` 追加在末尾。

**内嵌页仍然要进 `NavItems`**。选中态（`OnNavigated → SelectedNavItem`）、页面缓存键、命令面板的
`nav:<键>` 都靠 `NavItems`；`RefreshRailItems` 因为它在某个 `section.Tabs` 里会跳过它，所以侧栏上不会多出
一项。也就是说「内嵌」只影响**画在哪里**，不影响它是不是一个可导航的页面。

**`Sections` 改成可变**：插件增删会改变分节内容，所以要 `private IReadOnlyList<NavSection> _sections` +
`OnPropertyChanged(nameof(Sections))`；`RefreshSection` 还得比页签键序列（`SectionsEquivalent` /
`_activeSectionSignature`），否则同一分节内「插件页多了一个」不会重建 `SectionTabs`——只按分节根键判断是漏的。

**停用之后**：`RebuildPluginNavItems` 先记下当前页原本属于哪一节（`SectionRootForPage`），重建后若当前键已经
不在 `NavItems` 里，就导航回那一节的根页（没有则 `plugins`），而不是留在空白页上。用例
`Shell_DeactivatingSectionPagePlugin_RefreshesTabsAndFallsBackToTheSectionHome` 钉这一条——注意造
`NavigationService` 测试工厂时必须同时给「死页」和目标根页造页面，否则 `NavigateCore` 在工厂返回 null 时直接
`return false`，`CurrentKey` 不变，断言会以「回退没生效」的假象失败。

**可发现性（「关掉插件后入口找不到」）**：插件页的 `PagesText` 从「提供 N 个页签」改成
`提供 N 个页签：存档、飞船`（`PluginRowViewModel`）。用户在「工具 › 插件」里始终看得到这个插件**提供过**哪些
入口，停用只是让它暂时不出现，一键 `ActivateCommand` 就回来。比「入口凭空消失、只剩一个空分节」强。

**共享 UI 层 `src/CkanUI.Ui/`**：插件不能引用 `CkanUI.App`（那是外壳），但这五页的视图用了宿主里的
`PathToThumbnailConverter` 与 `SkeletonPulse`。于是抽一层 App 与插件都引用的基元：

- `src/CkanUI.Ui/Converters/PathToThumbnailConverter.cs`（原 `src/CkanUI.App/Converters/`）
- `src/CkanUI.Ui/Theming/SkeletonPulse.cs`（原 `src/CkanUI.App/Theming/`）
- `src/CkanUI.Ui/Assets/DdsDecoder.cs`（原 `src/CkanUI.Services/Assets/`）
- `src/CkanUI.Ui/Theming/CkanMotionTokens.cs`：宿主写资源、共享基元读资源，键名必须在共享层；`MotionHost` 的
  七个 `const` 改成它的别名，公开 API 不变。

搬的时候踩到的两个编译错，记下来省下一次：

- `SkeletonPulse` 直接写 `MotionHost.BaseKey` → 共享层不认识宿主类型。解法是键名下沉到 `CkanMotionTokens`。
- `PathToThumbnailConverter` 判断停用后缀时用 `LoadingImageService.DisabledSuffix`（服务层具体类型）→ 常量
  下沉成 `CkanUI.Services.Abstractions/Assets/LoadingImageNaming.DisabledSuffix`，服务层与 UI 层各自引用。

**缩略图预热改成接口**：`MainWindow.PrewarmThumbnails` 原来按具体页面类型 `switch` 到 `FlagsPageViewModel` /
`ShipsPageViewModel`（页面搬走后外壳就不该认识这些类型）。改成
`CkanUI.Services.Abstractions/IPageThumbnailWarmup.WarmUpThumbnails()`，页面自己负责「行集合是异步填的，
所以要等一次 `CollectionChanged` 再预热」的时序。

**命令面板不再列这些页**：`DefaultCommands` 的静态导航表删掉 `saves` / `modtext` / `kspconfig`（其余三页本来
就不在表里）。理由：插件可停用，静态表留一条 `nav:<键>` 就是一个点了没反应的面板项。面板要重新覆盖插件页，
得先让 `ICommandCatalog` 支持按前缀注销，再在插件激活/停用时动态注册——这条记为未解决项，别在静态表里补回去。

**迁移形态**：每页一个插件工程（`com.ckanui.saves` / `com.ckanui.assets` 三个页签 / `com.ckanui.kspconfig`），
沿用 `CkanUI.Plugin.Performance` 的骨架；**视图与视图模型必须同程序集同命名空间**，否则 `ViewLocator` 的
`ViewModel→View` 全名替换解析不到，会静默退到 `PagePlaceholderView`（页面一片空白，日志里什么都没有）。

**用例**：

- `tests/CkanUI.App.Tests/PluginSectionTabsTests.cs`：装三个真插件 dll 后 `instances` 分节的页签键序列必须是
  `[instances,saves,ships,flags,loadingimages,kspconfig,unmanaged,installhistory,playtime]`，每个键都在 `NavItems`
  里、都不在 `RailItems` 里；停用资产插件后三个页签一起消失，其他页签不受影响。
- `tests/CkanUI.ViewModels.Tests/PluginPageCatalogTests.cs`：`Section` 透传 + `Order` 排序、未知 `Section` 退回
  侧栏、停用后回退到分节根页。

**停用之后入口去哪儿了**：内嵌页签从分节里消失后，侧栏上没有任何痕迹——这正是「关掉插件某些入口就找不到」的
现实形态。三件事一起兜住：①当前页若被停掉的插件占着，重建时回退到该分节的根页（`instances`），导航不会
停在空白上；②插件页（工具 › 插件）的行文案点名每个页签落在哪一节，形如
`提供 4 个页签 · 实例：存档、飞船、旗帜、加载图 · 已停用，重新激活后这些入口才会出现`
（`PluginRowViewModel.PagesText`）；③重新激活插件，页签按 `Order` 回到原来的槽位，不需要重启宿主。
用例：`PluginsPageViewModelTests.Row_NamesTheSectionEachPageLivesIn_SoDisabledEntriesStayFindable`。

### 11.12 宿主能力注入：文本输入框这类「要弹窗」的能力

页面搬进插件后，逻辑能跟着走，**弹窗不能**——`TextInputDialogView` 与 `AvaloniaInstanceTextPrompt` 要
`TopLevel`/`Window`，住在 App 层，而且 InstancesPageView 自己也还在用同一份实现。插件不能反向引用 App，
于是「飞船重命名 / 复制」「旗帜重命名」会静默降级成「当前环境无法输入文本」——功能还在，只是按钮点了没反应。

解法与缩略图预热同构：页面只声明能力，外壳在建好视图之后注入。

- `src/CkanUI.ViewModels/Pages/Instances/ITextPromptSink.cs`：
  `IInstanceTextPrompt? TextPrompt { get; set; }`（页面实现，未注入时必须自己降级，不能静默失败）。
- `MainWindow.AttachPageServices(object page, Control view)`：`page is ITextPromptSink sink && sink.TextPrompt is null`
  时注入 `new AvaloniaInstanceTextPrompt(view)`。**两个建视图的地方都要调**：`ShowPage`（用户点进来）与
  `WarmPage`（预热队列提前实体化），漏掉后者就会出现「直接点进去能用、预热过的不能输入」。
- 判定「外壳认不认识这个页面」一律靠接口，不靠 key 或具体类型——`IPageThumbnailWarmup`、`ITextPromptSink` 都是
  这条口径。外壳里出现 `switch (pageKey)` 就是漏了一个插件点。

**测试工程也跟着页面走**：`DdsDecoder` 搬到 `CkanUI.Ui` 之后，它的行为锁从 `tests/CkanUI.Services.Tests/Assets/`
移到新工程 `tests/CkanUI.Ui.Tests`（引 `CkanUI.Ui` + `CkanUI.TestSupport`，不入 `Services.Tests`——那里已经
引不到 `CkanUI.Ui.Assets.DdsDecoder`）。插件页自己的用例搬进 `tests/CkanUI.Plugin.<X>.Tests`，插件测试工程
**不引 `CkanUI.App`**（App 引插件，反向引用就把插件测试绑死在外壳上）。

**无头宿主要在整套件里活着**：TestRunner 是一个进程依次跑全部测试程序集，所以插件测试工程里的
`HeadlessHost.EnsureInitialized()` 不能无脑 `AppBuilder.Configure<…>().SetupWithoutStarting()`——第二个程序集
再调一次就抛 `InvalidOperationException : Setup was already called on one of AppBuilder instances`
（症状是整条用例变 Error，而不是断言失败）。两条口径：

1. `Application.Current is null` 才建壳，否则复用；**只有自己建的壳才 `Styles.Add(new FluentTheme())`**。
   往别人的 `Application.Current.Styles` 里加东西会触发既有窗口的资源宿主回调，撞
   `TransitioningContentControl.set_PageTransition` 的线程断言（`The calling thread cannot access this object`）。
2. 无头壳属于建它的那个线程。复用别人建好的壳时，本线程没有窗口亲和性，`window.Show()` 一类用例必须
   `Assert.Ignore`（理由写清"单跑本程序集可验证"），不能为了全绿把窗口用例删掉或放宽断言。
3. **全量跑之前先构建整个解决方案**。TestRunner 是一个进程按顺序加载各测试程序集，同名依赖只会加载
   第一份（通常是先跑的 `CkanUI.App.Tests` 输出目录里那份）。只构建单个测试工程时，后面的程序集
   跑的是旧依赖，症状是"单跑绿、全量红"——本次就有一条主页回退用例这样假红了两次。

### 11.13 插件功能视觉微型角标：页面与子页面统一辨识

**动机**：随着模组翻译、性能、AI 助手、存档、飞船、旗帜、加载图、KSP 游戏设置、Mod 配置等功能全量插件化，
插件已深入到顶格侧栏入口与各分节内部（如「实例」与「模组」）的子页签条中。用户和排错人员需要一目了然地区分
「哪些是宿主原生基础建设，哪些是由可插拔插件注入的扩展能力」。

**实现与分层**：

1. **契约与视图模型层**：
   - `src/CkanUI.ViewModels/ShellViewModel.cs`：`NavItem(string Key, string Title, string Glyph, bool IsPlugin = false)`，
     `NavTab(NavItem item)` 代理此状态 `public bool IsPlugin => Item.IsPlugin;`。
   - `ShellViewModel.GetOrCreatePluginNavItem`：通过 `entry.IsPlugin`（来自插件目录收集的条目）统一定义为 `IsPlugin: true`。
   - `ShellViewModel.IsCurrentPagePlugin`：暴露当前选中项是否为插件 `SelectedNavItem?.IsPlugin ?? false`，并随导航切换触发通知。

2. **样式与本地化**：
   - `i18n/packs/{zh-CN,en-US}.json` 新增 `Ui_Plugin_Badge`（中"插件"，英"Plugin"），常量挂载于 `LocalizationKeys.PluginBadge`。
   - `src/CkanUI.App/Resources/FluentStyles.axaml`：定义 `Border.CkanPluginCornerTag` 微型胶囊标签样式（高 15px、圆角 3px、字号 9px SemiBold），在普通、悬停与选中态下与主题强调色保持协调自适应。

3. **视图层多维注入**：
   - **分节子页签（`SectionTabsControl`）**：在「实例」与「模组」分节内部，针对 `NavTab.IsPlugin` 为真的项在标题右侧挂载 `CkanPluginCornerTag`。
   - **外壳顶栏（`MainWindow.axaml`）**：在 `WindowTitleTextBlock`（第 3 列）右侧联动呈现角标，无论通过侧栏还是子页签切入插件，顶栏均有插件状态指示。
   - **侧栏入口与悬停卡片（`MainWindow.axaml.cs`）**：`CreateNavItem` 中为插件导航项挂载 `FAInfoBadge`（AttentionDot），抽屉提示追加 `(插件)`；`NavPopout` 标题旁及展开的子项列表中同步渲染角标。

**验证**：
- `tests/CkanUI.App.Tests/PluginSectionTabsTests.cs` 的 `页签与导航项正确标记插件身份_子页面与页面区分明晰`：断言内置项 `IsPlugin == false`，插件分节项与独立入口 `IsPlugin == true`。
- `tests/CkanUI.App.Tests/TitleBarLayoutTests.cs`：断言标题列角标不破坏窗口布局与标题文本独占列契约。
### 11.14 启停插件为什么会把界面卡住（0.2 修复）

**现象**：在插件页点「激活」或「停用」，界面有几秒钟不响应；连着点几次更像彻底卡死。

**根因一：分节入口项每轮重建都造新实例，而侧栏按引用差分。**
`BuildSections` 里每个分节都 `new NavSection(root, title, new NavItem(root, title, tabs[0].Glyph), tabs)`。
`RefreshRailItems` 的差分是 `ReferenceEquals(item, RailItems[i])`，`MainWindow.RebuildNavigation` 判断
「这一项属于分节还是插件自述页」也用 `ReferenceEquals(section.RailItem, item)`（决定「扩展」标题插在哪儿）。
于是激活一个「实例」分节里的插件（存档/飞船/旗帜/加载图/KSP 设置都在这节）时：分节的 tabs 变了 ⇒
`SectionsEquivalent` 为假 ⇒ `_sections` 整批替换 ⇒ 五个分节入口全是新实例 ⇒
`RailItems` 被逐项删除再插入（实测一个插件启停 12 次集合变更）⇒ 外壳按 `RailItems` 逐项销毁重建整套菜单十几次。
修法：`GetOrCreateSectionRailItem(root, title, glyph)` 在标题与字形没变时**复用上一轮那个 `NavItem` 实例**。
实例稳定后引用差分自然收敛到「只动真正变化的那一项」（一次启停 1 次变更）。

**根因二：`PluginService` 是同步实现，页面命令在 UI 线程上直接 `await` 它。**
`ActivateAsync` / `DeactivateAsync` / `ReloadAsync` / `UnloadAsync` 全是 `Task.FromResult(XxxCore(...))`，
而 `XxxCore` 里干的是 `Plugin.Initialize()` / `Deinitialize()`、写 `plugin-activation.json`、
`RaiseChanged()` 同步广播（订阅方里就有释放插件页缓存、重建导航栏）。
`await` 一个已完成的 Task 不会让出线程——整条链就跑在 UI 线程上。
修法：`PluginsPageViewModel` 的六个命令把服务调用包进 `Task.Run`（`ConfigureAwait(true)` 回 UI 线程继续），
`RunAsync` 里先 `await Task.Yield()` 让 `IsBusy` 上屏；`ServiceRegistration` 对 `plugins.Changed` 的释放动作
按 `Dispatcher.UIThread.CheckAccess()` 决定直接执行还是 `Post`（订阅方改成在 UI 线程收口）。
开关另加防连击：忙碌时把勾选回滚，避免手抖连点把命令堆起来。

**验证（都不需要真实桌面，符合「无头调试不要打扰使用电脑」）**：

| 用例 | 钉住什么 |
| --- | --- |
| `PluginPageCatalogTests.Shell_TogglingSectionEmbeddedPlugin_DoesNotTouchTheRail` | 分节内嵌插件启停对 `RailItems` **零变更**；改回「每轮造新实例」会红在「实际 12 次」 |
| `PluginPageCatalogTests.Shell_TogglingRailPlugin_TouchesTheRailExactlyOncePerToggle` | 侧栏入口插件增删各只发 1 次 Add/Remove，不是「清空 + 重填」 |
| `PluginToggleResponsivenessTests.启停插件在无头宿主里能跑完且侧栏只做最小重建` | 无头宿主里用**真插件 dll** 走完激活 → 停用，限时 10 s、限制集合变更与菜单 Reset 次数 |
| `PluginsPageViewModelTests.Activate_RunsTheServiceOffTheThreadThatOwnsTheDispatcher` / `Deactivate_…` | 用手动泵的 `SynchronizationContext` 模拟界面线程：服务体必须跑在别的线程上（改回直接 `await` 就红在「激活必须离开界面线程」） |
| `PluginPageCacheTests.并发读取与清理安全无死锁` | 激活/停用在后台线程跑，`PluginPageCache` 的读写与释放不能互相踩 |

**踩过的坑**：无头用例不要在带 Avalonia `SynchronizationContext` 的测试线程上 `await ConfigureAwait(true)`
（NUnit 的 `NoMessagePumpStrategy` 不会替我们泵消息，表现是永远挂住、没有异常）；
要驱动命令就自己泵 `Dispatcher.UIThread.RunJobs()`，并给等待加超时。


### 11.15 把「音乐」做成插件：管理 Soundtrack 播放列表（0.2）

**它和前面的插件不一样**：存档/飞船/AI 那些页面的业务在宿主服务里，插件只搬页面；
音乐这一页的读写全在插件内（`src/CkanUI.Plugin.Soundtrack/Playlists/SoundtrackService.cs`），
因为宿主根本没有「播放列表」这个概念。代价是插件必须自己守住写盘纪律。

**文件契约**：Soundtrack 4.x / SoundtrackEditor 把播放列表写死在
`KSPUtil.ApplicationRootPath + "Playlists/playlists.cfg"`（4.7.1.6 的 changelog 就是修这个路径）。
真实样例固化在 [soundtrack.sample/playlists.cfg](soundtrack.sample/playlists.cfg)（12 个播放列表、37 条曲目），
测试拿它做往返基准。曲目值是音频文件名去掉扩展名，支持 `.ogg/.wav/.mp3`。

**写盘纪律**（全部有用例）：
- 未知标量键进 `ExtraValues`、未知子节点整段原文进 `UnknownNodes`，保存时原样写回——不认识的字段不丢。
- `enabled` 没写就保持 `null`，保存不补：`没写` 与 `写了 False` 是两种含义。
- 重复键后者覆盖（样例里 `preloadTime` 出现两次），不抛异常。
- 保存 = 校验（名字非空/不重名）→ 备份 `<文件>.bak-yyyyMMdd-HHmmss` → 写临时文件 → `File.Move(overwrite:true)`。
- UTF-8 无 BOM；换行沿用原文件。

**页面**：左栏播放列表（启用开关、曲目数、场景摘要），右栏详情（改名、四个开关、曲目上移/下移/移除、
从乐曲库搜索添加、缺失曲目标「缺失」），工具栏新建/删除/重新载入/保存/放弃更改，脏状态可见。
没有 `playlists.cfg` 时给路径与「新建播放列表并保存」入口，保存时才创建 `Playlists` 目录。

**不做**：试听（跨平台解码没有可靠路径，只给「在资源管理器打开」）、编辑 `playWhen/playNext/Before/After`
（场景编排语义，误改会让整段音乐不播；读出显示、保存原样保留）。

**生效时机**：Soundtrack 在场景切换时读文件，保存后要回主菜单或重启游戏才听得到——保存成功的提示里写清。

**用例**：`tests/CkanUI.Plugin.Soundtrack.Tests` 65 条（44 条领域层：往返/未知节点/重复键/备份/原子替换；
17 条页面层：脏状态/保存成功与失败/空态/缺失曲目/未选实例；3 条插件入口：页签声明与生命周期；
1 条宿主分节接线在 `tests/CkanUI.ViewModels.Tests/PluginPageCatalogTests.cs`）。
