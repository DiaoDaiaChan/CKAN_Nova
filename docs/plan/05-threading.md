# 05 线程模型与封送规范

> 这是本项目**最容易出 bug 的一篇**。Core 的进度回调全部来自后台线程，且 `IUser` 有两个同步阻塞方法会被后台线程调用。

## 1. 线程清单

| 线程 | 来源 | 会做什么 | 触及 UI 吗 |
|---|---|---|---|
| UI 线程 | Avalonia 主循环 | 渲染、VM 属性变更、命令执行入口 | 是 |
| 下载线程池 | `NetAsyncModulesDownloader` | HTTP、写缓存文件、触发 `*Progress` | **否**（必须封送） |
| 安装线程 | `ModuleInstaller.InstallList` | 解包、写 GameData、写注册表、触发进度、**可能调用 `IUser.RaiseYesNoDialog`** | **否**（必须封送） |
| 仓库索引线程 | `RepositoryDataManager.Update()` | 下载 `.repo.json`、解析、触发 `Updated` | **否** |
| ThreadPool（我们自己的） | 过滤/排序/映射 | 纯 CPU | 否（结果回 UI 线程） |
| 定时器 | 自动刷新、PlayTime 计时 | 触发服务方法 | 否 |

## 2. 封送规则矩阵

| 场景 | 从 | 到 | 正确做法 | 错误做法 |
|---|---|---|---|---|
| Core 进度事件 | 后台 | UI | 服务层 `_dispatcher.Post(...)` + 节流 | VM 里直接 `SetProperty` |
| Core `RaiseYesNoDialog`（同步要返回值） | 后台 | UI | `_dispatcher.Invoke(() => dialog.Confirm(...))` | `Post`（拿不到返回值）；`Dialog.ShowDialog()` 不指定 owner |
| Core `RaiseSelectionDialog`（同步） | 后台 | UI | 同上 | 在后台线程 `new Window()` |
| Core `RaiseError`/`RaiseMessage` | 后台 | UI | `Post` | `Invoke`（可能死锁，见 §3） |
| 我们的过滤/排序 | UI | 后台 | `await Task.Run(...)` 后回 UI 赋值 | 在 UI 线程对 3,000 项排序 |
| 文件对话框 | UI | UI | 直接调用 | — |
| 点击"取消" | UI | 后台 | `CancellationTokenSource.Cancel()`（线程安全） | 直接改共享布尔字段（无内存屏障） |

**唯一封送入口**：`IDispatcher`（接口在 `Services.Abstractions`，实现在 `App`）。

```csharp
public interface IDispatcher
{
    bool CheckAccess();
    void Post(Action action);                 // 异步，不等待
    T    Invoke<T>(Func<T> func);             // 同步，等待（仅在必须同步返回时使用）
    void Invoke(Action action);
    Task<T> InvokeAsync<T>(Func<Task<T>> func);
}
```

业务代码（VM、Service）**只允许**调用这四个方法，不允许 `Dispatcher.UIThread.*`、不允许 `SynchronizationContext.Current`。

## 3. 死锁规避

### 3.1 危险模式：UI 线程等待后台，后台等待 UI

```
UI 线程:   await installer.InstallAsync(...)      // 等待安装
                ↓
后台线程:  OnInstallProgress → Invoke(() => 更新 UI)   // 等待 UI 线程空闲
                ↓
UI 线程:   （正在同步等待安装完成，无法处理封送）→ 死锁
```

**规避规则**：

1. UI 线程**永远不要**同步阻塞等待后台（`.Result` / `.Wait()` / `GetAwaiter().GetResult()` 全禁）；
2. Core 的同步回调（`RaiseYesNoDialog`/`RaiseSelectionDialog`）用 `Invoke`，但**发起方必须不是 UI 线程**——即调用链是 `UI → 命令 → Task.Run → InstallList → RaiseYesNoDialog → Invoke(UI)`，此时 UI 线程空闲，能响应；
3. 若不得不从 UI 线程发起同步调用，用 `Dispatcher.InvokeAsync` + 不阻塞等待（改变架构，而不是加锁）。

