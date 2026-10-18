# 快速验证

本页区分开发循环、全量验证和运行时 smoke，避免每张票重复执行全部门禁。

仓库没有 CI，**本机是唯一的验证位置**。按实际影响或明确交付约定选择定向检查及全仓 lint/build/unit verify；验收 Gate 表示一个具体有界场景，不表示模块功能全集，父级收尾也不追加整模块总验收。

## 执行层级

实施开始时确定交付层级，并按改动与验收条件选择下文“开发循环”的适用检查；模块已有 Gate、任务名或父子层级本身不扩大验证范围；owner 延续与并行调度按[任务跟踪器约定](../agents/issue-tracker.md#执行前沿与中断恢复)，层级不决定代理数量。调用 `$implement`、`$implement-spec` 时，按这里的分工执行验证与审查；父子任务中的技能收尾计入同一次父级收尾，不重复执行。

- **单任务 / 单 Issue**：按验收条件执行最小可复跑验证和范围差异审查；仓库验证和完整审查仅按实际影响或明确交付约定选择。已有规格直接复用，不为进入流程补建 Spec 或 child。
- **父级下的 child**：完成本 child 的验收条件、范围差异审查和适用定向验证；低层无法证明的集成行为选择对应有界验收场景。其有效证据直接进入父级汇总，不为每个 child 重复仓库检查或已有场景。
- **父级收尾**：汇总 child 与返修的覆盖、候选归属及仍有效的证据；只对真实跨 child 集成缺口补最小场景。按任务约定完成审查，仓库验证按实际影响或明确交付约定选择，不以父级身份触发完整验收。

提交和 GitHub 写入沿用 `AGENTS.md` 与本次任务授权；调用通用实施技能不自动增加提交授权。下文要求“已提交候选”的步骤以已有提交授权为前提；未授权提交时改用稳定的工作区候选，审查本轮任务差异（含已有任务提交与未提交改动）并记录实际状态，不把验证结果宣称为某个干净 commit 的证据。production cold-start 仍保留其专用提交归属要求。

## 开发循环

运行当前变更涉及的项目或过滤测试；模型元数据变化时先生成 DTO，再用 `--check` 确认无漂移：

```powershell
dotnet run --project .\server\tools\model-dto-sync\ModelDtoSync.csproj --no-restore -- .
dotnet run --project .\server\tools\model-dto-sync\ModelDtoSync.csproj --no-restore -- --check .
dotnet test .\tests\<Module>.Tests\<Module>.Tests.csproj --filter <Filter>
```

### 定向验证选择

单任务、child 和审查返修先从已确认验收条件选取最可能推翻方案的关键反例，说明触发条件、真实入口及预期状态；按本次变化选择旧输入兼容、权限变化、历史引用或异步交互等相关边界，不逐类打卡。有缺陷时先在最低层复现，再修复并确认失败后的状态。平台能力、公共契约、持久化、安全边界及难以回退的变化，须在扩大实施前用真实业务模块贯通 authoring 输入到 runtime consumer，不新建样例模块。

首片完成以真实入口在成功及选定反例下符合规格为准；编译、注册或 transport 模拟通过只证明其自身边界。用于证明接线的测试从真实路由或正式宿主进入，保留实际 Provider／实例生命周期，用真实控件事件验证输入、请求及状态收敛；不能在 fixture 手工补上被测入口本应提供的上下文，或仅直接调用回调。涉及装配或发行边界时使用对应正式产物与宿主，避免完整测试 Host 预载依赖掩盖缺口。已有有效证据可复用，按本次行为选择最低层检查：

- 定向命令沿用正式验证入口的配置与严格参数，只缩小文件、项目或测试范围。前端使用所属包的脚本和配置；lint 的 unused-disable、warning 等限制与完整验证一致，实际参数以当前脚本为准。
- 字段与规则变更验证值和边界；权限、查询与事务变更使用对应真实 API／SQL 集成测试；组件交互变更使用真实组件行为测试。低层无法证明的页面接线只做最短受影响路径的真实页面冒烟。冷启动、首次建库、迁移、恢复、管理员初始化及权限边界保留所需真实验收，分别按变化选择，不捆绑到 WorkspaceSmoke 或其他页面冒烟。
- 共享组件、公共服务、SDK、元数据、公共注册或生命周期变化，先定位受影响的既有消费者及其 fixture、注册断言和静态门禁，选择对应的定向检查。跨模块引用或被某个 Gate 消费，本身不触发全包、其他验收场景或全仓验证；新增测试通过不能替代既有消费者回归。
- **模块元数据远端消费者测试**：读取真实模块的 authoring 元数据、动作目录、注册或本地化贡献的测试散落在其他包，模块自己的定向测试覆盖不到，完整 verify 又首败即停，只能一次暴露一个。改动模块的动作（`dataActions`）、`metadata/` 下的 model／view／dic、`plugin.json`／`newp.plugin.json`、依赖或组合成员、迁移 manifest 声明，或前端的页面动作、行动作与本地化键后，各运行一次以下命令，再决定是否需要全仓 verify：
  - 后端：`dotnet test .\ICP-Platform.sln --filter "Category=ModuleMetadataConsumer"`，约 25 秒（含增量构建），覆盖 Kernel、Persistence、HostApi、Authorization 中读取真实模块的测试类。新增这类测试类时标 `[Trait("Category", "ModuleMetadataConsumer")]`。
  - 前端：`pnpm --dir frontend --filter @icp-platform/shell test:module-consumers`，约 30 秒。入口自动选取 `packages/shell/src` 下 import 了 `test-support/componentTestInputs` 的测试，新增消费者无需登记。
  - 前端根脚本 `pnpm --dir frontend test` 还会运行 `test:effect-network-lifecycle` 与 `check:effect-network-lifecycle`，按包定向测试不执行这两项。改动涉及前端网络请求、effect 或订阅生命周期时，另行运行 `pnpm --dir frontend run test:effect-network-lifecycle` 与 `pnpm --dir frontend run check:effect-network-lifecycle`。
  - 新增这类测试时，对模块贡献的集合（动作、键、数量）从真实计划或 authoring 文件推导期望，不写死精确列表；确需锁定的顺序或互补语义，用合成模块验证。
- 扩大验证前，由执行者（父子任务由协调者）在现有任务记录中简述：本次改变的行为、已有证据为何不足、较小范围检查还不能证明什么，以及所选范围如何补足。选择依据是具体证据缺口或任务明确要求的交付节点，不以“保险起见”替代判断，也不新增审批或清单文件。
- 新增或修改涉及真实组件懒加载、异步清理或共享状态的测试时，定向通过后运行一次所属包测试，核对全包负载下的可靠性。复用现有真实模块预加载和可靠清理方式；保留行为断言、默认超时与全局并行配置。

验证通过后，按所覆盖的行为和依赖保留有效证据；新提交、父级收尾或其他位置的变化不自动使证据失效。仅在新差异、失败或新证据影响原结论时补跑相关检查，并记录影响依据；跨候选复用注明原候选及仍有效的理由，最终汇总如实区分复用证据和本轮实跑。

### 界面呈现核对

新增界面或改变呈现、语言行为时，由实现该页面的功能 owner 核对本次受影响的实际页面，与所选标准页面对照控件、密度、操作位置和可见文案，期间未变的呈现沿用有效证据。标准 CRUD 按影响选择独立页面、Tab 内嵌或 Drawer；宽窄屏、容器换行及多语言仅在相应呈现或语言行为变化且旧证据失效时按需核对，不默认执行形态、屏宽与语言的全排列。呈现归属遵循[代码生成规则](../developer/code-quality-standard.md)。

在现有任务证据中记录对照页面、已查看的形态及发现的问题，区分原需求缺陷与新增设计偏好。验收入口默认执行功能路径；需要现有呈现分支时，对本次选定的场景使用 `accept.ps1 -Scenario <精确方法名> -Presentation`；直接调用测试时仅在本进程设置 `NEWP_ACCEPTANCE_PRESENTATION=1`，结束后移除该开关。成功截图、附带的宽窄屏和语言切换按此开关采集，失败诊断与真正的语言行为测试始终保留。纯呈现核对或截图补采复用仍有效的页面和产物，不重放已通过的业务旅程；数据语义仍在低层验证，不新增呈现 Gate 或逐项间距断言。

### Child 完成证据

每个 child 按上文“定向验证选择”落实验收条件、真实入口与关键反例。跨 child 共同约束（如权限不扩大、关系与审计同事务）由首个涉及的切片证明，后续复用有效证据，验证各自接入与新增差异。受影响的既有消费者与测试资产明确 owner，资产随功能适配；先复用已有测试和证据，只补未覆盖行为。下表按改动选用，不作为固定检查清单。

| 本次改动 | 关闭前需要的定向证据 |
| --- | --- |
| Model、View、Form 元数据 | 真实 authoring 经正式编译器生成可用 schema；列表字段引用有效；create/edit payload 正确处理服务端字段。 |
| Lookup、权限或错误契约 | 通过实际端点及权限流水线验证最小所需权限、分页和当前值；核对错误响应的 Problem Details 字段及真实消费方的状态保持。 |
| Display | 通过正式 planner/descriptor 触发 resolver，并核对最终响应中的 canonical companion 与批量解析行为。 |
| 前端 | 所属 workspace 包的定向测试、类型检查及严格 lint 通过；核对测试根目录、目标文件和实际检查范围，并按上文选择全包测试及界面呈现核对。 |
| 模型或迁移输入 | 运行实际依赖这些输入的迁移与历史 fixture 定向检查。历史 fixture 使用完整冻结的历史 authoring 输入，经正式投影验证固定 digest，与当前模型演进隔离。 |
| 层级、批量或分页查询 | 以代表性数据验证数据库往返次数、应用读取规模及数据库侧分页；复杂查询按风险核对执行计划和耗时。固定查询次数与有界返回量不能单独证明数据库计算成本有界；业务容量限制必须来自已确认规格。 |

owner 一次交回验收条件与消费路径的覆盖关系、候选及实际命令／结果、已知失败与未覆盖项、共享资源释放状态和仍需跨 child 集成证明的具体缺口。协调者核对范围差异、原始结果与候选归属，仅对缺失、矛盾或受新差异影响的证据补查或复跑，不逐项重做 owner 已完成的命令与呈现核对。属于本 child 的验收条件、关键反例或受影响路径仍缺证据时保持开放，不能以通过数量、owner 结论或父级后续验证代替。结论与证据引用写入现有 Issue 评论，缺失验证注明原因及复跑命令，不另建交接清单或审查轮次。

提交与 Issue 写入沿用本次任务授权。后续返修继续由原 owner 负责，并在原 child 补充纠正提交、验证结果及被替代的证据，父 Issue 汇总最终候选。

### Migration manifest authoring 与只读检查

功能 migration child 在自己的真实模块范围内显式完成 `finalize` 与定向 `check`。`finalize` 是唯一允许写回 manifest 派生字段的 authoring 动作；普通 build、测试发现和 `check` 都不得代替或隐式触发它。先生成确定性的 Release migration DLL，再 finalize 新尾节点，随后刷新 Host Release 副本并执行只读检查：

`finalize` 会把普通仓库当前 `HEAD` 中已跟踪的 manifest 作为已发布前缀事实，逐字段拒绝历史节点改写。隔离根没有自己的 Git worktree 时，必须先保留冻结的 Host Release manifest 副本作为同一前缀事实；缺少该副本会失败，且只能在 finalize 完成后再刷新 Host 副本。

```powershell
dotnet build .\server\modules\<Group>\<Module>\Migrations.SqlServer\<Module>.SqlServerMigrations.csproj -c Release
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

child 的可复跑证据固定记录以下字段，不把一次性临时目录或结果另建成长期事实源：候选 commit，module/provider/migration/compatibility，隔离副本来源，migration 与 Host Release build 命令及 exit code，两次 finalize 的输出和 manifest SHA-256，check 的各 provider 汇总、bundle 数与 exit code，Git HEAD/index/status/tracked-content 摘要，manifest/Release 资产摘要，以及只读数据库 observer 的连接来源、数据库数、日志前沿摘要、history 表数/行数/内容摘要。功能 child 只运行这组定向证据；父 Issue 汇总其覆盖与候选归属，仅按真实集成缺口或明确交付约定补充验证。

## 诊断与审查证据

诊断、范围差异审查和完整双轴审查均区分已观察现象、待验证假设与已确认根因。阻塞 finding 按任务约定的级别判定，并给出具体触发条件、当前消费路径及稳定证据（最小复现或完整的源码调用链）。代理间结论一致不能替代证据；未验证的假设先取证，再决定是否需要产品修复。

涉及第三方组件保护或框架行为时，核对已安装版本的实现和实际调用方式；调用链仍有不确定性时，用真实消费者最小回归验证。反证成立则撤销原 finding，保留纠正依据。

验证失败后，等本轮进程明确退出，收集全部原始失败与阶段结果，注明是否到达本次目标行为；按实际消费路径归组为产品缺陷、验收资产／fixture 错误、环境问题或未确认原因。原 owner 在最低层复现、修复并回归，资产问题同时核对同一旅程的前置数据、后续操作及清理依赖。

升级／迁移旅程首次失败后，先对照现有 fixture、正式命令和冻结资产核清整条关联链：旧 Host／元数据／历史包隔离与 canonical 兼容，版本及依赖选择和组织初始化前置，当前安装与迁移，旧数据／审计保留与补齐，重启及清理。复用已证实环节，只为缺失或受影响的契约补最低层证据；后续失败更新这条链，不逐个修补当前报错后立即重跑整轮。此核对写入现有任务记录，不另建清单或预检 Gate。

恢复条件是已确认原因及其定向回归、受影响前后步骤、候选与所用产物均已对应，仍有效证据和剩余缺口已说明。原 owner 满足条件后可在已分配资源窗口恢复对应检查，无需为每个小修再确认；协调者按[执行前沿与中断恢复](../agents/issue-tracker.md#执行前沿与中断恢复)及时纠偏。同一异常复现或同旅程连续出现资产缺陷时，继续最低层关联核对，暂不恢复整轮；新提交不重置失败收敛或扩大验证范围。原因未确认时仅做能区分假设的定向诊断，保留原始失败和未复现事实；不以重复通过、延时、重试、吞异常或放宽断言宣称根因消除。

Gate 超时先由资产 owner 检查现有截图、DOM／可访问名称、失败请求及脱敏响应，再交对应 owner 处理；超时本身不能证明控件未出现。driver 与 fixture 按下文“首次集成”和“验收资产开发”验证真实条件，结构测试或编译通过不替代行为证据。

## 父级交付

### Acceptance 场景所有权与首次集成

父 Issue 需要新增或改变某个 Acceptance Gate 时，为该旅程明确一个验收资产 owner，可由功能 owner 兼任，不因此另拆 child 或新建代理。功能 owner 负责行为、低层证据和界面呈现核对，资产 owner 负责正式 driver、旅程、清理及 Gate 运行的截图与诊断；同一份证据由对应 owner 核验，其他参与者复用。没有适用 Gate 时跳过。

资产 owner 随首个真实功能切片复用正式 driver／runner、fixture，按“定向验证选择”先证明真实入口与控件交互，再以可独立运行的最短页面场景覆盖低层不能证明的接线。场景包含本次必需的前置、消费及清理，在对应 child 关闭前完成；只挂载新按钮或完成一次操作不足以证明整条受影响路径。可独立的准备与功能实施并行，共享文件或运行资源的操作串行。无关分支复用有效证据，不追加成长 E2E 链或模块全集；临时入口复用正式方法，不另写替代交互。

浏览器 Gate 只证明用户路径可走通，数据语义在低层验证。首次运行前复用现有失败诊断，确保能定位当前路由、页面错误、截图、DOM／可访问名称、失败请求及脱敏响应正文；仍缺失的诊断由资产 owner 在其范围内补齐。失败按上文“诊断与审查证据”处理。

按“定向验证选择”确认具体集成缺口及有界场景，形成稳定候选后，由同一资产 owner 串行执行；提交归属沿用“执行层级”的授权规则：

```powershell
.\scripts\accept.ps1 -Scenario <AcceptanceGateTests精确方法名>
```

验收资产 owner 按“Child 完成证据”交回实际覆盖、候选、命令、结果与耗时，未覆盖项明确到具体行为。父级复用有效场景证据，只对真实跨 child 集成缺口新增或补跑最小场景，不在收尾重放已通过的功能全集。

### 父级收尾

采用父子 Issue 交付时，父级核对覆盖、候选归属和剩余集成缺口；以下双轴审查步骤仅在任务已要求完整双轴审查时适用：

1. 全部 child 完成且变更已集成后，以执行前 baseline 为 fixed point、当前已提交候选为终点，由独立审查者按任务要求执行一次 Standards + Spec 双轴审查。
2. 由原 owner 修复本次引入且有稳定证据的阻塞 finding，并运行对应最低层验证；阻塞级别以本次任务约定为准。
3. 先聚焦复核原 finding、修复差异和剩余 Spec 遗漏，尤其核对修复是否引入未经规格允许的权限、容量或行为限制。发现阻塞项则回到上一步，不重新开展完整双轴审查。
4. 阻塞项清零且聚焦复核通过后，核对最终候选与各项有效证据的归属；只为尚未覆盖的真实跨 child 集成缺口选择最小验收场景。全仓 lint/build/unit verify 按明确交付约定或实际影响执行；需要组合执行时显式列出场景：

```powershell
.\scripts\verify.ps1 -AcceptanceScenario <AcceptanceGateTests精确方法名>
```

没有验收缺口时沿用有效场景证据，不因父级收尾新增验收运行。需要仓库检查时使用 `scripts/verify.ps1`，未传 `-AcceptanceScenario` 不运行验收。失败后按“诊断与审查证据”收敛，只恢复受影响检查。

已通过的场景证据可用于最终覆盖结论；核对候选与依赖未受后续差异影响并注明复用理由。仅在相关差异使原结论失效，或原运行被确认无效且原因已排除时补跑对应场景。汇总实际覆盖与未覆盖项后，按授权回写证据和关闭父 Issue。

交付提示词使用[任务跟踪器中的模板](../agents/issue-tracker.md#交付提示词)，只补本次范围与授权差异。后续两至三次交付在父 Issue 收尾评论中简记验证总耗时、同一问题跨候选重跑验收场景的次数及原因（其中尚未到达本次目标行为的次数）、child 关闭后返修次数、最终验收才首次发现的缺陷数（注明其中可在低层发现的数量）。Gate 重跑原因区分产品缺陷、验收资产／fixture 错误、环境问题和未确认原因；关闭后返修区分产品、测试资产和纯维护修正，并注明按缺陷还是纠正提交计数。

后续任务发现交付遗漏时，在该任务现有记录中关联原交付并区分原需求缺陷与新增设计偏好；复盘时将前者计入“交付后用户发现的缺陷”，不由验收场景的零缺陷结果覆盖，也不因此自动获得原 Issue 的写入授权。耗时与运行次数复用现有命令结果，不另建长期流水文档。

## 全量验证

标准入口依次执行静态门禁、一次 Release 构建、一次 `migration-manifest check --all --no-build` preflight、`--no-build` 测试、前端 lint、前端测试与构建。preflight 复用本轮 Release 资产；失败会保留 CLI 原始诊断并立即截断后续测试与 Acceptance：

```powershell
.\scripts\verify.ps1
```

`-Quick` 仍是全仓验证变体，只跳过 diff-check、独立 dotnet restore 和前端 lint/build，不是局部修改的定向入口；定向验证使用上文的项目、包或过滤测试。明确要求完整验证的交付节点使用完整模式。完整模式的 lint 失败会保留 ESLint 文件、行号及规则名，并停止后续阶段。

结果写入 `.codex-temp/verify/runs/<run>/result.json`，`current-result.json` 指向最近一次结果。清单包含总耗时、各阶段耗时和命令；失败时读取 `failureStage`、同名阶段日志，以及 `msbuildDiagnosticPath` 下可能存在的 MSBuild 子节点诊断。

**verify.ps1 不要求也不检查干净工作树。** 本机是唯一的验证位置，脏树跑验证是常态；拦住它只会让 verify 变成提交前才能用的东西。归属靠记录而不靠设卡：`result.json` 的 `candidate` 段记录 `headCommit`、起跑时的 `dirty` 与 `dirtyEntryCount`，以及 `treeChangedDuringRun` 和 `headChangedDuringRun`。要一个可归属到某个提交的结论，就自己先提交再跑——此时 `dirty` 为 `false`，这份结果才真正属于那个 commit。两个 `*ChangedDuringRun` 任一为 `true`，都说明起跑与收尾时的候选不同，结论对应不到任何一份确定的代码。`status` 为 `running` 时，两者为 `null` 只表示尚未取得收尾端点，此时 `snapshotDiagnostic` 可以仍为 `null`；最终 `passed` 或 `failed` 结果中，两者为 `null` 才表示探针无法比较两端，应读取非空的 `snapshotDiagnostic`，不能当成 `false`。工作树探针不可用时，`dirty` 与 `dirtyEntryCount` 也为 `null`；HEAD 不可用时 `headCommit` 为 `null`，不会以 `false`、`0` 或哨兵字符串伪造已知状态。所有探针成功时 `snapshotDiagnostic` 为 `null`。HEAD 要单独看，因为跑到一半提交时已跟踪改动会同时从工作树消失，只比工作树察觉不到。该记录比较的是起跑与收尾两个端点，不能证明中途改变后又恢复的情况从未发生。

需要 Acceptance 时按具体证据缺口显式选择 `AcceptanceGateTests` 中的精确方法名，可列出有限场景；没有模块、All 或通配聚合入口。场景共享 `bin/Release`、`.codex-temp/dev/state.json` 与本机端口，必须串行，且一轮在跑时不要编辑文件：

```powershell
.\scripts\verify.ps1 -AcceptanceScenario WorkspaceSmoke_UsesFixedRelease
# 只运行场景，不追加全仓检查
.\scripts\accept.ps1 -Scenario NativeWorkspaceFolderCopy_UsesFormalDriver
```

`accept.ps1 -Scenario` 设置对应环境开关并使用精确测试过滤器，还读取本次原生 TRX，确认每个所选场景恰好有一个通过结果。零匹配、跳过、缺失或不可读的结果都失败；测试失败保留 `dotnet test` 原始输出和退出码。TRX 是临时执行确认，检查后删除，不另建裁决协议。旧 `accept.ps1 -Gate`、`verify.ps1 -AcceptanceGate`、`All` 和通配接口已退役并明确拒绝；普通 verify 不隐含运行验收。

### 环境集成测试的选择与报告

全量 verify 通过不等于所有 SQL Server、宿主重启和浏览器测试都已执行。`EnvironmentGatedFactAttribute` 在相应 `NEWP_RUN_*` 开关未设为 `1` 时将测试标为 skipped，这是开发循环的正常行为。不要为消除 skipped 无差别开启所有环境测试。

开始实施时，按改动与验收条件选择需要真实数据库、进程或浏览器的测试，记录项目、过滤器、环境开关和环境前提；开关以对应测试属性为准。父级收尾继承并核对这些要求。Gate 内部执行的集成测试只覆盖它明确调用的项目和过滤器：例如 IdentityFoundation 的 SQL Server smoke 不替代 HostApi 或其他项目的环境测试。

已确认其他可变资源独立、显式启用并行租约的 SQL/API fixture 可复用两个测试库；同库仍排他，两个库都忙时等待，默认 fixture 与 Native / Acceptance 保持原串行边界。连接、清理和接入条件见[本地 SQL Server](../guides/local-environment.md#本地-sql-server)。比较串行与并行耗时时，使用相同测试集合和候选，单独记录总耗时；租约等待计入单个测试耗时，不能将其当作测试自身成本，也不为并行扩大验证范围。

**文档批量复制容量测量**使用独立开关 `NEWP_RUN_WORKSPACE_SQLSERVER_CAPACITY`，不随 `NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION` 开启，因此按 `WorkspaceClipboardSqlServerHttpApiTests` 类过滤运行普通剪贴板 SQL 测试时不会带上它（单次约 9 分钟）。它只测量 20/50 文档的真实文件流、SQL 往返、锁区间与取消回收成本，没有业务硬阈值，结果写入证据 JSON。只有改动影响文档批量复制的文件流、事务与锁粒度、`FileCopyLease` 准备或回收，或任务明确要求容量数据时才运行：

```powershell
$env:NEWP_RUN_WORKSPACE_SQLSERVER_CAPACITY = "1"
try {
    dotnet test .\tests\HostApi.Tests\HostApi.Tests.csproj --filter FullyQualifiedName~ObjectCopy_DocumentBatchMeasuresTwentyFiftyConcurrencyAndReclamation
}
finally {
    Remove-Item Env:\NEWP_RUN_WORKSPACE_SQLSERVER_CAPACITY -ErrorAction SilentlyContinue
}
```

可用 `NEWP_COPY_COST_ARTIFACT_ROOT` 指定证据输出目录。容量测量单独运行，避免其他 SQL 测试的资源竞争影响数据；其租约与清理沿用上述专用测试库规则。

完成报告按所选范围列出实际命令与结果，区分：

- **已执行**：实际匹配的测试数、通过/失败/跳过数；失败保留原始诊断。
- **已跳过**：未启用的环境测试及原因；本次要求的测试被跳过或零匹配时，交付验证未完成，写明补跑命令和前提。
- **不适用**：未涉及的环境测试范围及简短理由，不把它们计为通过。

记录复用任务或 Issue 的现有验证说明；不额外建立清单文件。普通 verify 的绿色结果不能覆盖缺失的适用证据。

## 验收资产开发

Workspace 场景使用 `accept.ps1 -Scenario <精确方法名>`；需要同时执行仓库检查时才使用 `verify.ps1 -AcceptanceScenario <精确方法名>`。按[固定发行物准备与登记](../developer/componentized-development.md#workspace-固定发行物验收输入)准备所选场景需要的产物，设置 `NEWP_ACCEPTANCE_WORKSPACE_DELIVERY_INPUT`。允许 dirty 候选，运行期间候选和目标保持不变；按输入与产物依赖复用未变资产，仅重建受影响资产并重新登记当前候选。父级收尾本身不触发全部产物重建或场景重跑。

新增或修改 fixture 时，每类先用一个符合正式策略的输入贯通准备、消费与清理，再扩展权限、并发或容量场景；输入对照正式成功用例的实际序列化，区分可写／必填／服务端字段，并用定向 API 核对返回契约。已有有效证据直接复用。当前迁移期望从正式计划派生，历史 fixture 仍冻结历史输入。阶段性等待从实际被测阶段开始计时，端到端超时仍覆盖启动与清理；观察器保持一致读取且避免干扰被测写入，涉及并发时用低成本真实并发测试证明。以上只覆盖本次改动影响的资产，不新增统一预检 Gate。

闸门 runner 一律继承 `AcceptanceRunner`。收尾由它统一持有：无论场景成败都先归还一次性资源（用 `CancellationToken.None`，取消时更需要归还）、再反向扫描产物里的凭据。**这两步各自兜住异常**——扫描要读遍本轮全部产物，撞上被占用或已消失的文件就会抛，裸 `await` 会让这种基础设施异常顶掉更具体的场景失败。最后按「场景失败 → 查到凭据泄漏 → 扫描失败 → 清理抛异常 → 清理报未完成」由具体到泛报出去。子类只写两件事：场景序列和自己的资源归还方式；凭据一律经 `RegisterSecret` / `AddConnectionSecrets` 登记，`RunScenarioAsync` 负责场景归属，两者都不留绕过的口子。

常规闸门允许未提交的开发候选，不要求干净工作树；运行期间仍须保持候选不变并串行占用共享资源。`AcceptanceRepositoryGuard` 只用于专用脚本触发的 production cold-start，其性能数字必须归属到 commit；普通 verify 的候选记录解释见上文“全量验证”。

## 运行时 smoke

```powershell
.\scripts\dev.ps1
Invoke-WebRequest http://127.0.0.1:5000/api/health
.\scripts\stop-dev.ps1
```

开发服务的成功事实以 `.codex-temp/dev/runs/<run>/result.json` 为准；停止操作继续使用仓库脚本，不按进程名终止通用 `dotnet` 或 `node` 进程。
