# 标准 CRUD 后端内核设计与实施计划一致性 Review

## 1. Review 范围

本 review 的目标是确认：

**`docs/superpowers/plans/2026-05-02-standard-crud-backend-kernel.md` 是否已经与标准 CRUD 后端内核设计、标准 CRUD 契约设计、Person 试点边界和当前代码现状保持一致，并具备进入正式实现的条件。**

本次 review 以以下文档和代码事实为基线：

1. `docs/superpowers/specs/2026-05-02-standard-crud-backend-kernel-design.md`
2. `docs/superpowers/plans/2026-05-02-standard-crud-backend-kernel.md`
3. `docs/designs/platform/standard-crud-contract-design.md`
4. `docs/designs/platform/metadata-driven-backoffice-ui-design.md`
5. `docs/designs/modules/organization-person-center-pilot-scope-freeze-review.md`
6. `server/platform-modeling/Definitions/ModelDefinition.cs`
7. `server/platform-modeling/Definitions/FieldDefinition.cs`

本 review 不进入代码实现，不重写平台设计，只检查设计与计划之间的可执行一致性。

## 2. 总体结论

当前实施计划的方向与设计主线一致，但**尚不具备直接进入正式实现的条件**。

主要原因不是方向错误，而是计划存在几类会在执行阶段直接造成偏差或失败的问题：

1. 设计要求的核心扩展点在计划中只列出文件名，没有真正实现或接入。
2. 第一阶段设计明确包含 `options` 基础形态，但计划没有对应任务。
3. `list` 的过滤、排序、显示值解析和 Person projection 承接不足，无法验证标准 CRUD 契约的核心列表语义。
4. 第二阶段计划引入 `delete`，但没有真正实现 `conditional` 删除守卫。
5. 写操作和 `disable` 在计划中仍停留在 stub，后续却让 Person Controller 委托标准内核，执行后会得到 `DisableNotEnabled`。
6. 计划中的 EF 测试片段使用了过期的 `ModelDefinition / FieldDefinition` 构造签名，与当前代码不一致。

因此，本 review 结论是：

**计划需要修订后再进入实现。**

## 3. 阻断项

### 3.1 P0：扩展点只声明未实现，不能满足“标准内核 + 模块扩展点”设计

设计中明确要求 `QueryCustomizer / Validator / SummaryProviders / ActionHandlers / DisplayValueResolvers` 是模块差异的代码扩展点。

实施计划在文件结构中列出了：

- `IStandardCrudQueryCustomizer`
- `IStandardCrudValidator`
- `IStandardCrudActionHandler`
- `IStandardCrudDeleteGuard`
- `IStandardCrudDisplayValueResolver`

但后续任务中没有完整实现这些接口，也没有把它们接入 `StandardCrudDescriptor` 或 `StandardCrudService` 的流程。尤其是：

- `QueryCustomizer` 没有被创建或调用。
- `DisplayValueResolver` 没有被创建或调用。
- `Validator / ActionHandler / DeleteGuard` 在 Task 6 只出现在文件清单中，没有可执行代码。

这会导致平台内核只能做最基础字典读写，无法承接 Person 的关键差异。它与设计中“模块只注册扩展点，平台负责编排”的核心方案不一致。

**修订要求：**

计划必须增加一组任务，先实现并测试扩展点注册与调用链，再接入 Person。

### 3.2 P0：第一阶段漏掉 `options` 基础形态

设计第一阶段明确包括：

- `list`
- `detail`
- `options` 的基础形态
- summary block 装配

标准 CRUD 契约也明确 `options` 是七类基础契约之一。

当前计划只创建 `StandardCrudOptionItem` 类型，但没有：

- `IStandardCrudService.GetOptionsAsync`
- `IStandardCrudRepository.OptionsAsync`
- `StandardCrudEndpoint.OptionsAsync`
- `GET /api/<resource>/options` 的 adapter 设计
- PlatformCrud options 测试
- Person options 接入验证

这会导致第一阶段完成后仍无法验证 `options` 契约。

**修订要求：**

计划应在第一阶段增加 `options` 任务，至少支持按 `OptionLabelFields` 从当前模型返回 `{ value, label }`。

### 3.3 P0：Person list 经标准内核后会丢失 `CompanyName / DepartmentName`

Person 当前列表 DTO 需要 `CompanyName / DepartmentName`。设计也要求显示值解析由模块扩展点承接。

计划中的 `PersonCrudDescriptorFactory` 只把以下字段放入 list fields：

- `EID`
- `PersonNo`
- `ENAME`
- `CompanyId`
- `DepartmentId`
- `PositionId`
- `EmploymentStatus`
- `IsEnabled`

但 `PersonsController.MapListItem` 又从标准 CRUD 返回行中读取：

- `CompanyName`
- `DepartmentName`

