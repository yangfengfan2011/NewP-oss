# 标准 CRUD 后端内核实施计划 V2 一致性 Review

## 1. Review 范围

本 review 复核修订后的：

- `docs/superpowers/plans/2026-05-02-standard-crud-backend-kernel.md`

并对照以下基线：

1. `docs/superpowers/specs/2026-05-02-standard-crud-backend-kernel-design.md`
2. `docs/designs/platform/standard-crud-contract-design.md`
3. `docs/designs/platform/standard-crud-backend-kernel-plan-consistency-review.md`
4. 当前 `server/platform-modeling/Definitions/ModelDefinition.cs`
5. 当前 `server/platform-modeling/Definitions/FieldDefinition.cs`

本 review 只检查计划层一致性，不进入代码实现。

## 2. 总体结论

修订后的 V2 计划已经吸收上一轮 review 的 P0 阻断项，当前**具备进入正式实现的计划条件**。

这不是说实现风险已经消失，而是说：

- 设计要求的扩展点已在计划中有接口、测试和调用链。
- 第一阶段已包含 `options`。
- Person list 的 `CompanyName / DepartmentName` 已通过 `DisplayValueResolver` 承接。
- list filter/sort 与 Person keyword 查询已有任务。
- `conditional delete` 已通过 `IStandardCrudDeleteGuard` 明确 fail closed。
- `disable` 已先实现状态字段切换，再切 Controller。
- EF 测试辅助已使用当前 `ModelDefinition / FieldDefinition` 签名。

因此，当前不再存在已知计划级阻断项。

## 3. P0 修订复核

### 3.1 扩展点接入

上一轮问题：扩展点只出现在文件清单，没有实现和调用。

V2 计划已经补齐：

- `IStandardCrudQueryCustomizer`
- `IStandardCrudDisplayValueResolver`
- `IStandardCrudSummaryProvider`
- `IStandardCrudValidator`
- `IStandardCrudActionHandler`
- `IStandardCrudDeleteGuard`

并在 `StandardCrudService` 中通过构造函数注入后实际调用。

结论：已修正。

### 3.2 options 基础形态

上一轮问题：只创建 `StandardCrudOptionItem`，没有 options 流程。

V2 计划已补齐：

- `IStandardCrudRepository.OptionsAsync`
- `IStandardCrudService.OptionsAsync`
- `StandardCrudEndpoint.OptionsAsync`
- EF 仓储 options 测试
- Person Controller `GET options`

结论：已修正。

### 3.3 Person list 显示值

上一轮问题：标准内核返回字段缺少 `CompanyName / DepartmentName`。

V2 计划已补齐：

- `IStandardCrudDisplayValueResolver`
- `PersonCrudDisplayValueResolver`
- `CompanyId -> CompanyName`
- `DepartmentId -> DepartmentName`
- `PositionId -> PositionName`

结论：已修正。

### 3.4 list filter/sort

上一轮问题：仓储忽略 filters 和 sort。

V2 计划已补齐：

- `StandardCrudQueryCondition`
- `IStandardCrudQueryCustomizer`
- EF 仓储 `ApplyConditions`
- EF 仓储 `ApplySort`
- Person `keyword / companyId / departmentId / employmentStatus` 查询映射

结论：已修正。

### 3.5 conditional delete

上一轮问题：`Conditional` 实际无条件删除。

V2 计划已补齐：

- `IStandardCrudDeleteGuard`
- `DeleteGuardMissing` fail closed 行为
- Person `PersonCrudDeleteGuard`
- 账号绑定阻断删除测试

结论：已修正。

### 3.6 disable

上一轮问题：Controller 委托标准内核时，标准内核仍返回未启用错误。

V2 计划已补齐：

- `DisableAsync_SetsEnabledFieldFalse` 测试
- `EnabledField` 状态字段切换
- 完成服务主干后才切 `PersonsController`

结论：已修正。

### 3.7 ModelDefinition / FieldDefinition 签名

上一轮问题：测试片段使用过期构造签名。

V2 计划已补齐：

- `PlatformCrudModelDefinitions.Person()`
- `ModelKind.Entity`
- `FieldKind.Scalar / Reference / Audit`
- `FieldValueType.Text / ObjectId / Enum / Boolean / DateTime`
- `StoreType`
- `IsAuditField`

结论：已修正。

## 4. 仍需执行时关注的风险

以下不是计划级阻断项，但实现时必须留意：

1. EF shared-type entity 上的 `contains` 查询在 InMemory 与真实 Provider 上行为可能不同。实现时若真实 Provider 构建失败，应优先把查询转换收口在仓储内核，不让模块拼 SQL。
2. `PersonCrudDisplayValueResolver` 当前会逐行读取公司、部门、岗位，首轮可接受；后续如出现性能问题，应在平台或模块侧补批量 resolver，不要把 N+1 优化塞进元数据。
3. `PersonCrudValidator` 只覆盖当前计划中的最小字段规则；后续新增复杂规则应继续通过 validator/action handler 扩展，不应进入 descriptor 或 metadata。
4. `auditable delete` 仍只是策略枚举，当前 Person 试点只验证 `conditional`。后续若启用 `auditable`，需要单独设计审计行为。

## 5. 建议下一步

可以进入实现阶段。

推荐执行方式：

1. 先按 Task 1 到 Task 4 完成平台内核只读与扩展点。
2. 做一次阶段内 review，确认没有 Person 特判进入平台内核。
3. 再执行 Task 5 到 Task 8 接 Person 和写操作。
4. 最后执行 Task 9 验证与文档回填。

## 6. 结论

本次 V2 一致性 review 的最终结论是：

1. V2 计划已吸收上一轮 P0 阻断项。
2. 当前计划与后端内核设计、标准 CRUD 契约和 Person 试点边界保持一致。
3. 当前可以进入实现。
4. 实现阶段仍需按任务 checkpoint 做阶段 review，防止平台内核出现 Person 特判。

## 7. AI-First 检查清单

### 入口在哪里

- `docs/superpowers/plans/2026-05-02-standard-crud-backend-kernel.md`
- `docs/superpowers/specs/2026-05-02-standard-crud-backend-kernel-design.md`

### 边界在哪里

- 本 review 只复核 V2 计划。
- 不进入代码实现。
- 不扩大到其他模块迁移。

### 如何验证

- 对照上一轮 P0 列表逐项检查 V2 计划。
- 对照当前建模类型签名检查测试辅助代码。
- 对照 Person 试点边界检查 user-link、summary、delete 是否仍是代码扩展点。

### 失败信号是什么

- 执行时平台内核出现 Person 专属判断。
- 执行时跳过 Task 4 的扩展点调用链，直接切 Person Controller。
- 执行时把 `conditional delete` 简化为直接删除。

### 如何回滚

- 本 review 只新增文档；如实现阶段发现判断不成立，直接更新本文并回到计划修订。
