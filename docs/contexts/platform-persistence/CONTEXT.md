# Platform Persistence

Platform Persistence 上下文把候选 Canonical Module Plan 中由各模块拥有的模型与迁移贡献，编译成共享运行时模型和可确定执行的 provider-specific migration streams。它保障归属、顺序、统一历史、事务与失败语义，并只接受 Host 作为数据库迁移执行入口；它不拥有模块业务模型，也不决定插件是否激活。

迁移执行策略由 [ADR-0027：Host 是完整 Up Migration 的唯一执行入口](../../adr/0027-host-is-the-only-full-up-migration-entry.md) 约束；当前基线生命周期由 [ADR-0042：开发期使用可重建迁移基线直到兼容基线冻结](../../adr/0042-development-uses-a-resettable-migration-baseline.md) 约束；Required 字段演进由 [ADR-0043：Required 字段使用显式写入与迁移生命周期](../../adr/0043-required-fields-use-explicit-write-and-migration-lifecycles.md) 约束。

## Language

**归属模型（Owned Model）**：
由唯一模块声明并演进的逻辑模型。模型键、物理 schema/table 以及相关索引都必须能追溯到同一个 owner module；其他模块不得直接修改。
_Avoid_: 全局模型、Host 模型、按实体名推断归属

**共享运行时模型（Shared Runtime Model）**：
从同一份已编译持久化计划投影到共享 `EfPlatformDbContext` 的完整活动模型，用于 Standard CRUD、查询内核和现有 repository。它不是迁移真源，也不允许 Host 手工追加模块定义。
_Avoid_: 平台拥有的模型、模块 DbContext 集合、运行时扫描结果

**模块迁移流（Module Migration Stream）**：
一个模块针对一个 provider 拥有的线性 EF migrations 序列。它在已确认候选内不可变；实验基线期可以作为整条当前基线被替换，兼容基线冻结后其已发布步骤永久不可改写。
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

**Provider Target**：
从 EF design-time/provider model 投影的版本化、canonical、只读数据库目标，包含 owner、逻辑列顺序、provider type、nullability、长度/精度、数据库生成语义、provider annotation、键、外键、索引和 check constraint。它是数据库最终结构事实源，但不替代 model metadata 的 authoring 职责，也不依赖任何 Host 外结构写工具。
_Avoid_: 表单默认值、metadata diff target、运行中数据库快照

**逻辑列顺序（Logical Column Order）**：
Provider Target/Transition 中用于 canonical plan、契约投影和摘要的稳定字段顺序。它不声明数据库物理列位置，也不要求数据库重排已有列。
_Avoid_: 数据库 column ordinal、SQL Server column_id、物理存储顺序

**数据库物理列序（Database Physical Column Ordinal）**：
数据库 provider 为表列分配的物理位置；合法追加列可以使它不同于逻辑列顺序。它不是兼容性不变量，不能单独构成 Schema Drift。
_Avoid_: Provider Target 逻辑顺序、兼容性 ordinal、重排列目标

**Provider Transition**：
单个 migration 的版本化 canonical `from`/`to` Provider Target 及内容摘要。执行端从最终 Provider Target 逆推并逐项核对 operation；Drop 等本身不携带旧 facet 的操作必须携带版本化旧对象 descriptor，明确证明旧 owner、旧结构和逻辑列 ordinal。Provider Target/Transition 摘要独立演进，不重算或偷换 Model Digest v1 与既有 migration digest。
_Avoid_: 仅按操作类型放行、仅凭 migration id 推断旧对象、重算旧摘要

**迁移数据前置条件（Migration Data Precondition）**：
Host 在全局迁移锁内、当前 migration 操作与 history 前执行的只读数据检查。它只裁决当前 step 是否可以执行，不猜测回填值、不增加数据库永久 `DEFAULT`，也不自动改变目标 nullability。
_Avoid_: 自动修复、DDL 后补查、隐式 nullability 降级

**Required 字段迁移（Required Field Transition）**：
把新字段或现有 nullable 字段推进为持久化非空约束的有序前向迁移。已有空值时，它由“允许旧行存在的结构扩展、一次性回填、验证并收紧约束”三个独立历史步骤组成。
_Avoid_: 单步默认值兼容、表单必填、自动改列

**Required 字段回填（Required Field Backfill）**：
由模型 owner 为 Required 字段迁移显式提供、只修复既有缺失值的一次性数据动作。它不是数据导入、通用数据转换或新记录默认值，也不能保留为数据库永久 `DEFAULT`。
_Avoid_: 通用 UPDATE seam、启动回调、数据库默认约束

**数据库永久默认值（Persistent Database Default）**：
由数据库长期为后续写入补值的列约束。NewP 当前不支持该能力，创建默认、服务端赋值和迁移回填都不能投影为它。
_Avoid_: form default、一次性 backfill、服务端生成值

**迁移前沿（Migration Frontier）**：
某模块在一个候选快照中期望达到的最后 migration 身份。统一 EF Migration History 是实际执行事实，活动快照只记录其期望 frontier 和持久化计划摘要。
_Avoid_: 最新文件、最大时间戳、当前插件版本

**实验基线期（Experimental Baseline Phase）**：
兼容基线尚未冻结、只承诺当前空数据库基线的演进阶段。旧开发 catalog 不享有跨被替换基线的升级兼容；当前有效 history 前缀仍可以通过已确认的前向迁移演进。
_Avoid_: 开发环境宽松模式、永久迁移策略、旧库兼容期

