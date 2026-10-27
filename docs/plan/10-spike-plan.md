# 10 Spike 计划（2–3 天）

> 目的：用最小代价验证「**不改 Core 一行，能否在 Avalonia 里走通 浏览 → 装一个 mod → 干净退出**」。
> 这是整个 23 周计划（v1.3 全特性对齐口径，见 [06 篇](06-wbs-milestones.md)）的前置门禁：spike 不过，方案要么改（换 WPF）、要么加 Core 改动，要么放弃。

## 1. 目标 / 非目标

| 目标 | 非目标 |
|---|---|
| 验证 Core 的 7 项集成契约在 Avalonia 下成立 | 不追求界面美观 |
| 验证后台线程交互不死锁 | 不做 MVVM 分层/测试/DI 完善 |
| 验证 `CkanTransaction` 在真实安装里生效 | 不做列表虚拟化、不做搜索过滤 |
| 摸清 8 个待验证项（02 篇 §10） | 不做打包/CI |
| 产出一份"能跑的最小骨架"作为 M0 的种子 | 不改 Core（含不做 04 篇 §5 的 RFC） |

## 2. 前置条件（必须先完成）

| # | 条件 | 失败则 |
|---|---|---|
| P1 | 解决 NuGet 可达（08 篇 R-01） | spike 无法开始，先解决网络/镜像 |
| P2 | .NET 10 SDK（本机有 10.0.401）+ Avalonia 模板 | `dotnet new install Avalonia.Templates` |
| P3 | 一个**可用的测试 KSP 实例**（真实或最小构造） | 用 `CkanUI.TestSupport` 方式手工造一个 |
| P4 | 一个可访问的 CKAN 仓库源（或本地假源） | 用本地假 `.repo.json` + 2 个极小 zip |

## 3. 任务卡

### D1-AM（4h）编译打通

| 步骤 | 动作 | 完成判据 |
|---|---|---|
| 1 | `dotnet new avalonia.mvvm -o spike` | 能 `dotnet run` 出空窗口 |
| 2 | 加 `ProjectReference` → `Core/CKAN-core.csproj` | `dotnet build` 通过，`using CKAN;` 可用 |
| 3 | 若 restore 缺包 → 按 08 篇 §2.1 逐级处理 | restore 成功 |
| 4 | 记一笔：TFM 组合（UI net10.0 引用 Core 的哪个 TFM） | 日志：实际选中的 TFM |

> 预期坑：`Core/CKAN-core.csproj` 的 `TargetFrameworks` 包含 `net10.0`，Avalonia 项目是 `net10.0` → 应自然匹配。若走 `net10.0-windows` 会引入 Core 的 Windows 专属实现（`Win32RegistryConfiguration`），**优先选 `net10.0`**。

### D1-PM（4h）启动序列 + IUser

| 步骤 | 动作 | 完成判据 |
|---|---|---|
| 5 | `Logging.Initialize()`（先 `Directory.SetCurrentDirectory(AppContext.BaseDirectory)`）+ 放一份 `log4net.xml` | 启动后日志文件有 "Application started" 一行 |
| 6 | `Resolve<IConfiguration>()`、`Resolve<RepositoryDataManager>()` | 能打印配置里的 `GameVersion` 等字段 |
| 7 | 实现 `IUser`（6 个成员，`Dispatcher.UIThread.Invoke/Post`） | 见 SP-01：成员集合与签名确认 |
| 8 | `new GameInstanceManager(user, config)` → 打印所有实例名与路径 | 控制台/日志能看到实例列表 |
| 9 | 故意从后台线程调 `user.RaiseYesNoDialog("test")` | 窗口正常弹出并返回 bool，不抛 `InvalidOperationException`（**SP-10**） |

### D2-AM（4h）读注册表 + 列表

| 步骤 | 动作 | 完成判据 |
|---|---|---|
| 10 | `RegistryManager.Instance(inst, repoData)` | 拿到对象，锁文件出现 |
| 11 | 触发 `repoData.Update(user, ...)` 并 `await` 完成（或订阅 `Updated`） | 可用模组数 > 0（用真实源约 3,000+；假源 2 个） |
| 12 | 把 `InstalledModules` + 可用模组塞进 `ListBox`（双列：名称/版本） | 界面能滚动显示（不要求虚拟化） |
| 13 | 记录：加载耗时、内存（SP-08） | 记入 spike 报告 |

