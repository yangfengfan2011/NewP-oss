# 平台持久化操作内核详细设计

## 1. 背景与目标

当前 Standard CRUD 已经提供 create / update / delete 的基础路径，但写侧能力仍散落在 Standard CRUD service 与 EF 仓储中：

- 字段白名单、必填字段、唯一约束和审计字段由 `server/platform-crud/Runtime/StandardCrudService.cs` 执行。
- 实际 shared-type entity 写入由 `server/platform-crud/Persistence/EfStandardCrudRepository.cs` 直接调用 `EfPlatformDbContext.SaveChangesAsync`。
- 主键生成目前位于 `server/platform-crud/ObjectIds/`，虽然注释已经说明它可供模块写入复用，但项目依赖仍处在 Standard CRUD 内。
- `server/platform-persistence/` 与 `server/platform-persistence-efcore/` 已提供 provider、dialect、EF DbContext、迁移和 SQL query executor 等底座，但尚未提供平台级写入操作内核。

这会带来三个问题：

- 后续事务、批量插入、批量更新、批量删除、审计字段、失败回滚和 provider 差异如果继续塞进 Standard CRUD，会让 Standard CRUD 变成所有写入能力的必经路径。
- 模块应用服务如果绕过 Standard CRUD 直接写 EF repository，又会重复实现字段裁剪、主键生成、审计字段、唯一冲突归一化和事务边界。
- `platform-query` 已被设计为读侧内核，负责字段白名单、筛选、排序、分页、投影和 display value；写侧能力继续混入查询内核会破坏读写边界。

本设计新增“平台持久化操作内核”，定位为 `platform-query` 的兄弟能力：

- `platform-query` 负责读侧：字段白名单、筛选、排序、分页、投影、display value。
- `platform-persistence-operations` 负责写侧：显式事务、批量 insert/update/delete、审计字段、主键生成、唯一冲突归一化、失败回滚和 provider 写入差异。
- `platform-crud` 继续负责标准 CRUD 的资源契约、HTTP API、字段集、删除策略和模块扩展点；create/update/delete 可以作为第一批写侧内核接入者。
- 模块应用服务仍承载业务规则、权限和跨聚合编排；写侧内核只承接平台级持久化操作，不替代模块领域服务。
- EF persistence 继续是第一版实现底座；写侧内核不绕过 `EfPlatformDbContext`，也不直接替代现有 migrations、model projector、provider options。

## 2. 范围与非目标

第一版支持：

- 提供独立于 Standard CRUD 的写侧入口契约。
- 提供显式事务边界和同一 DI scope 内的 rollback 口径。
- 提供单资源批量插入、批量更新、批量删除的统一 command / result 模型。
- 支持字段白名单、必填字段、唯一约束、主键生成、创建/更新审计字段的内核执行。
- 支持 all-or-nothing 的批量写入结果语义：任一持久化失败时整批回滚，不报告部分成功为整体成功。
- 第一版 EF 实现使用 `AddRange`、逐行属性赋值、`RemoveRange` 和分块 `SaveChangesAsync`，并由一个显式事务包住整批。
- Provider 差异先通过 capability、chunk size 建议、异常归一化和 smoke 测试暴露出来。

第一版不支持：

- 不做通用 ORM 替代品，不提供任意 SQL DSL，不接管 EF model building、migration 或复杂关系映射。
- 不实现 provider-specific bulk copy、merge、upsert、`ExecuteUpdate` 或 `ExecuteDelete` 优化路径。
- 不处理复杂工作流、审批流、Saga、跨服务分布式事务、消息最终一致性、outbox/inbox 或异步补偿。
- 不处理跨数据库连接、跨 DbContext scope 或跨服务边界的事务。
- 不在本设计中修改当前 `platform-query` 查询内核实施范围。
- 不把执行流程、事务流程、chunk 流程、回滚流程、provider fallback 流程写进元数据。
- 不要求所有既有模块 repository 立刻迁移；第一版先让 Standard CRUD 写入路径成为接入样板。

## 3. 架构定位

建议后续形成如下后端边界：

```text
模块应用服务
  | 业务规则、权限、跨聚合编排
  v
IPlatformPersistenceOperations / IPlatformUnitOfWork
  | 写侧平台内核：事务、批量、审计、主键、字段边界、失败口径
  v
EF persistence adapter
  | EfPlatformDbContext、provider options、DbUpdateException 归一化
  v
SQL Server / MySQL / Oracle
```

