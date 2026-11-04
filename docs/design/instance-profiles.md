# 实例配置档与多实例存储（0.2）

> 目标：一份游戏安装承载多套整合包（原版 / RP-1 / 任意 .ckan 清单），切换不复制实例；
> 同时给多实例的磁盘占用一个说得清的账本。需求来源：m11817、m11824。

## 1. 为什么不复制实例

`IInstanceService.CloneAsync` 走 Core 的 `GameInstanceManager.CloneInstance`，把整个游戏目录复制一份
（src/CkanUI.Services/Instances/InstanceService.cs:71）。一份 KSP + GameData 常见 5–20 GB，
每换一个整合包就复制一次，磁盘和切换时间都不可接受。

实测/事实：模组只改两个位置——`<GameDir>/GameData`（文件）与 `<GameDir>/CKAN/registry.json`（已装状态）。
游戏本体、`KSP_x64_Data`、存档、设置都不随整合包变化。因此「配置档」= 这两个目录的一对快照，
切换 = 同卷目录 rename（把当前那对换进 `CKANProfiles/<名字>/`，把目标那对换回游戏根目录），
毫秒级、零复制。

## 2. 目录布局

```
<GameDir>/
  GameData/                     ← 当前生效的整合包
  CKAN/                         ← 当前生效的 registry 与 CKAN 元数据
  CKANProfiles/
    profiles.json               ← activeProfile / launchProfile / 每档元数据
    vanilla/GameData, CKAN/     ← 「原版」配置档
    rp1/GameData, CKAN/         ← 「RP-1」配置档
    rp1/modpack.ckan            ← 导入来源（可追溯）
```

当前生效的那一档**不在** `CKANProfiles` 里——它就在游戏根目录的正常位置，游戏和 CKAN 都无需知道
配置档存在。这一点决定了兼容性：退出本工具、直接用 KSP 启动器启动，看到的仍是当前那一档。

## 3. 切换序列（InstanceProfileService.SwitchAsync）

1. 校验目标档存在、名字合法、不是当前档；
2. 游戏进程不在跑（`IInstanceService.IsGameRunning`），否则拒绝——正在写入的 GameData 不能换；
3. 若切的是当前实例，先 `IRegistryService.CloseAsync()`（=`RegistryManager.DisposeAll`），
   释放 registry.json 的锁与句柄；
4. 把 live `GameData`/`CKAN` 移进 `CKANProfiles/<当前档>/`（槽位已有内容则拒绝，绝不覆盖）；
5. 把目标档的 `GameData`/`CKAN` 移到游戏根目录；
6. 写 `profiles.json`（activeProfile + lastUsedUtc）；
7. 当前实例 `IRegistryService.RefreshAsync()` 重开注册表。

任何一步失败都按相反顺序回滚已移动的目录，**不允许留下半个配置档**；回滚失败会写进返回消息，
让用户知道现在处于哪个中间态。

## 4. 首次接管（CreateAsync(adoptCurrent: true)）

已有实例第一次建立配置档时，当前 `GameData`/`CKAN` 就是「原版」基线：把它们移进新档，
manifest 记为 active。之后游戏根目录的 GameData 由目标档换入。这样不需要用户手工搬目录，
也不会出现「切走之后原版不见了」。

## 5. 启动时自动切换

`profiles.json` 的 `launchProfile` 是「启动游戏时用哪一档」。实例页启动流程
（src/CkanUI.ViewModels/Pages/InstancesPageViewModel.cs，LaunchSelectedAsync）在放注册表锁之前调用
`IInstanceProfileService.ApplyLaunchProfileAsync(instanceName)`：

- 没设置 / 已经是目标档：直接放行，零开销；
- 设置了别的档：先切换（切换内部会关注册表、换目录、重开），再放锁、启动；
- 切换失败：**不启动**，错误条说明原因（例如游戏已在跑、目标档被删）。

顺序不能反：GameData 必须在对游戏进程可见之前就换好，否则进去的是上一个整合包的世界。

## 6. 存储账本（InstanceStorageService）

`ScanAsync` 对每个实例统计：

| 项 | 来源 |
| --- | --- |
| live GameData | 递归文件长度合计 |
| CKAN | registry、缓存元数据等 |
| 各配置档 | `CKANProfiles/<名字>/GameData`（当前档用 live 数字，不重复计） |
| 卷剩余/容量 | `DriveInfo`，取不到为 null（界面显示「—」） |

读取失败的目录进 `Diagnostics`，不让一个无权限目录把整张表变成错误。

## 7. 配置档去重

多个配置档里同名同内容的文件（典型：原版 Squad、共享依赖库、同一版本的美化包）默认各存一份。
`DeduplicateProfilesAsync` 把它们合并成硬链接：

- 只扫 `CKANProfiles/*/GameData` 的普通文件，**不碰当前生效的 GameData**；
- 长度 ≥ 128 KiB 才参与（小文件硬链接收益不抵风险）；
- 先按长度分组，再按 SHA-256 分组，同组保留路径序第一个；
- 同卷校验（`DedupVolume.SameVolume`），跨卷跳过并给警告；
- 默认 dry-run，只有显式 `dryRun:false` 才写盘；写盘先建临时名再原子替换。

不做的：把公共文件搬进独立内容仓再全量链接（那会改变 CKAN 卸载语义，需要重写注册表文件清单），
已知缺口记录在此，后续要做得先解决「卸载一个模组时不能删到别的配置档还在用的文件」。

## 8. 界面

- **整合包分享**（插件 `com.ckanui.modpackshare`，页 `modpack-profiles`，模组包分节）：
  配置档列表、从 GitHub / 本地 .ckan 导入、新建并接管当前状态、切换、删除、设为启动档；
- **存储管理**（插件 `com.ckanui.storage`，页 `storage`，数据分节）：
  多实例占用总览、配置档明细、卷剩余空间、配置档去重（dry-run → 确认 → 执行）。

## 9. 已知边界

- 切换要求游戏未运行；正在运行的游戏不会热切换（KSP 不支持）。
- 配置档是目录级的，不允许两个进程同时改同一个实例（切换会先关注册表，但外部工具不受控）。
- 配置档去重不处理当前生效的 GameData；跨实例去重仍由既有「去重」页负责（F-41）。
- 未在真实 20 GB 实例上做过切换计时；目录 rename 的时间复杂度与目录项数量相关。
- 没有实现「内容仓 + 符号链接物化」；原因见 §7 最后一段。