### D2-PM（4h）装一个 mod

| 步骤 | 动作 | 完成判据 |
|---|---|---|
| 14 | 选中一个**无依赖**的小模组，`RelationshipResolver` 算出变更 | 变更集非空且合理 |
| 15 | `new NetAsyncModulesDownloader(user, config)` + 下载 | 缓存目录出现 `.zip`，进度有回调 |
| 16 | `ModuleInstaller.InstallList(new[]{mod}, opts, registry, ref cfgOnlyDirs, null, userAgent, downloader, null, ConfirmPrompt: false)` | GameData 下文件落位，注册表出现条目 |
| 17 | 观察：进度事件来自哪个线程？Core 有没有弹框？（SP-11） | 记录线程 ID 与调用栈 |
| 18 | 卸载该模组（`ModuleInstaller` 的卸载方法，签名以源码为准） | 文件与注册表都干净 |

### D3-AM（4h）边界与坑

| 步骤 | 动作 | 完成判据 |
|---|---|---|
| 19 | 安装过程中点"取消"：验证下载器是否接受 `CancellationToken`（SP-09） | 得出结论 + 记录可行的取消策略 |
| 20 | 安装过程中强杀进程 → 重启打开同一实例 | 无锁冲突？无半装？（SP-12，R-03） |
| 21 | 用老 `ckan.exe` 占住实例 → 新 UI 启动 | `IsMaybeLocked` 能探测到（SP-13） |
| 22 | 切换实例（`DisposeAll` → `SetCurrentInstance` → `Instance`） | 数据不串台，锁正确转移 |
| 23 | 用户取消 `RaiseSelectionDialog`（返回什么？） | 与 Core 期望一致（SP-04） |

### D3-PM（4h）收尾与结论

| 步骤 | 动作 | 完成判据 |
|---|---|---|
| 24 | 把 spike 代码整理成 M0 的种子（`App.axaml.cs` 启动序列 + `AvaloniaUser`） | 能交付给正式开发 |
| 25 | 写 spike 报告（模板见 §7），逐条回答 SP-01..SP-13 | 13 项全有结论 |
| 26 | 更新 02 篇 §10 的表格（已解决/仍未知） | 文档与实际一致 |
| 27 | 给出 Go / No-Go 建议 | 明确写一句 |

## 4. 验收清单（Go 条件，全部必须满足）

| # | 条件 |
|---|---|
| A1 | Avalonia 项目能引用 Core 并编译（net10.0） |
| A2 | 启动序列 ①–⑧ 全部走通，无异常 |
| A3 | `IUser` 从后台线程弹框不崩、不死锁 |
| A4 | 能读出真实/假仓库的模组列表并显示 |
| A5 | 能下载并安装一个模组，注册表与磁盘都正确 |
| A6 | 能卸载干净 |
| A7 | 正常退出后重启无锁冲突 |
| A8 | **Core 零改动**（`git -C external/CKAN status --porcelain` 为空） |
| A9 | 记录了 SP-01..SP-13 的结论 |

任一条不满足 → 见 §6 决策树。

## 5. 产出物

| 产出 | 形态 |
|---|---|
| `spike/` 代码（可丢弃，但启动序列与 `AvaloniaUser` 会被移植） | git 分支 `spike/m0-seed` |
| `doc/uir/spike-report.md` | 结论表 + 耗时记录 + 意外发现 |
| 更新后的 02 篇 §10 | 已解决项打勾、新发现的坑补入 08 篇 |
| 修正后的工时估算 | 若 spike 暴露额外复杂度，回头改 06 篇 |

## 6. 失败分支决策树

```
A1 失败（编译不过）
 ├─ 缺 NuGet 包 → 继续解决包源，不算方案失败
 └─ TFM 不兼容 → 试 net10.0-windows；若 Avalonia 只能配 windows TFM，
                 则跨平台目标作废（退回 WPF，决策 D3 部分失效）

A3 失败（弹框死锁/崩溃）
 ├─ 是 SyncContext 问题 → 改 Invoke 策略 + 让 Core 调用不发生在 UI 线程 → 重试
 └─ Core 内部逻辑强依赖 WinForms 消息泵 → 说明 Core 并非完全解耦
        → 触发"改 Core"（决策 D5 失效）→ 重新评估整个项目（成本 +50%）

A5 失败（装不上）
 ├─ 权限/路径问题 → 环境问题，不算失败
 ├─ InstallList 需要 GUI 环境（例如依赖 Application.DoEvents）→ 同上，触发改 Core
 └─ 事务层在非理想环境失效 → 记录，按 R-03 缓解

A8 失败（必须改 Core）
 → 停下来写 RFC，重新估算；若 RFC 涉及 >200 行 Core 改动，重新考虑"是否值得"

A2/A4/A6/A7 失败 → 都是集成细节，属可修问题，不影响 Go 结论
```

