# 快速验证

本页区分开发循环、全量验证和运行时 smoke，避免每张票重复执行全部门禁。

仓库没有 CI，**本机是唯一的验证位置**。没有第二次机会在别处发现问题，所以合并前
至少要在本机跑一次 `verify.ps1`。

## 执行层级

实施开始时确定本次交付层级，并按改动选择下文“开发循环”中的适用检查；调用 `$implement` 等通用技能时，按这里的分工解释其全量验证和完整审查步骤。

- **单任务 / 单 Issue**：局部改动执行最小可复跑验证和范围差异审查；完整功能交付在本任务末尾执行所要求的完整审查、仓库验证及适用 Gate。已有规格直接复用，不为进入流程补建 Spec 或 child。
- **父级下的 child**：完成本 child 的验收条件、范围差异审查和下文适用的定向验证；命中用户旅程时按 Gate-only 所有权规则验证。仓库完整验证和完整双轴审查在父级集中执行，不因每个 child 调用 `$implement` 而重复。
- **父级收尾**：汇总全部 child 和修复的最终候选，按下文“父级收尾”执行一次所要求的完整双轴审查、聚焦复核和仓库完整验证。

提交和 GitHub 写入沿用 `AGENTS.md` 与本次任务授权；调用通用实施技能不自动增加提交授权。下文要求“已提交候选”的步骤以已有提交授权为前提；未授权提交时改用稳定的工作区候选，审查本轮任务差异（含已有任务提交与未提交改动）并记录实际状态，不把验证结果宣称为某个干净 commit 的证据。production cold-start 仍保留其专用提交归属要求。

## 开发循环

运行当前变更涉及的项目或过滤测试；模型元数据变化时先生成 DTO，再用 `--check` 确认无漂移：

```powershell
dotnet run --project .\server\tools\model-dto-sync\ModelDtoSync.csproj --no-restore -- .
dotnet run --project .\server\tools\model-dto-sync\ModelDtoSync.csproj --no-restore -- --check .
dotnet test .\tests\<Module>.Tests\<Module>.Tests.csproj --filter <Filter>
```

### 定向验证选择

单任务、child 和审查返修均按当前差异选择最低层验证：

- 定向命令沿用正式验证入口的配置与严格参数，只缩小文件、项目或测试范围。前端使用所属包的脚本和配置；lint 的 unused-disable、warning 等限制与完整验证一致，实际参数以当前脚本为准。
- 低风险、局部且行为边界清晰的简单修改，以能直接捕获本次行为的最小定向检查为完成条件。即使位于共享组件，单一属性、文案或样式调整也不自动扩大到所属包、浏览器或全仓验证；只有触及公共契约、构建边界、复杂状态关系，或定向检查无法证明真实消费路径时才扩大，并在执行前说明具体风险与理由。
- 公共注册、契约或网络读取生命周期变化，同时检查受影响的既有消费者、精确注册断言和对应静态门禁；新增测试通过不能代替这些检查。
- 新增或修改涉及真实组件懒加载、异步清理或共享状态的测试时，定向通过后运行一次所属包测试，核对全包负载下的可靠性。复用现有真实模块预加载和可靠清理方式；保留行为断言、默认超时与全局并行配置。

检查通过且候选未再改变时保留有效证据；仅在新差异、失败或尚未解决的风险使其失效时补跑。上述检查不增加每个 child 的全仓验证轮次。

### 界面呈现核对

新增界面或改变共享呈现时，在首个真实切片及交付前刷新并核对实际页面，与所选标准页面对照控件、密度、操作位置和可见文案。标准 CRUD 同时查看本次受影响的独立页面、Tab 内嵌和 Drawer 形态，并检查窄容器换行；未涉及的形态无需扩大验证。呈现归属遵循[代码生成规则](../developer/code-quality-standard.md)。

在现有任务证据中记录对照页面、已查看的形态及发现的问题，区分原需求缺陷与新增设计偏好。这是现有浏览器旅程之外的轻量呈现检查，不新增自动化 Gate、重复旅程或逐项间距断言；数据语义仍在低层验证。

### Child 完成证据

采用父子 Issue 交付时，开始每个 child 前把验收条件对应到实际消费路径和最低层验证。先复用现有测试；只为本次改变且尚未覆盖的行为补回归。按改动选择下表适用项，不把整表变成每张票的固定检查清单。