与既有能力关系：

- `platform-query`：兄弟能力，不依赖写侧内核；写侧内核也不调用查询内核做筛选分页。两者可以共享模型字段定义和 provider 底座，但读写流程分离。
- `platform-crud`：第一批 adapter。Standard CRUD descriptor 可映射为写侧 descriptor，Standard CRUD service 的 create/update/delete 逐步改为调用写侧内核。
- 模块应用服务：可直接调用写侧内核完成平台级持久化操作，不必须绕 Standard CRUD。
- `server/platform-persistence`：承载 provider、dialect、公共 persistence 抽象，可作为写侧内核的基础依赖。
- `server/platform-persistence-efcore`：承载第一版 EF 实现，负责与 `EfPlatformDbContext.Database.CurrentTransaction`、`SaveChangesAsync`、`DbUpdateException` 对接。

建议命名：

- 公共写侧契约可以放入独立项目 `server/platform-persistence-operations`，避免概念继续绑在 Standard CRUD 或查询内核。
- EF 实现第一版可放在 `server/platform-persistence-efcore/Operations`，因为它需要直接使用 `EfPlatformDbContext`；如果后续体量扩大，再拆成独立 EF operations adapter 项目。
- 现有 `IPlatformObjectIdGenerator` 应从 `platform-crud` 迁移或复制成中立契约，写侧内核不能依赖 `platform-crud`。

## 4. 设计约束

- 元数据只声明“它是什么、归谁、允许什么”：资源归属、模型名、主键字段、允许写字段、必填约束、唯一约束、审计字段声明、删除能力。
- 执行顺序、事务开启、chunk 处理、异常捕获、rollback、provider fallback 必须在代码内核中实现。
- 写侧 descriptor 不是长期运行时状态真源；authoring 仍应回到可提交源码或 metadata 源目录。
- 默认禁止隐式覆盖和复制顺序决定最终写入字段；字段冲突必须由内核稳定失败。
- 写侧内核不跨模块读取或修改别的模块持久化细节；模块如需跨模块规则，必须通过契约服务或应用服务协作。
- Standard CRUD 不是通用写侧能力的唯一入口；其他模块未来可以直接面向 `IPlatformPersistenceOperations`。
- 第一版只支持单资源命令。跨资源同事务可以通过 `IPlatformUnitOfWork` 包住多个单资源命令，但不提供跨资源批量 command。
- 所有 cancellation token 必须贯穿事务、查询待写入行、`SaveChangesAsync` 和 provider 操作。

## 5. 核心契约建议

### 5.1 `IPlatformUnitOfWork`

`IPlatformUnitOfWork` 是显式事务入口，负责开启、提交、回滚和异常边界。

```csharp
public interface IPlatformUnitOfWork
{
    Task<T> ExecuteAsync<T>(
        PlatformTransactionOptions options,
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken = default);

    Task<IPlatformTransaction> BeginAsync(
        PlatformTransactionOptions options,
        CancellationToken cancellationToken = default);
}
```

第一版推荐 `ExecuteAsync` 作为主要入口，减少调用方忘记 commit/rollback 的风险。`BeginAsync` 仅用于需要多段调用、但仍在同一应用服务方法中收口的场景。

### 5.2 `IPlatformTransaction`

`IPlatformTransaction` 表示一个已经开启的事务边界。

```csharp
public interface IPlatformTransaction : IAsyncDisposable
{
    string TransactionId { get; }
    Task CommitAsync(CancellationToken cancellationToken = default);
    Task RollbackAsync(CancellationToken cancellationToken = default);
}
```

约定：

- `DisposeAsync` 在未 commit 时必须尝试 rollback。
- `CommitAsync` 成功后，后续 cancellation 不再把结果改写为失败。
- `RollbackAsync` 应幂等；重复 rollback 不应制造第二个业务错误。

### 5.3 `IPlatformPersistenceOperations`

外部建议只暴露一个清晰写侧入口，名称优先使用 `IPlatformPersistenceOperations`。`IPlatformWriteExecutor` 可作为内部实现类名，但不建议让两个同义接口同时成为公共入口。

```csharp
public interface IPlatformPersistenceOperations
{
    Task<PlatformWriteBatchResult> InsertAsync(
        PlatformWriteDescriptor descriptor,
        PlatformInsertBatchCommand command,
        CancellationToken cancellationToken = default);

    Task<PlatformWriteBatchResult> UpdateAsync(
        PlatformWriteDescriptor descriptor,
        PlatformUpdateBatchCommand command,
        CancellationToken cancellationToken = default);

    Task<PlatformWriteBatchResult> DeleteAsync(
        PlatformWriteDescriptor descriptor,
        PlatformDeleteBatchCommand command,
        CancellationToken cancellationToken = default);
}
```

