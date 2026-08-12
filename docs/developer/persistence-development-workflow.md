# 持久化开发流程

当前持久化主链路是：候选 Canonical Module Plan → Compiled Persistence Plan → 共享运行时模型与模块自有 provider migration streams → Host 启动期完整 EF Up Migration。Host 是唯一受支持的迁移执行入口；`PlatformPersistenceModelCatalog` 和三个全局 migration 项目只保留为 Legacy Baseline 兼容事实，不是新增模块的默认接入点。

开始前先阅读 [Platform Persistence Context](../contexts/platform-persistence/CONTEXT.md) 和 [ADR-0027](../adr/0027-host-is-the-only-full-up-migration-entry.md)。

## 新增或修改模块模型

1. 在已确认设计中写清模型 owner、物理表、provider 范围、迁移前沿、失败信号和回滚思路。
2. 修改 `modules/<ModuleId>/metadata/models/*.model.json`；模块 metadata 是模型 authoring 真源。
3. 运行模型校验和 DTO 同步，不手改 `DTOs/Generated/*.g.cs`。
4. 在模块自有 migration 项目中增加不可变的线性迁移流，例如 `server/modules/<ModuleId>/Migrations.SqlServer/`。
5. 让 migration manifest、target model digest 与模型投影一致，并通过候选计划编译。
6. 通过 Host 启动链路应用 migration；不要运行独立 schema apply 工具、`dotnet ef database update` 或手工 SQL 代替正式入口。
7. 补充模块、持久化和 Host readiness 的定向测试。

首个公开外部插件 profile 只支持 SQL Server。Oracle 或 MySQL 的新增迁移必须有单独设计与真实 provider 验证，不能因为 Legacy Baseline 存在三个 provider 项目就机械生成三份迁移。

## 迁移规则

- 所有环境通过同一条 Host 启动链路执行待应用的完整 EF `Up`，包括 Drop、Rename 和 Alter；当前不按开发/发布环境或风险等级裁剪操作。
- Migration 可以定义 `Down`，但平台不自动执行 `Down`。已提交迁移的恢复使用修正后的前向 migration 或已验证的数据库恢复流程。
- 完整 Up 不放松边界：模块归属、provider、全局唯一 Migration ID、线性迁移链、bundle/content/model/from-to digest、期望 frontier、事务和迁移锁都必须通过校验。
- EF design-time/provider model 投影为长期只读 `Provider Target v1`；每个 migration 形成 `Provider Transition v1`。Create/Add/Rename/Alter 及键、外键、索引必须逐 facet 与 canonical `from`/`to` 匹配，不能只按操作类型放行。
- Drop table/column/key/FK/index 无法从 EF operation 本身恢复完整旧 facet，新增迁移必须使用 `ProviderTransitionDescriptorCodec` 生成版本化 `NewP:ProviderTransition:FromDescriptor/v1` annotation；descriptor 的 owner、对象名、结构和列 ordinal 都会在连接前校验。不要手写 descriptor JSON。
- 所有模块与 Legacy Baseline 共用统一 EF Migration History；模块不得维护私有 history 或从 Host 外回写迁移事实。
- 模块 migration 会在同一事务内同时写入 `dbo.__EFMigrationsHistory` 与 `dbo.__NewPMigrationHistory`。后者保存 `unified-history/v1` 的 owner、step-scoped bundle membership、content、model/from-to、全局前缀及 Provider Target/Transition 事实；任何未知版本、缺失事实、非有效前缀或事实不匹配都会在新增 DDL 前阻断 Host readiness。Legacy Baseline 只兼容已编译显式 frontier 内的既有 EF history，不执行 fact backfill。
- manifest 的 `bundleChecksum` 是当前整份 assembly 的完整性校验，会在追加 migration 后变化；history 的 `BundleDigest` 是从不可变 migration step 链输入计算的稳定成员事实。新增 migration 不得复用、删除或改写旧 step 来规避任一校验。
- history 中的 Provider Target/Transition fact 使用 #43 v1 canonical 算法的 owner-scoped 投影：保留 owner 表完整结构及 FK principal 物理引用，排除其他 owner 的表。它只服务历史前缀稳定性；执行端仍必须用 full Provider Target/Transition 对当前候选逐 operation 精确校验。
- 每个 migration 的 DDL 与 history 写入共用事务；不能参与事务或无法证明模块归属的操作必须在执行前失败。
- 业务默认值只属于 `form.create.defaultValue`，不得进入 Provider Target 或 migration 的数据库 `DEFAULT`。新增 nullable 列让旧行保留 `NULL`；新增 required 列要求表为空；nullable 收紧 required 要求存量值均非空。两类检查都在迁移锁内且在 DDL/history 前执行，禁止 migration 回填或自动降级 nullability。
- 已冻结 migration 若历史上只携带 CLR 默认值，执行端仅在目标明确无 DEFAULT、值确为该 CLR 类型默认值且新增列 required 时通用消除该参数，并仍执行“表必须为空”前置检查；不得改写旧 migration/content digest 或添加模块特例。
- 数据库超前、history 不是有效迁移链前缀或摘要不一致时，Host 保持 Not Ready，不运行旧快照、自动回退或改写 history。
- migration 成功后仍要等待整个插件候选原子激活，不能单独写入活动状态。
- `DevSchemaSync` CLI 和直接 SQL apply 路径不再是目标能力；Host 仅保留所有环境一致的只读 schema/history/digest 检查。
- 真实生产数据库进入受支持范围后，再通过独立设计增加显式安全模式；不得仅依据环境名隐式改变迁移语义。

## 本地连接

开发与定向测试可以直接使用已经配置的本地 SQL Server 连接：

1. `NEWP_DEV_SQLSERVER_CONNECTION_STRING`
2. `apps/host-api/appsettings.Development.json` 的 `ConnectionStrings:SqlServer`

Acceptance 会复用该服务器和登录身份，但创建本轮唯一的临时 catalog；连接值不得进入命令参数、日志或提交内容。

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

涉及真实 DDL 时，还必须从空 catalog、已有受支持基线、包含 Drop/Rename/Alter 的完整 Up、并发迁移锁、失败重试、history/frontier/digest 不一致、Host restart/readiness 和只读一致性检查八个入口验证；不能用生成了 migration 文件代替数据库证据。