## 7. Spike 报告模板

```markdown
# Spike 报告（YYYY-MM-DD）

## 环境
- OS / .NET SDK / Avalonia 版本 / Core commit
- 包源状态（是否用了镜像）

## 结论：GO / NO-GO / 有条件 GO

## SP 清单

| ID | 问题 | 结论 | 证据（日志/截图/行号） |
|----|------|------|------------------------|
| SP-01 | IUser 成员集合 | ... | ... |
| ... | | | |

## 耗时
| 天 | 计划 | 实际 | 卡在哪 |

## 意外发现
- ...

## 对计划的影响
- 06 篇工时调整：...
- 08 篇新增风险：...
- 02 篇待验证项更新：...
```

## 8. 时间盒（硬约束）

| 天 | 上限 | 超时动作 |
|---|---|---|
| D1 | 8h | 若 A1（编译）没打通 → 停止 spike，先解决包源，**不要**用"读源码猜 API"的方式继续 |
| D2 | 8h | 若 A5（装不上）没打通 → 停止，写报告说明卡点，进入 §6 决策 |
| D3 | 8h | 无论进展如何，D3 结束时必须出报告与 Go/No-Go |

**总上限 24h。** 超过即说明"直接复用 Core"的成本被低估，这个信号本身比 spike 结果更重要。

## 9. 附：技术栈相关新增验证项（v1.1 回填，依据 [11 篇](11-tech-stack-decision.md) §10）

| ID | 问题 | 判据 | 耗时 |
|---|---|---|---|
| SP-09 | Win10 1607 / 1809 上自包含 `net10.0` 能否启动、渲染后端是什么 | 能启动；D3D11 或 ANGLE 为佳，软件渲染可接受 | 2h（需两份 VM） |
| SP-10 | 3,000 / 10,000 行 `DataGrid` 滚动帧时间 | 3,000 行 p99 ≤16.7ms；10,000 行 p99 ≤33ms | 3h |
| SP-11 | 材质三档降级（Win11 Mica / Win10 AcrylicBlur / 纯色） | 三档正确切换、无透明穿透、不崩溃 | 3h |
| SP-12 | 自包含发布体积与冷启动 | 体积 ≤90MB；首帧 ≤1.5s | 2h |
| SP-13 | CJK 字体回退与 100/150/200% DPI | 无方块字、无截断、无模糊 | 2h |
| SP-14 | 统计图表库：`OxyPlot.Avalonia` 在 Avalonia 12 下可用性 vs 自绘饼图（v1.2，12 篇 §3 / 08 篇 R-22） | 二者其一能画饼图、随主题变色、3,000 mod 数据 ≤1s | 3h |

**v1.2 附注（全特性对齐）**：SP 阶段不验证全部 55 条功能，只验证"**技术上会不会做不成**"的 4 类：①早期 Win10 能否启动（SP-09）②万行列表帧时间（SP-10）③材质三档（SP-11）④图表库（SP-14）。其余功能点属"工作量"而非"可行性"，直接进 WBS 的 UI-FEAT 组（06 篇 §1.9），在 TST-05 矩阵里验收（07 篇 §8）。

## 10. 附：存档工作台（E1）验证项（v1.4，依据 [13 篇](13-savegame-sfs.md) §13）

> 与前 14 项不同，这四项**可以在没有 UI、没有 Core 的情况下用现有真实存档当场验证**（spike 阶段就要做，因为结论决定 CLI 是否开放 `--write`）。