执行口径：

- 如果当前 scope 已存在平台事务，操作加入该事务。
- 如果调用方没有显式事务，单个 batch 操作由内核创建一个可见的内部事务并在操作结束时提交或回滚。
- 多个 batch 的原子性必须由调用方显式使用 `IPlatformUnitOfWork.ExecuteAsync` 包住。

### 5.4 `PlatformWriteDescriptor`

第一版建议保留写侧 descriptor，因为字段白名单、审计字段、主键字段、唯一约束和删除能力都需要一个静态可校验边界。

它只能声明：

```csharp
public sealed class PlatformWriteDescriptor
{
    public required string ResourceKey { get; init; }
    public required string ModuleId { get; init; }
    public required string ModelName { get; init; }
    public required string IdField { get; init; }
    public IReadOnlyList<string> InsertFields { get; init; } = [];
    public IReadOnlyList<string> UpdateFields { get; init; } = [];
    public PlatformDeleteCapability DeleteCapability { get; init; } = PlatformDeleteCapability.None;
    public PlatformAuditFieldSet AuditFields { get; init; } = PlatformAuditFieldSet.None;
    public IReadOnlyList<PlatformRequiredField> RequiredFields { get; init; } = [];
    public IReadOnlyList<PlatformUniqueConstraint> UniqueConstraints { get; init; } = [];
}
```

它不能声明：

- 先查哪个表、再写哪个表的执行流程。
- 事务传播策略、savepoint 策略或 rollback 分支。
- chunk size、重试次数、provider-specific bulk copy 策略。
- 失败后补偿动作、消息发送、跨服务协调。
- 根据运行时数据决定下一步动作的条件分支。

Standard CRUD descriptor 到写侧 descriptor 的映射示例：

- `ResourceKey / ModuleId / ModelName / IdField` 直接映射。
- `FieldSet.CreateFields` 映射为 `InsertFields`。
- `FieldSet.UpdateFields` 映射为 `UpdateFields`。
- `RequiredFields` 和 `UniqueConstraints` 复用同类声明。
- `DeletePolicy` 映射为 `DeleteCapability`，第一版只执行明确允许的物理删除。
- `CreatedAt / UpdatedAt` 等审计字段由 `AuditFields` 显式声明或从模型定义中的 audit field 投影后由 adapter 生成。

### 5.5 Command 与 Result

批量 command 建议区分 insert/update/delete，避免一个万能 command 同时承载三种语义。

```csharp
public sealed record PlatformInsertBatchCommand(
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Items,
    PlatformWriteOptions? Options = null);

public sealed record PlatformUpdateBatchCommand(
    IReadOnlyList<PlatformUpdateItem> Items,
    PlatformWriteOptions? Options = null);

public sealed record PlatformDeleteBatchCommand(
    IReadOnlyList<string> Ids,
    PlatformWriteOptions? Options = null);

public sealed record PlatformUpdateItem(
    string Id,
    IReadOnlyDictionary<string, object?> Values);
```

`PlatformWriteOptions` 属于命令选项，不进入 descriptor：

```csharp
public sealed class PlatformWriteOptions
{
    public int? ChunkSize { get; init; }
    public bool ReturnRows { get; init; } = true;
}
```

第一版不提供 `ContinueOnError`。如果后续确实需要部分成功，必须另开设计，明确 API 语义、事务语义、审计语义和调用方展示方式。

结果模型：

```csharp
public sealed class PlatformWriteBatchResult
{
    public bool Succeeded { get; init; }
    public int RequestedCount { get; init; }
    public int AffectedCount { get; init; }
    public IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows { get; init; } = [];
    public IReadOnlyList<PlatformWriteError> Errors { get; init; } = [];
}
```

结果约束：

- `Succeeded == true` 时 `AffectedCount` 必须等于本次命令期望影响数量，除非 delete 对不存在 ID 有显式“必须存在”错误。
- `Succeeded == false` 时事务必须 rollback，`AffectedCount` 对外报告为 0。
- validation 可以一次返回多个字段错误，但只要存在错误就不能进入持久化阶段。
- provider 抛出的唯一冲突、必填冲突、长度冲突等必须归一化为稳定错误，不允许吞掉 provider 差异后返回泛化成功。

