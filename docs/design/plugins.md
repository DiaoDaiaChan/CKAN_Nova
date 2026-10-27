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
| Initialize() / Deinitialize() | 激活/停用钩子；Deinitialize 必须可反复调用（重载路径 Initialize → Deinitialize → Initialize） |

IPluginService（IPluginService.cs）：PluginsDirectory、Active、Dormant、Failures、event Changed、DiscoverAsync、LoadAsync(path,
activate)、ActivateAsync / DeactivateAsync / ReloadAsync / UnloadAsync(id)、AddFromPathAsync(sourcePath, activate)。
Failures 与 Activate/Deactivate 是相对任务书列表的追加：按钮按 Id 操作，失败条目要留痕给用户看。DTO：PluginState（Dormant=0/Active=1/Failed=2）、PluginInfo（AssemblyPath、State、
FailureReason、Pages，派生 IsActive / DisplayName = 「名字 - 版本」，对齐原版 IGUIPlugin.ToString()）、PluginLoadResult（Ok / Fail）、
PluginLoadFailure（None / NotFound / NotAnAssembly / MissingDependency / NoPluginType / HostVersionTooOld / InstantiationFailed /
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
  源自本页命令，命令末尾统一 ApplyState() 重拉快照。事件留给未来的 Shell 级监听者（刷新导航项）。

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

## 8. 已知缺口

- **插件页签还没渲染**：PluginPageDescriptor 只声明「Key / 标题 / VM 类型全名」，宿主侧没有把这批描述翻译成导航项或工具项并注册进
  PageTypes（当前是 static readonly Dictionary）。落地需 Lead 让页面工厂支持动态 key 并按 ViewLocator 规则解析插件 VM 类型；
  在此之前 Pages 只在页面上显示「提供 N 个页签」。
- **ALC 卸载是「尽力而为」**：Unload() 只是标记可回收，插件若持有静态引用、启动线程或注册全局事件，程序集不会被真正回收（宿主不阻止也不检测）；装载计数/回收验证未做。
- **不做签名校验**：任何能写进插件目录的 dll 都会被加载，信任边界是文件系统权限；
- **不递归子目录**：插件依赖必须与插件 dll 平铺在同一目录（否则 Load 探测不到 → MissingDependency）；MinimumHostVersion
  用宿主程序集版本比较，构建号不参与，Beta 里宿主版本号若未更新则版本校验形同虚设。
- 页面不监听 Changed：若未来 Shell 自己加载插件（例如启动时发现），本页需用户点一次「扫描目录」才看到。

## 9. 需要 Lead 落地（本文件禁止自行改动的位置）

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
