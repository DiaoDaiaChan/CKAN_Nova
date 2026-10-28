# 深链（ckan:// 协议）设计与接线

> 归属：**ARC-05**（协议与单实例）。实现分三层：协议解析（`CkanUI.Platform`）、路由（`CkanUI.ViewModels`）、入口接线（`CkanUI.App`）。
> 相关文档：[feature-matrix](feature-matrix.md) F-55、[pages](pages.md)、[service-contracts](service-contracts.md)。

---

## 1. 为什么要有这一层

`ckan://` 的第一职责是**单实例唤醒**：用户双击第二条协议链接时，第二份进程不该起窗口，而应把参数转交给已在运行的那份。
只有「唤醒」还不够——老 CKAN 的协议入口能直接跳到某个模组或某个实例。所以本轮补的是**唤醒之后的落点**：把参数变成「切到某个页面并选中某一行」。

设计约束（决定了实现形状）：

1. **ViewModels 层不引用 `CkanUI.Platform`**（架构铁律）。`CkanUri` 住在 Platform，路由住在 ViewModels，两者只交换两个字符串 `(kind, value)`，不交换类型。
2. 路由必须有**可解释的返回值**。深链是外部输入，失败原因要能进日志（`Log.Warn`），所以 `DispatchAsync` 返回 `DeepLinkOutcome` 而不是 `bool`。
3. 页面定位能力由页面自己提供（`ShowIdentifierAsync` / `SelectRepository`），路由用 `Func<string, object?>` 拿页面实例，不直接持有页面类型。

---

## 2. 协议语法

```
ckan://<kind>/<value>
```

| kind | value 语义 | 落点 |
|---|---|---|
| `mod` | 模组 Identifier（如 `ModuleManager`） | 模组页 → 选中该行；不在当前投影里时先写进搜索框再等一次搜索 |
| `instance` | 实例名（大小写不敏感） | 先 `RefreshAsync` 再按名字找 → 切换实例 → 实例页 |
| `repo` | 仓库名或仓库 URI（先按名、再按 URI） | 仓库页 → 选中该行 |

**一律拒绝**（`CkanUri.TryParse` 返回 `false`）的形态：带 query、带 fragment、路径段多于一段、未知 kind、value 解码后为空或全空白、非 `ckan` scheme。
理由写在校验处：query/fragment 没有语义，出现即说明调用方拼错了，**宁可拒绝也不要静默丢弃**。

`CkanUri.Build` 与 `TryParse` 互为逆运算（`Uri.EscapeDataString` 编码 value）。

---

## 3. 解析层（`CkanUI.Platform`）

| 成员 | 位置 | 职责 |
|---|---|---|
| `CkanUri.Scheme` = `"ckan"` | `src/CkanUI.Platform/SingleInstance/CkanUri.cs:19` | scheme 常量 |
| `CkanUriCommand(string Kind, string Value)` | `src/CkanUI.Platform/SingleInstance/CkanUri.cs:10` | 解析结果 |
| `CkanUri.TryParse(raw, out command)` | `:28` | 绝对 URI、scheme 比对、query/fragment 拒绝、kind 白名单（`mod`/`instance`/`repo`）、单段路径、解码后非空 |
| `CkanUri.Build(command)` | `:81` | 反向生成 |

kind 统一小写输出（`kind.ToLowerInvariant()`），白名单比对用 `OrdinalIgnoreCase`。

---

## 4. 路由层（`CkanUI.ViewModels`）

`src/CkanUI.ViewModels/Navigation/DeepLinkRouter.cs`：

- `public sealed record DeepLinkOutcome(bool Handled, string Summary)`（`:9`），两个静态工厂 `Done(summary)`（`:12`）与 `Ignored(summary)`（`:15`）。
- `public sealed class DeepLinkRouter`（`:28`），常量 `ModKind = "mod"`（`:31`）、`InstanceKind = "instance"`（`:34`）、`RepoKind = "repo"`（`:37`）。
- 构造 `DeepLinkRouter(INavigationService navigation, IInstanceService? instances = null, Func<string, object?>? pageResolver = null)`（`:47`）——两个可选项都可缺省，缺省时对应分支返回「尚未就绪」而不是抛异常。
- `public async Task<DeepLinkOutcome> DispatchAsync(string? kind, string? value, CancellationToken cancellationToken = default)`（`:62`）：
  - 空 kind → `Ignored("深链缺少命令类型。")`；空 value → `Ignored("深链缺少取值。")`；未知 kind → `Ignored("未知的深链类型「x」。")`；
  - `mod` → `DispatchModAsync`（`:94`）：`NavigateAsync("mods")` 后调页面的 `ShowIdentifierAsync`；
  - `instance` → `DispatchInstanceAsync`（`:111`）：`RefreshAsync` → 按名匹配（`:124`，`OrdinalIgnoreCase`）→ `SwitchAsync` → `NavigateAsync("instances")`，找不到不切换；
  - `repo` → `DispatchRepositoryAsync`（`:141`）：`NavigateAsync("repos")` 后 `SelectRepository(nameOrUri)`；
  - `OperationCanceledException` → `Ignored("已取消。")`，不向上抛。