## 6. 事务设计

### 6.1 显式事务边界

写侧内核的事务边界必须在代码里可见：

```csharp
await unitOfWork.ExecuteAsync(
    new PlatformTransactionOptions { IsolationLevel = PlatformIsolationLevel.ReadCommitted },
    async ct =>
    {
        await operations.InsertAsync(personDescriptor, insertCommand, ct);
        await operations.UpdateAsync(accountDescriptor, updateCommand, ct);
        return true;
    },
    cancellationToken);
```

单个 batch 操作可以由 `IPlatformPersistenceOperations` 创建内部事务，但多个 batch 要求应用服务显式使用 `IPlatformUnitOfWork`。

### 6.2 嵌套事务策略

第一版采取保守策略：

- 同一 `EfPlatformDbContext` scope 内已经存在平台事务时，新的 operations 加入当前事务。
- 调用 `BeginAsync` 时如果已经存在平台事务，返回稳定错误 `PlatformPersistence.TransactionAlreadyActive`。
- 第一版不支持 savepoint，不支持嵌套 commit，不支持内层 rollback 后外层继续提交。
- 如后续需要 savepoint，必须补充独立设计，覆盖 SQL Server / MySQL / Oracle 差异和 EF provider 支持情况。

### 6.3 Cancellation、异常与 rollback

口径：

- validation 阶段收到 cancellation：不进入持久化，返回或抛出取消信号，数据库无变化。
- chunk 写入前收到 cancellation：不处理后续 chunk，rollback 当前事务。
- `SaveChangesAsync` 或 provider 操作收到 cancellation：rollback 当前事务；如果 provider 已报告事务状态不明，返回稳定失败信号并要求 smoke 测试覆盖。
- commit 前出现异常：rollback。
- commit 成功后出现响应构造异常：数据库事实已经提交，调用方不得再声明已 rollback；服务 adapter 应把这类错误作为响应构造缺陷单独暴露。
- 未知异常默认 rollback 后重新抛出，由上层 API adapter 转为稳定 500；已知 persistence 异常转为 `PlatformWriteBatchResult.Failure`。

### 6.4 EF `DbContext` lifetime

第一版 EF 实现依赖 scoped `EfPlatformDbContext`：

- `IPlatformUnitOfWork`、`IPlatformPersistenceOperations`、`EfPlatformDbContext` 必须在同一 DI scope 中解析。
- 事务绑定到 `EfPlatformDbContext.Database.CurrentTransaction`。
- 现有 `EfSqlQueryExecutor` 已会加入当前 EF transaction；同一 scope 内的 SQL 查询可以参与同一事务。
- 不支持跨 scope 传递 transaction，不支持把 `IPlatformTransaction` 交给后台线程继续使用。
- 不支持多个 `EfPlatformDbContext` 实例共享同一个平台事务。

## 7. 批量写入设计

### 7.1 批量插入

第一版能力：

- 按 `InsertFields` 裁剪输入字段，未知字段返回稳定错误，不静默丢弃。
- 缺失主键时由中立的 `IPlatformObjectIdGenerator` 或后续等价接口生成。
- 创建审计字段由内核覆盖，客户端提交的 `CreatedAt / CreatedBy` 默认不可信。
- 批量必填和唯一约束在进入 EF 前先做内存与数据库预检查。
- 使用 EF shared-type entity `DbSet<Dictionary<string, object?>>.AddRange`。
- 默认 chunk size 建议 500，允许命令选项在 1 到 provider capability 上限之间调整。
- 所有 chunk 在同一个事务中执行；中间任何 chunk 失败都 rollback 已保存 chunk。

唯一冲突口径：

- 同一 batch 内的重复值应在内存预检阶段失败。
- 数据库已有值冲突应在写前查询或 provider 唯一约束异常阶段失败。
- 并发竞争导致预检通过但提交失败时，必须把 `DbUpdateException` 归一化为唯一冲突错误。

### 7.2 批量更新

第一版能力：

- 每个 update item 必须有 ID。
- 只允许更新 `UpdateFields` 中声明的字段；未知字段或只读字段返回稳定错误。
- 更新审计字段由内核覆盖为当前时间和当前操作者，不接受客户端提交值。
- 先按 ID 分块读取目标行，缺失 ID 作为整批失败，默认不跳过。
- 对每行应用字段后分块 `SaveChangesAsync`，但事务覆盖整批。
- 唯一约束基于“现有行 + 本次更新值”的 candidate 计算，避免未提交唯一字段时漏检。

