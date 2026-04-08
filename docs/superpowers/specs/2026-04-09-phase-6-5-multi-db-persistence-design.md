# Phase 6.5 多数据库持久化设计
## 1. 背景

当前新平台已经完成 `ConfigCenter`、`Authorization`、`Audit`、`Files` 等模块的基础实现，但 `ConfigCenter` 与 `Authorization` 的仓储仍以进程内内存实现为主，尚未形成真实数据库持久化底座。按照当前计划，下一阶段应进入 `Phase 6.5`，优先为真实承接模块补齐数据库持久化、本地初始化、迁移脚本与可复跑验证。

本轮新增约束如下：

- 同一套程序必须支持通过配置切换 `SQL Server`、`Oracle`、`MySQL`
- 业务层调用数据库操作的方式必须统一，不能因为数据库不同而改类、改方法、改调用方式
- 允许在底层保留手写 SQL 能力，但数据库差异只能沉到基础设施层，不能泄漏到业务层
- 本轮不预留模型驱动接口，也不建设类似旧平台的元数据配置工具

旧平台的 [ModelGuide.md](D:/Github/ICP-X/OldP/ApiDoc/05_Models/ModelGuide.md) 提供了重要参考：它清晰地区分了业务模型、数据库结构和展示元数据，也区分了独立实体对象与关系对象。这些分层思想值得吸收，但旧平台的 `.dic/.model/.schema` 三件套与配置工具体系本质上属于完整的模型驱动平台，不适合作为本轮持久化落地的前置条件。

因此，本轮设计选择采用当前主流的代码优先持久化方案：统一业务模型和仓储契约，使用代码定义数据库映射与迁移，必要时在持久化层受控使用原生 SQL。

## 2. 目标

- 为 `ConfigCenter` 与 `Authorization` 建立真实数据库持久化能力
- 同一套宿主程序通过配置切换 `SQL Server`、`Oracle`、`MySQL`
- 保证业务层只依赖统一仓储接口与服务接口，不感知数据库类型
- 保留底层手写 SQL 能力，用于复杂查询、性能敏感查询和 Provider 特定能力
- 三种数据库都具备自动迁移、初始化、种子数据与 smoke 验证闭环
- 形成可持续扩展到后续模块的持久化基础设施模式

## 3. 非目标

- 本轮不建设旧平台式模型驱动设计器、元数据配置工具或代码生成平台
- 本轮不让业务人员直接通过界面定义数据库表、字段和关系
- 本轮不预留独立的模型驱动接口层
- 本轮不优先处理 `Audit` 与 `Files` 的数据库化，除非后续证明它们成为阻塞点
- 本轮不在没有真实性能证据前引入统一缓存层
- 本轮不引入跨模块共享数据库实现细节或跨模块共享仓储

## 4. 方案对比结论

### 4.1 方案 A：EF Core 10 + 多 Provider + 统一仓储契约

做法：

- 业务层继续依赖现有模块内的 `Models`、`Repositories` 接口与 `Services`
- 基础设施层使用 `EF Core 10`，启动时根据配置选择 `SQL Server`、`Oracle`、`MySQL`
- 三种数据库分别维护独立迁移集
- 对复杂查询开放统一 SQL 执行入口

优点：

- 最符合“业务层调用统一”的核心要求
- 最贴合当前仓库已有的模块边界与仓储抽象
- 支持主流工程化流程：代码审查、版本管理、测试、自动迁移、回滚
- 与当前行业主流框架的实践一致

缺点：

- 需要显式处理三种 Provider 的迁移与 DDL 差异
- 基础设施层需要额外维护 Provider 选择、方言与集成测试

### 4.2 方案 B：统一仓储接口 + Dapper/ADO.NET + 方言层

做法：

- 业务层继续统一
- 仓储实现完全由手写 SQL 和方言层驱动

优点：

- 性能可控，SQL 粒度最细

缺点：

- 三数据库的 DDL、分页、函数、主键策略、批量操作都要自己维护
- 自动迁移与闭环验证成本最高
- 不适合作为当前阶段的首选

