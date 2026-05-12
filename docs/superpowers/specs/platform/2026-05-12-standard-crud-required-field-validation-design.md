# 标准 CRUD 必填字段校验设计

## 1. 背景

标准 CRUD 已完成 Person 资源试点和路由自动映射，`/api/organization/persons` 的标准读写入口已经由平台 mapper 委托 `StandardCrudEndpoint` 与 `StandardCrudService` 执行。当前内核已经具备字段白名单、创建/更新审计字段、唯一约束、删除守卫、显示值和摘要扩展点，但写入必填校验仍没有平台级承接。

现状里有三类风险：

- 标准 CRUD 创建或更新时只过滤允许字段，不判断必填字段是否缺失、为 `null` 或空白字符串。
- Person 专属 `PersonCrudValidator` 当前明确留空，如果把 `PersonNo / ENAME / IsEnabled` 这类基础必填规则重新写回模块 validator，会削弱标准 CRUD 的可复制性。
- `ModelDefinition.Field.Required` 已经能表达存储非空投影，但它和标准 CRUD 写入契约之间尚未建立受控关系；直接让 CRUD 内核读取所有模型 metadata 又会引入新的隐式耦合。

因此，本设计把“标准 CRUD 写入必填字段”作为 descriptor 声明和内核执行能力。descriptor 只表达字段、作用范围和错误契约，执行顺序、缺失判断和失败返回由 `StandardCrudService` 统一处理。

## 2. 目标

- 新增标准 CRUD 必填字段声明模型，使资源可以显式声明 create/update 写入必填字段。
- 由 `StandardCrudService` 在唯一约束和模块专属 validator 之前执行必填校验。
- Person 试点先声明模型当前已非空的 `PersonNo`、`ENAME`、`IsEnabled`，不改变 `CompanyId`、`DepartmentId`、`EmploymentStatus` 的 optional 口径。
- 必填校验返回稳定 `StandardCrudError`，避免数据库异常或模块 validator 成为第一失败信号。
- 保持 metadata 只表达“字段是什么、是否允许为空”等静态事实；缺失判断、写入流程和错误返回仍在代码内核。

## 3. 非目标

- 本轮不做数据库 `NOT NULL` 迁移，也不修改既有 `Person` metadata 中组织归属字段的可空性。
- 本轮不做 `PersonNo` 数据库唯一索引；并发唯一性应另开设计。
- 本轮不把 Company / Department / Position 迁移到标准 CRUD。
- 本轮不新增前端表单 required 自动渲染协议；前端可以继续使用现有表单校验，后端以内核校验兜底。
- 本轮不把复杂组合规则、跨字段规则、外部依赖校验放进 metadata；这些仍属于模块 validator 或专属 service。

## 4. 范围

涉及目录：

- `server/platform-crud/Descriptors/`
- `server/platform-crud/Runtime/`
- `tests/PlatformCrud.Tests/`
- `server/modules/Organization/Crud/PersonCrudDescriptorFactory.cs`
- `tests/Organization.Tests/PersonStandardCrudResourceTests.cs`
- `docs/verification/organization-module-verification.md`

不涉及目录：

- `apps/host-api/`，路由映射入口不变。
- `frontend/modules/organization/`，本轮不改变 UI 行为。
- EF migrations 工程，本轮不做数据库结构收紧。

## 5. 设计约束

- descriptor 是标准 CRUD API 写入契约的受控入口；必填声明必须跟随 descriptor，而不是分散到 Controller 或模块 service。
- `ModelDefinition.Field.Required` 可以作为设计依据和后续生成来源，但本轮不让 `StandardCrudService` 直接依赖具体模块 metadata provider。
- 必填校验必须发生在仓储写入前，错误必须稳定、可测试、可被前端识别。
- 更新当前按“提交字段更新”处理，不升级为完整 replace 语义；遗漏字段不应阻塞更新，清空必填字段才应失败。
- `false` 是合法 bool 值，`0` 是合法数值；只有缺失、`null` 和空白字符串按缺失处理。
- 内核执行顺序应保持可解释：descriptor 静态校验 -> 字段白名单 -> 审计字段补齐 -> 必填校验 -> 唯一约束 -> 模块 validator -> 仓储写入。

## 6. 总体方案

推荐方案：在 `StandardCrudDescriptor` 增加 `RequiredFields` 声明，并新增 `StandardCrudRequiredField` 与 `StandardCrudRequiredFieldScope`。

候选方案对比：

| 方案 | 做法 | 优点 | 风险 |
| --- | --- | --- | --- |
| A. 直接从模型 metadata 推导 | CRUD 内核读取 `ModelDefinition.Required` 后自动校验 | 单一 metadata 真源更强 | 让 CRUD 内核依赖模型 provider 和模块装载细节，容易形成隐式规则；无法区分存储非空、表单必填和写入必填 |
| B. descriptor 显式声明 | `StandardCrudDescriptor.RequiredFields` 声明字段与错误契约，内核统一执行 | 与现有 `UniqueConstraints` 模式一致，边界清晰，易测试和回滚 | 需要模块 descriptor 暂时重复声明与 metadata 一致的必填字段 |
| C. 模块 validator 承接 | 每个资源在 `IStandardCrudValidator` 中写必填规则 | 改动最小 | 平台能力不可复制，后续资源会继续产生样板规则 |

