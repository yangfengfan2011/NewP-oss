# 标准 CRUD 写入接管、唯一约束与模板收口设计

## 1. 背景

标准 CRUD 后端内核与 Organization `Person` 试点已经完成首轮落地。当前 `Person` 的 `list / detail / options / disable / delete` 已经由 `PersonsController` 委托 `StandardCrudEndpoint` 接管，但 `create / update` 仍走 `PersonService`。

这不符合标准 CRUD 后端内核的目标。人员资源的创建和更新是最基本的 CRUD 主干，应由标准 CRUD 内核接管；模块只保留领域差异、跨模块集成和特殊动作。与此同时，`PersonNo` 在同一公司下唯一这一类规则不应继续作为 Person 专属样板散落在 service/repository 中，而应评估为标准 CRUD 的通用唯一约束能力。

本阶段先处理两件事：

1. 让 `Person create/update` 切到 `StandardCrudEndpoint`。
2. 在 `server/platform-crud` 中新增标准唯一约束能力，用 descriptor 声明 `PersonNo + CompanyId` 这种 scoped uniqueness。

模板文档和完成后 review 仍然需要做，但必须建立在 create/update 已由标准 CRUD 接管、唯一性能力已平台化之后。

## 2. 当前事实

### 2.1 已有标准 CRUD 能力

`server/platform-crud` 已提供：

- `StandardCrudDescriptor`：声明资源、模型、字段集合、显示字段、摘要块、删除策略和扩展点。
- `EfStandardCrudRepository`：基于 `EfPlatformDbContext` shared-type entity 执行标准读取、创建、更新、启停和删除。
- `StandardCrudService`：编排 `list / detail / options / create / update / disable / delete`。
- `StandardCrudEndpoint`：作为 Controller adapter 门面，避免 Controller 直接依赖仓储。
- 扩展点：query customizer、display value resolver、summary provider、validator、action handler、delete guard。

### 2.2 Person 当前接管矩阵

| 能力 | 当前入口 | 当前状态 | 本阶段目标 |
| --- | --- | --- | --- |
| `list` | `PersonsController` -> `StandardCrudEndpoint.ListAsync` | 已接管 | 保持 |
| `detail` | `PersonsController` -> `StandardCrudEndpoint.GetDetailAsync` | 已接管 | 保持 |
| `options` | `PersonsController` -> `StandardCrudEndpoint.OptionsAsync` | 已接管 | 保持 |
| `disable` | `PersonsController` -> `StandardCrudEndpoint.DisableAsync` | 已接管 | 保持 |
| `delete` | `PersonsController` -> `StandardCrudEndpoint.DeleteAsync` + `PersonCrudDeleteGuard` | 已接管 | 保持 |
| `create` | `PersonsController` -> `PersonService.CreateAsync` | 未接管 | 切到 `StandardCrudEndpoint.CreateAsync` |
| `update` | `PersonsController` -> `PersonService.UpdateAsync` | 未接管 | 切到 `StandardCrudEndpoint.UpdateAsync` |
| `user-link` | `PersonsController` -> `PersonService.LinkUserAsync` | 模块特殊入口 | 保持在 Organization 模块 |

### 2.3 PersonNo 唯一性现状

当前唯一性规则在 `IPersonRepository.ExistsPersonNoAsync(companyId, personNo, excludedPersonId)` 和 `PersonService` 中完成。

这条规则具有明显的通用形状：

- 唯一字段：`PersonNo`
- 作用域字段：`CompanyId`
- 创建时：检查同一 `CompanyId` 下是否存在相同 `PersonNo`
- 更新时：排除当前行，并且即使请求没有提交 `PersonNo`，也要用现有行的 `PersonNo` 与新 `CompanyId` 一起检查
- 比较方式：字符串大小写不敏感

因此它适合作为标准 CRUD 唯一约束能力，而不是继续作为 Person 专属 service 逻辑。

## 3. 目标

- `PersonsController.Create` 委托 `StandardCrudEndpoint.CreateAsync`。
- `PersonsController.Update` 委托 `StandardCrudEndpoint.UpdateAsync`。
- 新增标准 CRUD 唯一约束声明与校验流程。
- `PersonCrudDescriptorFactory` 声明 `PersonNo` 在 `CompanyId` scope 内唯一。
- 更新时标准 CRUD 内核先读取现有行，合并现有值与更新值，再执行唯一性校验。
- `PersonCrudValidator` 不再承载 `PersonNo` 唯一性，只保留无法用标准约束表达的资源专属规则。
- 保持 `user-link` 特殊入口和跨模块摘要 provider 不进入平台内核。
- 形成后续模板文档的真实基线。

