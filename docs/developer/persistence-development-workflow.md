# 持久化开发流程

当前持久化主链路是：候选 Canonical Module Plan → Compiled Persistence Plan → 共享运行时模型与 provider migration streams → Host 启动期完整 EF Up Migration。Host 是唯一受支持的迁移执行入口；SQL Server 的 `platform.foundation` 只承载当前尚无模块 bundle 的 Authorization 与 ConfigCenter，新增模块必须拥有自己的 migration stream。SQL Server 只支持空 catalog 或由当前计划创建的有效 history 前缀，旧库必须删除重建。

开始前先阅读 [Platform Persistence Context](../contexts/platform-persistence/CONTEXT.md)、[ADR-0027](../adr/0027-host-is-the-only-full-up-migration-entry.md)、[ADR-0028](../adr/0028-sqlserver-starts-from-an-empty-database-baseline.md)、[ADR-0042](../adr/0042-development-uses-a-resettable-migration-baseline.md) 和 [ADR-0043](../adr/0043-required-fields-use-explicit-write-and-migration-lifecycles.md)。

## 先判断迁移阶段

当前仓库处于实验基线期，尚未建立兼容基线冻结工件。Model、持久化不变量或安全语义变化时，若现有数据没有保留价值，优先把受影响模块的当前 SQL Server migration 基线、manifest、摘要、snapshot 与 frontier 作为一个候选整体更新，并显式重建为空 catalog。若任务明确要保留当前有效 history 前缀中的数据，且模块能提供语义正确的旧行处理方式，可以追加符合 ADR-0043 的前向 migration；这不为已经被替换的旧基线建立升级兼容，也不允许公共 table-replacement seam。

兼容基线一旦通过独立设计工件明确冻结，已发布 migration 就不再允许重写，只能增加前向步骤。不得根据 `Development`、`Acceptance` 或 `Production` 环境名、分支名或数据库是否已有数据自行推断阶段；不确定是否已经冻结时停止修改 migration，并先核对当前设计工件。

## 新增或修改模块模型

1. 在已确认设计中写清模型 owner、物理表、provider 范围、迁移前沿、失败信号和回滚思路。
2. 修改 `modules/<ModuleId>/metadata/models/*.model.json`；模块 metadata 是模型 authoring 真源。
3. 运行模型校验和 DTO 同步，不手改 `DTOs/Generated/*.g.cs`。
4. 在模块自有 migration 项目中维护线性迁移流，例如 `server/modules/<ModuleId>/Migrations.SqlServer/`；实验基线期整体更新当前基线，兼容基线冻结后只追加前向步骤。
5. 让 migration manifest、target model digest 与模型投影一致，并通过候选计划编译。
6. 通过 Host 启动链路应用 migration；不要运行独立 schema apply 工具、`dotnet ef database update` 或手工 SQL 代替正式入口。
7. 补充模块、持久化和 Host readiness 的定向测试。

新模块首个 migration 使用 `migration-manifest finalize` 从空 authoring 前缀生成首节点，摘要仍由工具计算。工具只有在有效 HEAD 尚未发布整个模块 authoring 目录时才承认新 stream；已发布模块缺少 manifest、非 Git checkout 缺少冻结 Host 前缀仍拒绝。运行时不能消费空 migration 链。完成后使用只读 `check --all` 校验当前产物，不手填摘要或改写已发布前缀。

首个公开外部插件 profile 只支持 SQL Server。Oracle 或 MySQL 的新增迁移必须有单独设计与真实 provider 验证，不能因为仓库存在对应 provider 项目就机械生成三份迁移。

## Required 字段生命周期

先分别确认字段的存储、创建、更新与所有权语义，不要只写“required”：

| 维度 | 规则 |
| --- | --- |
| 存储必填 | 成功持久化后的值不得为空 |
| 创建输入必填 | 只适用于由调用方提供的字段；服务端拥有字段不要求客户端提交 |
| 普通更新 | 保持 PATCH 语义；可以省略并保留旧值，显式空值必须拒绝，合并后仍满足存储不变量 |
| 服务端必填 | 由可信平台或模块 owner 在持久化前赋值，客户端不得覆盖 |
| 创建默认值 | 只服务新记录的表单或应用写入，不处理旧行 |
| 一次性迁移回填 | 只修复已有缺失值，不影响后续新记录 |
| 数据库永久 `DEFAULT` | 当前不支持，不进入 Model、Provider Target、migration 或实际 schema |

新增或收紧存储必填字段时按数据状态选择：

