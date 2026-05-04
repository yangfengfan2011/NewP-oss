# 标准 CRUD Person 收口与接入模板设计

## 1. 背景

`docs/superpowers/specs/2026-05-02-standard-crud-backend-kernel-design.md` 已完成标准 CRUD 后端内核与 Organization `Person` 试点的首轮落地。当前代码已经具备平台级 descriptor、仓储、服务、endpoint 门面，以及 Person 资源的查询扩展、显示值解析、摘要块、校验和删除守卫。

这一轮不继续扩展到 `Position / Company / Department` 等第二资源，也不直接调整运行时代码。下一阶段先完成两个收口动作：

1. 对 `Person` 试点做完成后一致性 review，明确当前接管矩阵、保留锚点、风险和后续可收缩范围。
2. 把已验证的标准 CRUD 接入方式沉淀成模板文档，供后续 agent 复制到新资源时使用。

这样可以避免把 `Person` 当前的过渡状态误认为长期架构，也避免后续试点在没有模板的情况下重复理解同一套接入边界。

## 2. 当前事实

### 2.1 标准 CRUD 内核现状

`server/platform-crud` 已提供以下能力：

- `StandardCrudDescriptor` 声明资源、模型、字段集合、显示字段、摘要块、删除策略和扩展点。
- `EfStandardCrudRepository` 基于 `EfPlatformDbContext` shared-type entity 执行标准读取、写入、启停和删除。
- `StandardCrudService` 编排 `list / detail / options / create / update / disable / delete`。
- `StandardCrudEndpoint` 作为 Controller adapter 门面，避免 Controller 直接依赖仓储。
- 扩展点包括 query customizer、display value resolver、summary provider、validator、action handler、delete guard。

### 2.2 Person 当前接管矩阵

| 能力 | 当前入口 | 当前状态 | 收口判断 |
| --- | --- | --- | --- |
| `list` | `PersonsController` -> `StandardCrudEndpoint.ListAsync` | 已接管 | 保持标准 CRUD 接管 |
| `detail` | `PersonsController` -> `StandardCrudEndpoint.GetDetailAsync` | 已接管 | 保持标准 CRUD 接管 |
| `options` | `PersonsController` -> `StandardCrudEndpoint.OptionsAsync` | 已接管 | 保持标准 CRUD 接管 |
| `disable` | `PersonsController` -> `StandardCrudEndpoint.DisableAsync` | 已接管 | 保持标准 CRUD 接管 |
| `delete` | `PersonsController` -> `StandardCrudEndpoint.DeleteAsync` + `PersonCrudDeleteGuard` | 已接管 | 保持条件删除接管 |
| `create` | `PersonsController` -> `PersonService.CreateAsync` | 未切到 endpoint | 本阶段只记录，不直接切换 |
| `update` | `PersonsController` -> `PersonService.UpdateAsync` | 未切到 endpoint | 本阶段只记录，不直接切换 |
| `user-link` | `PersonsController` -> `PersonService.LinkUserAsync` | 模块特殊入口 | 必须继续留在 Organization 模块内 |

这意味着“标准 CRUD 写能力已在内核具备”与“Person Controller 的 create/update 已切到标准 endpoint”不是同一件事。当前代码中 create/update 仍走 `PersonService`，因此下一阶段必须把这个状态显式写入 review 结果，不能让计划文档里的完成口径掩盖实际接管边界。

### 2.3 PersonService 保留职责

`PersonService` 当前仍承接：

- `create/update` 的人员领域写入入口。
- 同公司 `PersonNo` 唯一性校验。
- `CompanyId / DepartmentId / PositionId` 从 DTO 字符串进入领域前的解析边界。
- `PersonNo` 更新只读约束。
- `user-link` 账号绑定入口。
- 旧读取路径和摘要构造逻辑，作为试点回滚锚点仍有价值。

其中 `user-link` 与账号目录、授权摘要属于 Organization 模块跨模块集成，不应进入平台 CRUD 内核。`create/update` 是否继续切到标准 CRUD endpoint，需要先补齐 validator 对“更新时公司变化导致现有 PersonNo 冲突”的覆盖，再单独设计，不在本轮直接改代码。