第一版不做：

- 不使用 EF `ExecuteUpdate`，因为当前资源是 shared-type entity，字段白名单、审计字段和 provider 差异需要先有更强验证。
- 不支持按任意筛选条件批量更新。批量更新只接受明确 ID 列表，避免写侧内核变成动态 SQL 更新器。

### 7.3 批量删除

第一版能力：

- 只支持 ID 列表删除。
- 删除能力必须由 `DeleteCapability` 显式声明。
- `DeleteCapability.None` 返回稳定失败。
- `DeleteCapability.Physical` 可执行 `RemoveRange`。
- `DeleteCapability.Auditable` 或 soft delete 先作为声明保留，第一版不执行，避免把审计删除流程混入未验证路径。
- 缺失 ID 默认整批失败并 rollback。

第一版不做：

- 不支持任意条件删除。
- 不支持跨资源级联删除。
- 不替代模块 delete guard。Standard CRUD 的条件删除守卫仍在 Standard CRUD service 或模块应用服务层执行，然后再调用写侧内核。

### 7.4 Provider 差异

第一版不引入 provider-specific bulk copy。SQL Server / MySQL / Oracle 差异通过下面机制处理：

- `PlatformWriteProviderCapabilities` 暴露 provider 名称、推荐 chunk size、单命令参数安全上限、是否支持当前 delete/update 策略。
- EF adapter 根据 `PersistenceOptions.Provider` 选择 capability。
- provider 不支持的能力返回稳定错误，不做静默 fallback。
- `DbUpdateException` 由 provider-specific normalizer 转换为稳定错误，例如唯一冲突、字符串长度、必填字段、外键或连接失败。
- smoke 测试至少覆盖三类 provider 的事务 rollback、唯一冲突、批量插入小样本。

### 7.5 数据库底层实现约束

这部分约束用于防止后续实现时把 EF adapter 写成“看起来统一、实际由数据库偶然兜底”的薄封装。

第一批高风险验证点：

- `DbContext` 显式事务：批量写入会按 chunk 调用 `SaveChangesAsync`，只有同一个数据库事务覆盖所有 chunk，all-or-nothing 才是数据库事实。否则前半批可能已经提交，后半批失败时 API 只能靠上层猜测状态。
- EF execution strategy：如果 provider retry 只重放某一次 `SaveChangesAsync`，而不是重放“开启事务、执行全部写入、提交事务”整个委托，就可能造成重复插入、半批重放或事务状态不明。第一版必须先证明写事务不会被局部 retry 破坏。
- `ChangeTracker` rollback 清理：数据库 rollback 不会自动清理 `DbContext.ChangeTracker` 中的 `Added / Modified / Deleted` 状态。rollback 后继续复用同一 scope，可能把已经回滚的实体再次写入，形成幽灵写入或重复错误。

这三项必须早于批量能力扩展验证，因为它们决定写侧内核是否能准确回答“数据库到底提交了什么”。字段白名单、审计字段、唯一冲突归一化等上层能力都依赖这个事实边界。

模型与 schema：

- 写侧内核启动期或首次使用时必须把 `PlatformWriteDescriptor` 和 `ModelDefinition` / EF metadata 对齐校验：模型存在、主键字段存在且是主键、写字段存在、审计字段存在、唯一约束字段存在。
- 字符串长度、必填、主键、索引和唯一约束的数据库事实仍来自 `EfModelProjector` 投影和 migration；写侧 descriptor 不能声明一个数据库 schema 中不存在的字段或约束。
- 唯一约束预检只能提升错误体验，不能替代数据库唯一索引。并发场景最终以数据库约束为事实源。
- 写侧内核不执行 DDL，不触发 dev schema sync，不在写入事务中创建表、字段或索引。模型变化仍按 `docs/developer/ef-migration-ai-rules.md` 和三库 migration 流程处理。

事务与 EF execution strategy：

- 默认隔离级别为 `ReadCommitted`；更高隔离级别必须由调用方显式请求，并由 provider capability 校验后才允许执行。
- 第一版不假设 SQL Server snapshot、MySQL repeatable read 或 Oracle read consistency 的细节能提供一致语义；需要依赖强一致时优先使用数据库唯一约束和明确事务边界。
- 如果后续启用 EF Core retry / execution strategy，必须以“整个事务委托”为重试单位，不能在已经写入部分 chunk 后只重试后半段。
- 第一版如无法证明 command 可幂等重放，应禁用写事务自动重试或把瞬时失败归一为稳定失败；重试次数和策略不得进入 descriptor。