## 4. 非目标

- 不接入 `Position / Company / Department` 等第二资源。
- 不删除 `PersonService`；本阶段只把 create/update 主路径切走，`user-link` 和回滚锚点仍保留。
- 不实现数据库唯一索引迁移。
- 不解决并发竞争下的最终一致性问题；本阶段先提供应用层标准校验，后续如需强一致再补数据库索引设计。
- 不把唯一约束写入 metadata 文件。
- 不引入规则 DSL、表达式脚本或元数据执行器。
- 不改变前端页面运行时。

## 5. 唯一约束能力评估

### 5.1 是否适合平台化

适合。

理由：

- 唯一性是 CRUD 写入前最常见的跨资源通用校验，不应每个模块重复实现。
- 它可以被静态声明：哪些字段唯一、在哪些 scope 字段内唯一、如何比较、冲突时返回什么错误。
- 它属于“声明、约束、静态可校验输入”，符合 descriptor 的职责。
- 执行流程仍由 `StandardCrudService` 和 repository 完成，没有把算法下沉到 metadata。
- update 场景的“合并现有行 + 更新字段 + 排除当前 id”可以由代码内核统一处理，减少模块遗漏。

### 5.2 平台化边界

唯一约束进入 `StandardCrudDescriptor`，但只表达约束，不表达执行流程。

建议新增：

```csharp
public sealed class StandardCrudUniqueConstraint
{
    public required string Key { get; init; }
    public required IReadOnlyList<string> Fields { get; init; }
    public IReadOnlyList<string> ScopeFields { get; init; } = [];
    public bool CaseInsensitive { get; init; } = true;
    public bool TrimWhitespace { get; init; } = true;
    public bool IgnoreWhenAnyFieldMissing { get; init; } = false;
    public required string ErrorCode { get; init; }
    public required string Message { get; init; }
    public required string ReasonKey { get; init; }
}
```

并在 `StandardCrudDescriptor` 上新增：

```csharp
public IReadOnlyList<StandardCrudUniqueConstraint> UniqueConstraints { get; init; } = [];
```

Person 声明示例：

```csharp
UniqueConstraints =
[
    new StandardCrudUniqueConstraint
    {
        Key = "personNoWithinCompany",
        Fields = ["PersonNo"],
        ScopeFields = ["CompanyId"],
        CaseInsensitive = true,
        TrimWhitespace = true,
        ErrorCode = "Organization.Person.PersonNoDuplicated",
        Message = "同一公司下人员工号必须唯一。",
        ReasonKey = "organization.person.personNoDuplicated"
    }
]
```

### 5.3 不进入通用能力的内容

以下内容不放进标准唯一约束：

- 复杂条件唯一，例如“仅当状态为某几类时唯一”。
- 依赖外部服务的唯一性，例如跨系统账号名。
- 需要事务锁、数据库索引或最终一致补偿的强并发约束。
- 需要动态规则、脚本或用户可配置表达式的唯一性。

这些场景仍由模块 validator 或后续专门设计承接。

## 6. 写入流程设计

### 6.1 Create 流程

`StandardCrudService.CreateAsync` 调整为：

1. 校验 descriptor。
2. 按 `CreateFields` 裁剪输入字段。
3. 执行标准唯一约束校验。
4. 执行模块 validator。
5. 调用 repository 创建。
6. 按 detail 流程返回标准详情。

唯一性校验失败返回：

```json
{
  "statusCode": 409,
  "code": "Organization.Person.PersonNoDuplicated",
  "message": "同一公司下人员工号必须唯一。",
  "reasonKey": "organization.person.personNoDuplicated"
}
```

### 6.2 Update 流程

`StandardCrudService.UpdateAsync` 必须先读取现有行，再执行写入校验：

1. 校验 descriptor。
2. 按 `UpdateFields` 裁剪输入字段。
3. 读取现有 detail 行；不存在时返回 `StandardCrud.NotFound`。
4. 合并现有行与本次更新字段，得到 candidate row。
5. 由内核覆盖 `UpdatedAt`。
6. 对 candidate row 执行标准唯一约束校验，并排除当前 id。
7. 执行模块 validator。
8. 调用 repository 更新。
9. 按 detail 流程返回标准详情。

