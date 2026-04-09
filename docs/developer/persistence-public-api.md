# 持久化公共 API 手册

本文档面向仓库内部开发者，说明当前平台持久化层已经稳定暴露的入口、职责边界和推荐依赖方式。

如果你想看：

- 宿主怎么启用持久化，请继续看本文和 [`persistence-usage-guide.md`](./persistence-usage-guide.md)
- 日常新增或修改一个持久化模块的推荐顺序，请看 [`persistence-development-workflow.md`](./persistence-development-workflow.md)
- Phase 6.5 的设计背景，请看 [`../superpowers/specs/2026-04-09-phase-6-5-multi-db-persistence-design.md`](../superpowers/specs/2026-04-09-phase-6-5-multi-db-persistence-design.md)

## 1. 当前分层

当前持久化链路按下面几层组织：

- `server/platform-modeling`
  - 模型语义定义与校验器
- `server/platform-persistence`
  - Provider 无关的持久化内核抽象
- `server/platform-persistence-efcore`
  - EF Core 第一批适配器
- `server/platform-persistence-composition`
  - 聚合首批平台模型定义
- `apps/host-api`
  - 宿主接入入口

理解这组 API 时，最重要的原则是：

- 宿主依赖宿主入口
- 模块优先依赖模型语义和仓储契约
- Provider 差异沉在持久化层
- 不要让业务代码直接承担 Provider 切换和迁移装配职责

## 2. 宿主入口

### 2.1 `AddPlatformPersistence`

代码锚点：

- `apps/host-api/Extensions/PersistenceBootstrapExtensions.cs`

职责：

- 为宿主注册平台持久化能力
- 内部调用 `AddEfCorePersistence`
- 把首批平台模型定义聚合后传给持久化适配层

谁应该依赖它：

- `HostApi` 这类宿主程序集
- 需要统一装配平台持久化的启动代码

谁不应该直接依赖它：

- 模块内业务服务
- 模块内仓储实现
- 普通测试中的业务逻辑代码

当前使用方式：

```csharp
builder.Services.AddPlatformPersistence(builder.Configuration);
```

### 2.2 `ApplyPlatformPersistenceAsync`

代码锚点：

- `apps/host-api/Extensions/PersistenceBootstrapExtensions.cs`

职责：

- 在应用启动期执行持久化迁移入口
- 内部从 DI 中解析 `IPersistenceMigrationRunner`
- 当前首批行为是：如果 `Persistence:AutoMigrate=true`，则执行数据库迁移

谁应该依赖它：

- 宿主启动流程

谁不应该直接依赖它：

- 模块内普通服务
- 模块级仓储

当前使用方式：

```csharp
await app.ApplyPlatformPersistenceAsync();
```

### 2.3 `GetPlatformModelDefinitions`

代码锚点：

- `apps/host-api/Extensions/PersistenceBootstrapExtensions.cs`

职责：

- 返回当前宿主使用的平台模型定义集合
- 当前内部委托给 `PlatformPersistenceModelCatalog.GetDefinitions()`

说明：

- 这是宿主装配辅助入口，不是模块注册协议本身。
- 新增模块持久化时，通常应先补模块自己的 `*ModelDefinitions`，再看是否需要补进组合目录。

## 3. EF Core 适配层入口

### 3.1 `AddEfCorePersistence`

代码锚点：

- `server/platform-persistence-efcore/Extensions/EfCorePersistenceServiceCollectionExtensions.cs`

职责：

- 绑定 `PersistenceOptions`
- 注册 `ISqlDialect`
- 注册 `EfPlatformDbContext`
- 注册 `ISqlQueryExecutor`
- 注册 `IPersistenceMigrationRunner`
- 根据 `Persistence:Provider` 解析连接串和默认迁移程序集

谁应该依赖它：

- 持久化装配层
- 需要直接搭建 EF Core 持久化测试容器的基础设施测试

谁通常不应该直接依赖它：

- 普通业务模块
- 只想使用平台统一入口的宿主代码

当前方法签名的关键点：

- 需要 `IConfiguration`
- 需要一组 `ModelDefinition`

也就是说，EF Core 适配层不自己发现业务模型，而是接收外部传入的模型语义定义。

## 4. 配置模型

### 4.1 `PersistenceOptions`

代码锚点：

- `server/platform-persistence/PersistenceOptions.cs`

当前配置段名：

```text
Persistence
```

当前属性：

- `Provider`
  - 当前支持 `SqlServer`、`Oracle`、`MySql`
- `Adapter`
  - 当前首批只有 `EfCore`
- `AutoMigrate`
  - 启动期是否执行迁移
- `AutoSeed`
  - 当前已保留在配置模型中，但首批通用迁移执行器并未直接消费它

对开发者的含义：

- `Provider` 是当前主要切换项
- `Adapter` 用来表达“Provider”和“适配器”是两个概念
- `AutoMigrate` 直接影响 `ApplyPlatformPersistenceAsync` 的行为
- `AutoSeed` 目前不要误解成“平台已经有统一数据库 seed 管线”

### 4.2 `PersistenceProvider`

代码锚点：

- `server/platform-persistence/PersistenceProvider.cs`

当前枚举值：

- `SqlServer`
- `Oracle`
- `MySql`

用途：

- 选择连接串名
- 选择方言实现
- 选择默认迁移程序集