EF change tracker 与内存：

- 大批量写入必须控制 `ChangeTracker` 增长；每个 chunk `SaveChangesAsync` 后应有明确的跟踪状态处理策略。
- 如果事务 rollback 或 provider 异常发生，当前 `EfPlatformDbContext` 的 tracked entity 状态可能已经与数据库事实不一致；内核必须 `ChangeTracker.Clear()` 或要求丢弃当前 scope，不允许继续用同一个上下文执行后续写入。
- `ReturnRows=true` 时只能返回内核已确认的字段快照；第一版不依赖数据库生成列，避免 commit 后再读取失败导致调用方误判提交状态。

数据类型、时间与比较语义：

- 值转换以 `ModelDefinition` 的语义类型为入口，继续由 EF provider 映射到底层数据库类型；业务层和 descriptor 不写 provider-specific store type。
- 审计时间统一使用 UTC；DateTime 精度差异不能参与业务相等判断，测试应按容忍范围或“非空且处于调用窗口内”验证。
- 大小写不敏感唯一约束不能默认依赖数据库 collation 一致；内核应先按约束规则规范化候选值，并仍捕获数据库唯一冲突。
- 字符串长度、必填和类型不匹配应尽量在进入 EF 前稳定失败；provider 抛出的长度、not-null、类型转换异常仍必须归一化。

锁、超时与连接状态：

- 死锁、锁等待超时、连接中断、事务状态不明应作为独立 provider 错误族处理，不允许返回业务成功。
- command timeout 属于 provider adapter 或 options 配置，不进入 descriptor；第一版不提供按资源配置超时。
- 同一 scope 内的受控 SQL 必须沿用 `EfPlatformDbContext.Database.CurrentTransaction`，不能另开连接绕过当前事务。

## 8. Standard CRUD 接入关系

Standard CRUD create/update/delete 可以作为首个接入者，但不是写侧内核的上层总入口。

接入方式：

1. Standard CRUD service 继续负责 descriptor 静态校验、模块 validator、delete guard、HTTP 错误模型适配和详情返回组装。
2. Standard CRUD adapter 将 `StandardCrudDescriptor` 映射为 `PlatformWriteDescriptor`。
3. create 映射为 `PlatformInsertBatchCommand`，单条写入也是 batch size 1。
4. update 映射为 `PlatformUpdateBatchCommand`，保留现有“读取现有行 + candidate 唯一约束”的语义。
5. delete 映射为 `PlatformDeleteBatchCommand`，保留 delete policy 和 guard 的前置判断。
6. 写入成功后，Standard CRUD 仍可按 detail 流程返回字段和 summary；该响应组装不属于写侧内核。

其他模块未来可直接面向 `IPlatformPersistenceOperations`：

- 模块应用服务先完成业务规则与权限判断。
- 应用服务构造本模块的 `PlatformWriteDescriptor` 或通过模块内 adapter 获取 descriptor。
- 调用写侧内核完成持久化。

本设计不改动当前 Standard CRUD 查询内核实施范围：

- Standard CRUD List、导出、display value、字段投影仍按 `platform-query` 读侧设计推进。
- 写侧 descriptor 不包含筛选、排序、分页、投影、display value。
- `platform-query` 不承接 create/update/delete 的事务或 rollback 语义。

## 9. 数据与状态

输入来源：

- Standard CRUD descriptor 或模块自有写侧 descriptor。
- API request body 或模块应用服务生成的 command。
- 当前用户、租户、时间等写入上下文。
- EF model definition 中的字段、主键、索引、审计字段信息。

运行时状态：

- 当前 DI scope 的 `EfPlatformDbContext`。
- 当前平台事务上下文。
- 当前 batch 的 validation result、chunk 进度和 provider capability。

持久化事实：

- 第一版不新增数据库表，不新增迁移。
- 所有写入仍落到现有 EF shared-type entity 表。
- 审计字段写入到模型已有字段，例如 `CreatedAt / UpdatedAt`；不存在字段时不隐式新增。

状态一致性要求：

- 同一 batch 要么全部提交，要么全部 rollback。
- 事务失败后，已处理 chunk 不得残留。
- cancellation 后如果未 commit，数据库状态应与操作前一致。
- result 不能把已 rollback 的操作报告为成功。

## 10. 错误处理与失败信号