| 场景 | 迁移方式 |
| --- | --- |
| 新表或目标空表新增 required 字段 | 直接创建非空列 |
| 有既有行的表新增 required 字段 | migration 1 新增 nullable 列 → migration 2 回填缺失旧值 → migration 3 验证无空值并收紧 required |
| nullable 字段改为 required，且已经无空值 | 验证后直接收紧 required |
| nullable 字段改为 required，且仍有空值 | 先回填缺失值，再由后续 migration 验证并收紧 required |
| 没有语义正确的回填值 | 保持 nullable，或在实验基线期确认数据可丢弃后重建 catalog |

三个阶段使用独立的线性 migration step。回填只修改当前 owner 拥有的目标和缺失值，必须显式、确定、可审查并参与 migration 事务；它不能跨 owner、不能依赖人工修改数据库才能完成，也不能留下数据库永久 `DEFAULT`。不要求新增独立命令或 CLI；迁移可以显式指定回填值或受限的确定性逻辑，测试可以手动准备旧行和指定测试值，但最终 Gate 必须自动重放完整迁移链。

当前已有界实现支持从 Model 投影存储可空性、表单 create/edit required 与创建默认值到 Standard CRUD 规范字段策略，并允许 SQL Server 模块 migration 使用受 owner、目标列、迁移阶段和 CLR 类型约束的标量 `NULL`-only 回填操作；ExampleRecord 是当前纵向采用者。旧 `RequiredFields` 只保留稳定错误展示覆盖，不能再作为 Required 语义真源。该能力不接受任意 SQL、回调、跨 owner 更新或数据库永久 `DEFAULT`；非 SQL Server provider、复杂确定性逻辑以及其他既有 descriptor 的采用仍须作为后续显式范围处理。

## 迁移规则

- 所有环境通过同一条 Host 启动链路执行待应用的完整 EF `Up`，包括 Drop、Rename 和 Alter；当前不按开发/发布环境或风险等级裁剪操作。
- 实验基线替换只改变仓库当前 authoring 候选与开发 catalog 生命周期，不放松 Host 执行校验，也不允许 Host 根据环境名或失败码自动删库。
- Migration 可以定义 `Down`，但平台不自动执行 `Down`。已提交迁移的恢复使用修正后的前向 migration 或已验证的数据库恢复流程。
- 完整 Up 不放松边界：模块归属、provider、全局唯一 Migration ID、线性迁移链、bundle/content/model/from-to digest、期望 frontier、事务和迁移锁都必须通过校验。
- EF design-time/provider model 投影为长期只读 `Provider Target v1`；每个 migration 形成 `Provider Transition v1`。Create/Add/Rename/Alter 及键、外键、索引必须逐 facet 与 canonical `from`/`to` 匹配，不能只按操作类型放行。
- Drop table/column/key/FK/index 无法从 EF operation 本身恢复完整旧 facet，新增迁移必须使用 `ProviderTransitionDescriptorCodec` 生成版本化 `NewP:ProviderTransition:FromDescriptor/v1` annotation；descriptor 的 owner、对象名、结构和列 ordinal 都会在连接前校验。不要手写 descriptor JSON。
- 所有模块迁移流与 Platform Foundation Stream 共用统一 EF Migration History；模块不得维护私有 history 或从 Host 外回写迁移事实。
- 每个 migration 会在同一事务内同时写入 `dbo.__EFMigrationsHistory` 与 `dbo.__NewPMigrationHistory`。后者保存 `unified-history/v1` 的 owner、step-scoped bundle membership、content、model/from-to、全局前缀及 Provider Target/Transition 事实；任何未知版本、缺失事实、非有效前缀或事实不匹配都会在新增 DDL 前阻断 Host readiness。SQL Server 不兼容旧 history，也不执行 fact backfill。
- manifest 的 `bundleChecksum` 是当前整份 assembly 的完整性校验，会在追加 migration 或实验基线整体替换后变化；history 的 `BundleDigest` 是从候选内不可变 migration step 链输入计算的稳定成员事实。兼容基线冻结后不得复用、删除或改写旧 step；实验基线替换后的旧 history 不做摘要兼容，必须要求重建 catalog。
- 全局唯一 Migration ID 同时决定跨模块的应用顺序：统一 history 按 ID 字典序排列，前缀校验是逐位置比对。实验基线整体替换时，如果新 Migration ID 的时间戳前缀晚于其他模块已有 migration，该模块在全局序列中的位置会后移，插到那些 migration 之后。任何部分应用的数据库或按历史前缀构造的验收 fixture 都会因此不再是有效前缀，Host 以 `PERSISTENCE_MIGRATION_HISTORY_NON_PREFIX` 拒绝接管。替换基线时要么保持新 ID 的原有排序位置，要么同步更新依赖历史前缀的 fixture，并用对应 Acceptance Gate 取证。
- history 中的 Provider Target/Transition fact 使用 #43 v1 canonical 算法的 owner-scoped 投影：保留 owner 表完整结构及 FK principal 物理引用，排除其他 owner 的表。它只服务历史前缀稳定性；执行端仍必须用 full Provider Target/Transition 对当前候选逐 operation 精确校验。
- 每个 migration 的 DDL 与 history 写入共用事务；不能参与事务或无法证明模块归属的操作必须在执行前失败。
- `form.create.defaultValue`、服务端赋值、一次性 migration backfill 与数据库永久 `DEFAULT` 是四个不同概念。当前不支持数据库永久 `DEFAULT`；前三者不得投影或遗留为数据库默认约束。
- Required 字段回填只服务当前有效 history 前缀的显式前向演进，不能用来跨越已被替换的实验基线。若旧数据没有保留价值，应更新当前空库基线并显式重建开发 catalog；若选择保留数据，则必须执行上节的有序迁移而不是放松数据前置条件或自动降级 nullability。
- 已冻结 migration 若历史上只携带 CLR 默认值，执行端仅在目标明确无 DEFAULT、值确为该 CLR 类型默认值且新增列 required 时通用消除该参数，并仍执行“表必须为空”前置检查；不得改写旧 migration/content digest 或添加模块特例。
- 数据库超前、history 不是有效迁移链前缀或摘要不一致时，Host 保持 Not Ready，不运行旧快照、自动回退或改写 history。
- migration 成功后仍要等待整个插件候选原子激活，不能单独写入活动状态。
- Host 外结构同步 CLI 和直接 SQL apply 路径已经移除；Host 仅保留所有环境一致的只读 schema/history/digest/target 检查。
- 首个需要保留数据与 history 的受支持数据库进入范围前，必须先通过独立设计确认兼容基线冻结，并写清数据保留、备份恢复、受控 backfill、发布授权和失败处理；ADR-0043 只确定 Required 字段生命周期，不替代该发布设计。不得仅依据环境名隐式改变迁移语义。

