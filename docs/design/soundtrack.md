# 音乐页签：管理 Soundtrack 播放列表（0.2）

> 「音乐」页签（插件 `com.ckanui.soundtrack`，页签键 `music`，独立侧栏入口）读写 KSP 根目录的
> `Playlists/playlists.cfg`，让玩家在启动器里增删曲目、排序、开关播放列表、试听，而不用进游戏点 SoundtrackEditor。

## 1. 为什么是这个文件

- SoundtrackEditor 的 `Persistor.cs` 把保存路径写死成 `KSPUtil.ApplicationRootPath + "Playlists/playlists.cfg"`；
  4.7.1.6 的 changelog 明确写了「修正新启动器导致的 _configSavePath 错误」——说明这个路径是这套工具链的事实契约。
- 文件是 ConfigNode 文本，一个 `playlist` 节点一段：
  `name / enabled / loop / shuffle / pauseOnGamePause / disableAfterPlay / playNext / playBefore / playAfter /
  channel / preloadTime`，加上 `tracks { track = ... }` 与 `playWhen { scene = ... }` 两个子节点。
- 曲目值就是音频文件名去掉扩展名；Soundtrack 支持 `.ogg / .wav / .mp3`。
- 真实样例固化在 `docs/design/soundtrack.sample/playlists.cfg`（4122 B、12 个播放列表、37 条曲目、
  含重复的 `preloadTime` 键与 `timeOfDay` 条件），测试直接拿它做往返基准，不另抄一份「想象出来的格式」。

## 2. 读写策略：宁可少改，不要丢数据

| 情形 | 处理 |
| --- | --- |
| 本插件不认识的标量键 | 进 `ExtraValues`，保存时原样写回 |
| 本插件不认识的子节点 | 整段原文（含大括号）进 `UnknownNodes`，保存时原样写回 |
| `enabled` 这类可选键没写 | 模型里是 `null`，保存时**不补**——「没写」和「写了 False」是两种含义 |
| 重复键（样例里 `preloadTime` 出现两次） | 后者覆盖前者，不抛异常 |
| 换行 | 沿用原文件的风格（CRLF/LF），新文件默认 CRLF |
| 编码 | UTF-8，不写 BOM（KSP 的 ConfigNode 读 BOM 会多出一个不可见字符） |
| 保存 | 先复制 `<文件>.bak-yyyyMMdd-HHmmss`，再写同目录临时文件，`File.Move(overwrite:true)` 原子替换 |
| 读失败/没权限 | 进 `Diagnostics` 显示在页面上，页面不崩、不假装成功 |

保存前的硬校验：播放列表名非空、不重名、`playWhen` 已知键的取值能通过触发器目录校验、
成对的数值下限不大于上限。保存时先把多选值统一成 `A | B`（旧版编辑器写出的逗号分隔会被顺手修掉），
因为 Soundtrack 的枚举解析只认 `|` 和 `&`，逗号会让整份文件静默回落原版播放列表。
缺失曲目、未知键只提示不拦——Soundtrack 自己也能带着缺失曲目跑，只是那一首播不出来。

## 3. 页面

页面按「什么场景会响 → 哪个列表 → 哪些曲目 → 还有什么条件」组织，主体是左边的条件节点树，
右边只放选中节点的编辑面。

- 页头（单行）：标题「音乐」+ 一句用法说明；右侧只留实例名。游戏目录、模组状态、各类计数和
  `playlists.cfg` 路径全部收进页脚状态条——这些是核对信息，不该占正文高度。
