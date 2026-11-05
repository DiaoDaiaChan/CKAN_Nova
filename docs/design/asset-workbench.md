# 资产工作台（飞船 / 旗帜 / 加载图）

> 规格来源：用户需求「管理旗帜，飞船，和游戏加载界面的加载图片」；[主题令牌](theme-tokens.md) 的可换肤约束。
> 姊妹文档：[存档管理](pages.md)、[实例可靠性处置](instance-repair.md)、[主题令牌](theme-tokens.md)。
> 相关代码：`src/CkanUI.ViewModels/Pages/Assets/`、`src/CkanUI.Services/Assets/`、`src/CkanUI.App/Views/Assets/`。

---

## §1. 定位与边界

KSP 的「皮」有三类直接摆在同一份游戏目录里：`Ships/**.craft`（自己造的飞船）、`GameData/**/Flags/*.png`（旗帜）、以及加载界面图片。它们都不归 CKAN 管——CKAN 只认 `GameData` 里的模组，这些文件要么是游戏原生资源，要么是用户自己丢进去的。结果是：想删一艘试飞的飞船、换一面旗、加几张加载图，只能开资源管理器手工翻目录，删的时候还要记得带上同名的 `.loadmeta` 与 `@thumbs` 缩略图，否则进游戏就是缩略图丢失或版本号空白。

本特性把三类资产收进同一个「资产工作台」，挂在侧栏**实例组**下，与「存档」并列：同一个游戏实例的三种可换的皮。

边界（刻意不做的事）：

- **不解析 `.craft` 内部结构**。只读 `.loadmeta` 里的现成元数据（`version` / `partCount` / `stageCount`），不做零件树重建——那是游戏自己的事。
- **不碰 `GameData` 里的模组文件**。旗帜扫描只**读**模组自带的 `Flags` 目录并允许设为默认；写入永远落在 `GameData/CkanUI/Flags`，卸载本工具不会伤及模组。
- **不假装能改原版加载图**。原版加载图内嵌在游戏资源里（实测该安装 `GameData` 下没有原版加载图目录；有图的是 `ROLoadingImages/PluginData/Screens` 里 72 张自带的 `.dds`），所以这一页管的是**目录清单 + 目录里的图片**：既覆盖 `LoadingScreenManager` 那种 cfg 目录清单，也覆盖 ROLoadingImages 那种「路径写死、只吃 dds」的模组。
- **不动存档**。改旗帜时只写 `settings.cfg` 的 `FLAG` 行；已有存档里的 `persistent.sfs` 旗帜不动，避免一脚踩进存档兼容性。

| 层 | 文件 | 职责 |
|---|---|---|
| 契约 | `src/CkanUI.Services.Abstractions/Assets/IAssetLibraryServices.cs` | `ICraftLibraryService`、`IFlagLibraryService`、`ILoadingImageService` |
| 契约 | `src/CkanUI.Services.Abstractions/Assets/AssetLibraryDtos.cs` | `CraftKind`、`CraftAsset`、`FlagAsset`、`LoadingImageAsset` |
| 服务 | `src/CkanUI.Services/Assets/CraftLibraryService.cs` | 三类载具扫描 + `.loadmeta` / `@thumbs` 伴生文件 |
| 服务 | `src/CkanUI.Services/Assets/FlagLibraryService.cs` | 旗帜扫描、默认旗帜读写、导入 |
| 服务 | `src/CkanUI.Services/Assets/LoadingImageService.cs` | 目录清单持久化 + 图片扫描 |
| 表现层 | `src/CkanUI.ViewModels/Pages/Assets/AssetPageViewModel.cs` | 三页共用的忙态、错误条、无实例收口、外壳唤起 |
| 表现层 | `src/CkanUI.ViewModels/Pages/Assets/{Ships,Flags,LoadingImages}PageViewModel.cs` | 各自的命令集与文案 |
| 视图 | `src/CkanUI.App/Views/Assets/{Ships,Flags,LoadingImages}PageView.axaml` | 纯 token 布局，无颜色/字号/圆角字面量 |

---

## §2. 三页流程

### 飞船（`ships`）