### 4.3 方案 C：统一业务层 + 每种数据库独立仓储实现

做法：

- 业务接口统一
- 为同一个仓储接口分别写 `SqlServer/Oracle/MySql` 三套实现

优点：

- 业务层表面统一

缺点：

- 基础设施层膨胀严重
- 后续每增加一个查询或行为都要维护三套实现
- 长期成本高

### 4.4 选择结论

本轮选择 **方案 A**：

- 默认持久化主干采用 `EF Core 10 + 多 Provider`
- 业务层完全统一
- 底层保留受控的原生 SQL 能力
- 不采用模型驱动，不建设元数据配置工具

## 5. 为什么本轮不采用模型驱动

本轮不采用旧平台式模型驱动，不是因为模型驱动没有价值，而是因为它不符合当前阶段的最小可行目标。

原因如下：

- 当前要解决的是持久化落地，而不是先重建一套模型平台
- 旧平台的 `.dic/.model/.schema` 与配置工具不是简单的“建表工具”，而是一整套模型定义、显示语义、数据库结构和代码生成体系
- 若本轮先做模型驱动，工作重点会从“让 `ConfigCenter` / `Authorization` 真实落库”转移到“先建设模型设计器与生成器”
- 当前仓库已经具备模块模型、仓储接口与服务层，代码优先只是在现有骨架上补齐持久化，不需要重新发明平台
- 代码优先与当前主流框架一致，更适合版本控制、自动化验证和 AI 协作式开发

结论：

- 本轮物理数据库表、映射与迁移全部以代码为准
- 不预留模型驱动接口
- 不把未来可能的元数据工具纳入当前设计范围

## 6. 核心设计约束

- 业务层不得出现按数据库类型分支的类、方法或调用逻辑
- 业务服务层不得直接操作 `DbConnection`、`DbCommand` 或 Provider 判断逻辑
- 仓储接口必须保持数据库无关
- Provider 差异只能存在于基础设施层
- 原生 SQL 必须参数化，不允许业务层字符串拼接动态 SQL
- 原生 SQL 执行必须复用当前 `DbContext` 的连接与事务
- 不允许跨模块直接访问对方数据库表或持久化实现细节

## 7. 总体架构

### 7.1 分层结构

- `Models / DTOs / Services`
  - 业务层
  - 定义业务对象、接口、规则和用例
- `Repositories` 接口
  - 业务层看到的数据访问契约
  - 例如 `IConfigRepository`、`IRoleRepository`
- `Persistence`
  - 基础设施层
  - 负责把仓储接口接到真实数据库

### 7.2 模块职责

#### `server/modules/*/Models`

放业务模型，作为领域对象和 EF 实体的第一实现承载，不在本轮复制另一套数据库专用实体模型。

借鉴旧平台的 `ENTITY/LINK` 思想：

- `ConfigItem`、`Role`、`Permission` 等更接近独立实体对象
- `UserRole`、`RolePermission` 等更接近关系对象

但本轮不照搬旧平台 `.model/.schema` 的实现方式，而是在代码中表达同样的分层语义。

#### `server/modules/*/Repositories`

只放数据库无关的仓储接口。

例如：

- `IConfigRepository`
- `IRoleRepository`
- `IPermissionRepository`
- `IUserRoleRepository`

#### `server/modules/*/Persistence`

只放数据库落地实现，不放另一套重复的业务模型定义。

主要内容包括：

- EF Core 实体映射配置
- EF Core 仓储实现
- 必要的原生 SQL 查询实现
- 模块级种子/持久化辅助类

例如：

- `ConfigItemEntityConfiguration.cs`
- `EfConfigRepository.cs`
- `ConfigCenterSqlQueryRepository.cs`
- `RoleEntityConfiguration.cs`
- `EfRoleRepository.cs`

### 7.3 平台级持久化底座

建议新增统一持久化基础设施目录，例如：

- `server/platform-persistence`

职责包括：