这个顺序是关键。对于 Person，如果请求只提交新的 `CompanyId`，没有提交 `PersonNo`，内核仍能用现有 `PersonNo` 和新 `CompanyId` 判断是否冲突。

## 7. 仓储接口设计

`IStandardCrudRepository` 新增通用存在性检查：

```csharp
Task<bool> ExistsAsync(
    StandardCrudDescriptor descriptor,
    IReadOnlyDictionary<string, object?> equalityValues,
    string? excludedId = null,
    CancellationToken cancellationToken = default);
```

职责：

- 只执行等值条件。
- `excludedId` 不为空时排除当前主键。
- 字段比较遵循服务层已规范化后的值。
- 不理解业务语义。

`EfStandardCrudRepository.ExistsAsync` 可先沿用当前 shared-type entity 的内存过滤方式，与现有 list/filter 实现保持一致。后续性能优化可以单独改为表达式查询，不影响服务层契约。

## 8. Person 接管设计

### 8.1 Controller adapter

`PersonsController.Create` 改为：

- 将 `PersonDto` 映射成标准字段字典：
  - `PersonNo`
  - `ENAME`
  - `CompanyId`
  - `DepartmentId`
  - `PositionId`
  - `EmploymentStatus`
  - `IsEnabled`
- 调用 `StandardCrudEndpoint.CreateAsync(PersonCrudDescriptor, values)`。
- 成功时将 `StandardCrudDetail` 映射回既有 `PersonDetailDto` 或 `PersonDto`。
- 失败时使用 `result.Error.StatusCode` 和标准错误体。

`PersonsController.Update` 改为：

- 不传入 `PersonNo`，保持工号只读。
- 传入 `ENAME / CompanyId / DepartmentId / PositionId / EmploymentStatus / IsEnabled`。
- 调用 `StandardCrudEndpoint.UpdateAsync`。
- 成功时映射回既有 DTO。

### 8.2 PersonCrudValidator 收缩

`PersonCrudValidator` 不再检查 `PersonNo` 唯一性。

它可以保留：

- 对无法通过 descriptor 表达的资源专属规则。
- 对特殊字段组合的业务冲突检查。

如果本阶段没有剩余资源专属规则，可以暂时保留空实现或移除注册。优先选择保留空实现并在注释中说明唯一性已经由标准约束承接，避免后续 agent 误把唯一性补回模块 service。

### 8.3 PersonService 保留范围

`PersonService` 本阶段仍保留：

- `user-link` 账号绑定入口。
- 旧 create/update 实现作为短期回滚锚点。
- 模块内测试仍可覆盖旧 service 行为，但新 Controller 契约必须证明 create/update 已走标准 CRUD endpoint。

后续完成 review 后，再决定是否删除或收缩旧 create/update。

## 9. Descriptor 校验规则

`StandardCrudDescriptorValidator` 需要新增唯一约束静态校验：

- `UniqueConstraints[*].Key` 必填且同资源内唯一。
- `Fields` 必须非空。
- `Fields + ScopeFields` 必须存在于资源字段集合中。
- 对 update 有效的唯一约束，其字段和 scope 字段必须能从 `DetailFields` 读取。
- `ErrorCode / Message / ReasonKey` 必填。
- 约束字段不得包含 `IdField`。

这些失败都应返回稳定中文异常，帮助后续 agent 在小上下文内定位 descriptor 写错，而不是在运行时静默漏检。

## 10. 测试设计

### 10.1 PlatformCrud.Tests

新增或扩展测试覆盖：

- create 时 scoped unique 冲突返回 `409`。
- create 时不同 scope 允许相同唯一字段。
- update 时排除当前 id，不因自身冲突。
- update 时 scope 变化会用现有唯一字段重新检查冲突。
- descriptor 唯一约束缺字段时校验失败。
- `ExistsAsync` 排除当前 id 生效。

### 10.2 Organization.Tests

新增或扩展测试覆盖：

- `PersonsController.Create` 通过标准 CRUD endpoint 创建人员。
- `PersonsController.Update` 通过标准 CRUD endpoint 更新人员。
- `PersonNo` 同公司重复创建返回标准 `409` 错误。
- 更新人员公司时，如果目标公司已有相同 `PersonNo`，返回标准 `409` 错误。
- 更新时提交不同 `PersonNo` 不会修改工号。
- `user-link` 特殊入口仍走 `PersonService.LinkUserAsync`。

