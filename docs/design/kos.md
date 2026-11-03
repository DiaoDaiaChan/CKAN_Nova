# kOS 脚本页签（0.2）

> 目标：在 CKAN UI 里管理并编辑 kOS 的 Archive 卷脚本，不用进游戏就能改 `.ks`。
> 插件 Id `com.ckanui.kos`，页签 key `kos`，挂在「工具」分节下（Section=`plugins`，Order=30），不进侧栏一级入口。

## 1. 文件契约来自 kOS 自己

- kOS 官方文档 `general/volumes`（2021，对应 kOS 1.4）写明：**Archive 卷的文件就是 `Ships/Script` 下扩展名 `.ks` 的真实文件**。0.14 及更早放在 `Plugins/PluginData/Archive`，本插件不迁移历史目录。
- 非 Archive 卷（本地卷、硬盘卷）存在存档的 persistence 里，不是独立文件，本插件不碰。
- 因此本页签只读写 `<gameDir>/Ships/Script/*.ks`，不解析 kOS 的编译产物，也不执行脚本。
- 本机取证：Steam 实例 `D:/SteamLibrary/steamapps/common/Kerbal Space Program` 装有 kOS 1.6.0.1（`GameData/kOS/kOS.version`：NAME=kOS VERSION=1.6.0.1 KSP_VERSION=1.12），`Ships/Script` 存在但为空；`D:/Kerbal Space Program_newmod` 没有 kOS。因此端到端只能在临时目录夹具上验证。

## 2. 服务层规则

`IKosScriptService`（`Scripts/KosScriptModels.cs`）与实现 `KosScriptService`：

| 操作 | 规则 |
| --- | --- |
| `LoadCatalog` | 目录不存在不是失败，返回 `DirectoryExists=false` 与期望路径；`ModInstalled` 看 `GameData/kOS`；只认 `.ks`，按文件名 OrdinalIgnoreCase 排序；单个文件读不到只进 `Diagnostics` |
| `Load` | `FileShare.ReadWrite`；识别 UTF-8 BOM；正文换行统一成 `\n`，同时返回原换行；超过 2 MB 给可读失败，不塞进编辑器 |
| `Save` | 先备份 `<名>.bak-yyyyMMdd-HHmmss`；写同目录临时文件（UTF-8 无 BOM、按原换行还原）后 `File.Move(overwrite:true)`；`expectedLastWriteUtc` 与磁盘相差超过 1 秒即拒绝，防止外部编辑器同时改 |
| `Create` | 目录不存在就建；目标同名已存在失败，不覆盖；默认 CRLF、UTF-8 无 BOM |
| `Rename` | 新旧名字都校验；目标存在失败；`File.Move` 不覆盖 |
| `Delete` | 先备份再删；文件不存在给可读失败 |
| 名字校验 | 空、含路径分隔符/冒号/`..`、不以 `.ks` 结尾一律失败（保存时统一补成 `.ks`） |
| 失败口径 | 公开方法不抛异常，全部转 `OperationResult.Fail` 中文文案 |

备份留在同目录，用户可以直接拿 `.bak-*` 回滚；页面不自动清理备份。

## 3. 页面

- 左侧：搜索、脚本列表（名称、大小、修改时间、行数、`boot.ks` 标记）、新建入口。
- 右侧：纯文本编辑器（`AcceptsReturn`/`AcceptsTab`、等宽字体），工具条保存、重命名、删除、重新载入、打开所在文件夹、刷新；状态行给行数、字符数、换行风格、未保存标记与错误/成功提示。
- 未保存保护：切换脚本、刷新、重新载入前不静默丢稿，页内确认条给「保存并切换 / 放弃修改 / 取消」。关闭页面或切到别的页签时宿主目前拦不住（见 §6）。
- 空态分层：没选实例 / 没装 kOS / 装了 kOS 但 `Ships/Script` 不存在 / 目录存在但没有脚本。四层文案不同，缺数据一律空，不伪造。
- 外部改动：保存被服务拒绝时保留编辑器正文并显示原因，用户可重新载入或另存。

## 4. 不做

- 不做语法高亮、自动补全、脚本执行/编译——那是 kOS 终端的事。
- 不解析存档 persistence 里的非 Archive 卷。
- 不做常驻文件监视：页面激活时扫描一次，刷新由用户或重新进入页面触发。

## 5. 测试

- 领域层：文件名校验、遍历攻击名、BOM、CRLF/LF 往返、备份内容、新建冲突、重命名冲突、删除备份、外部改动检测、2 MB 上限、坏文件不影响其它文件。
- 页面层：加载与选中、四层空态、脏标记、保存参数（含 `expectedLastWriteUtc`）、保存失败保留正文、新建补扩展名、重命名冲突、删除确认、未保存切换的三种选择、Dispose 幂等。
- 宿主接线：真实插件 dll + 只注册 `IInstanceService` 的 DI，验证页签声明与 `ActivatorUtilities` 构造。

当前数字：插件自身 70 条（45 领域 + 25 页面）；App.Tests 另有 1 条真实 Avalonia 视图加载与 1 条宿主 DI 接线，ViewLocator 解析断言随既有用例。

## 6. 已知缺口

- 宿主级关闭/导航切换拦不住未保存稿：`IPluginPage` 现为空标记接口，页面只能自保（对外暴露 `HasUnsavedChanges`）。要拦得住需要宿主提供页面离开确认契约。
- 非 UTF-8（如 GBK）脚本按 UTF-8 读取会得到替换字符；当前只做 UTF-8（含 BOM）往返。
- 备份撞名的 `-1..-20` 重试循环没有确定性单测；备份与原子替换之间的并发窗口未压测。
- 仅在 Windows 上验证；真实装有 kOS 且 `Ships/Script` 有脚本的实例未跑端到端。