| ID | 问题 | 判据 | 耗时 |
|---|---|---|---|
| SP-15 | KSP 的 ConfigNode 解析器容忍度：`//` 注释、同行 `NODE {`、空行、值尾空格、BOM | 造 6 个变体，KSP 均可载入 | 1h |
| SP-16 | 同节点重复键，KSP 取首个还是末个 | 造 `funds = 1` + `funds = 2`，看载入值 | 1h |
| SP-17 | `.loadmeta.saveMD5` 为空时是否被校验；手工改 `.sfs` 不同步 `.loadmeta` 的后果 | KSP 主菜单显示的数值来源确认 | 1h |
| SP-18 | **端到端**：对 `quicksave.sfs` 副本格式化 → KSP 载入 → 飞船/资金/UT 与格式化前一致 | 逐项比对 + 截图 | 2h |

**SP-18 是 E1 的 Go/No-Go**：若格式化后 KSP 载入异常或数值变化，则 CLI 只保留 `sfs validate`（只读），取消 `--write`，UI 的格式化按钮降级为"仅检查"。**注意**：SP-15..18 全部在存档副本上做，绝不碰用户的 `persistent.sfs`。

SP-09/SP-11 失败**不构成对框架的 NO-GO**（Avalonia 已是既定决策）；但若 SP-09 在 1607 上完全无法启动，按 11 篇 §12.4 触发"提高系统下限或退回 `net481`"的二次决策。

---

## §11 E2 日志与 AI 排障的 spike（v1.4 新增，依据 [14 篇](14-log-and-ai-debug.md) §11）

| ID | 问题 | 方法（≤时间盒） | 通过标准 | 失败怎么办 |
|---|---|---|---|---|
| **SP-19** | 不改 Core，能否把 log4net 阈值提到 `INFO` 并抓到 `MemoryAppender`？ | 在我们的启动早期向**同一 repository** 注册自建 `MemoryAppender`，并 `repo.Threshold = Level.Info`；跑一次真实安装 | 能同时收到 Core 的 WARN/ERROR 与我们设的 INFO；`CKAN.log` 仍可写 | 只靠 `IUser` + 异常两路，AI 分析质量下降 → 加大"用户粘贴"权重、UI 提示"请开诊断模式" |
| **SP-20** | 10 万行虚拟化在 Tier 3 机器（软渲染）能否保 p99 ≤16.7ms？ | 灌 10 万行假日志，滚动 + 过滤切换 | 滚动 p99 ≤16.7ms，过滤 ≤100ms | 窗口化到 5k 行 + 索引化搜索；分页加载 |
| **SP-21** | OpenAI 兼容端点（官方 / Azure / Ollama `/v1`）能否用同一客户端？ | 三端点各跑一次流式请求 + 解析 | 三者都能流式返回并解析出 JSON | 只保留官方 + Azure，文档明说；Ollama 标"实验性" |
| **SP-22** | DPAPI(`CurrentUser`) 在非域账户 / 漫游配置 / 无 %LocalAppData% 写权限时是否可用？ | 三种账户环境各存一次 | 三种环境都能存取 | 降级为"每次输入 Key，不落盘" |

**SP-19 是 E2 的 Go/No-Go 之一**：若拿不到 INFO 级日志，则"AI 排障"的实际可用性会大幅下降（Core 全库仅 59 条日志语句、默认 root=WARN），此时应调整产品叙事为"**帮你整理并脱敏日志，便于贴给懂的人看**"，而不是"AI 能直接诊断"。

**注意**：SP-19/SP-21 涉及真实网络请求与真实 Key，实验时用最小额度账户，且**不得把实验日志提交到仓库**。

---

## §12 E3 Mod 配置的 spike（v1.5 新增，依据 [15 篇](15-mod-config-adapters.md) §11）

| ID | 问题 | 方法（≤时间盒） | 通过标准 | 失败怎么办 |
| --- | --- | --- | --- | --- |
| **SP-23** | ConfigNode **保字往返**（注释/空行/顺序/末尾换行）在 5 个真实 `.cfg` 上成立吗？ | 复用 E1 解析器，读→改 1 项→写→逐字节 diff | 除目标行外零差异 | 关闭"原地编辑"，改为"生成覆盖块 + 提示用户自行合并"或直接改走 MM patch 路径 |
| **SP-24** | 写盘后 KSP 启动是否按新值生效（KSPCF + Kerbalism 各一组） | 改 1 项 → 启游戏 → 看日志/游戏内表现 | 生效且无报错 | 全部标 `requiresRestart` 并在 UI 明示"需重启" |
| **SP-25** | adapter schema 能否表达 6 个 mod 的真实配置（颜色/路径/按键/枚举） | 为 6 个 mod 各写一份草稿 adapter.json | 6 个都能完整表达，无需逃生口 | 扩展字段类型，或允许 `custom: raw` 逃生 |
| **SP-26** | 骨架导出产物能否被加载器自举 | 导出 → 放入用户目录 → 重启加载 | 加载成功且字段齐全 | 修导出器格式 |