| 本次改动 | 关闭前需要的定向证据 |
| --- | --- |
| Model、View、Form 元数据 | 真实 authoring 经正式编译器生成可用 schema；列表字段引用有效；create/edit payload 正确处理服务端字段。 |
| Lookup、权限或错误契约 | 通过实际端点及权限流水线验证最小所需权限、分页和当前值；校验错误断言 Problem Details 的具体字段。 |
| Display | 通过正式 planner/descriptor 触发 resolver，并核对最终响应中的 canonical companion 与批量解析行为。 |
| 前端 | 所属 workspace 包的定向测试、类型检查及严格 lint 通过；核对测试根目录、目标文件和实际检查范围，并按上文选择全包测试及界面呈现核对。 |
| 模型或迁移输入 | 运行实际依赖这些输入的迁移与历史 fixture 定向检查。历史 fixture 使用完整冻结的历史 authoring 输入，经正式投影验证固定 digest，与当前模型演进隔离。 |
| 层级、批量或分页查询 | 以代表性数据验证数据库往返次数、应用读取规模及数据库侧分页；复杂查询按风险核对执行计划和耗时。固定查询次数与有界返回量不能单独证明数据库计算成本有界；业务容量限制必须来自已确认规格。 |

协调者关闭 child 前核对验收条件、范围提交和上述适用证据；测试通过数量不能替代正式调用链的证明。提交与 Issue 写入沿用本次任务授权。缺失的适用验证要明确记录原因和复跑命令，尚未证明的完成判据不能标记通过。后续返修继续由原 owner 负责，并在原 child 补充纠正提交、验证结果及被替代的证据，父 Issue 汇总最终候选。

### Migration manifest authoring 与只读检查

功能 migration child 在自己的真实模块范围内显式完成 `finalize` 与定向 `check`。`finalize` 是唯一允许写回 manifest 派生字段的 authoring 动作；普通 build、测试发现和 `check` 都不得代替或隐式触发它。先生成确定性的 Release migration DLL，再 finalize 新尾节点，随后刷新 Host Release 副本并执行只读检查：

`finalize` 会把普通仓库当前 `HEAD` 中已跟踪的 manifest 作为已发布前缀事实，逐字段拒绝历史节点改写。隔离根没有自己的 Git worktree 时，必须先保留冻结的 Host Release manifest 副本作为同一前缀事实；缺少该副本会失败，且只能在 finalize 完成后再刷新 Host 副本。

```powershell
dotnet build .\server\modules\<Module>\Migrations.SqlServer\<Module>.SqlServerMigrations.csproj -c Release
dotnet run --project .\server\tools\migration-manifest\MigrationManifest.csproj -c Release -- finalize --module <Module> --provider SqlServer --migration <MigrationId> --compatibility <Compatibility>
dotnet build .\apps\host-api\HostApi.csproj -c Release
dotnet run --project .\server\tools\migration-manifest\MigrationManifest.csproj -c Release --no-build --no-restore -- check --all --no-build
```

新尾节点的纵向重放使用真实业务模块的隔离副本，不修改正式 migration 历史：隔离副本保留已发布的正式 manifest 前缀，并且 Release bundle 中恰好比该前缀多一个真实尾 migration。对隔离根使用 `--repository-root <隔离副本>` 运行两次相同的 `finalize`；第一次应报告 `finalized`，第二次应报告 `already finalized`，两次后的 manifest SHA-256 必须一致。同步隔离 Host Release 副本后，再运行同一工具的 `check --all --no-build --repository-root <隔离副本>`，让正式 `PersistencePlanCompiler` 一起参与检查。

仓库用 Organization 当前真实尾节点保存了这条隔离重放入口。它要求上面的 Release 资产已经存在，测试会动态选择当前尾节点、只改临时副本，并在详细输出中写出单行 JSON 证据：

```powershell
dotnet test .\tests\MigrationManifest.Tests\MigrationManifest.Tests.csproj -c Release --no-build --no-restore --filter FullyQualifiedName~MigrationManifestVerticalReplayTests.OrganizationTail --logger "console;verbosity=detailed"
```

该入口在隔离 Git repo 中同时保留 staged 与 unstaged 修改；check 前后比较 HEAD、index tree、porcelain v2 状态、全部 tracked worktree 内容，以及 authoring manifest、migration Release DLL、Host manifest/DLL 的 SHA-256。数据库状态由单独的只读 observer 证明，避免把“没有配置 SQL Server”误报为通过。启用后 observer 在一次测试中保持 master 与全部可访问 ONLINE user database 的连接；每次快照都从 master 重新读取并稳定排序数据库集合与 create date、state、user access、read-only、recovery model、compatibility level、collation、访问状态等元数据，逐库窄读所有 `__NewPMigrationHistory` 表的规范列并计算内容摘要，再从 `sys.dm_db_log_stats(database_id).log_end_lsn` 读取每库日志前沿。它不扫描业务表。check 前后的数据库集合、元数据、日志前沿、history 表数/行数/内容摘要必须完全相等：