由于计划没有实现 `DisplayValueResolver`，也没有在 list projection 中补出这两个字段，Person list 通过标准内核后会返回空公司名和部门名，无法满足人员中心既有前端契约。

**修订要求：**

计划必须在第一阶段加入显示值解析任务，并为 Person 明确实现：

- `CompanyId -> CompanyName`
- `DepartmentId -> DepartmentName`
- 后续可扩展 `PositionId -> PositionName`

### 3.4 P0：查询过滤与排序没有实现，无法满足标准 list 契约

标准 CRUD 契约要求 list 支持：

- `page`
- `pageSize`
- `sortBy`
- `sortDirection`
- view query 字段

设计要求 Person 的 `keyword / companyId / departmentId / employmentStatus` 通过 `QueryCustomizer` 映射。

计划中的 `EfStandardCrudRepository.ListAsync` 只实现了：

- `CountAsync`
- `Skip`
- `Take`

没有实现：

- filters
- sort
- keyword
- Person 查询扩展

虽然 `PersonsController` 把 filters 传入了 `StandardCrudListQuery`，但仓储完全忽略这些值。这样第一阶段无法验证标准 CRUD 契约的核心列表语义。

**修订要求：**

计划应补充：

- 平台基础过滤与排序测试。
- `QueryCustomizer` 将 `keyword / companyId / departmentId / employmentStatus` 转为受控查询条件。
- EF shared-type entity 查询时应用这些条件。

### 3.5 P0：第二阶段 `conditional delete` 没有真正实现条件守卫

设计明确要求 Person 第二阶段 `delete` 使用 `conditional`，且删除前要按引用关系或模块规则判断是否允许。

计划中 `DeleteAllowedAsync` 的行为是：

1. 如果不是 `None`，直接调用 `_repository.DeleteAsync`。
2. 找不到返回 404。
3. 找到就删除成功。

这意味着 `Conditional` 与 `Auditable` 在计划中没有实际差异，也没有调用 `IStandardCrudDeleteGuard`。

这与标准 CRUD 契约中的删除策略分档不一致，也会把 Person 的 `conditional` 删除变成无条件删除。

**修订要求：**

计划必须增加：

- `IStandardCrudDeleteGuard.CanDeleteAsync`
- delete guard 失败返回 `409` 标准业务冲突错误。
- Person delete guard 至少检查账号绑定关系。
- `Conditional` 无 guard 时应 fail closed，而不是直接删除。

### 3.6 P0：`disable` 被委托给标准内核，但标准内核仍返回未启用错误

计划 Task 6 要求 `DisableAsync` 先作为 stub 返回：

- `StandardCrud.DisableNotEnabled`

Task 7 又要求 `PersonsController.Disable` 在 `_standardCrudEndpoint` 存在时委托给标准内核。

Task 8 注册 DI 后 `_standardCrudEndpoint` 会存在，因此真实请求会走标准内核的 `DisableAsync`，得到未启用错误，而不是停用成功。

这与设计中第二阶段平台化 `disable` 的目标不一致，也会破坏已有人员停用能力。

**修订要求：**

计划必须先实现 `disable` 的状态字段切换，再让 `PersonsController` 委托标准内核。推荐顺序：

1. 实现 `EnabledField` 通用 action handler。
2. 用测试证明 `DisableAsync` 会把 `IsEnabled` 写为 `false` 并返回最新 detail。
3. 再改 Person Controller 委托。

### 3.7 P0：EF 测试片段与当前 `ModelDefinition / FieldDefinition` 构造签名不一致

当前代码中：

```csharp
public sealed record ModelDefinition(
    string Name,
    string TableName,
    ModelKind Kind,
    IReadOnlyList<FieldDefinition> Fields,
    IReadOnlyList<IndexDefinition> Indexes);

public sealed record FieldDefinition(
    string Name,
    string StoreName,
    FieldKind Kind,
    FieldValueType ValueType,
    string StoreType,
    bool Required,
    bool IsPrimaryKey,
    bool IsAuditField);
```

计划中的 `StandardCrudEfRepositoryTests` 使用的是旧式构造：

```csharp
new ModelDefinition(Name: ..., TableName: ..., Fields: ..., Indexes: ...)
new FieldDefinition("EID", "EID", FieldValueType.ObjectId, Required: true, IsPrimaryKey: true)
```

这会在 Task 4 直接编译失败。

**修订要求：**

计划必须改为当前签名，例如显式提供：

- `ModelKind.Entity`
- `FieldKind.Builtin` 或当前项目实际使用的字段 kind
- `StoreType`
- `IsAuditField`

## 4. 非阻断但应修订的问题

### 4.1 P1：错误模型与标准 CRUD 契约不完全一致

标准 CRUD 契约建议错误结构包含：

- `code`
- `message`
- `reasonKey`
- `fieldErrors`

计划中的 `StandardCrudError` 只有：