### 3.2 危险模式：对话框 owner 跨线程

`Window.ShowDialog(owner)` 要求 owner 在同一 UI 线程且已加载。Core 从后台线程要弹框时，`owner` 必须是**主窗口**（提前捕获），不能用"当前活动窗口"（后台线程拿不到）。

### 3.3 危险模式：事件订阅在后台线程解除

Core 事件在后台线程触发，若在事件处理中途 `-=`，可能抛 `InvalidOperationException`。规则：订阅/退订一律在 UI 线程（`Activate`/`Deactivate` 内），且服务层的事件转发用不可变委托快照（`var h = Updated; h?.Invoke()`）。

## 4. 进度节流

Core 每装一个文件都可能发一次 `InstallProgress`，3,000 文件的模组会产生上千次跨线程封送 → UI 卡顿。

| 规则 | 参数 |
|---|---|
| 百分比节流 | 同一任务 100ms 内只更新一次 UI；只接受"百分比变化"或"消息变化" |
| 消息限频 | 同一消息文本重复不重发 |
| 批量合并 | 多个任务同时进行时，用 `IOperationQueue` 每 100ms 统一推一次汇总 |
| 完成事件 | 不节流（`OneComplete` 必须立即上报） |
| 单例实现 | `ThrottledProgressReporter`：内部 `Timer(100ms)` + 最后一次值合并，UI 侧只接收合并后的值 |

```csharp
public sealed class ThrottledProgressReporter : IProgressSink
{
    private readonly IDispatcher _ui;
    private readonly Timer _timer;
    private OperationProgress? _pending;      // 只保留最新
    private readonly object _gate = new();

    public void Report(OperationProgress p)
    {
        lock (_gate) { _pending = p; }        // 后台线程安全写入
    }
    private void OnTick(object? _)
    {
        OperationProgress? p;
        lock (_gate) { p = _pending; _pending = null; }
        if (p is not null) _ui.Post(() => Updated?.Invoke(p));
    }
}
```

## 5. 取消模型

| 层 | 机制 |
|---|---|
| 用户界面 | 每个 `OperationProgress` 带 `CanCancel`，取消按钮 → `IOperationQueue.Cancel(taskId)` |
| 服务层 | 每个任务持有 `CancellationTokenSource`；`Cancel()` 后在下一个检查点抛出 `OperationCanceledException` |
| Core 下载 | `NetAsyncModulesDownloader` 是否接受 `CancellationToken` **待验证（SP-09）**；若不接受，则只能"停止订阅 + 放弃结果"并在完成后清理临时文件 |
| Core 安装 | `InstallList` 一般不接受取消；策略：一个模组装完后检查取消标志，在模组边界处中止并回滚（事务） |
| 结果 | 取消统一表现为 `OperationResult.Cancelled`，UI 文案"已取消，未做改动" |

**关键约定**：取消是"请求"，不是"保证立即"。UI 在按下取消后进入"正在取消…"状态并禁用按钮，直到任务真正结束。

## 6. async/await 规范

| 规则 | 说明 |
|---|---|
| `async void` 只允许出现在事件处理器与命令体 | 其他一律 `async Task` |
| 禁止 `.Result` / `.Wait()` | 死锁源 |
| `ConfigureAwait(false)` | 服务层一律加；VM 层不加（需要回 UI 上下文） |
| 异常 | 命令体内的异常由 `IErrorReporter` 捕获；禁止空 `catch {}` |
| 并发限制 | 应用级最多 1 个"安装/卸载"任务、N 个下载任务（N = `IConfiguration` 的并发数） |
| 重入保护 | 命令用 `AsyncRelayCommand(execute, canExecute)`，执行期间 `canExecute = false` |

## 7. 禁止清单（Code Review 必查）

