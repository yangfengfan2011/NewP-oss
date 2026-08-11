# 快速验证

本页区分开发循环、冻结候选和运行时 smoke，避免每张票重复执行全部门禁。

## 开发循环

运行当前变更涉及的项目或过滤测试；模型元数据变化时先生成 DTO，再用 `--check` 确认无漂移：

```powershell
dotnet run --project .\server\tools\model-dto-sync\ModelDtoSync.csproj --no-restore -- .
dotnet run --project .\server\tools\model-dto-sync\ModelDtoSync.csproj --no-restore -- --check .
dotnet test .\tests\<Module>.Tests\<Module>.Tests.csproj --filter <Filter>
```

## 冻结候选

标准入口按 CI 顺序执行静态门禁、一次 Release 构建、`--no-build` 测试、前端测试与构建：

```powershell
.\scripts\verify.ps1
```

结果写入 `.codex-temp/verify/runs/<run>/result.json`，`current-result.json` 指向最近一次结果。清单包含总耗时、各阶段耗时和命令；失败时读取 `failureStage`、同名阶段日志，以及 `msbuildDiagnosticPath` 下可能存在的 MSBuild 子节点诊断。

需要 Acceptance 时必须显式选择已要求的 Gate，并使用干净候选：

```powershell
.\scripts\verify.ps1 -AcceptanceGate IdentityFoundation,Personnel
```

## 运行时 smoke

```powershell
.\scripts\dev.ps1
Invoke-WebRequest http://localhost:5000/api/health
.\scripts\stop-dev.ps1
```

开发服务的成功事实以 `.codex-temp/dev/runs/<run>/result.json` 为准；停止操作继续使用仓库脚本，不按进程名终止通用 `dotnet` 或 `node` 进程。