稳定错误建议：

- `PlatformPersistence.UnknownField`：输入字段不在 insert/update 白名单中。
- `PlatformPersistence.RequiredFieldMissing`：必填字段缺失或被清空。
- `PlatformPersistence.UniqueConflict`：唯一约束冲突。
- `PlatformPersistence.ValueTooLong`：字符串或二进制值超过模型定义或数据库列长度。
- `PlatformPersistence.TypeMismatch`：输入值无法按模型字段类型写入。
- `PlatformPersistence.DeleteNotSupported`：资源未声明删除能力。
- `PlatformPersistence.RowNotFound`：更新或删除目标不存在。
- `PlatformPersistence.TransactionAlreadyActive`：第一版不允许重复 Begin。
- `PlatformPersistence.ProviderCapabilityUnsupported`：provider 不支持当前能力。
- `PlatformPersistence.LockTimeout`：数据库锁等待超时。
- `PlatformPersistence.DeadlockDetected`：数据库检测到死锁并中止本事务。
- `PlatformPersistence.TransactionStateUnknown`：连接中断或 provider 报告事务状态不明，需要 smoke 和日志定位。
- `PlatformPersistence.RollbackFailed`：rollback 本身失败，需要日志和 smoke 定位。

失败信号是什么：

- 批量写入中前半批已写入，后半批失败，但 API 返回整体成功。
- 事务失败或 cancellation 后数据库仍残留已处理 chunk。
- Standard CRUD 或模块绕过写侧内核直接访问别的模块持久化细节。
- 写侧 descriptor 出现 `Steps`、`RollbackSteps`、`ProviderFallbackSql`、`TransactionMode` 等执行流程字段。
- provider 差异被静默吞掉，例如 Oracle 不支持某能力却自动走未验证 SQL。
- 唯一冲突只在某个 provider 返回泛化 500，无法定位到约束。
- 更新时未知字段被静默忽略，导致调用方以为写入成功。
- `CreatedAt / UpdatedAt` 接受客户端值而不是由内核覆盖。
- `platform-query` 设计或实现被加入写入事务、批量写入、rollback 逻辑。
- rollback 后同一个 `EfPlatformDbContext` 仍保留已保存实体的 tracked 状态，并继续参与后续写入。
- 写侧实现依赖某个 provider 的 collation、DateTime 精度或默认隔离级别，导致三库结果不一致。
- EF execution strategy 只重试了部分 chunk，造成重复插入或部分提交误报。

## 11. 回滚与降级思路

设计文档层面：

- 如本设计方向不采用，可删除本文件，并从 `docs/architecture/ai-first-design-check-targets.txt` 移除对应登记行。

未来实现层面：

- Standard CRUD create/update/delete 在迁移期间保留现有 `IStandardCrudRepository` 直接写入路径，写侧内核通过 adapter 和 DI 注册逐步接入。
- 若写侧内核在某个 provider 上失败，可把 Standard CRUD adapter 切回原 EF repository 路径，同时保留读侧 `platform-query` 不受影响。
- 若批量写入出现一致性风险，先关闭批量入口，只保留单条 create/update/delete 走旧路径。
- 若 provider capability 或异常归一化不足，回滚 provider-specific normalizer，不影响 descriptor 与 command 模型。
- 若中立主键生成器迁移有风险，Standard CRUD 可暂时继续使用现有 `platform-crud/ObjectIds`，写侧内核接入延后。

## 12. 验证方案

本设计落地后的最小验证建议：

```powershell
dotnet build server\platform-persistence-operations\PlatformPersistence.Operations.csproj -nr:false -v minimal
dotnet build apps\host-api\HostApi.csproj -nr:false -v minimal
dotnet test tests\PlatformPersistenceOperations.Tests\PlatformPersistenceOperations.Tests.csproj -nr:false -v minimal
dotnet test tests\PlatformCrud.Tests\PlatformCrud.Tests.csproj --filter StandardCrud -nr:false -v minimal
powershell -ExecutionPolicy Bypass -File .\scripts\validate-ai-first-design.ps1 -Path .\docs\superpowers\specs\platform\2026-06-07-platform-persistence-operations-kernel-design.md
powershell -ExecutionPolicy Bypass -File .\scripts\check-ai-first-design-targets.ps1
```

测试覆盖：