- 页脚状态条：Soundtrack 是否安装（tooltip 给完整路径）、列表数 / 曲目数 / 缺失数、`playlists.cfg` 全路径。
- 工具栏：新建播放列表、删除、重新载入、保存（备份）、放弃更改；右侧显示未保存标记。
- 左栏「播放流程」画布：场景是左侧根节点，向右一条分叉线接出该场景下的每条播放列表，
  每条列表再拉出一条横向流程链——「哪首歌 → 什么条件播」一眼看完，而不是让人逐条读卡片。
  - 根节点按 `scene` 分组，标题用中文（如「载具组装厂、航天器机库」），副标题保留 cfg 里的原始枚举名
    （`VAB | SPH`）。没写 `scene` 的列表落到「未限定场景」并排最前，其余按 KSP 场景顺序。
    点击根节点收起/展开这一组，组头显示「N 个列表 · M 首」与缺失汇总。
  - 每条列表是两行卡片：第一行名字 + 缺曲红点 + 右侧启用开关，第二行才是流程链。
    名字和链子抢一行，长条件（如「凌晨、黄昏、夜晚」）会被开关挤成省略号。
  - 流程链：`曲目`（首曲名，多首写 `+N`）→ 星球 → 其余条件，节点之间用 `›` 相连，链尾不接箭头。
    场景已经是根节点，链上不再重复；条件多于三条时收成一条「还有 N 项」，避免一条链撑爆整行。
  - 缺曲目的列表在名字前点一颗红点（tooltip 给缺失数），不摆整块「N 首缺失」徽标。
  - 点任意一条链切换右侧详情，并自动展开所属分组——选中项藏在收起的组里等于点击没反应。
- 右栏「触发条件」编辑区：每条条件一行——键名（tooltip 给语义）+ 值选择器 + 「应用」/「移除」。
  值控件按触发器类型走：flags 用按钮打开多选浮层，勾选后按目录顺序拼成 `A | B`；boolean 用「是/否」下拉；
  bodyName 用可搜索下拉（库存天体 + 文档里出现过的值，去重忽略大小写）；数字用数值框（接受小数）；
  未知键只读文本框，看不懂也不能删。选完仍先按触发器目录校验再写模型；「+ 添加条件」展开同一套选择器，
  从 17 个键里选，切键时自动填入该键的合法默认值，重复键、非法枚举都在表单里报错，不会写进模型。
  下方保留一行 `键 = 值` 原文供核对 cfg；没写 `playWhen` 时明说「playWhen 里没有写任何键」。
  行内 chip 不重复场景（场景已是分组标题）。
- 右栏编辑面（沿用）：改名、`loop / shuffle / pauseOnGamePause / disableAfterPlay` 勾选、
  曲目列表（上移/下移/移除/在资源管理器打开，缺失曲目标「缺失」）、乐曲库入口卡片。
  400px 宽下优先保内容：勾选框排两列、短标签 + tooltip 交代键名；曲目行只留名字、大小和操作，
  路径进 tooltip；右侧滚动条是叠在内容上的，所以滚动内容留 10px 内边距，文字不会压在滚动条底下。
- 底部播放条：上一曲 / 播放暂停 / 下一曲 / 停止、可拖动进度、音量与静音。
  队列取当前选中列表的曲目（缺失曲目跳过），单曲也可以从曲目行直接播。
- 启用开关按 Soundtrack 的语义显示两态：cfg 没写 `enabled` 时它按开启处理，开关显示为开；
  用户真的拨动才落盘——关掉写 `enabled = False`，再打开回到「没写」（不补 `enabled = True`）。
- 空态：没有 `playlists.cfg` 时给出文件路径与「新建播放列表并保存」入口（保存时创建 `Playlists` 目录）；
  没选实例时给「先在实例页选一个实例」。
- 乐曲库：扫描 `<游戏目录>/Music` 与 `<游戏目录>/GameData`（跳过 `Parts`，上限 20000 个文件、10 层深），
  只认 `.ogg/.wav/.mp3`。库只是「可添加的候选」，不复制文件。上千首的库存不在右栏里挤：右栏只放入口卡片，
  点「打开乐曲库…」弹独立窗口——无系统标题栏（`WindowDecorations=BorderOnly` + 自绘头部），可拖动、双击标题栏最大化、
  右上角最小化/最大化/关闭；窗口内按名称/路径搜索、按扩展名筛选、只看未加入、按名称/大小/路径排序，
  行内试听、双击或按钮添加，Esc 关闭。加入后写回同一批行对象，主页面立即显示「已加入」。