| 禁止 | 替代 |
|---|---|
| `Dispatcher.UIThread.Invoke*` 出现在 `ViewModels/` 或 `Services/`（除 `Threading/`） | `IDispatcher` |
| `Thread.Sleep` / `Task.Delay` 做同步等待 | 真异步 |
| 在 `ObservableObject` 属性 setter 里发起 IO | 命令 |
| 事件处理里 `throw`（会进 Core 的调用栈） | 记录 + 用户提示 |
| 共享可变字段跨线程无锁访问 | `CancellationTokenSource` / `Interlocked` / `lock` |
| 在 `Task.Run` 里创建 `Window` / 访问 `Application.Current.MainWindow` 的属性 | 封送 |
| `foreach` 遍历 `ObservableCollection` 时修改它 | 快照后遍历 / 整体替换 |

## 8. 如何测线程问题

| 手法 | 做法 |
|---|---|
| 单测（headless） | `FakeDispatcher` 用单线程 `SynchronizationContext` 模拟 UI 线程；断言所有 UI 更新都在该线程执行 |
| 断言不变量 | `DispatcherInvariantTest`：VM 的 `PropertyChanged` 必须来自 UI 线程（在 `FakeDispatcher` 里加线程断言） |
| 压力测试 | 用 300 个模组 × 100 次进度回调，断言封送次数 ≤ 阈值（验证节流生效） |
| 死锁回归 | 集成测试：从 UI 线程发 `InstallList`，Core 中途要确认 → 断言在 2s 内完成而非挂死 |
| 真实手工 | 安装大模组（如 RSS/RO）期间拖动窗口、切换页、点取消 |

## 9. 线程相关的验收门禁（M2）

1. 安装过程中窗口可自由拖动、缩放、切页，无"未响应"；
2. 安装过程中点关闭 → 明确的询问，不卡死；
3. Core 主动弹框出现在安装中途时，UI 线程不被阻塞（进度仍在动）；
4. 取消后 30s 内界面回到可操作状态，无残留"正在取消"；
5. 强杀进程后重启，无锁冲突、无半装文件（事务生效）。

---

## §12 附：E2 日志流与 AI 流式响应的线程规则（v1.4 新增，依据 [14 篇](14-log-and-ai-debug.md)）

| 场景 | 规则 |
|---|---|
| log4net 回调 | **任意线程**触发（下载/解包线程最常见）。appender 内**只写无锁环形缓冲**（`Interlocked` 递增序号），绝不碰 UI、绝不调用 `Util.Invoke`；由 UI 侧以 100ms 节流批量拉取 |
| `IUser` 消息 | 已在我们的实现里封送过（02 篇契约），可直接投递到缓冲，但**仍走同一缓冲**，保证序号单调 |
| 未捕获异常 | `AppDomain.UnhandledException` 可能发生在任意线程且进程即将终止 → 只做**同步直写文件**（不排队、不封送），保证崩溃前落地 |
| 日志列表刷新 | 节流 100ms、批量 `Add`、**仅在可视区**创建行对象；`FollowTail` 时用 `ScrollToEnd`，用户手动上滚立即置 `FollowTail=false` |
| AI 流式响应 | `HttpClient` + `stream:true` 在后台线程读；每个增量 `IAsyncEnumerable` 产出 → 必须封送到 UI 线程再更新 VM。**禁止**在后台线程直接改 `ObservableCollection` |
| 取消 | AI 请求、诊断包打包、日志导出三者都必须支持 `CancellationToken`；切换页面/关闭窗口时取消，且**取消后不得留下半张 zip** |
| 死锁面 | 新增风险：`Redactor` 若在读文件时持有锁、而文件又被安装线程占用 → 只读快照拷贝后再脱敏，禁止跨线程持锁 |

---

## §13 E4 本地化与线程（v1.6 新增，依据 [16 篇](16-i18n.md)）