- 第一批高风险验证应先覆盖 `DbContext` 显式事务、EF execution strategy 与 manual transaction 的组合关系、rollback 后 `ChangeTracker` 清理。这三项如果失败，会直接破坏批量写入的 all-or-nothing 事实边界。
- 单事务成功提交测试：同一 UoW 内多次 insert/update 全部可见。
- 单事务失败回滚测试：第二个操作失败后，第一个操作不残留。
- 批量插入成功测试：多 chunk 插入后数量、主键、审计字段正确。
- 批量插入失败测试：中间 chunk 唯一冲突后整批 rollback。
- 批量更新字段白名单测试：未知字段、只读字段稳定失败且无数据变化。
- 批量删除字段白名单测试：只接受 ID 列表，不接受任意条件删除。
- 唯一冲突测试：batch 内重复、数据库已有值、并发提交失败均归一为稳定错误。
- 审计字段测试：创建覆盖 `CreatedAt`，更新覆盖 `UpdatedAt`，客户端提交值无效。
- 主键生成测试：缺失 ID 时生成稳定 EID，显式 ID 策略由 descriptor 控制。
- Provider smoke 测试：SQL Server / MySQL / Oracle 的小批量插入、唯一冲突和 rollback。
- Cancellation 后状态一致性测试：取消发生在 chunk 前、`SaveChangesAsync` 中和 commit 前时均无残留。
- ChangeTracker 清理测试：chunk 失败或 rollback 后同一 scope 不能继续携带脏 tracked 状态。
- 数据库底层异常归一化测试：not-null、长度超限、类型不匹配、死锁或锁等待超时至少覆盖 provider normalizer 的映射。
- 隔离级别与 provider capability 测试：不支持的隔离级别稳定失败，默认 `ReadCommitted` 能成功执行。
- EF execution strategy/manual transaction 测试：启用 retry 配置时不得只重放部分 chunk；无法保证幂等时返回稳定失败。
- AI-first 设计校验脚本：确保入口、边界、验证、失败信号、回滚思路齐备。

本次只新增设计文档，因此实际执行验证只需要运行两条 AI-first 脚本。

## 13. AI-First 入口与边界

入口在哪里：

- 设计入口：`docs/superpowers/specs/platform/2026-06-07-platform-persistence-operations-kernel-design.md`。
- 后续代码入口建议：`IPlatformPersistenceOperations` 和 `IPlatformUnitOfWork`。
- 第一批接入者建议：Standard CRUD create/update/delete adapter。

边界在哪里：

- 写侧内核只做平台级持久化操作，不做查询内核、不做通用 ORM、不做复杂工作流。
- 允许后续设计触碰的区域建议为 `server/platform-persistence-operations`、`server/platform-persistence-efcore/Operations`、Standard CRUD 写侧 adapter 和定向测试。
- 本设计不触碰 `platform-query` 读侧设计，不修改 Standard CRUD List、导出或 display value 范围。
- 元数据和 descriptor 只声明静态边界，不表达执行流程。

如何验证：

- 文档阶段运行 AI-first 设计校验脚本。
- 实现阶段运行 PlatformPersistenceOperations 定向测试、PlatformCrud StandardCrud 写侧回归测试、Host build、provider smoke。
- 关键行为以事务 rollback、批量 all-or-nothing、字段白名单、审计字段、唯一冲突和 cancellation 一致性为准。

失败信号是什么：

- 写入部分成功却被报告为整体成功。
- 事务失败后数据残留。
- 模块绕过写侧内核直接访问别的模块持久化细节。
- descriptor 承载执行流程或 provider fallback 流程。
- provider 差异被静默吞掉。
- Standard CRUD 变成其他模块复用写侧能力的必经路径。

如何回滚：

- 文档层面删除本文件和 AI-first target 登记。
- 实现层面保留并切回现有 Standard CRUD / EF repository 直接写入路径。
- 关闭批量入口，保留单条 create/update/delete 旧路径。
- 写侧内核不影响 `platform-query`，读侧能力可独立保留。

## 14. 后续实施计划建议

本次不创建实施计划。建议在你明确要求后，另写一份独立实施计划，并在计划中先做一致性 review：

- 确认写侧内核项目命名、契约所在目录和 EF adapter 所在目录。
- 确认 `IPlatformObjectIdGenerator` 从 Standard CRUD 迁移到中立位置的方式。
- 确认 Standard CRUD 单条 create/update/delete 迁移顺序。
- 确认 provider smoke 的可用连接环境和最小样本。
- 确认第一版是否暴露批量 API，还是先只在内核测试和 Standard CRUD 内部使用。
