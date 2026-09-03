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

### Migration manifest authoring 与只读检查

功能 migration child 在自己的真实模块范围内显式完成 `finalize` 与定向 `check`。`finalize` 是唯一允许写回 manifest 派生字段的 authoring 动作；普通 build、测试发现和 `check` 都不得代替或隐式触发它。先生成确定性的 Release migration DLL，再 finalize 新尾节点，随后刷新 Host Release 副本并执行只读检查：

```powershell
dotnet build .\server\modules\<Module>\Migrations.SqlServer\<Module>.SqlServerMigrations.csproj -c Release
dotnet run --project .\server\tools\migration-manifest\MigrationManifest.csproj -c Release -- finalize --module <Module> --provider SqlServer --migration <MigrationId> --compatibility <Compatibility>
dotnet build .\apps\host-api\HostApi.csproj -c Release
dotnet run --project .\server\tools\migration-manifest\MigrationManifest.csproj -c Release --no-build --no-restore -- check --all --no-build
```

新尾节点的纵向重放使用真实业务模块的隔离副本，不修改正式 migration 历史：隔离副本保留已发布的正式 manifest 前缀，并且 Release bundle 中恰好比该前缀多一个真实尾 migration。对隔离根使用 `--repository-root <隔离副本>` 运行两次相同的 `finalize`；第一次应报告 `finalized`，第二次应报告 `already finalized`，两次后的 manifest SHA-256 必须一致。同步隔离 Host Release 副本后，再运行同一工具的 `check --all --no-build --repository-root <隔离副本>`，让正式 `PersistencePlanCompiler` 一起参与检查。

仓库用 Organization 当前真实尾节点保存了这条隔离重放入口。它要求上面的 Release 资产已经存在，测试会动态选择当前尾节点、只改临时副本，并在详细输出中写出单行 JSON 证据：

```powershell
dotnet test .\tests\MigrationManifest.Tests\MigrationManifest.Tests.csproj -c Release --no-build --no-restore --filter FullyQualifiedName~MigrationManifestVerticalReplayTests --logger "console;verbosity=detailed"
```

child 的可复跑证据固定记录以下字段，不把一次性临时目录或结果另建成长期事实源：候选 commit，module/provider/migration/compatibility，隔离副本来源，migration 与 Host Release build 命令及 exit code，两次 finalize 的输出和 manifest SHA-256，check 的各 provider 汇总、bundle 数与 exit code，以及正式工作树、manifest、构建资产和数据库在重放前后未改变的对照。功能 child 只运行这组定向证据；全部 child 与审查修复完成后，父 Issue 才在冻结候选上运行一次下文的仓库完整验证。

### Acceptance Gate-only 所有权

父 Issue 需要新增或改变某个 Acceptance Gate 时，child 划分必须指定恰好一个“验收资产所有者”，并在该 child 的验收标准中写清它所拥有的用户旅程。该 child 负责交付第一版可运行的 runner、fixture 与结构测试；定向测试通过并形成精确提交后，在候选不再编辑的条件下串行执行：

```powershell
.\scripts\accept.ps1 -Gate <Gate>
```

Gate-only 通过并记录提交、命令、结果与耗时后，该 child 才能关闭；失败若导致代码变化，则对新提交重跑。它是 child 的开发证据，不代替父 Issue 的正式验证。其他 child 只有在改动命中该用户旅程时才复跑同一个 Gate；未命中时只跑自己的最低层定向测试，不能为了“保险”重复跑 Gate 或全量验证。父级审查修复若改变该旅程，也先用同一 Gate-only 收敛。

全部 child 和审查修复完成后冻结候选，父 Issue 只运行一次正式入口：

```powershell
.\scripts\verify.ps1 -AcceptanceGate <Gate>
```

因此，Gate 资产由一个 child 持有，影响范围决定其他 child 是否复跑，最终通过结论始终由父 Issue 的冻结候选验证持有。

## 全量验证

标准入口依次执行静态门禁、一次 Release 构建、一次 `migration-manifest check --all --no-build` preflight、`--no-build` 测试、前端测试与构建。preflight 复用本轮 Release 资产；失败会保留 CLI 原始诊断并立即截断后续测试与 Acceptance：

```powershell
.\scripts\verify.ps1
```

结果写入 `.codex-temp/verify/runs/<run>/result.json`，`current-result.json` 指向最近一次结果。清单包含总耗时、各阶段耗时和命令；失败时读取 `failureStage`、同名阶段日志，以及 `msbuildDiagnosticPath` 下可能存在的 MSBuild 子节点诊断。

**verify.ps1 不要求也不检查干净工作树。** 本机是唯一的验证位置，脏树跑验证是常态；拦住它只会让 verify 变成提交前才能用的东西。归属靠记录而不靠设卡：`result.json` 的 `candidate` 段记录 `headCommit`、起跑时的 `dirty` 与 `dirtyEntryCount`，以及 `treeChangedDuringRun` 和 `headChangedDuringRun`。要一个可归属到某个提交的结论，就自己先提交再跑——此时 `dirty` 为 `false`，这份结果才真正属于那个 commit。两个 `*ChangedDuringRun` 任一为 `true`，都说明起跑与收尾时的候选不同，结论对应不到任何一份确定的代码。`status` 为 `running` 时，两者为 `null` 只表示尚未取得收尾端点，此时 `snapshotDiagnostic` 可以仍为 `null`；最终 `passed` 或 `failed` 结果中，两者为 `null` 才表示探针无法比较两端，应读取非空的 `snapshotDiagnostic`，不能当成 `false`。工作树探针不可用时，`dirty` 与 `dirtyEntryCount` 也为 `null`；HEAD 不可用时 `headCommit` 为 `null`，不会以 `false`、`0` 或哨兵字符串伪造已知状态。所有探针成功时 `snapshotDiagnostic` 为 `null`。HEAD 要单独看，因为跑到一半提交时已跟踪改动会同时从工作树消失，只比工作树察觉不到。该记录比较的是起跑与收尾两个端点，不能证明中途改变后又恢复的情况从未发生。

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
