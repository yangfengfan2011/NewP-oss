# 快速验证

本页区分开发循环、全量验证和运行时 smoke，避免每张票重复执行全部门禁。

仓库没有 CI，**本机是唯一的验证位置**。没有第二次机会在别处发现问题，所以合并前
至少要在本机跑一次 `verify.ps1`。

## 开发循环

运行当前变更涉及的项目或过滤测试；模型元数据变化时先生成 DTO，再用 `--check` 确认无漂移：

```powershell
dotnet run --project .\server\tools\model-dto-sync\ModelDtoSync.csproj --no-restore -- .
dotnet run --project .\server\tools\model-dto-sync\ModelDtoSync.csproj --no-restore -- --check .
dotnet test .\tests\<Module>.Tests\<Module>.Tests.csproj --filter <Filter>
```

## 全量验证

标准入口依次执行静态门禁、一次 Release 构建、`--no-build` 测试、前端测试与构建：

```powershell
.\scripts\verify.ps1
```

结果写入 `.codex-temp/verify/runs/<run>/result.json`，`current-result.json` 指向最近一次结果。清单包含总耗时、各阶段耗时和命令；失败时读取 `failureStage`、同名阶段日志，以及 `msbuildDiagnosticPath` 下可能存在的 MSBuild 子节点诊断。

**verify.ps1 不要求也不检查干净工作树。** 本机是唯一的验证位置，脏树跑验证是常态；拦住它只会让 verify 变成提交前才能用的东西。归属靠记录而不靠设卡：`result.json` 的 `candidate` 段记录 `headCommit`、起跑时的 `dirty` 与 `dirtyEntryCount`，以及 `treeChangedDuringRun` 和 `headChangedDuringRun`。要一个可归属到某个提交的结论，就自己先提交再跑——此时 `dirty` 为 `false`，这份结果才真正属于那个 commit。两个 `*ChangedDuringRun` 任一为 `true`，都说明跑的过程中候选变过，结论对应不到任何一份确定的代码；HEAD 要单独看，因为跑到一半提交时已跟踪改动会同时从工作树消失，只比工作树察觉不到。

需要 Acceptance 时必须显式选择已要求的 Gate。闸门共享 `bin/Release`、`.codex-temp/dev/state.json` 与本机端口，必须串行，且一轮在跑时不要编辑文件：

```powershell
.\scripts\verify.ps1 -AcceptanceGate IdentityFoundation,Personnel
```

## 运行时 smoke

```powershell
.\scripts\dev.ps1
Invoke-WebRequest http://127.0.0.1:5000/api/health
.\scripts\stop-dev.ps1
```

开发服务的成功事实以 `.codex-temp/dev/runs/<run>/result.json` 为准；停止操作继续使用仓库脚本，不按进程名终止通用 `dotnet` 或 `node` 进程。