**兼容基线冻结（Migration Compatibility Freeze）**：
开始承诺保存既有数据库数据与 migration history 的显式发布里程碑。冻结后的已发布迁移只能通过追加的前向步骤演进，不能由环境名或首次部署自动推断冻结。
_Avoid_: Production 环境名、分支冻结、隐式上线时间

**开发数据库重建（Development Database Reset）**：
把归属明确且未受保护的开发 catalog 显式恢复为空数据库基线的生命周期操作。它不是 migration、失败自动恢复或 Host 启动 fallback。
_Avoid_: 自动删库、schema drift 修复、migration data transform

**完整 Up 迁移（Full Up Migration）**：
Host 根据统一 Migration History，把已编译计划从当前有效前缀推进到期望 frontier 时执行的完整 EF `Up` 操作序列。它允许 Drop、Rename、Alter 等结构变化，但不放松模块归属、迁移链、摘要、事务和迁移锁校验，也不自动执行 `Down`。
_Avoid_: safe-expand only、metadata diff SQL、按环境隐式裁剪操作

**数据库超前（Database Ahead）**：
统一 Migration History 已超过当前已编译计划的目标 frontier。完整 Up 迁移不保证旧活动版本兼容更晚 schema，因此 Host 必须把它作为阻断性一致性失败，不得自动运行旧快照或执行 `Down`。
_Avoid_: 可安全忽略的前缀、自动回滚、激活成功

**统一迁移历史（Unified Migration History）**：
同一 provider 数据库中由 Platform Persistence 统一维护的 EF migration 执行事实。`dbo.__EFMigrationsHistory` 保存全局执行顺序；每个 migration 还在同一事务中向 `dbo.__NewPMigrationHistory` 写入版本化 owner、step-scoped bundle membership、content、model/from-to、全局前缀及 Provider Target/Transition 摘要。所有模块 migration stream 与 Platform Foundation Stream 共用该历史，Migration ID 在整份计划中全局唯一；模块不拥有私有 history。SQL Server 只接受空 catalog 或由当前已编译计划创建的有效 history 前缀，不补写、兼容或改写旧 history。
_Avoid_: 模块 history table、活动快照状态、手工回写记录

**迁移包完整性摘要（Bundle Checksum）与步骤成员摘要（Bundle Digest）**：
`BundleChecksum` 是候选当前 migration assembly 的 whole-artifact SHA-256，计划编译及执行连接前必须匹配；assembly 追加 migration 或实验基线整体替换后它可以变化。history 中的 `BundleDigest` 从候选内不可变 step 链输入计算，并在合法追加 migration 时保持旧前缀稳定；实验基线替换后的旧 fact 不重算兼容，必须重建 catalog。
_Avoid_: 把 whole-assembly checksum 持久化为旧 step 身份、忽略当前 artifact checksum、追加时改写旧 history

**Provider 历史事实投影（Provider History Fact Projection）**：
统一 history 对 Provider Target/Transition v1 的 owner-scoped canonical 投影。它保留 migration owner 表的所有列、key、index、FK 和 FK principal 物理引用，只排除其他 owner 的表，使后续增加无关模块时旧 fact 不变；修改旧 owner 表或其既有跨模块引用仍会摘要不一致。该投影不是新的 Provider Target 协议，也不替代连接前 full target/transition operation 校验。
_Avoid_: 把当前 full target 摘要持久化为不可追加的旧步骤身份、投影掉 FK principal、用 history 投影放松 operation 校验

**Schema Drift**：
数据库实际结构或统一 Migration History 与已验证的归属、迁移链、模型摘要和期望 frontier 不一致。列必须按稳定名称及结构 facet 对应；数据库物理列序差异本身不是 drift。Host 只读一致性检查负责报告并阻断 drift；所有 schema 写入仍只能由 Host 执行 EF Up migrations。
_Avoid_: 正常待执行迁移、自动修复计划、第二个结构写入入口

**只读一致性检查（Read-only Consistency Check）**：
Host 在 migration 成功后、插件活动快照提交和 readiness Ready 之前运行的统一检查。它重新核对完整 history 前缀与版本化摘要，并把 SQL Server 系统目录投影与 canonical Provider Target 按稳定对象名及结构 facet 比较；Development、Acceptance 与 Production 使用同一执行顺序。该检查只发出目录和 history SELECT，不生成 SQL 预览，不写 DDL、history 或业务数据。
_Avoid_: 开发环境开关、漂移修复器、启动后后台轮询

**Provider Adapter**：
在内部隐藏特定数据库的迁移锁、EF migrator 配置、统一 history reconciliation 和事务执行的实现 seam。首个外部插件门禁只支持 SQL Server；不支持的 provider 必须在 DDL 前失败。
_Avoid_: 插件接口、Host 分支、通用 SQL 生成器

**平台基础迁移流（Platform Foundation Stream）**：
SQL Server 从零摘要开始的平台自有 migration stream，只创建当前尚未拥有模块 migration bundle 的内建模型；当前 owner 是 Authorization 与 ConfigCenter，不包含 Organization。它与模块迁移流使用相同的 manifest、完整性、Provider Target/Transition、统一 history、事务和锁校验，不享有特殊执行授权。
_Avoid_: 旧 history 兼容器、平台内核拥有模块私有表、仅凭 migration id 放行

**空数据库基线（Empty Database Baseline）**：
SQL Server 唯一受支持的重建起点。catalog 可以包含 NewP ownership marker，但不得包含业务表或 migration history；实验基线替换后的旧 history、已有平台业务表或其他非空业务结构必须在新增 DDL 前稳定拒绝并要求显式重建。
_Avoid_: 自动升级被替换旧库、删除旧对象后继续、永久兼容模式