左表 + 右详情。表格列固定为 **缩略图 / 名字 / 分类 / 版本 / 零件 / 级数 / 大小 / 修改时间**，与游戏内载具列表的阅读顺序一致。分类用 `Vab` → 「火箭」、`Sph` → 「飞机」、`Subassembly` → 「子装配」翻译，不显示枚举名。

命令：刷新、导入（`.craft`，落到当前分类的目录）、重命名、复制、打开所在目录、删除。**重命名 / 复制 / 删除**三个动作都由服务层成组处理 `.craft` + `.loadmeta` + `@thumbs/<分类>/<名>.png`，页面只负责确认与文案；删除前必须走 `IDialogService.ConfirmAsync`，文案显式说明「同名缩略图与版本信息会一起删除，无法撤销」。

### 旗帜（`flags`）

卡片墙（`WrapPanel`，每张 150 宽，76 高预览）。扫描范围是 `GameData` 下**所有名为 `Flags` 的目录**——实测一台 RP-1 安装里就有 `Squad/Flags`、`ConformalDecals/Flags`、`KerbalismConfig/Flags`、`RealChute/Flags`、`RP-1/Flags` 五处。卡片显示名字、来源包（`GameData` 下第一段目录名）、以及当前默认时的 `CkanBadge Accent` 徽章。

默认旗帜的事实来源是 `settings.cfg` 的 `FLAG` 行（形如 `flag = Squad/Flags/default`），**不是**存档里的 `persistent.sfs`——后者是**每个存档各记一份**的既有值，改 `settings.cfg` 只影响新存档与主菜单。页头因此常显一行「当前默认：`Squad/Flags/default`」，让「我改了没生效」这类困惑有一处可对照。

导入永远落到 `GameData/CkanUI/Flags`（本工具自己的目录），同名自动加 ` (2)` 后缀，不覆盖用户已有旗帜。

### 加载图（`loadingimages`）

左目录清单 + 右画廊。页头下方常显**一行**指引（点左侧目录看该目录的图；停用只加后缀、不删文件）：这一页的操作对象是目录而不是单个文件，第一次打开只看到画廊时用户不知道图从哪来、该往哪放，所以指引常驻；但最初的三列步骤 + 边界说明被用户判定为信息过载，已压成一句，画廊标题再带当前目录名，切换结果肉眼可见。

目录行从纯路径改成 **目录名 + 完整路径（次要，`ToolTip` 给全文）+ 来源徽章 + 「含子目录」徽章**：260 宽的侧栏里只露盘符前缀等于没有信息，用户分不清哪条是模组要塞、哪条是自己放图的目录。

画廊**只显示当前选中目录的图**：点左侧目录即时换画廊、自动选中该目录第一张、计数跟着可见集合走；切换只过滤内存里的扫描结果，不重新读盘（重排整片 `WrapPanel` 是切页卡顿的来源），扫描结果与目录都没变时卡片实例照旧复用。

目录清单持久化在 `<game>/GameData/CkanUI/loading-image-folders.txt`（一行一个绝对路径），文件缺失时回落到服务层按目录存在性推导的默认清单：`Screenshots`、`LoadingImages`、`GameData/*/{LoadingImages,LoadingScreens}`，外加两个**认得出的模组来源**（见下）；**至少保留一个目录**，删到最后一个时命令直接拒绝并给出「至少要保留一个目录。」。画廊默认只扫目录第一层，认 `.png` / `.jpg` / `.jpeg` / `.dds`；来源模组声明要递归时（LSM 的 `ignoreSubfolders = False`）该目录连子目录一起扫。

#### 逐张启用 / 停用

每张卡片右下角有一个「停用 / 启用」按钮（`CkanGhost CkanSmall`），不需要先选中再找工具条按钮——停用哪张是看着图做的决定。停用 = 给文件加 `.disabled` 后缀（`a.dds` → `a.dds.disabled`），**不删文件、不移动文件**：

- ROLoadingImages 只扫 `Screens` 目录里的 `*.dds`，LSM 只认 `fileMasks = *.png;*.jpg`，改扩展名两边都自然跳过；
- 移动目录不行——LSM 的目录可能递归扫描，挪走会被重新收进来；删除更不行，用户只想「这次先别用它」；
- 随时点「启用」原样改回来。