选择 B。后续如果要减少重复，可以增加 descriptor factory helper，从 `ModelDefinition` 生成默认 required 声明，再由资源显式覆盖；但这应作为下一轮工具化设计，不并入本轮内核能力。

## 7. 关键模型

新增 descriptor 模型：

```csharp
public sealed class StandardCrudRequiredField
{
    public required string Field { get; init; }
    public StandardCrudRequiredFieldScope Scope { get; init; } = StandardCrudRequiredFieldScope.CreateAndUpdate;
    public required string ErrorCode { get; init; }
    public required string Message { get; init; }
    public required string ReasonKey { get; init; }
}

public enum StandardCrudRequiredFieldScope
{
    CreateOnly,
    UpdateOnly,
    CreateAndUpdate
}
```

`StandardCrudDescriptor` 新增：

```csharp
public IReadOnlyList<StandardCrudRequiredField> RequiredFields { get; init; } = [];
```

Person 试点声明：

- `PersonNo`：create 必填。当前 `PersonNo` 不在 update 白名单中，更新时不会被修改。
- `ENAME`：create/update 必填。创建必须提交，更新时如果提交空值则失败。
- `IsEnabled`：create/update 必填。`false` 是合法值；缺失或 `null` 失败。

`CompanyId`、`DepartmentId`、`PositionId`、`EmploymentStatus` 暂不声明为必填，因为 `modules/Organization/metadata/models/Person.model.json` 当前明确为 nullable，且 `OrganizationModelMetadataProviderTests.Person_OrganizationFields_AreProjectedAsOptional` 已锁定 optional 口径。

## 8. 核心流程

### 8.1 创建流程

1. `CreateAsync` 校验 descriptor。
2. 按 `FieldSet.CreateFields` 过滤请求字段。
3. 内核补齐创建审计字段。
4. 对 `Scope` 包含 create 的 required field 执行缺失判断。
5. 若缺失，返回对应 `StandardCrudError`，不进入唯一约束和仓储写入。
6. 若通过，继续执行唯一约束、模块 validator 和仓储写入。

### 8.2 更新流程

1. `UpdateAsync` 先读取现有详情，保持当前 not found 行为。
2. 按 `FieldSet.UpdateFields` 过滤请求字段。
3. 内核补齐更新审计字段。
4. 对 `Scope` 包含 update 的 required field 只检查“本次提交的字段”。如果字段未提交，不因历史数据为空阻塞本次更新。
5. 若提交字段被置为 `null` 或空白字符串，返回对应 `StandardCrudError`。
6. 若通过，继续构造 candidate 并执行唯一约束、模块 validator 和仓储写入。

### 8.3 缺失判断

必填字段按以下规则判断：

- 字段不存在：create 失败；update 不检查未提交字段。
- 值为 `null`：失败。
- 值为 `string` 且 `Trim()` 后为空：失败。
- 值为 `false`、`0`、非空集合或其他对象：通过。

本轮不做字符串自动 trim 写回；唯一约束已有自己的大小写和空白规范化，必填校验只判断缺失。

## 9. 接口与集成点

后端集成点：

- `StandardCrudDescriptor.RequiredFields`：资源声明入口。
- `StandardCrudDescriptorValidator.ValidateRequiredFields()`：静态校验 required 声明字段必须在字段集合中，错误契约不能为空，重复字段与 scope 组合必须失败。
- `StandardCrudService.ValidateRequiredFields()`：运行时执行入口。
- `PersonCrudDescriptorFactory.Create()`：Person 试点声明必填字段。

前端集成点：

- 本轮不新增 API，也不要求前端修改。
- 前端现有保存失败处理继续消费标准错误响应。
- 后续如要把 required 声明下发给表单 runtime，应单独设计 descriptor/options schema 或 metadata view schema 的投影关系。

模块集成点：

- `PersonCrudValidator` 继续保留为空实现，用于未来人员专属组合规则。
- `PersonUserLinkService` 不受影响；账号绑定不是标准 CRUD 写入字段。

## 10. 数据与状态

本轮不新增数据库表、列或迁移。

运行时状态只来自 descriptor 实例：

- descriptor 是写入必填契约真源。
- metadata 是字段存储和投影事实来源，暂不在运行时被 CRUD 服务直接读取。
- 数据库存量空值不在本轮批量修复；更新时未提交的 required field 不触发历史数据失败。

一致性要求：