页面侧的定位能力（这两个成员就是路由与页面之间的契约）：

| 成员 | 位置 | 语义 |
|---|---|---|
| `ModsPageViewModel.ShowIdentifierAsync(string? identifier, CancellationToken)` | `src/CkanUI.ViewModels/Pages/ModsPageViewModel.cs:1628` | 命中当前投影直接选中；未命中把标识符写进 `SearchText`，等待 `PendingSearch`（搜索防抖 80ms）完成后再试一次 |
| `ReposPageViewModel.SelectRepository(string? nameOrUri)` | `src/CkanUI.ViewModels/Pages/ReposPageViewModel.cs` | 先按 `Name`、再按 `Uri` 大小写不敏感匹配 |

---

## 5. App 侧接线（三条入口）

全部在 `src/CkanUI.App/App.axaml.cs`：

1. **启动参数**：`_ = StartAsync(shell, desktop.Args ?? Array.Empty<string>());`（`:193`）；`StartAsync`（`:231`）在 `finally` 里 `await DispatchDeepLinksAsync(launchArgs)`（`:262`）——**放在 finally** 是为了「外壳启动失败也要把深链消费掉并记日志」，避免参数被静默吞掉。
2. **单实例转发**：`SingleInstanceGuard.ArgumentsReceived`（`src/CkanUI.Platform/SingleInstance/SingleInstanceGuard.cs:38`）→ `HandleExternalArguments(args)`（`App.axaml.cs:422`）→ `_ = Instance.DispatchDeepLinksAsync(args);`（`:438`）。
3. **实际派发**：`DispatchDeepLinksAsync(IReadOnlyList<string> args)`（`:445`）逐条 `CkanUri.TryParse`（`:461`）→ `DeepLinkRouter.DispatchAsync` → 成功 `Log.Info`「深链已执行」、返回未落到目标 `Log.Warn`「深链未落到目标」、抛异常 `Log.Warn`。解析失败只记 `Log.Warn`，继续处理下一条。

DI 注册（`src/CkanUI.App/ServiceRegistration.cs`）：

```csharp
services.AddSingleton(sp => new DeepLinkRouter(
    sp.GetRequiredService<INavigationService>(),
    sp.GetService<IInstanceService>(),
    key => PageTypes.TryGetValue(key, out var pageType) ? sp.GetService(pageType) : null));
```

`pageResolver` 复用 `ServiceRegistration.PageTypes`（页面键 → VM 类型），所以新增页面时只需登记 `PageTypes`，深链自动可用。

---

## 6. 失败语义一览

| 情形 | 结果 | 日志 |
|---|---|---|
| 参数不是 `ckan://` | 直接跳过 | `Log.Warn`（含原始参数） |
| kind 未知 / value 为空 | `Ignored` | `Log.Warn`「深链未落到目标」 |
| 模组标识符搜不到 | `Done`（已切到模组页，选中失败由页面状态栏说明） | `Log.Info` |
| 实例名不存在 / 实例已锁定 | `Ignored`（不切换实例） | `Log.Warn` |
| 页面尚未就绪（`pageResolver` 为空或返回外来类型） | `Ignored("…尚未就绪") ` | `Log.Warn` |
| 令牌取消 | `Ignored("已取消。") ` | 不记 |
| 路由内部异常 | 记 `Log.Warn` | 继续下一条参数 |

统一原则：**深链失败绝不能阻断启动**，也不能让应用崩在外部输入上。

---

## 7. 测试

`tests/CkanUI.ViewModels.Tests/DeepLinkRouterTests.cs`（16 例）：空 kind / 空 value / 未知 kind、kind 大小写与空白归一、`mod` 未命中写搜索框、`pageResolver` 缺失或返回外来类型、`instance` 大小写不敏感匹配并导航、找不到不切换、锁定实例报失败、无实例服务、`repo` 按名与按 URI、`repo` 无匹配、已取消令牌、`Done`/`Ignored` 工厂语义。

协议解析（`CkanUri`）由 `tests/CkanUI.Platform.Tests` 覆盖（两类入口共用同一解析器）。

---

## 8. 已知缺口

1. **协议注册仍需安装步骤**：`ckan://` 在 Windows 上要写进注册表（`HKCR\ckan` 或 `HKCU\Software\Classes\ckan`）。当前仓库只有便携包，注册在安装器/首次运行时做（见 [ops/release.md](../ops/release.md) 与 `docs/plan/19-windows-install-flow.md`），未自动化。
2. **`value` 只支持单段**：设计上不允许 `mod/ModuleManager/1.0` 这类多段路径（版本、子动作），需要时得先扩协议再扩路由，否则老客户端拼错的链接会被静默拒绝——这是刻意选择。
3. **`mod` 深链依赖搜索兜底**：目标模组未安装且不在当前搜索结果内时，只能落到「已切到模组页 + 搜索框里填了标识符」。
4. **托盘与深链共用处理器**但托盘启动不计游戏时长（见 [feature-matrix](feature-matrix.md) F-48 残留）。