```powershell
$env:NEWP_RUN_MIGRATION_MANIFEST_DB_READONLY = "1"
try {
    dotnet test .\tests\MigrationManifest.Tests\MigrationManifest.Tests.csproj -c Release --no-build --no-restore --filter FullyQualifiedName~CheckAll_WithSqlServerReadOnlyObserver_PreservesDatabaseState --logger "console;verbosity=detailed"
}
finally {
    Remove-Item Env:\NEWP_RUN_MIGRATION_MANIFEST_DB_READONLY -ErrorAction SilentlyContinue
}
```

observer 按 Acceptance 相同顺序读取 `NEWP_ACCEPTANCE_SQLSERVER_ADMIN_CONNECTION`、`NEWP_DEV_SQLSERVER_CONNECTION_STRING` 和 `apps/host-api/appsettings.Development.json:ConnectionStrings:SqlServer`。环境门禁未开启时 xUnit 明确记为 skipped；已开启但连接缺失、无效或不可达，SQL Server 版本或权限不支持 `sys.dm_db_log_stats`，任一 ONLINE user database 不可访问，或任一 history 表无法完整读取时，测试都 fail closed，不能退回 catalog-only 快照形成绿色结果。`DatabaseSnapshot_WhenFixtureCommitsMetadataSchemaDataAndHistoryWrites_Changes` 另用一次性数据库把夹具写入与被测 `check` 分开：切换 compatibility level 时元数据摘要必须变化，提交普通业务行或 schema 变更时日志前沿必须变化，提交 history 行时日志前沿与 history 内容摘要都必须变化；测试先恢复原 compatibility level 并证明元数据摘要复原，再在 `finally` 中再次恢复后删除该精确命名的夹具数据库。

child 的可复跑证据固定记录以下字段，不把一次性临时目录或结果另建成长期事实源：候选 commit，module/provider/migration/compatibility，隔离副本来源，migration 与 Host Release build 命令及 exit code，两次 finalize 的输出和 manifest SHA-256，check 的各 provider 汇总、bundle 数与 exit code，Git HEAD/index/status/tracked-content 摘要，manifest/Release 资产摘要，以及只读数据库 observer 的连接来源、数据库数、日志前沿摘要、history 表数/行数/内容摘要。功能 child 只运行这组定向证据；全部 child 与审查修复完成后，父 Issue 才在冻结候选上运行一次下文的仓库完整验证。

## 诊断与审查证据

诊断、范围差异审查和完整双轴审查均区分已观察现象、待验证假设与已确认根因。阻塞 finding 按任务约定的级别判定，并给出具体触发条件、当前消费路径及稳定证据（最小复现或完整的源码调用链）。代理间结论一致不能替代证据；未验证的假设先取证，再决定是否需要产品修复。

涉及第三方组件保护或框架行为时，核对已安装版本的实现和实际调用方式；调用链仍有不确定性时，用真实消费者最小回归验证。反证成立则撤销原 finding，保留纠正依据。

Gate 超时先由资产 owner 检查现有截图、DOM／可访问名称、失败请求及脱敏响应，区分产品行为、定位／夹具错误与环境故障，再交对应 owner 修复。超时本身不能证明目标控件未出现；重跑前完成原因对应的最低层检查。

## 父级交付

### Acceptance Gate-only 所有权与首次集成

父 Issue 需要新增或改变某个 Acceptance Gate 时，child 划分必须指定恰好一个验收资产所有者，并写清所拥有的用户旅程。没有适用 Gate 时跳过，不为遵循流程新增 Gate。

资产 owner 随首个真实功能 child 提前参与，将第一版可运行 runner、fixture 与必要的低层测试接入该切片。先贯通真实 authoring、runtime 页面挂载和一次写入成功，再扩展同类资源；使用真实业务模块。既有 Gate 随功能逐步扩展，最终验收 child 负责完整旅程收口，不能把首次集成留到最后。实施继续遵守原生依赖和本次任务的顺序约定。

浏览器 Gate 只证明用户路径可走通，数据语义在低层验证。首次运行前复用现有失败诊断，确保能定位当前路由、页面错误、截图、DOM／可访问名称、失败请求及脱敏响应正文；仍缺失的诊断由资产 owner 在其范围内补齐。失败按上文“诊断与审查证据”处理。