- Provider 枚举与配置模型
- `DbContext` 基座与注册入口
- Provider 选择逻辑
- 统一 SQL 执行入口
- Provider 方言服务
- 迁移与初始化执行入口

## 8. Provider 切换设计

### 8.1 配置模型

宿主显式配置：

- `Persistence:Provider = SqlServer | Oracle | MySql`
- `ConnectionStrings:SqlServer`
- `ConnectionStrings:Oracle`
- `ConnectionStrings:MySql`
- `Persistence:AutoMigrate = true | false`
- `Persistence:AutoSeed = true | false`

保留现有 `ConnectionStrings:DefaultConnection` 仅会制造歧义，不作为本轮目标配置形态。

### 8.2 启动流程

宿主启动时固定执行以下流程：

1. 读取 `Persistence:Provider`
2. 选择对应 EF Core Provider
3. 绑定对应连接串
4. 选择对应迁移集
5. 执行迁移
6. 执行统一种子数据逻辑
7. 启动 Host

### 8.3 Provider 边界

Provider 相关差异只允许出现在：

- `DbContext` 注册配置
- Provider 方言服务
- 迁移集
- 少量 Provider 特定 SQL

业务层和业务仓储接口中不得出现：

- `if (provider == Oracle)`
- `if (provider == MySql)`
- `if (provider == SqlServer)`

## 9. 数据库表与实体设计

### 9.1 表结构设计原则

物理数据库表采用代码优先方式定义：

- 业务实体在 `Models` 中定义
- 表、列、长度、索引、唯一约束、关系、默认值在 EF Core 映射中定义
- 表结构变更通过 migration 进入版本库

也就是说，本轮“数据库表怎么设计”的答案是：

- 先设计业务模型
- 再通过代码映射定义物理表
- 最终由迁移落到三种数据库

### 9.2 是否复制数据库专用实体

本轮默认 **不复制第二套持久化实体模型**。

原因：

- 当前模块模型与数据库结构差异不大
- 复制第二套实体会增加维护成本和理解成本
- 现阶段目标是稳定落库，不是建立 CQRS/读写双模型体系

只有在未来出现以下情况，才考虑额外的持久化实体或读模型：

- 数据库结构与业务模型差异显著
- 某些只读场景需要专门的投影模型
- 某些性能热点需要独立读模型

### 9.3 `ENTITY/LINK` 的吸收方式

借鉴旧平台的核心思想，而不是照搬实现形式：

- 独立业务对象使用普通实体映射
- 关系对象单独建表并显式建索引和唯一约束
- 审计字段、创建时间、更新时间等采用统一基类或统一配置方式复用

例如：

- `Role` 表示独立实体
- `Permission` 表示独立实体
- `UserRole` 表示关系表
- `RolePermission` 表示关系表

## 10. 原生 SQL 能力设计

### 10.1 基本原则

- 默认 CRUD 与事务优先使用 EF Core
- 只有在复杂查询、报表型查询、性能敏感查询或 Provider 特定能力场景下，才在持久化层使用原生 SQL

### 10.2 统一入口

原生 SQL 不能直接在业务服务层随意书写，必须通过统一基础设施入口暴露。

建议提供：

- `ISqlQueryExecutor`
- `ISqlDialect`

或等价的统一查询执行接口。

### 10.3 使用规则

允许：

- 仓储实现内部调用 SQL 执行器
- 只读复杂查询使用参数化 SQL
- Provider 方言服务负责分页、函数、锁提示、标识列等差异

禁止：

- 业务服务层直接拿连接和命令对象
- 业务服务层手写数据库分支 SQL
- 字符串拼接动态 SQL

### 10.4 事务一致性

原生 SQL 必须复用当前 `DbContext` 的连接与事务上下文，避免出现：

- EF Core 一套事务
- 原生 SQL 另一套事务

## 11. 迁移策略

### 11.1 统一模型，分库迁移

不采用“一套迁移跑三库”的策略。

采用：

- 同一套业务模型
- 同一套仓储接口
- 三套独立迁移集

