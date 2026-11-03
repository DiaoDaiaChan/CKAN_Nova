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

### 11.3 启动时机（性能红线）

`App.axaml.cs` 在 `_pluginTools.Install()` 之后把引导丢进后台任务（`StartPluginBootstrap`，`Task.Run` + 取消令牌，
`Shutdown` 时 `Cancel()`）。**不要在启动路径上同步跑**：982e93d 的教训是启动期同步重活把帧率从 60 打到 3，
插件扫描要读磁盘、反射类型、执行插件构造函数，正好属于那一类。引导完成时 `IPluginService.Changed` 触发，
`ShellViewModel` 在同步上下文里差分重建导航栏，页签自己就出现了，不需要用户点任何按钮。

### 11.4 内置插件清单

| 插件 | 目录 | Id | 贡献 |
| --- | --- | --- | --- |
| 性能 | `src/CkanUI.Plugin.Performance` | `com.ckanui.performance` | 「性能」页签（`performance`），读 KPP 遥测、发命令，契约见 [performance-tab.md](performance-tab.md) |

宿主侧不改一行就能拿到这个页签：插件只引用 `CkanUI.Services.Abstractions` + `Avalonia`，
构造函数里声明 `IInstanceService` 之类的宿主服务，`PluginPageFactory` 用 `ActivatorUtilities` 注入。

### 11.5 再加一个首方插件

1. 新建 `src/CkanUI.Plugin.<名字>`（net10.0，只引 `Avalonia`（无版本，走中央包管理）+ 契约层 `ProjectReference`，
   `AvaloniaUseCompiledBindingsByDefault=false`），实现 `ICkanUiPlugin` 并声明 `Pages`。
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