- required field 字段必须在 `FieldSet.CreateFields` 或 `FieldSet.UpdateFields` 中至少一个相关 scope 下可写。
- required field 字段必须在 `DetailFields` 中可读取，便于 mutation 返回和后续验证定位。
- 若字段只声明 create 必填，可以不在 update 白名单中。

## 11. 错误处理与风险

必填失败返回：

- HTTP 状态由 endpoint 沿用 `StandardCrudError.StatusCode`。
- 推荐状态码：`400`。
- 错误 code、message、reasonKey 由 descriptor 显式声明，保证资源可给出业务语义文案。

主要风险：

- 如果把 `CompanyId` 等当前 optional 字段也声明为必填，会和 metadata 测试、历史迁移口径冲突。
- 如果 update 校验合并后的 candidate，历史空数据会导致无关字段更新失败。
- 如果 required 声明不做静态校验，可能出现字段不在白名单中却永远无法满足的契约。

控制方式：

- Person 首批只声明 `PersonNo / ENAME / IsEnabled`。
- update 只校验本次提交字段是否被清空。
- descriptor validator 锁定字段归属、重复声明和错误契约。

回滚方式：

- 移除 `StandardCrudDescriptor.RequiredFields` 声明与运行时调用即可恢复旧行为。
- Person 若出现兼容问题，可先清空 `PersonCrudDescriptorFactory` 的 `RequiredFields`，保留平台类型和测试再分步恢复。
- 本轮不含数据库迁移，回滚不涉及数据结构。

## 12. 验证方案

平台 CRUD 测试：

```powershell
dotnet test .\tests\PlatformCrud.Tests\PlatformCrud.Tests.csproj --filter "StandardCrudDescriptorTests|StandardCrudServiceWriteTests" -nr:false -v minimal
dotnet test .\tests\PlatformCrud.Tests\PlatformCrud.Tests.csproj -nr:false -v minimal
```

Organization 测试：

```powershell
dotnet test .\tests\Organization.Tests\Organization.Tests.csproj --filter "PersonStandardCrudResourceTests|OrganizationModelMetadataProviderTests" -nr:false -v minimal
dotnet test .\tests\Organization.Tests\Organization.Tests.csproj -nr:false -v minimal
```

Host 构建：

```powershell
dotnet build .\apps\host-api\HostApi.csproj -nr:false -v minimal
```

设计校验：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\validate-ai-first-design.ps1 -Path .\docs\superpowers\specs\platform\2026-05-12-standard-crud-required-field-validation-design.md
powershell -ExecutionPolicy Bypass -File .\scripts\check-ai-first-design-targets.ps1
```

## 13. 未决问题

本设计没有需要阻塞实施的未决问题。

已明确的后续独立议题：

- 是否把 required 声明自动投影给前端标准表单 runtime。
- 是否提供从 `ModelDefinition.Required` 生成 descriptor required fields 的 helper。
- 是否把 Company / Department / Position 迁入标准 CRUD 后复用同一 required field 能力。

## 14. 实施拆分建议

1. 平台 descriptor 扩展：新增 `StandardCrudRequiredField`、`StandardCrudRequiredFieldScope` 和 `RequiredFields`。
2. 静态校验：扩展 `StandardCrudDescriptorValidator`，覆盖空字段、重复声明、字段不在字段集合、错误契约缺失等失败信号。
3. 运行时校验：在 `StandardCrudService.CreateAsync` 和 `UpdateAsync` 中加入 required 校验，覆盖 create 缺失、create 空白、update 清空和 bool false 合法场景。
4. Person 试点：在 `PersonCrudDescriptorFactory` 声明 `PersonNo / ENAME / IsEnabled`，补充 Organization descriptor 测试。
5. 验证记录：更新 `docs/verification/organization-module-verification.md`，记录必填校验平台化验证命令与结论。

## 15. AI-First 最小检查清单

- 入口在哪里：`server/platform-crud/Descriptors/StandardCrudDescriptor.cs`、`server/platform-crud/Runtime/StandardCrudService.cs` 和 `server/modules/Organization/Crud/PersonCrudDescriptorFactory.cs`。
- 边界在哪里：本轮只做标准 CRUD 写入必填校验，不改数据库迁移、不改前端表单 runtime、不迁移其他 Organization 主数据 Controller。
- 如何验证：运行 `PlatformCrud.Tests`、`Organization.Tests`、`HostApi` 构建，以及本设计的 AI-first 校验脚本。
- 失败信号是什么：必填字段缺失仍进入仓储写入；必填错误由数据库异常暴露；Person 必填规则回流到 `PersonCrudValidator`；`CompanyId` 等当前 optional 字段被误收紧；update 遗漏字段导致历史空数据阻塞。
- 如何回滚：清空资源 descriptor 的 `RequiredFields` 可先恢复资源行为；移除平台 required 校验调用可整体恢复旧标准 CRUD 写入行为；由于无数据库迁移，回滚不需要数据结构处理。