## 本地连接

开发与定向测试可以直接使用已经配置的本地 SQL Server 连接：

1. `NEWP_DEV_SQLSERVER_CONNECTION_STRING`
2. `apps/host-api/appsettings.Development.json` 的 `ConnectionStrings:SqlServer`

普通自动化测试及 Personnel / Authorization 常规 Acceptance 默认独占复用 `NewP_Test`；只有需要独立 catalog / migration history / 数据库级状态的场景才创建并清理一次性库。连接选择、schema 重置边界和例外见[本地开发环境](../guides/local-environment.md)。连接值不得进入命令参数、日志或提交内容。

开发 catalog 只能在仍匹配当前实验基线时复用。基线整体替换或 Host 返回 `PERSISTENCE_DATABASE_REBUILD_REQUIRED` 时，先停止当前 Host，核对目标 catalog 的 ownership marker、repository/worktree identity、数据可丢弃性和保护状态，再显式删除并新建空 catalog；Host 重新启动后负责创建 schema。不得用 Required 字段回填绕过基线不兼容，也不得把 Acceptance、验收证据、显式保护或来源不明的数据库作为重建目标。

## 验证

```powershell
# 同步受管 DTO
dotnet run --project .\server\tools\model-dto-sync\ModelDtoSync.csproj -- .
dotnet run --project .\server\tools\model-dto-sync\ModelDtoSync.csproj -- --check .

# 定向测试后执行仓库冻结验证
.\scripts\verify.ps1

# 真实 SQL Server disposable catalog smoke（连接值由环境或本地配置解析，不进入命令行）
.\tests\integration\persistence\Run-SqlServerSmoke.ps1
```

涉及真实 DDL 时，还必须从空 catalog、当前计划创建的有效 history 前缀、包含 Drop/Rename/Alter 的完整 Up、并发迁移锁、失败重试、history/frontier/digest 不一致、Host restart/readiness 和只读一致性检查八个入口验证；不能用生成了 migration 文件代替数据库证据。Required 字段迁移还要准备包含旧行和空值的可复跑 fixture，证明回填只修改目标缺失值、失败后可重试、收紧后无空值，并确认实际 schema 没有数据库永久 `DEFAULT`。