## 3. 目标

- 产出一份 Person 标准 CRUD 接管完成后一致性 review 文档。
- 产出一份标准 CRUD 新资源接入模板文档。
- 明确 Person 当前过渡状态：哪些能力已由标准 CRUD 接管，哪些仍由模块服务持有。
- 明确 `create/update` 暂不在本轮切换，避免无设计地改变人员写入路径。
- 明确下一次接入新资源前必须遵守的文件、扩展点、测试、验证和回滚要求。
- 保持本轮 diff 聚焦文档，不触碰运行时代码。

## 4. 非目标

- 不把 `Position / Company / Department` 接入标准 CRUD。
- 不修改 `PersonsController` 的 create/update 路径。
- 不删除 `PersonService / IPersonRepository / EfPersonRepository` 中的回滚锚点。
- 不实现审计删除。
- 不新增规则 DSL、元数据执行器或授权规则中心。
- 不改变前端页面运行时。

## 5. 设计输出

### 5.1 Person 收口 Review 文档

新增：

```text
docs/superpowers/specs/2026-05-04-standard-crud-person-closeout-review.md
```

该文档用于记录实际代码与原设计之间的收口结论。建议包含：

- 当前接管矩阵。
- `PersonService` 保留职责。
- `PersonCrudDescriptorFactory` 的字段白名单、摘要块和删除策略审查。
- `PersonCrudValidator` 与 `PersonCrudDeleteGuard` 的边界审查。
- Controller adapter 映射层审查。
- 验证命令和当前已知 warning。
- 风险、失败信号和回滚方式。

review 文档必须直接引用当前文件路径和行为，不写抽象口号。若发现计划文档与代码事实不一致，应在 review 中记录差异，不通过静默改代码来“抹平”差异。

### 5.2 标准 CRUD 接入模板文档

新增：

```text
docs/superpowers/specs/2026-05-04-standard-crud-resource-template.md
```

该文档用于指导后续资源接入。建议按“新资源要做什么”和“禁止做什么”组织：

- 适用条件：普通主数据、稳定 DTO、可声明字段白名单、删除策略可显式表达。
- 不适用条件：复杂工作流、审批、跨聚合事务、强授权规则、需要专属导入导出的资源。
- 必备文件：
  - `server/modules/<Module>/Crud/<Resource>CrudDescriptorFactory.cs`
  - `server/modules/<Module>/Crud/<Resource>CrudQueryCustomizer.cs`
  - `server/modules/<Module>/Crud/<Resource>CrudDisplayValueResolver.cs`
  - `server/modules/<Module>/Crud/<Resource>CrudValidator.cs`
  - `server/modules/<Module>/Crud/<Resource>CrudDeleteGuard.cs`，仅 `conditional` 删除需要
  - Controller adapter 映射方法
  - 模块 DI 注册
  - 单元测试和契约测试
- descriptor 模板字段。
- query customizer 模板。
- validator 模板。
- summary provider 模板。
- delete guard 模板。
- controller adapter 模板。
- 验证命令。
- 失败信号。
- 回滚方式。

模板文档应强调：descriptor 只声明“是什么、归谁、允许什么”，不能承载执行流程、分支算法、切换逻辑或回滚逻辑。

## 6. 一致性 Review 规则

Person 收口 review 必须逐项回答以下问题：

1. `StandardCrudDescriptor` 是否只声明字段、资源和扩展点，没有脚本、表达式或流程编排。
2. `server/platform-crud` 是否没有出现 Person 专属字段判断或 Organization 业务规则。
3. Person 的跨模块摘要是否通过 provider 降级，失败时不拖垮 detail 主体。
4. 删除是否显式声明 `Conditional` 并注册 `PersonCrudDeleteGuard`，无 guard 时是否 fail closed。
5. `UpdatedAt` 是否由标准 CRUD 内核在 update/disable 类行为中统一维护。
6. Controller adapter 是否只做请求转发和 DTO 映射，不承载领域规则。
7. `PersonService` 保留职责是否都是模块领域、跨模块集成或回滚锚点。
8. create/update 未切换到 endpoint 是否已被文档化，且没有被模板误写成标准要求。
9. 现有测试是否覆盖 list/detail/options/disable/delete、summary 降级、validator 和 delete guard。
10. 回滚路径是否仍成立：停用 descriptor 接管后，Person 模块服务仍能作为读取或写入锚点恢复。