建议目录：

- `Migrations/SqlServer`
- `Migrations/Oracle`
- `Migrations/MySql`

### 11.2 原因

三种数据库在以下方面存在显著差异：

- 自增/标识列
- 时间类型
- 布尔值表达
- 索引与约束能力
- 默认值表达式
- 分页语法
- Provider 生成的 DDL 细节

因此追求“一套迁移同时优雅支持三库”会显著增加复杂度并降低稳定性。

### 11.3 种子与初始化

种子逻辑统一定义，初始化入口分 Provider 执行。

统一种子示例：

- 默认管理员角色
- 默认权限同步
- `ConfigCenter` 基础配置项

Provider 差异只影响：

- 建表方式
- 迁移执行
- 必要的初始化 SQL

## 12. 模块落地顺序

### 12.1 第一批模块

本轮只纳入：

- `ConfigCenter`
- `Authorization`

原因：

- `ConfigCenter` 已经是旧资产导入的自然落点
- `Authorization` 是平台公共能力，且与启动种子和权限同步强相关

### 12.2 后续模块

暂不纳入：

- `Audit`
- `Files`

除非后续验证表明它们成为当前阶段的实际阻塞点。

## 13. 目录结构建议

建议新增或调整如下目录：

- `apps/host-api`
  - 只负责读取配置、选择 Provider、注册持久化、执行迁移与种子
- `server/platform-persistence`
  - 放持久化基座、Provider 选择、方言、统一 SQL 执行入口、迁移入口
- `server/modules/ConfigCenter/Persistence`
  - 放 `ConfigCenter` 的 EF 映射、EF 仓储实现、必要 SQL 查询实现
- `server/modules/Authorization/Persistence`
  - 放 `Authorization` 的 EF 映射、EF 仓储实现、必要 SQL 查询实现
- `tests/integration/persistence`
  - 放多数据库集成验证与 smoke 脚本

## 14. 验收标准

本轮“支持三数据库”必须达到以下硬标准：

- 业务层代码零分库分支
- 三种数据库切换仅改配置
- 三种数据库都能完成自动迁移
- 三种数据库都能完成基础 seed
- `ConfigCenter` 关键读写与导入查询通过
- `Authorization` 权限同步、角色授权与用户角色查询通过
- 重启后数据仍可读取，证明数据可跨进程保存
- 原生 SQL 查询在三种数据库下行为一致

## 15. 验证方案

### 15.1 单元测试

- 保持现有模块测试以业务服务与仓储契约为主
- 单元测试不依赖具体数据库

### 15.2 集成测试

新增 Provider 维度的集成测试，至少覆盖：

- `ConfigCenter`
- `Authorization`

### 15.3 Smoke 验证

对三种数据库分别执行：

- 建库/连接
- 迁移
- 种子
- 核心读写
- 重启后复验

## 16. 风险与控制

### 16.1 主要风险

- 三种 Provider 的迁移细节不完全一致
- Oracle 与 MySQL 在类型和默认值上的差异可能导致同一模型映射需要细调
- 原生 SQL 若管理不严，容易重新把数据库差异泄漏到业务层

### 16.2 控制方式

- 业务层严格禁止分库分支
- 原生 SQL 统一从持久化层入口调用
- 分库迁移显式隔离
- 第一批仅聚焦 `ConfigCenter` 与 `Authorization`
- 通过集成测试和 smoke 及时发现 Provider 差异

## 17. 结论

`Phase 6.5` 采用当前主流的代码优先持久化方案：

- 统一业务模型与仓储契约
- 使用 `EF Core 10 + 多 Provider`
- 通过配置切换 `SQL Server`、`Oracle`、`MySQL`
- 通过三套独立迁移集完成数据库落地
- 在持久化层保留受控原生 SQL 能力

本轮不采用模型驱动，不建设元数据配置工具，不预留模型驱动接口。

这一路线最符合当前仓库现状、阶段目标和团队已确认的约束，也最有利于后续测试、演进、协作与稳定交付。