**SP-23 是 E3 的 Go/No-Go**：保字往返不成立则"原地编辑"整条路线作废，只能退到只读查看 + MM patch 导出。

---

## §13 E4 本地化的 spike（v1.6 新增，依据 [16 篇](16-i18n.md) §13）

| ID | 问题 | 方法（时间盒） | 通过标准 | 失败怎么办 |
| --- | --- | --- | --- | --- |
| **SP-27** | Avalonia 在 Win10 1607 / Win11 24H2 上 CJK（及希腊文/emoji）字形回退是否成立 | 同一后备链渲染混合文本 | 全部正确显示、无需 per-run 指定 | 退化为"启动时固化字体族 + 禁止运行时切换语言"（+12h，I-04 降级） |
| **SP-28** | 语言切换能否驱动**全部**绑定刷新（DataTemplate/ToolTip/菜单/DataGrid 列头） | 切换后脚本抓 20 视图文本 | 全部更新且不重建窗口 | 改为"切语言后重建主窗口"（+8h，体验略差） |
| **SP-29** | 官方 resx 能否原样复制并按清单名直读 | 复制 3 个 resx 到测试项目按 `Localization/de-DE/ManageMods.de-DE.resx` 清单名读 | 读取成功（`ResourceSet` 或 `ResourceManager`） | 改用"构建期生成强类型类 + JSON 快照"（+6h） |
| **SP-30** | 伪本地化 + 德语长文案下关键屏是否零截断 | `--pseudo` 跑 5 屏 | 无 `…` 截断、无横向滚动 | 交付 16 篇 §7.2 的 auto-size 改造（已在 I18N-11 内） |

**SP-27 是 M10 的 Go/No-Go**（CJK 用户是主战场，字体问题无退路）。

## §14 E5 元数据 i18n spike（v1.3 新增，SP-31..SP-35）

| ID | 问题 | 做法 | 通过标准 | 不通过时 |
|---|---|---|---|---|
| **SP-31** | 200 条真实摘要的机翻质量与成本 | 抽 200 条（含长尾/URL/专名）跑真实模型，人工抽检 50 条 | 可读率 ≥80%、成本 < $1 | 退化为"只消费人工包"（46h 版本） |
| **SP-32** | 3,799 个 `Fnv1a.Hash` + 比对耗时；3,639 行 `DictPack` 载入耗时 | 本机 37.9 MB 仓库缓存 + 生成包 | 哈希 ≤200ms；载入 ≤80ms | 移入线程池 + 结果缓存 |
| **SP-33** | 搜索并集是否破坏既有结果集 | 20 个英文查询对拍老 GUI | 结果集完全一致 | 译文不参与匹配（降级 M-07） |
| **SP-34** | 本地模型（Ollama）零外发可用 | 指向本地端点跑 50 条 | 质量可用且无外发 | 仅保留云端路径，默认关闭 |
| **SP-35** | `i18n_tools --root` 能否在独立根跑通 `entry put-batch` + `packindex`；G6 对长文本的合理阈值 | 建临时 root，灌 60 条真实摘要 + 术语副本 | 写入/journal/packindex 全通；G6 误伤率 <5% | 改用 `validate --strict` 子集（G2/G4/G9）+ 自定长度阈值 |

**SP-31 与 SP-35 是 M11 的 Go/No-Go**：SP-31 不过 → 只做消费端；SP-35 不过 → 机翻改走应用内路径（不产出社区包）。

## §15 E6 资源释放 spike（v1.4 新增，SP-36..SP-40）