### 3.1 触发器目录（驱动展示与编辑校验）

展示层认全 SoundtrackEditorForked 的 17 个 `playWhen` 键，键名与语义取自它的
`Persistor.cs`（ParsePlayWhen / 写回）与 `Playlist.cs` 的 `PrerequisitesMet`：

| 类别 | 键 | 展示 |
| --- | --- | --- |
| 场景 | `scene` | 分组标题 + 详情 chip，枚举名翻中文 |
| 飞船 | `bodyName` / `situation` / `vesselType` / `vesselState` | 星球（单值，空=任意）/ 飞行状态 / 载具类型 / 生命周期 |
| 环境 | `timeOfDay` / `cameraMode` / `inAtmosphere` / `paused` | 时段 / 视角 / 大气层内外 / 暂停与否 |
| 数值范围 | `minAltitude`+`maxAltitude`、`minVelocitySurface`+`maxVelocitySurface`、`minVelocityOrbital`+`maxVelocityOrbital`、`minVesselDistance`+`maxVesselDistance` | 合成一条「下限 – 上限」，≥1000 m 转 km |

- 全部条件按 AND 生效；flags 类（scene / timeOfDay / cameraMode / situation / vesselType / vesselState）
  要求「允许集包含当前值」。数值范围含端点。
- `timeOfDay` 由 Soundtrack 按 KSC 当地太阳时判定，与飞船在哪个天体无关——页面上不额外解释，避免误导。
- 模型默认值（`float.MaxValue / MinValue / 0`）视为没有限制，不显示成条件；否则每条列表都会挂一排 3.4E+38。
- 不认识的键原样列在最后：看不懂也不能假装它不存在。
- 编辑时按目录校验取值：flags 只收已知枚举、布尔只收 `True/False`、数字只收有限小数；
  未知键不给编辑，只读原样保留。
- `situation` 多值写成逗号会让 Soundtrack 回读抛异常——本页保存前统一改写成 `LANDED | ORBITING`。
- `minVesselDistance` 在 SoundtrackEditorForked 4.7.1.8 里被读成 `minAltitude`（上游 bug），
  页面仍按正确键名写出，但游戏侧不会生效；编辑提示里写明这一点，不假装它能用。

## 4. 不改什么

- 播放只做本地回放：后端是 Windows Media Player COM（`WMPlayer.OCX`，仅 Windows 10/11 x64）。
  创建后端时关掉 `settings.enableErrorDialogs`，否则设置 `URL` 打开 `.ogg` 时 WMP 会弹
  「旧版 Windows Media Player / 扩展名无法识别」确认框并打断界面；其他平台没有播放后端——
  按钮不崩，但不会出声；不做转码，也不做整盘索引。
- `playWhen` 只编辑值：不删除未知键、不改文件里的键顺序；`playNext/Before/After` 与 `channel`
  仍是场景编排语义，页面只读保留。所有写入先过目录校验，避免一个非法枚举让整份 cfg 回落。
- 不动 Soundtrack 自己的配置与存档：本插件只碰 `Playlists/playlists.cfg`。

## 5. 生效时机

Soundtrack 在场景切换时读播放列表，运行中的游戏不会热加载本页的修改。改完保存后需要
**回到主菜单或重启游戏**才听得到；页面在保存成功的提示里写清这一点，避免用户以为没生效。

## 6. 已知缺口

- 播放在非 Windows 平台没有后端（见 §4）。
- `playWhen` 的键集合按 SoundtrackEditorForked 4.7.1.8 固定为 17 个；游戏后续版本新增的键
  会以只读行显示，需先更新触发器目录才能编辑。
- 音乐文件若不在 `Music`/`GameData` 下（例如被其他 mod 放在别处），会显示「缺失」；
  这是保守扫描的代价，宁可标缺失也不做全盘搜索。
- 未在装有 Soundtrack 的真实实例上做端到端试听验证（本机两个实例都没装 Soundtrack）。