| 关注点 | 规则 |
| --- | --- |
| `CultureInfo.DefaultThreadCurrentUICulture` | **全局静态**：在主窗口构造前设置一次；切换语言时设置并广播，避免"新线程用旧语言" |
| 后台线程消息 | `IUser.RaiseMessage` 可能在下载/解包线程触发 → 已在 `GUIUser` 等价层封送；本地化解析在封送后于 UI 线程进行，避免 `ResourceManager` 并发访问 |
| 已定型消息 | 字符串一旦生成不再翻译（16 篇 §4.4）；日志缓冲里保存**原文**，不做二次本地化 |
| 热切换广播 | `LanguageChanged` 在 UI 线程同步发出；订阅者只重算绑定，不得做 IO |
| 取消 | 语言切换**不取消**进行中的安装（与 `Core` 操作无关），仅刷新界面 |

## §14 E5 元数据译文的线程纪律（v1.2 新增）

| 场景 | 规则 |
|---|---|
| 启动载入译文包 | 后台线程 `DictPack.LoadJsonLines`，完成后**原子替换** provider 内的快照引用；UI 只读当前引用，不加锁 |
| 仓库 `Updated` 后的增量比对 | 3,799 × 2 次 `Fnv1a.Hash`（UTF-8 逐字节）放线程池；耗时上限 200ms（SP-32 验），**绝不占 UI 线程** |
| 机翻批处理 | 串行队列 + 可取消；批与批之间让出线程；UI 只显示"进度/剩余/预计花费" |
| 取文本路径 | `TryGet` 是纯内存查表（两个 `DictPack` 依次查），可在 UI 线程同步调用 |
| 包下载/校验 | `UpdateVerify` 在后台；校验失败必须在 UI 上明确报原因（`sha256-mismatch` / `size-mismatch` / `unreadable` / `no-manifest`） |
| 语言切换 | 只换载入的语言包（按需），不重算仓库指纹、不重译 |

## §15 E6 的线程与时序纪律（v1.3 新增）

| 场景 | 规则 |
|---|---|
| `onExit` 回调（`Core/GameInstance.cs:383-390`） | 线程池线程 → 必须封送到 UI 线程再触发恢复 |
| 监视器轮询 | 独立后台线程，500 ms 周期，**不允许**在 UI 线程做 `Process.GetProcessesByName` |
| L2 释放 Core 数据 | 后台线程；作用域 `Dispose` 会触发大量终结 → 明确 `GC.WaitForPendingFinalizers` 后再读数 |
| L3 `GC.Collect` | **暂停所有托管线程**（含 UI）→ 只在窗口最小化/隐藏时执行，且 UI 必须已有"正在整理内存…"提示 |
| L4 `EmptyWorkingSet` | P/Invoke 瞬时返回；不要在 UI 线程等 page fault |
| KSP CPU 采样 | `TotalProcessorTime` 差分，1 s 间隔，连续 3 次低于阈值才继续 |
| 恢复 | 单飞（`SemaphoreSlim(1,1)`）+ 幂等：重复触发不重复重载 |

## §16 E8 外观切换的线程纪律（v1.5 新增）

依据 [20 篇](20-appearance-personalization.md) §14。

| 动作 | 线程 | 规则 |
| --- | --- | --- |
| 切变体 / 改强调色 / 改圆角密度 | **UI 线程** | 一次性改 `RequestedThemeVariant` 或资源字典，不要在后台线程动 UI 资源（Avalonia 资源系统非线程安全） |
| 背景图解码 / 降采样 | 后台线程 | 解码完在 UI 线程换 `IImage`；同一次选择只允许一个解码任务（取消旧的） |
| 主题包读取与校验 | 后台线程 | 校验是纯计算（JSON + 对比度），完成后 UI 线程应用；启动时校验不得阻塞首帧 |
| 窗口材质重解析 | UI 线程 | 监听 `TopLevel.ClientSize/Scaling/RenderScaling` 变化，节流 100ms 再重解析，避免拖窗口时抖动 |
| 模糊背景离屏合成 | Render 线程（合成器） | 不能在 UI 线程做像素操作；模糊只对背景层做一次 |

两条禁止：①**禁止**在 `PropertyChanged` 回调里同步触发主题重建（会形成重入）；②**禁止**在列表滚动期间改字号/密度（先暂停虚拟化刷新，滚停后再应用）。