定向测试通过并形成精确提交后，在候选不再编辑的条件下，由同一资产 owner 串行执行：

```powershell
.\scripts\accept.ps1 -Gate <Gate>
```

首个切片通过后再扩展同类资源；完整约定旅程 Gate-only 通过并记录提交、命令、结果与耗时后，验收资产 child 才能关闭。其他 child 只有在改动命中该用户旅程时才由同一 owner 复跑该 Gate；未命中时只跑自己的最低层定向测试。失败后先定位原因、完成修复和定向验证，再对新候选复跑；不能通过放宽断言掩盖产品缺陷。进入父级收尾后，按下一节由完整 verify 承担最终 Gate。

### 父级收尾

采用父子 Issue 交付时，完整审查和仓库完整验证在父级执行；以下双轴审查步骤在任务已要求完整双轴审查时适用：

1. 全部 child 完成后，以执行前 baseline 为 fixed point、当前已提交候选为终点，按任务要求执行一次 Standards + Spec 双轴审查。
2. 由原 owner 修复本次引入且有稳定证据的阻塞 finding，并运行对应最低层验证；阻塞级别以本次任务约定为准。
3. 先聚焦复核原 finding、修复差异和剩余 Spec 遗漏，尤其核对修复是否引入未经规格允许的权限、容量或行为限制。发现阻塞项则回到上一步，不重新开展完整双轴审查。
4. 阻塞项清零且聚焦复核通过后，冻结最终已提交候选，串行运行一次仓库完整验证，包含父 Issue 要求的全部 Gate。Gate 资产仍由原 owner 负责；完整 verify 前不固定加跑同一候选的 Gate-only：

```powershell
.\scripts\verify.ps1 -AcceptanceGate <Gate>
```

没有适用 Gate 时运行 `scripts/verify.ps1`。验证失败后先读取原始诊断，修复对应问题并执行最低层验证；候选有改动时完成聚焦复核。需要隔离运行时问题或确认修复时，可由原 owner 单独运行受影响的 Gate-only。最终仍须在稳定候选上完成一次包含全部要求 Gate 的完整 verify，早期或排错用的 Gate-only 结果不替代最终验收。只有候选代码变化，或前次运行因已确认且已排除的环境或基础设施问题而无效时才重新运行完整 verify。通过后核对候选归属，按授权回写证据和关闭父 Issue。

交付提示词引用本页，任务中只补充范围、baseline、适用 Gate、owner 和授权差异。后续两至三次交付在父 Issue 收尾评论中简记验证总耗时、同一候选重复运行相同 Gate 的次数及原因、child 关闭后返修次数、最终验收才首次发现的缺陷数（注明其中可在低层发现的数量）。Gate 重跑原因区分产品缺陷、验收资产错误和环境问题；关闭后返修区分产品、测试资产和纯维护修正，并注明按缺陷还是纠正提交计数。

后续任务发现交付遗漏时，在该任务现有记录中关联原交付并区分原需求缺陷与新增设计偏好；复盘时将前者计入“交付后用户发现的缺陷”，不由最终 Gate 的零缺陷结果覆盖，也不因此自动获得原 Issue 的写入授权。耗时与运行次数复用现有命令结果，不另建长期流水文档。

## 全量验证

标准入口依次执行静态门禁、一次 Release 构建、一次 `migration-manifest check --all --no-build` preflight、`--no-build` 测试、前端 lint、前端测试与构建。preflight 复用本轮 Release 资产；失败会保留 CLI 原始诊断并立即截断后续测试与 Acceptance：

```powershell
.\scripts\verify.ps1
```

`-Quick` 跳过 diff-check、独立 dotnet restore 和前端 lint/build，适用于开发循环，不能替代交付前的完整验证。完整模式的 lint 失败会保留 ESLint 文件、行号及规则名，并停止后续阶段。

结果写入 `.codex-temp/verify/runs/<run>/result.json`，`current-result.json` 指向最近一次结果。清单包含总耗时、各阶段耗时和命令；失败时读取 `failureStage`、同名阶段日志，以及 `msbuildDiagnosticPath` 下可能存在的 MSBuild 子节点诊断。