## 7. create/update 后续策略

本轮设计明确：**不直接切换 Person create/update 到 `StandardCrudEndpoint`。**

原因：

- 当前 `PersonService.UpdateAsync` 会在公司变化时用原有 `PersonNo` 重新检查同公司唯一性。
- 当前 `PersonCrudValidator.ValidateUpdateAsync` 只阻断 `PersonNo` 直接修改，还没有在标准 update 路径中读取现有行并校验“公司变化 + 原工号”的冲突。
- 若直接切换 Controller，可能让部分领域校验从写入路径中消失。

后续若要切换 create/update，必须另开设计，至少补齐：

- validator 或 action handler 读取现有行的方式。
- update 时 `PersonNo` 只读但仍参与唯一性校验的规则。
- 标准错误码与现有 `BadRequest` 兼容策略。
- Controller 返回 DTO 与标准 detail 的映射策略。
- 定向测试证明切换前后行为一致。

## 8. 验证方案

本轮主要验证文档与现状一致，不验证新代码行为。完成 review 和模板文档后，应运行：

```powershell
dotnet test .\tests\PlatformCrud.Tests\PlatformCrud.Tests.csproj -nr:false -v minimal
dotnet test .\tests\Organization.Tests\Organization.Tests.csproj -nr:false -v minimal
dotnet build .\apps\host-api\HostApi.csproj -nr:false -v minimal
pnpm --dir .\frontend --filter @icp-modules/organization build
```

如果只是新增文档且没有代码改动，可以把验证范围降为：

```powershell
git diff --check
```

同时在最终报告中说明未重跑后端和前端构建的原因。

## 9. 风险与失败信号

风险：

- 把当前 Person 过渡态误写成所有资源的推荐模板。
- 模板过度依赖 Person 的账号绑定和授权摘要，导致简单资源接入成本变高。
- 在没有设计的情况下切换 create/update，丢失人员领域校验。
- review 文档只复述计划，不核对实际代码。

失败信号：

- 模板要求每个资源都实现 summary provider。
- descriptor 示例包含规则算法、执行顺序或回滚步骤。
- review 没有明确写出 create/update 仍走 `PersonService`。
- 后续新资源为了接入标准 CRUD 直接修改 Host 或 Shell 内部结构。
- 删除策略没有显式声明就暴露 delete 入口。

## 10. 回滚思路

本轮只新增文档，回滚方式是删除本设计产生的 review 和模板文档，不影响运行时代码。

如果后续基于 review 发现 Person 接管存在问题，回滚仍沿用原标准 CRUD 设计：

- 停用 `PersonsController` 对 `StandardCrudEndpoint` 的读取或动作接管。
- 恢复 `PersonService / EfPersonRepository` 对相应能力的主路径。
- 保留 `user-link` 特殊入口不变，避免账号绑定降级能力受影响。

## 11. AI-First 检查清单

- 入口在哪里：
  - `docs/superpowers/specs/2026-05-04-standard-crud-person-closeout-review.md`
  - `docs/superpowers/specs/2026-05-04-standard-crud-resource-template.md`
- 边界在哪里：
  - 本轮只做 review 和模板文档。
  - 不切换 Person create/update。
  - 不接入第二资源。
- 如何验证：
  - 文档自检。
  - `git diff --check`。
  - 如有代码改动，再运行平台 CRUD、Organization、HostApi 和前端模块构建。
- 失败时看什么：
  - review 是否脱离当前代码事实。
  - 模板是否把 Person 专属逻辑写成通用要求。
  - 是否出现未设计的运行时代码改动。
- 如何回滚：
  - 删除本轮新增文档。
  - 不需要数据库、Host 或前端回滚。