### 4.3 `PersistenceAdapterKind`

代码锚点：

- `server/platform-persistence/PersistenceAdapterKind.cs`

当前枚举值：

- `EfCore`

用途：

- 说明“数据库 Provider”和“持久化适配器”不是一个概念
- 为后续可能增加其他适配器预留配置位

当前边界：

- 首批只有 `EfCore`，不要把它理解为运行时可热切换多个适配器

## 5. 核心抽象

### 5.1 `IModelDefinitionProvider`

代码锚点：

- `server/platform-modeling/Definitions/IModelDefinitionProvider.cs`

职责：

- 返回一组模型语义定义

当前状态说明：

- 该接口已经落地，但当前宿主首批组合入口直接使用的是 `PlatformPersistenceModelCatalog`
- 它更偏向模型定义提供协议，而不是当前宿主唯一使用路径

适用场景：

- 需要抽象“谁来提供模型定义集合”的场景

### 5.2 `PlatformPersistenceModelCatalog`

代码锚点：

- `server/platform-persistence-composition/PlatformPersistenceModelCatalog.cs`

职责：

- 聚合当前首批平台模型定义
- 当前组合的是：
  - `ConfigCenterModelDefinitions.All`
  - `AuthorizationModelDefinitions.All`

谁应该依赖它：

- 宿主装配层
- 迁移设计时 `DbContext` 工厂

谁不应该直接依赖它：

- 普通业务服务
- 和平台组合无关的模块内部逻辑

### 5.3 `ISqlDialect`

代码锚点：

- `server/platform-persistence/Abstractions/ISqlDialect.cs`

职责：

- 提供当前 Provider 的基础 SQL 方言能力
- 当前首批暴露：
  - `Name`
  - `Parameter(string name)`

适用场景：

- 需要做受控原生 SQL 时

不建议：

- 在业务层自行根据数据库类型写 `if (provider == ...)`

### 5.4 `ISqlQueryExecutor`

代码锚点：

- `server/platform-persistence/Abstractions/ISqlQueryExecutor.cs`
- `server/platform-persistence-efcore/Sql/EfSqlQueryExecutor.cs`

职责：

- 统一执行原生 SQL 查询
- 由持久化层负责承接底层数据库访问细节

适用场景：

- 仓储里存在难以直接用仓储接口表达、但又需要受控执行的 SQL 查询

不建议：

- 业务服务直接拼 SQL 并绕开仓储边界

### 5.5 `IPersistenceMigrationRunner`

代码锚点：

- `server/platform-persistence/Abstractions/IPersistenceMigrationRunner.cs`
- `server/platform-persistence-efcore/PersistenceMigrationRunner.cs`

职责：

- 统一迁移执行入口

当前首批实现：

- `PersistenceMigrationRunner`
- 当 `Persistence:AutoMigrate=true` 时执行 `EfPlatformDbContext.Database.MigrateAsync()`

谁应该依赖它：

- 宿主启动入口

谁不应该直接依赖它：

- 普通业务服务
- 模块业务层

### 5.6 `EfPlatformDbContext`

代码锚点：

- `server/platform-persistence-efcore/EfPlatformDbContext.cs`

职责：

- 持有当前组合后的模型定义集合
- 在 `OnModelCreating` 中通过 `EfModelProjector` 把模型语义投影到 EF Core 模型

适用场景：

- EF Core 适配层
- 基础设施级测试
- 迁移项目

不建议：

- 让业务服务直接把它当成应用主数据访问入口

## 6. 模块作者最常接触的代码位置

当前首批模块示例：

- `server/modules/ConfigCenter/Persistence/Descriptors/ConfigCenterModelDefinitions.cs`
- `server/modules/ConfigCenter/Persistence/Ef/EfConfigRepository.cs`
- `server/modules/Authorization/Persistence/Descriptors/AuthorizationModelDefinitions.cs`
- `server/modules/Authorization/Persistence/Ef/`

这两层分别对应：

- `Descriptors`
  - 模型语义定义
- `Ef`
  - EF Core 仓储实现

这也是后续新增持久化模块时建议沿用的基本结构。

## 7. 推荐依赖关系

推荐从上到下理解依赖方向：

1. 宿主启动代码依赖 `AddPlatformPersistence` / `ApplyPlatformPersistenceAsync`
2. 组合层依赖 `PlatformPersistenceModelCatalog`
3. EF 适配层依赖 `AddEfCorePersistence`、`EfPlatformDbContext`、方言与迁移抽象
4. 模块持久化目录依赖模型语义定义和仓储实现

尽量避免反向依赖：

- 不要让业务层直接依赖 Provider 选择逻辑
- 不要让业务层自己选择迁移程序集
- 不要让宿主直接读写模块私有 SQL 细节

## 8. 进一步阅读

- 使用方式与命令：[`persistence-usage-guide.md`](./persistence-usage-guide.md)
- 开发顺序与边界：[`persistence-development-workflow.md`](./persistence-development-workflow.md)
- 本地三库准备：[`../guides/local-multi-db-container-setup.md`](../guides/local-multi-db-container-setup.md)
- 本机运行记录：[`../guides/2026-04-09-local-multi-db-runtime-record.md`](../guides/2026-04-09-local-multi-db-runtime-record.md)