**verify.ps1 不要求也不检查干净工作树。** 本机是唯一的验证位置，脏树跑验证是常态；拦住它只会让 verify 变成提交前才能用的东西。归属靠记录而不靠设卡：`result.json` 的 `candidate` 段记录 `headCommit`、起跑时的 `dirty` 与 `dirtyEntryCount`，以及 `treeChangedDuringRun` 和 `headChangedDuringRun`。要一个可归属到某个提交的结论，就自己先提交再跑——此时 `dirty` 为 `false`，这份结果才真正属于那个 commit。两个 `*ChangedDuringRun` 任一为 `true`，都说明起跑与收尾时的候选不同，结论对应不到任何一份确定的代码。`status` 为 `running` 时，两者为 `null` 只表示尚未取得收尾端点，此时 `snapshotDiagnostic` 可以仍为 `null`；最终 `passed` 或 `failed` 结果中，两者为 `null` 才表示探针无法比较两端，应读取非空的 `snapshotDiagnostic`，不能当成 `false`。工作树探针不可用时，`dirty` 与 `dirtyEntryCount` 也为 `null`；HEAD 不可用时 `headCommit` 为 `null`，不会以 `false`、`0` 或哨兵字符串伪造已知状态。所有探针成功时 `snapshotDiagnostic` 为 `null`。HEAD 要单独看，因为跑到一半提交时已跟踪改动会同时从工作树消失，只比工作树察觉不到。该记录比较的是起跑与收尾两个端点，不能证明中途改变后又恢复的情况从未发生。

需要 Acceptance 时必须显式选择已要求的 Gate。闸门共享 `bin/Release`、`.codex-temp/dev/state.json` 与本机端口，必须串行，且一轮在跑时不要编辑文件：

```powershell
.\scripts\verify.ps1 -AcceptanceGate IdentityFoundation,Personnel
```

`accept.ps1` 除了启用环境开关和选择测试，还读取本次运行的原生 TRX，确认每个所选 Gate 恰好有一个通过结果。零匹配、跳过、缺失或不可读的结果都失败；`-Gate All` 要求三个 Gate 全部通过。测试自身失败时保留 `dotnet test` 原始输出和退出码。TRX 是临时执行确认，检查后删除，不另建裁决或证据协议。

### 环境集成测试的选择与报告

全量 verify 通过不等于所有 SQL Server、宿主重启和浏览器测试都已执行。`EnvironmentGatedFactAttribute` 在相应 `NEWP_RUN_*` 开关未设为 `1` 时将测试标为 skipped，这是开发循环的正常行为。不要为消除 skipped 无差别开启所有环境测试。

开始实施时，按改动与验收条件选择需要真实数据库、进程或浏览器的测试，记录项目、过滤器、环境开关和环境前提；开关以对应测试属性为准。父级收尾继承并核对这些要求。Gate 内部执行的集成测试只覆盖它明确调用的项目和过滤器：例如 IdentityFoundation 的 SQL Server smoke 不替代 HostApi 或其他项目的环境测试。

完成报告按所选范围列出实际命令与结果，区分：

- **已执行**：实际匹配的测试数、通过/失败/跳过数；失败保留原始诊断。
- **已跳过**：未启用的环境测试及原因；本次要求的测试被跳过或零匹配时，交付验证未完成，写明补跑命令和前提。
- **不适用**：未涉及的环境测试范围及简短理由，不把它们计为通过。

记录复用任务或 Issue 的现有验证说明；不额外建立清单文件。普通 verify 的绿色结果不能覆盖缺失的适用证据。

## 验收资产开发

闸门 runner 一律继承 `AcceptanceRunner`。收尾由它统一持有：无论场景成败都先归还一次性资源（用 `CancellationToken.None`，取消时更需要归还）、再反向扫描产物里的凭据。**这两步各自兜住异常**——扫描要读遍本轮全部产物，撞上被占用或已消失的文件就会抛，裸 `await` 会让这种基础设施异常顶掉更具体的场景失败。最后按「场景失败 → 查到凭据泄漏 → 扫描失败 → 清理抛异常 → 清理报未完成」由具体到泛报出去。子类只写两件事：场景序列和自己的资源归还方式；凭据一律经 `RegisterSecret` / `AddConnectionSecrets` 登记，`RunScenarioAsync` 负责场景归属，两者都不留绕过的口子。

常规闸门允许未提交的开发候选，不要求干净工作树；运行期间仍须保持候选不变并串行占用共享资源。`AcceptanceRepositoryGuard` 只用于专用脚本触发的 production cold-start，其性能数字必须归属到 commit；普通 verify 的候选记录解释见上文“全量验证”。

## 运行时 smoke

```powershell
.\scripts\dev.ps1
Invoke-WebRequest http://127.0.0.1:5000/api/health
.\scripts\stop-dev.ps1
```

开发服务的成功事实以 `.codex-temp/dev/runs/<run>/result.json` 为准；停止操作继续使用仓库脚本，不按进程名终止通用 `dotnet` 或 `node` 进程。