## 11. 验证方案

实现完成后至少运行：

```powershell
dotnet test .\tests\PlatformCrud.Tests\PlatformCrud.Tests.csproj -nr:false -v minimal
dotnet test .\tests\Organization.Tests\Organization.Tests.csproj -nr:false -v minimal
dotnet build .\apps\host-api\HostApi.csproj -nr:false -v minimal
pnpm --dir .\frontend --filter @icp-modules/organization build
```

如果未改前端代码，仍建议保留 Organization 前端构建 smoke，确认 DTO 返回形态没有破坏页面消费。

## 12. 风险与失败信号

风险：

- update 未合并现有行就做唯一性校验，导致 scope 变化漏检。
- 唯一性能力被设计成 metadata 脚本，突破“强约束元数据 + 清晰代码内核”边界。
- Person create/update 切换后返回 DTO 形态变化，破坏前端或测试。
- 旧 `PersonService` 仍保留，后续 agent 误以为 Controller 仍应走旧 service。
- 应用层唯一校验不能替代数据库唯一索引，并发写入仍可能竞态。

失败信号：

- `PersonNo` 唯一性仍通过 `PersonService` 或 `PersonCrudValidator` 专属逻辑完成。
- `StandardCrudService.UpdateAsync` 没有读取现有行。
- descriptor 唯一约束允许引用不在 `DetailFields` 中的字段。
- Controller 中出现唯一性查询或领域判断。
- 平台内核出现 `PersonNo`、`CompanyId` 的 Person 专属分支。

## 13. 回滚思路

如果 create/update 接管失败：

- 将 `PersonsController.Create/Update` 临时切回 `PersonService.CreateAsync/UpdateAsync`。
- 保留标准唯一约束代码，但从 `PersonCrudDescriptorFactory` 移除 Person 约束声明即可停止生效。
- 不影响已接管的 `list/detail/options/disable/delete`。
- `user-link` 特殊入口不参与本次切换，无需回滚。

如果通用唯一约束方向不成立：

- 删除 `StandardCrudUniqueConstraint`、descriptor 属性、repository `ExistsAsync` 和服务层唯一性调用。
- 将 Person 唯一性校验短期恢复到 `PersonCrudValidator` 或 `PersonService`。
- 保留 create/update endpoint 接管设计，重新评估唯一性扩展点边界。

## 14. 后续模板与 Review

本阶段完成后，再生成两份文档：

- `docs/superpowers/specs/2026-05-04-standard-crud-person-closeout-review.md`
- `docs/superpowers/specs/2026-05-04-standard-crud-resource-template.md`

其中模板必须以“create/update 已由标准 CRUD 接管”和“唯一性通过 descriptor 标准约束声明”为基线，不再把 Person 当前旧 service 写入路径作为推荐模式。

## 15. AI-First 检查清单

- 入口在哪里：
  - `server/platform-crud/Descriptors/StandardCrudUniqueConstraint.cs`
  - `server/platform-crud/Descriptors/StandardCrudDescriptor.cs`
  - `server/platform-crud/Runtime/StandardCrudService.cs`
  - `server/platform-crud/Persistence/EfStandardCrudRepository.cs`
  - `server/modules/Organization/Crud/PersonCrudDescriptorFactory.cs`
  - `server/modules/Organization/Controllers/PersonsController.cs`
- 边界在哪里：
  - descriptor 只声明唯一约束。
  - 唯一性执行流程在 `StandardCrudService`。
  - Person 专属账号绑定仍在 Organization 模块。
  - 平台内核不出现 Person 特判。
- 如何验证：
  - 平台 CRUD 唯一约束单测。
  - Organization Person create/update 契约测试。
  - HostApi 构建。
  - Organization 前端构建 smoke。
- 失败时看什么：
  - update scope 变化是否漏检。
  - 错误码是否稳定。
  - Controller 是否夹带领域规则。
  - descriptor 是否引用了不可读取字段。
- 如何回滚：
  - Controller create/update 切回 `PersonService`。
  - 移除 Person descriptor 的唯一约束声明。
  - 保留其他标准 CRUD 接管能力不动。
