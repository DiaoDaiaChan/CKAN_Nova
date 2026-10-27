# 卸载残留目录清理（F-43 / UI-22）

> 对应规格：[12 全特性对齐清单](../plan/12-feature-parity.md) F-43、[03 界面规格](../plan/03-ui-spec.md) UI-22。
> 代码：`src/CkanUI.Services.Abstractions/IResidualCleanupService.cs`、`src/CkanUI.Services/Cleanup/`、`src/CkanUI.ViewModels/Dialogs/ResidualCleanupDialogViewModel.cs`。

## 1. 数据怎么流出来

上游 `IInstaller` 在卸载/升级时会返回 `possibleConfigOnlyDirs`（"只剩配置、没有实际文件"的目录）。这条链路在本仓被完整贯通：

```
CkanInstallerBackend.Remove()/Install()   ← Core 返回的 possibleConfigOnlyDirs
  → InstallerBackendResult.ResidualDirectories
    → InstallerService.Execute(...)
      → InstallReport.ResidualDirectories
        → ChangesPageViewModel.ShowResidualCleanupAsync(...)
          → IResidualCleanupDialogService.ShowAsync(...)
```

## 2. 探测与安全边界

- **候选来源**：`IResidualDirectorySource.GetCandidates(InstanceInfo)`。默认实现 `HeuristicResidualDirectorySource` 扫 `GameData` 顶两层，只留"不含 `.dll` 且（为空 或 只含配置文件 `.cfg/.version/.txt/.md/.json/.yml/.yaml/.readme`）"的目录，并只保留最浅的那一层（避免父子重复）。
- **拒绝理由**（`BlockReasonFor`，顺序即优先级）：
  1. 不在 `GameData` 之下 → 「目录不在 GameData 之下，已拒绝。」
  2. 自身是重解析点（符号链接 / 联接）→ 符号链接理由。
  3. 内部含重解析点 → 「目录内含有符号链接，已跳过。」
  4. 内部含**受管文件** → 「目录内含有受管文件，请到模组页卸载对应模组。」
- **路径规范化**：拒绝空白、含 `:`、绝对路径、任何空段、`.`、`..`。
- **失败语义**：与「未管理文件」页的整批拒绝不同，这里**逐个跳过**（"能删多少删多少"）。IO/权限异常 → `IErrorReporter.Report(..., "ResidualCleanupService.Delete", ...)` 记入 `Failed`，最终返回可重试失败「有 N 个目录没能删除：… / 可能被其它程序占用或权限不足，关闭占用后可以重试。」

## 3. 界面

三出口：**打开目录**（`IResidualCleanupShellLauncher`，走系统外壳）、**删除勾选**（`ResidualCleanupChoice.Delete`）、**全部保留**（`ResidualCleanupChoice.Keep`）。默认不勾选任何行，关闭即保留。行上展示相对路径、文件数、字节数与样例文件名，被拒的行显示理由且不可勾选。

## 4. 已知缺口

1. 启发式候选来源只看"配置残留"形态；真正的"所有孤儿目录"需要跨实例扫描（归 F-41 去重）。
2. 没有撤销：删除即不可恢复，因此只在应用成功后的同一会话里提示一次。
