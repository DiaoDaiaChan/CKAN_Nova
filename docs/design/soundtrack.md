# 音乐页签：管理 Soundtrack 播放列表（0.2）

> 「音乐」页签（插件 `com.ckanui.soundtrack`，页签键 `music`，Section=`plugins`）读写 KSP 根目录的
> `Playlists/playlists.cfg`，让玩家在启动器里增删曲目、排序、开关播放列表，而不用进游戏点 SoundtrackEditor。

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

保存前的校验只有两条硬规则：播放列表名非空、不重名。其余（曲目不存在、字段不认识）都只提示不拦——
Soundtrack 自己也能带着缺失曲目跑，只是那一首播不出来。

## 3. 页面

- 页头：实例名、游戏目录、`playlists.cfg` 路径、Soundtrack 模组是否安装（`GameData/Soundtrack*`）、
  播放列表数 / 曲目数 / 缺失曲目数。
- 左栏：播放列表行（名称、启用开关、曲目数、场景摘要）。点击切换右栏。
- 右栏：选中播放列表的编辑面——改名、`loop / shuffle / pauseOnGamePause / disableAfterPlay` 勾选、
  曲目列表（上移/下移/移除/在资源管理器打开，缺失曲目标「缺失」）、从乐曲库添加（搜索）。
- 工具栏：新建播放列表、删除、重新载入、保存（备份）、放弃更改；右上角显示未保存标记。
- 空态：没有 `playlists.cfg` 时给出文件路径与「新建播放列表并保存」入口（保存时创建 `Playlists` 目录）；
  没选实例时给「先在实例页选一个实例」。
- 乐曲库：扫描 `<游戏目录>/Music` 与 `<游戏目录>/GameData`（跳过 `Parts`，上限 20000 个文件、10 层深），
  只认 `.ogg/.wav/.mp3`。库只是「可添加的候选」，不复制文件。

## 4. 不改什么

- 不做音频播放：EULA 范围内没有可靠的跨平台解码路径，页面上只提供「在资源管理器打开」。
  试听请用系统播放器。
- 不编辑 `playWhen` / `playNext/Before/After` / `channel`：这些是场景编排语义，误改会让整段音乐不播；
  读出来显示、保存时原样保留，但页面不提供编辑控件。
- 不动 Soundtrack 自己的配置与存档：本插件只碰 `Playlists/playlists.cfg`。

## 5. 生效时机

Soundtrack 在场景切换时读播放列表，运行中的游戏不会热加载本页的修改。改完保存后需要
**回到主菜单或重启游戏**才听得到；页面在保存成功的提示里写清这一点，避免用户以为没生效。

## 6. 已知缺口

- 没有试听（见 §4）。
- 播放列表条目的 `playWhen` 只读，不能新建条件。
- 音乐文件若不在 `Music`/`GameData` 下（例如被其他 mod 放在别处），会显示「缺失」；
  这是保守扫描的代价，宁可标缺失也不做全盘搜索。
- 未在装有 Soundtrack 的真实实例上做端到端试听验证（本机两个实例都没装 Soundtrack）。
