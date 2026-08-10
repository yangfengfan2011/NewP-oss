# Platform Persistence

Platform Persistence 上下文把候选 Canonical Module Plan 中由各模块拥有的模型与迁移贡献，编译成共享运行时模型和可确定执行的 provider-specific migration streams。它保障归属、顺序、历史、事务与失败语义，但不拥有模块业务模型，也不决定插件是否激活。

## Language

**归属模型（Owned Model）**：
由唯一模块声明并演进的逻辑模型。模型键、物理 schema/table 以及相关索引都必须能追溯到同一个 owner module；其他模块不得直接修改。
_Avoid_: 全局模型、Host 模型、按实体名推断归属

**共享运行时模型（Shared Runtime Model）**：
从同一份已编译持久化计划投影到共享 `EfPlatformDbContext` 的完整活动模型，用于 Standard CRUD、查询内核和现有 repository。它不是迁移真源，也不允许 Host 手工追加模块定义。
_Avoid_: 平台拥有的模型、模块 DbContext 集合、运行时扫描结果

**模块迁移流（Module Migration Stream）**：
一个模块针对一个 provider 拥有的、不可变且线性的 EF migrations 序列。序列中的每个 migration 都携带稳定位置、内容身份和前后模型摘要。
_Avoid_: 全局迁移列表、metadata diff SQL、任意启动脚本

**迁移包（Migration Bundle）**：
插件包内一个 provider 的标准 EF migrations assembly 及其可静态校验 manifest。它与模块模型使用同一个 target model digest，并受插件包完整性哈希保护。
_Avoid_: SQL 脚本目录、Host migrations assembly、运行时生成迁移

**已编译持久化计划（Compiled Persistence Plan）**：
由同一个候选 Canonical Module Plan 和目标 provider 确定生成的不可变值，包含带归属的模型、稳定排序的 migration streams、期望 frontier 与 plan digest。同一实例同时供运行时模型注册和迁移执行使用。
_Avoid_: DbContext 配置、插件扫描结果、迁移执行日志

**模型摘要（Model Digest）**：
模块规范化模型的内容身份，用于证明模型声明、migration target 与运行时投影一致。模型变化但没有对应 migration frontier 时，候选必须失败。
_Avoid_: 程序集版本、插件版本、数据库 schema 版本号

**迁移前沿（Migration Frontier）**：
某模块在一个候选快照中期望达到的最后 migration 身份。EF migration history 是实际执行事实，活动快照只记录其期望 frontier 和持久化计划摘要。
_Avoid_: 最新文件、最大时间戳、当前插件版本

**安全扩展迁移（Safe Expand Migration）**：
可由正常激活流程自动执行、并允许旧活动版本继续运行的向前 schema 变化，例如创建 schema/table/index，或增加 nullable/有安全默认值的列。
_Avoid_: Down、Rename、Drop、破坏性 Alter、任意 SQL

**数据库超前（Database Ahead）**：
某些已提交的安全扩展迁移超过当前活动快照的 frontier，通常源于后续 migration 失败。旧活动版本必须仍能在该 schema 上工作，重试从已提交前缀之后继续。
_Avoid_: 自动回滚、schema drift、激活成功

**Schema Drift**：
数据库实际结构或 migration history 与已验证的归属、链和期望 frontier 不一致。DevelopmentSchemaSync 只能报告 drift；正常 schema 写入仍必须通过 EF migrations。
_Avoid_: 待执行迁移、数据库超前、模型摘要变化

**Provider Adapter**：
在内部隐藏特定数据库的锁、EF migrator 配置、history reconciliation 和事务执行的实现 seam。首个外部插件门禁只支持 SQL Server；不支持的 provider 必须在 DDL 前失败。
_Avoid_: 插件接口、Host 分支、通用 SQL 生成器

**Legacy Baseline**：
现有统一 migrations assembly 和 `dbo` 映射形成的受保护历史起点。它可以通过显式、一次性的兼容适配接入新计划，但不得被静默重写或作为新外部模块的默认路径。
_Avoid_: 清空 history、重新生成初始迁移、永久兼容模式

**已验证旧基线切换（Verified Legacy Cutover）**：
内建模块在自己拥有的 migration manifest 中冻结一次性 legacy 摘要、首迁移和允许丢弃的旧物理表；平台只定义通用协议，并仅在 built-in provenance 完整匹配时把该事实编译为执行器不可由公共调用方伪造的决策。外部模块或普通 `CompiledPersistencePlan` 自报相同字段不能获得破坏性迁移权限。
_Avoid_: 平台内核硬编码模块私有表、外部插件自报 Drop 权限、仅凭 migration id 放行