| ID | 问题 | 做法 | 通过标准 | 不过时的退路 |
|---|---|---|---|---|
| **SP-36** | 仓库元数据对象图**真实占用**（决定 E6 收益量级） | 用现成 37.9 MB 缓存 JSON，独立小程序反序列化后测堆 | 对象图 ≥150 MB 且 L2 后堆下降 ≥60% | **降级为只做 L0+L1**（砍掉 RLS-04 14h） |
| **SP-37** | `EmptyWorkingSet` 后切回的实际卡顿 | 三档各 5 次，记录恢复耗时分布 | 激进档 P95 ≤5 s | 关闭 L4，只留 L0–L3 |
| **SP-38** | Avalonia 的 GPU 位图能否靠"清视觉树 + 释放位图"真正回落 | 3,000 行列表 + 缩略图，前后对比 GPU 进程内存 | 可测下降 | 只释放显式创建的 `RenderTargetBitmap` |
| **SP-39** | KSP（Unity）能否被 `MainWindowHandle` 稳定探测 | 实测 5 次，记录首次非零时刻 | 100% 在 60 s 内探测到 | 用 CPU 增长 + 窗口标题兜底 |
| **SP-40** | LOH 压缩在 2–4 GB 堆的耗时与瞬时峰值 | 构造大堆后压缩 | 耗时 ≤5 s、峰值增幅 ≤15% | 只做第一遍 `GC.Collect` |

**SP-36 是 E6 的 Go/No-Go**：若"大量资源"不在仓库数据上，L2/RLS-04 直接砍掉，把力气花在 L0/L1。

## §16 E7 安装 spike（v1.5 新增，SP-41..SP-45）

依据 [19 篇](19-windows-install-flow.md) §16。全部在虚拟机里做，总计 7h，可在 M13 之前一次性跑完。

| ID | 问题 | 时间盒 | Go/No-Go | 失败分支 |
| --- | --- | --- | --- | --- |
| SP-41 | **干净 Win10 1607 镜像（未装 VC++）上，Avalonia 应用能否启动？** | 2h | **是** | 不能 → PKG-04 必做且必须内嵌 `vc_redist.x64.exe`（+25MB）；内嵌仍失败 → 退回 app-local 拷贝 DLL（官方不推荐但可用） |
| SP-42 | `CkanUISetup.exe -s -t <DIR>` 能否在 1607 上静默装/卸与自定义目录？ | 2h | 是 | 不能 → 只保留默认路径一键安装，自定义目录改为手动解压 zip（发布说明注明） |
| SP-43 | HKCU 的 `ckan://` 注册能否压过旧 ckan.exe 写的 HKLM？ | 1h | 是 | 不能 → 首启向导把"清理 HKLM（需提权）"改为必经步骤 |
| SP-44 | 单文件发布（`PublishSingleFile`）与 Velopack `current` 目录是否冲突 | 1h | 不冲突 | 冲突 → 保持非单文件（09 篇 §11 已定非单文件，此条只做确认） |
| SP-45 | 签名后的 `Setup.exe` 在 SmartScreen 上的实际表现（新证书信誉） | 1h | 记录即可 | 不阻塞；写进发布说明 |

判定规则沿用本文 §4：任一条"必须为是"的项失败且无替代路径，则 M13 范围收缩为"便携 zip + 手动注册脚本"，并在 08 篇增开一条"安装形态延期"风险。

## §17 E8 外观 spike（v1.6 新增，SP-46..SP-50）

依据 [20 篇](20-appearance-personalization.md) §18.2，共 11h。

| ID | 问题 | 时间盒 | Go/No-Go | 失败分支 |
| --- | --- | --- | --- | --- |
| SP-46 | Win10 22H2 + `win10-x64` 下 AcrylicBlur 观感与滚动帧率；再叠背景模糊是否仍 ≤16.7ms | 3h | **是** | 否则"磨砂"限定为 Win11，Win10 只给纯色 + 背景图 |
| SP-47 | Win10 `AllowsTransparency` 假圆角的输入法候选框/最大化/贴边/多屏表现 | 3h | 记录即可 | 结果决定该开关是否面向用户开放（R-74） |
| SP-48 | 4K 背景图解码峰值与降采样策略 | 2h | 是 | 否则限制最大 2560 宽 |
| SP-49 | 主题包白名单校验器拦 `<`/`{`/越界值/未知键；对比度门禁 200 个随机主题稳定性 | 2h | **是**（安全门禁） | 否则不开放导入，只留内置主题 |
| SP-50 | 运行期切 `RequestedThemeVariant` 时是否残留旧画刷缓存（尤其 AcrylicBlur 下的窗口底） | 1h | 是 | 否则材质变更走"重建窗口"路径 |

判定沿用本文 §4：SP-46 / SP-49 任一失败且无替代 → M14 收缩为"深色 + 强调色 + 圆角 + 密度"，磨砂与主题包导入延后。