- `StatusCode`
- `Code`
- `Message`
- `Args`

这可以作为兼容结构起步，但计划应显式说明为何暂不实现 `reasonKey / fieldErrors`，或直接补齐字段。否则后续前端用户消息契约会继续分叉。

### 4.2 P1：计划中提到 `PersonCrudQueryCustomizer / PersonCrudValidator / SummaryProvider` 文件，但任务步骤没有实现这些文件

文件结构声明会创建：

- `PersonCrudQueryCustomizer.cs`
- `PersonCrudValidator.cs`
- `PersonUserLinkSummaryProvider.cs`
- `PersonAuthorizationSummaryProvider.cs`

但任务步骤实际只完整创建了 `PersonCrudDescriptorFactory.cs`。这会造成执行者误以为扩展点已经覆盖，实际没有。

### 4.3 P1：Controller adapter 使用可选具体类型注入，执行顺序有风险

计划在 `PersonsController` 中加入：

```csharp
StandardCrudEndpoint? standardCrudEndpoint = null
```

并在 Task 8 才注册 DI。这个顺序在单元测试中可能没问题，但 Host 构建和运行时行为依赖 ASP.NET Core 对可选构造参数的解析细节，风险偏高。

更稳妥的方式是：

- Task 5 不改 Controller 构造函数，只建立 endpoint 和 descriptor。
- Task 8 先完成 DI 注册。
- 再集中切换 Controller 委托。

### 4.4 P1：计划没有明确写操作字段白名单如何应用

设计要求 create/update 只能写 descriptor 中允许的字段。计划虽然有 `CreateFields / UpdateFields`，但 `StandardCrudService` 的 create/update stub 没有实现字段过滤，EF 仓储也直接写入传入 values。

修订时应确保服务层先按 `CreateFields / UpdateFields` 过滤输入，再进入仓储。

## 5. 当前一致项

尽管存在阻断项，计划在以下方面与设计保持一致：

1. 已明确以 `2026-05-02-standard-crud-backend-kernel-design.md` 为直接实施输入。
2. 新增工程位置 `server/platform-crud` 与设计一致。
3. 新增测试工程 `tests/PlatformCrud.Tests` 与设计一致。
4. 采用 Controller adapter 而不是直接替换通用 Controller，符合设计的风险控制。
5. 分两阶段推进，与设计中的第一阶段只读、第二阶段写操作和删除方向一致。
6. 没有试图把规则写入元数据，仍保持代码扩展点方向。
7. 没有把 ConfigCenter、Authorization、Audit 一起迁移，范围控制正确。

## 6. 建议修订顺序

建议不要在当前计划上直接开工，而是先按以下顺序修订计划：

1. 修正 `ModelDefinition / FieldDefinition` 测试片段。
2. 补齐第一阶段 `options` 任务。
3. 补齐 `QueryCustomizer` 与基础 filter/sort 任务。
4. 补齐 `DisplayValueResolver` 与 Person `CompanyName / DepartmentName` projection 任务。
5. 补齐 `SummaryProvider` 的实际实现和注册任务。
6. 补齐 create/update 字段白名单和返回最新 detail 的任务。
7. 补齐 `disable` 通用状态切换任务。
8. 补齐 `DeleteGuard` 与 Person conditional delete 测试。
9. 重新做一次计划一致性 review。

## 7. 结论

本次 review 的最终结论是：

1. 标准 CRUD 后端内核设计方向正确，计划总体分层方向也正确。
2. 当前计划存在多个 P0 级执行阻断项。
3. 当前不建议直接进入实现。
4. 应先修订 `docs/superpowers/plans/2026-05-02-standard-crud-backend-kernel.md`，再重新 review。

## 8. AI-First 检查清单

### 入口在哪里

- `docs/superpowers/specs/2026-05-02-standard-crud-backend-kernel-design.md`
- `docs/superpowers/plans/2026-05-02-standard-crud-backend-kernel.md`
- `docs/designs/platform/standard-crud-contract-design.md`

### 边界在哪里

- 本 review 只检查设计与计划一致性。
- 不进入代码实现。
- 不重新设计标准 CRUD 语义。
- 不扩大到其他模块迁移。

### 如何验证

- 对照设计目标逐项检查计划任务是否覆盖。
- 对照当前代码签名检查计划片段是否可编译。
- 对照 Person 试点边界检查 delete、summary、user-link 是否越界或漏项。

### 失败信号是什么

- 扩展点只出现在文件清单，不出现在任务步骤。
- 计划声明平台化某能力，但服务实现仍是 stub。
- `conditional delete` 没有 delete guard。
- 列表查询参数传入后被仓储忽略。
- Person 列表显示值在标准内核路径下丢失。

### 如何回滚

- 本 review 只新增文档；如结论不再适用，直接更新或回退本文。
- 不影响已提交的设计 spec。
- 不影响后续重写实施计划。