因此扫描必须把 `.disabled` 也算可管理图片，否则停用后卡片从列表消失、用户再也点不回来。同一名字的 `a.dds` 与 `a.dds.disabled` 同时存在时只留一张卡片（优先启用态），否则画廊里两张同名图分不清哪张游戏在用。`LoadingImageAsset.FileName` 始终是**去掉后缀**的原始名，`FilePath` 是磁盘上的真实路径，`IsEnabled` 由后缀推导；缩略图转换器解码前先剥离后缀再按真实扩展名分派，否则停用图的预览整片空白。卡片上停用态用 `CkanBlockingScrimBrush` 遮罩 + 「已停用」徽章，图片本身降到 0.35 透明度，但仍可点、可预览。启停会改路径，重扫后按「文件名 + 目录」把选中落回同一张。

#### 与加载图模组的联动

加载图在 KSP 里有两条完全不同的落地路径，本页把两条都纳进目录清单，并用 `CkanBadge` 给每张图标出来源：

| 模组 | 读取位置 | 格式 | 说明 |
|---|---|---|---|
| ROLoadingImages（RP-1 元安装自带） | `GameData/ROLoadingImages/PluginData/Screens` | 仅 `.dds` | 路径与下标（`Screens[3]`）写死在 `LoadingImageReplacer` 里，没有 cfg、没有开关；塞图/删图就是这个 mod 的全部配置方式 |
| LoadingScreenManager（LSM） | `GameData/LoadingScreenManager/PluginData/` 下**所有** `*.cfg` 的 `FOLDER` 节点 `path = …` | `*.png;*.jpg`（`fileMasks`） | 目录清单由 mod 自己维护：主配置之外 mod 还能往同目录塞追加配置，只读主 cfg 会漏；`ignoreSubfolders = False` 时该目录递归扫描；本页只读 `path`，不写回 cfg |

`ILoadingImageService.DescribeFolders(gameDir)` 返回「目录 → `LoadingImageFolderInfo(Provider, Recursive)`」，页面把来源贴到 `LoadingImageAsset.Provider` 上，画廊卡片显示来源徽章与格式徽章（`PNG` / `DDS`），递归标记则转成 `ScanAsync(folders, recursiveFolders)` 的第二个参数。

`.dds` 预览走自带解码器 `src/CkanUI.Services/Assets/DdsDecoder.cs`（纯 BCL，不引 NuGet）：支持 `DXT1` / `DXT3` / `DXT5` 与未压缩 8/16/24/32 位，只解第一层 mip，缩略图按目标宽度**跳过无关块**采样（2560×1440 的加载图实测 65ms → 13ms）。`PathToThumbnailConverter` 按扩展名分派，解出来直接写 `WriteableBitmap`（BGRA8888）。实测该安装 `ROLoadingImages/PluginData/Screens` 的 72 张全部解码成功。

---

## §3. 服务层与磁盘约定

| 资产 | 扫描位置 | 伴生文件 | 写入位置 |
|---|---|---|---|
| 飞船 | 库根 = `<game>/Ships`（根库）或 `<game>/saves/<存档名>/Ships`（存档库）；每个库下 `{VAB,SPH,Subassemblies}/*.craft` | 同名 `.loadmeta`；`<库根>/@thumbs/<目录名>/<名>.png` | 同库同目录，跨库走 `TransferAsync` |
| 旗帜 | `<game>/GameData/**/Flags/*.png` | `settings.cfg` 的 `FLAG` 行 | `<game>/GameData/CkanUI/Flags` |
| 加载图 | 目录清单里的第一层图片（含 ROLoadingImages 的 `PluginData/Screens`、LSM cfg 里的 `path`），已停用的 `*.disabled` 也列出 | `GameData/CkanUI/loading-image-folders.txt` | 清单中的目录（启停 = 改 `.disabled` 后缀） |

`.loadmeta` 是 KSP 自己写的紧凑键值文件，服务层只取 `version`、`partCount`、`stageCount` 三个键，缺失时对应字段给 `null`，界面显示 `—`（`AssetFormat.OrDash`）。不做「必须存在 `.loadmeta`」的假设：从论坛下载的 `.craft` 往往没有伴生文件。

所有服务方法返回 `OperationResult` / `OperationResult<T>`，**不抛异常**——`UnauthorizedAccessException`（游戏正在运行锁文件）、`IOException`（盘掉线）都转成 `Fail(message, detail)`，由页面落到错误条。

---

## §4. UI 规格

三页共用骨架，全部取自设计令牌，无字面量：

| 部位 | 实现 |
|---|---|
| 页容器 | `Grid` + `Margin="{DynamicResource CkanPagePadding}"`，行 `Auto,Auto,Auto,*`；加载图页多一行常驻指引条，行定义顺延为 `Auto,Auto,Auto,Auto,*` |
| 页标题 | `TextBlock.CkanPageTitle` + 右侧 `Border.CkanBadge` 绑 `StatusText` |
| 工具条 | `Border.CkanToolbar` + `Classes="stagger-card"`；主操作 `Button.CkanPrimary`，次操作 `.CkanSecondary`，单项操作 `.CkanGhost`，删除 `.CkanDanger` |
| 库选择器 | 仅飞船页：工具条首位 `ComboBox` 绑 `Libraries` / `SelectedLibrary`，第一项是「根目录（所有存档共用）」，其后是各 `saves/<存档名>`；切库即改扫盘根，选中载具随之重载 |
| 列表 | `Border` + `Classes="CkanCard"`，卡片内表头与行共用同一套 `ColumnDefinitions` |
| 选中态 | `ListBoxItem` 内 3px `CkanAccentBrush` 竖条，`opacity` 由选中态驱动 |
| 空态 | `Border.CkanEmptyStateCard.Elevated` + `Border.CkanIconTile.Lg.Muted` + 一句说明 + 引导按钮 |
| 忙态 | 覆盖全页的 `CkanBlockingScrimBrush` + `ProgressBar IsIndeterminate` + `BusyText` |
| 错误条 | `Border.CkanInfoBanner` + `CkanDangerBackgroundBrush` / `CkanDangerBrush`，展开细节用 `CkanCaption` |
| 指引条 | `Border.CkanInfoBanner`（加载图页，位于工具条与主体之间）：`CkanIconInfo` + 一句说明；`CkanFontSizeCaption` + `CkanTextSecondaryBrush`，不抢主内容 |

详情栏固定 320–340 宽，用 `GridSplitter`（6px）分隔，与存档页一致；三页间宽度差异只来自画廊需要更宽的主区，不引入新的间距值。

页面进入动画沿用 `stagger-card`（只挂在工具条与主体卡片上），不额外新造动效；选中态与空态切换不做过渡，避免动 `IsVisible` 打断布局。

---

## §5. 失败与空态

| 场景 | 表现 |
|---|---|
| 没有可用实例 | 错误条「还没有可用的游戏实例。」，列表清空，不扫盘 |
| 目录不存在（实例无效 / 盘离线） | 服务层返回空集合而非失败，页面进空态 |
| 扫描失败 | 显示服务层的 `Message`；没有 `Message` 时用「刷新…失败。」兜底 |
| 游戏正在运行导致文件锁 | 删除 / 重命名失败，错误细节给原始异常消息，提示关游戏 |
| 导入非图片文件 | 选择器已按扩展名过滤；服务层二次校验并 `Fail` |
| 跨库搬运时源与目标同一目录 | `Fail`「源与目标是同一个目录，不需要搬运。」，源文件保持不动 |
| 目标库已有同名载具 | 不覆盖：按 `UniqueName` 落成 `Alpha (2)`，并在状态栏说明落到了哪个库 |
| 只有根库、没有存档 | 「复制到…」不弹空选择框，直接提示「没有别的载具库可以放。」 |
| 存档列表读失败 | 保留根库继续可用，错误条给服务层 `Message`，不清空当前列表 |
| 启停时同名文件已存在（`a.dds` 与 `a.dds.disabled` 同时存在） | `Fail`「同名文件已存在，先处理它再切换启停。」，原文件保持不动 |
| 启停被拒（`UnauthorizedAccessException` / `SecurityException`） | `Fail`「没有权限改这个文件：确认游戏已关闭、目录可写，或先用「在文件夹中打开」手动改名。」，原始异常消息进 `Detail` |
| 启停撞上占用或只读目录（`IOException`） | `Fail`「文件正被占用或目录只读：关掉游戏和图片查看器后再试。」 |
| 启停时文件已被外部删掉 | `Fail`「找不到这张加载图。」，随后刷新把卡片摘掉 |
| 没有文件选择器（无头环境） | 「当前环境没有可用的文件选择器。」，不抛异常 |

原则：**失败只影响当前动作，不清空已加载的列表**——用户花了时间找到的那艘飞船不该因为一次改名失败就从列表里消失。

---

## §6. 验收

- `dotnet build src/CkanUI.App/CkanUI.App.csproj -c Release` 与 `src/CkanUI.ViewModels` 均 0 警告 0 错误（`TreatWarningsAsErrors`）。
- 三页视图不含任何颜色 / 字号 / 圆角 / 高度字面量，全部走 `{DynamicResource}`。
- `tests/CkanUI.Services.Tests/Assets/AssetLibraryServiceTests.cs`：17 例覆盖路径映射、`.loadmeta` 解析、伴生文件成组改名/复制、旗帜目录识别与 `settings.cfg` 行替换、导入重名后缀、目录清单回落与持久化，以及启停往返（改名不删文件、重扫仍可见且 `IsEnabled = false`、再启用还原）与同名冲突不碰磁盘。
- `tests/CkanUI.ViewModels.Tests/Assets/AssetPageViewModelTests.cs`：无实例收口、刷新装载与默认选中、删除前确认、重命名去空白、命令的选中门控、默认旗帜识别、最后一个目录不可删、停用后卡片仍在且选中落回同一张、按钮文案随启停切换；刷新只显示选中目录、切目录即时过滤且选中第一张、扫描未变时卡片实例复用。
- 导航：`ShellViewModelTests` 断言 23 项侧栏与 `ships` / `flags` / `loadingimages` 的下标（挂在 `instances` 组下）。
- 本地化：新增 70 个 `Ui_Assets_*` / `Ui_Ships_*` / `Ui_Flags_*` / `Ui_LoadingImages_*` 键，`zh-CN` 与 `en-US` 同步；页面文案走 `Text(兜底, LocalizationKeys.X)`，兜底与 `zh-CN.json` 逐字一致（`PageTextLocalizationTests` 守护）。

---

## §7. 取舍

- **为什么是页面而不是对话框**：这三件事是长期维护型操作（隔三差五加一艘船），不是一次性流程；与首次启动向导相反。
- **为什么旗帜单独一页而不并进载具页**：两者数据结构、缩略图规则、写入位置全不同，合并只会让命令集变成两个无关子集的和。
- **为什么加载图不做「预览实际加载效果」**：原版加载图内嵌在资源包里，模组（LSM）的取图逻辑是外部程序行为，本工具复现不出来；给一个假的预览比不给更坏。
- **为什么自带 DDS 解码器**：RP-1 的 72 张加载图全是 `DXT1`，不认这个格式等于这一页在 RP-1 上是空的。Avalonia 的 `Bitmap.DecodeToWidth` 读不了 DDS，候选是引 `ImageSharp` 之类的大依赖；实际只需要「块解压 + BGRA 拷贝」，约 300 行纯 BCL 就够，还能按缩略图尺寸跳过块，比解码全图再缩更快。
- **为什么按扩展名而不是 `CanPreview` 分派**：预览是转换器的一个分支，不值得为它在 ViewModel 上多养一个状态位；格式徽章已经告诉用户这是 `DDS`。
- **为什么停用是改扩展名而不是移到回收站或另一个目录**：两个加载图模组都是「目录 + 扩展名匹配」取图，后缀是唯一两边都认的开关；移动会被 LSM 的递归扫描重新收进来，删除不可撤销，而 `.disabled` 既立刻生效又随时能改回。
- **为什么默认旗帜只写 `settings.cfg`**：写存档等于替用户决定既有存档的美术，且 `persistent.sfs` 的旗帜字段与 `settings.cfg` 的键名不同，跨存档批量改会踩版本差异。
