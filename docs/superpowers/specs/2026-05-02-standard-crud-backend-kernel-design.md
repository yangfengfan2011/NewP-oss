# 标准 CRUD 后端内核设计

## 1. 背景

当前平台已经有标准 CRUD 契约设计、显示层 `nav / view / model` 元数据主线，以及 Organization 人员中心试点。人员中心已经证明前端大部分页面骨架可以由运行时元数据和标准列表组件承接，但后端仍然保留大量重复的手写主干：

- `Controller` 重复声明 `list / detail / create / update / disable` 等入口。
- `Service` 重复处理读取、存在性检查、字段映射、保存后刷新详情等流程。
- `Repository` 重复围绕 `EfPlatformDbContext` shared-type entity 做查询、映射和保存。
- 每个模块都需要自己把标准错误、摘要块降级、选项查询和动作结果收口。

这会让后续 AI 开发继续生成大量样板代码，也会让前端运行时生成能力和后端实现方式产生断层。因此需要补一层标准 CRUD 后端代码内核。

本设计以 `Person` 作为首轮试点对象，目标是把通用 CRUD 主干平台化，同时保留人员中心特有的领域规则、跨模块摘要和特殊动作扩展点。

## 1.1 设计基线与推进关系

本设计不是重新定义 CRUD 语义，而是在既有平台设计基础上继续推进“后端代码内核”。

后续实施计划应以本文作为直接输入，同时必须继承以下基线：

1. `docs/designs/platform/standard-crud-contract-design.md`  
   作为标准 CRUD 的契约语义基线，继续定义 `list / detail / create / update / enable-disable / delete / options` 的接口分类、读取模型、删除策略和错误语义。本文不替代它，而是把这些契约推进到后端内核实现层。

2. `docs/designs/platform/metadata-driven-backoffice-ui-design.md`  
   作为前后端元数据边界基线，继续约束 `nav / view / model` 只表达静态 authoring，不承载执行逻辑。后端 CRUD 内核只能消费稳定契约和 descriptor，不能把元数据变成规则脚本。

3. `docs/designs/modules/organization-person-center-pilot-scope-freeze-review.md`  
   作为 Person 试点范围基线，继续确认首轮以人员中心为试点对象，保留账号绑定和授权摘要为模块扩展点，不把权限、规则中心、复杂摘要交互拉入当前主线。

4. `docs/superpowers/specs/2026-05-02-standard-crud-backend-kernel-design.md`  
   也就是本文，作为后续实施计划的直接设计输入。实施计划应从本文拆任务，而不是回到 `standard-crud-contract-design.md` 重新设计一遍 CRUD 语义。

因此，推进顺序应明确为：

```text
standard-crud-contract-design.md
        ↓
standard-crud-backend-kernel-design.md（本文）
        ↓
后端平台化实施计划
        ↓
Person 试点实现
```

## 2. 目标

- 新增平台级标准 CRUD 后端内核设计，承接标准 CRUD 契约的代码实现主线。
- 通过 `StandardCrudDescriptor` 显式声明资源、模型、路由、字段映射、读写能力和扩展点。
- 平台内核负责标准流程：列表、详情、创建、更新、启停、删除、选项查询的主干编排。
- 模块只负责模型专属差异：领域校验、查询扩展、动作处理、摘要块 provider、显示值解析。
- 首轮试点使用 `Person`，减少 `PersonsController / PersonService / EfPersonRepository` 中可复用样板。
- 保持元数据边界：元数据只声明“是什么、归谁、允许什么”，不承载执行流程、规则算法或回滚逻辑。

## 3. 非目标

- 不把所有模型自动暴露成 CRUD API。
- 不引入规则 DSL、表达式脚本或元数据执行器。
- 不在首轮迁移所有模块。
- 不重写平台持久化投影、迁移体系或 `EfPlatformDbContext`。
- 不实现功能授权、对象授权或统一规则中心。
- 不把账号绑定、授权写操作、复杂导入导出、审批流纳入本设计。

## 4. 总体方案

后端平台化采用“标准内核 + 模块扩展点”的方式。

```text
StandardCrudDescriptor
        ↓
StandardCrudRepository
        ↓
StandardCrudService
        ↓
StandardCrudControllerAdapter
        ↓
模块扩展点：
  - query customizer
  - validator
  - mapper
  - action handler
  - summary block provider
  - display value resolver
  - delete policy
```

平台内核不理解 `Person` 的业务含义，只理解标准 CRUD 流程。Person 模块通过 descriptor 告诉平台：

- 资源 key 是什么。
- 后端路由是什么。
- 主键字段、显示字段、状态字段是什么。
- 哪些字段可创建、可更新、只读。
- 列表关键字匹配哪些字段。
- 哪些 summary block 由哪些 provider 装配。
- `disable / delete` 等动作由哪些 handler 或 policy 承接。

## 5. 核心设计

### 5.1 `StandardCrudDescriptor`

`StandardCrudDescriptor` 是模块注册标准 CRUD 资源的唯一入口。它是代码契约，不是元数据文件。

首轮建议字段：

- `ResourceKey`
- `ModuleId`
- `ModelName`
- `Route`
- `IdField`
- `DisplayField`
- `EnabledField`
- `ListFields`
- `DetailFields`
- `CreateFields`
- `UpdateFields`
- `OptionLabelFields`
- `DeletePolicy`
- `QueryCustomizer`
- `Validator`
- `SummaryProviders`
- `ActionHandlers`
- `DisplayValueResolvers`

设计边界：

- descriptor 可以引用 `ModelDefinition` 和 metadata 中的静态字段信息。
- descriptor 不保存动态规则结果。
- descriptor 不承载脚本或表达式。
- descriptor 里的行为点必须是代码实现类，例如 validator、handler、provider。

### 5.2 通用仓储内核

`StandardCrudRepository` 基于现有 `EfPlatformDbContext` 和 `ModelDefinition` 工作。

首轮职责：

- 按模型名访问 shared-type entity。
- 按主键读取。
- 按字段集合投影 list/detail。
- 支持基础过滤、分页、排序。
- 支持创建、更新、启停字段切换。
- 支持按删除策略执行删除。
- 返回字典型持久化行或标准 record，不直接返回模块领域对象。

仓储内核只处理数据库层通用问题，不处理 PersonNo 同公司唯一、账号绑定等领域规则。

### 5.3 通用服务内核

`StandardCrudService` 是主要流程编排层。

标准读取流程：

1. 根据 descriptor 解析查询条件。
2. 调用仓储获取列表或详情基础数据。
3. 解析显示值字段。
4. 装配 summary block。
5. 返回标准读取模型。

标准写入流程：

1. 读取 descriptor 中允许写入的字段。
2. 执行模块 validator。
3. 调用仓储创建或更新。
4. 重新读取最新 detail。
5. 返回最新 detail。

标准动作流程：

1. 校验目标存在。
2. 找到 action handler。
3. 执行动作。
4. 按动作声明返回 detail、列表刷新信号或空响应。

### 5.4 Controller Adapter

首轮不强求直接使用一个完全泛型的 ASP.NET Controller 替换所有现有 Controller。

推荐先采用 `Controller Adapter`：

- `PersonsController` 保持稳定路由和对外契约。
- Controller 内部委托给 `IStandardCrudEndpoint<Person>` 或等价门面。
- 账号绑定等特殊入口仍保留在 `PersonsController` 中。

这样可以降低路由与 OpenAPI 行为变化风险。等 Person 试点稳定后，再评估是否抽出真正的通用 Controller 基类。

## 6. 模块扩展点

### 6.1 查询扩展

`QueryCustomizer` 负责资源专属查询语义。

Person 首轮需要：

- `keyword` 匹配 `PersonNo` 和 `Name`。
- `companyId`、`departmentId`、`employmentStatus` 映射到对应字段。

平台只负责执行已声明的过滤条件，不猜测关键字应该匹配哪些字段。

### 6.2 校验扩展

`Validator` 负责 create/update/delete/action 前的领域校验。

Person 首轮需要：

- 创建时同一公司下 `PersonNo` 唯一。
- 更新时 `PersonNo` 只读。
- 更新时公司、部门、岗位字段必须是合法 GUID 或空。
- 所有标准更新行为都由 CRUD 内核自动维护 `UpdatedAt`；资源显式声明该字段可写时，内核覆盖为当前时间，不信任请求输入。
- 删除时根据删除策略和引用关系判断是否允许。

### 6.3 摘要块扩展

`SummaryBlockProvider` 负责只读摘要块装配。

Person 首轮需要：

- `userLink`
- `authorizationSummary`

摘要块统一返回：

```json
{
  "status": "ready | notConfigured | dependencyUnavailable",
  "data": {}
}
```

依赖失败时只影响对应摘要块，不应拖垮人员详情主字段读取。

### 6.4 动作扩展

`ActionHandler` 负责非普通 create/update 的资源动作。

Person 首轮需要：

- `disable`

第二阶段需要：

- `delete`

`disable` 可以由通用状态字段 handler 承接；如果 Person 后续需要停用前复杂校验，再替换成 Person 专属 handler。

`disable` 属于状态更新行为，因此也必须复用标准更新路径自动刷新 `UpdatedAt`，避免 Controller 或模块服务重复维护审计字段。

### 6.5 删除策略扩展

删除必须显式声明策略，不允许平台根据是否存在接口来猜测。

首轮设计仍沿用标准 CRUD 契约的三档：

- `none`
- `conditional`
- `auditable`

本设计把 `delete` 放入第二阶段。Person 第二阶段默认采用 `conditional`：只有满足人员未被关键外部关系阻断时才允许删除。若账号绑定、授权摘要或后续业务引用存在阻断，必须返回标准业务冲突错误。

## 7. Person 试点范围

### 7.1 第一阶段

第一阶段只平台化低风险读取主干：

- `list`
- `detail`
- `options` 的基础形态
- summary block 装配
- Controller adapter 基础入口

Person 仍保留：

- create/update/disable 的现有实现
- user-link 特殊入口
- 专属 service/repository 中尚未迁移的写操作逻辑

第一阶段目标是验证后端标准读取模型是否能支撑人员中心前端运行时页面。

### 7.2 第二阶段

第二阶段平台化写操作和删除：

- `create`
- `update`
- `disable`
- `delete`

Person 的领域差异通过 validator、action handler、delete policy 承接。

第二阶段完成后，`PersonService` 中的通用读写主干应明显减少，只保留人员专属规则和跨模块集成。

## 8. 与现有文件的关系

首轮设计预期新增平台内核目录，建议放在：

```text
server/platform-crud/
```

建议新增测试目录：

```text
tests/PlatformCrud.Tests/
```

Person 试点涉及但不立即大规模删除的文件：

- `server/modules/Organization/Controllers/PersonsController.cs`
- `server/modules/Organization/Services/PersonService.cs`
- `server/modules/Organization/Persistence/Ef/EfPersonRepository.cs`
- `server/modules/Organization/Repositories/IPersonRepository.cs`
- `tests/Organization.Tests/PersonListContractTests.cs`
- `tests/Organization.Tests/PersonServiceTests.cs`

设计原则是先让新内核并行接管部分能力，再逐步收缩旧实现。不得为了追求 diff 好看而一次性删除可回滚锚点。

## 9. 错误处理

平台 CRUD 内核统一使用用户可见消息契约或兼容结构返回错误。

首轮错误类别：

- 字段校验失败：`400`
- 目标不存在：`404`
- 业务规则冲突：`409`
- 依赖暂不可用：`503`
- 系统异常：`500`

模块 validator 和 handler 不应返回裸字符串异常。它们应返回稳定错误码、消息 key 和必要参数，方便前端统一提示和后续多语言处理。

## 10. 验证方案

### 10.1 平台内核测试

新增 `PlatformCrud.Tests`，覆盖：

- descriptor 注册校验。
- list 查询、分页、排序。
- detail 读取。
- create/update 字段白名单。
- disable 状态字段切换。
- delete policy 为 `none / conditional / auditable` 时的行为。
- summary provider 异常降级。

### 10.2 Person 试点测试

继续使用 `Organization.Tests`，覆盖：

- Person list 由标准内核返回当前前端所需字段。
- Person detail 可装配 `userLink / authorizationSummary`。
- Person create/update 仍保持同公司工号唯一。
- Person disable 返回刷新后的 detail。
- Person delete 第二阶段按 `conditional` 策略返回成功或标准冲突错误。

### 10.3 构建验证

每个实现阶段至少运行：

```powershell
dotnet build .\server\platform-crud\PlatformCrud.csproj -nr:false -v minimal
dotnet test .\tests\PlatformCrud.Tests\PlatformCrud.Tests.csproj -nr:false -v minimal
dotnet test .\tests\Organization.Tests\Organization.Tests.csproj -nr:false -v minimal
dotnet build .\apps\host-api\HostApi.csproj -nr:false -v minimal
```

如阶段改动影响前端契约，还需运行：

```powershell
pnpm --dir .\frontend --filter @icp-modules/organization build
```

## 11. 风险与失败信号

失败信号：

- descriptor 开始承载脚本、表达式或流程编排。
- 平台内核为了 Person 特判字段或业务规则。
- Person 迁移后仍保留大量重复 list/detail/create/update 主干。
- 删除未声明策略就暴露入口。
- summary provider 异常导致 detail 主体读取失败。
- 通用 Controller 改动导致既有路由或返回结构破坏。

主要风险：

- 通用化过早覆盖写操作，导致领域规则边界不清。
- shared-type entity 字段类型和 DTO 类型之间映射复杂度被低估。
- 删除策略如果没有测试，会重新变成模块隐式约定。

## 12. 回滚思路

- 第一阶段失败时，停用 Person 的标准 CRUD descriptor，恢复现有 `PersonService / EfPersonRepository` 读取主干。
- 第二阶段失败时，只回退 create/update/disable/delete 的标准内核接管，保留第一阶段只读能力。
- 若平台 CRUD 内核抽象方向不成立，删除 `server/platform-crud` 和 `tests/PlatformCrud.Tests`，不影响既有 Organization 模块可运行性。
- Person 的 `user-link` 特殊入口始终保留在模块内，避免平台化失败影响账号绑定降级能力。

## 13. 分阶段实施建议

1. 建立 `server/platform-crud` 与 `tests/PlatformCrud.Tests`。
2. 实现 descriptor、只读仓储和只读服务主干。
3. 用 Person 接入 list/detail/summary provider。
4. 做第一阶段一致性 review。
5. 实现 create/update/disable/delete 主干与扩展点。
6. 用 Person 接入写操作、停用和条件删除。
7. 回填验证文档与迁移边界说明。

## 14. AI-First 检查清单

- 入口在哪里：
  - `server/platform-crud`
  - `tests/PlatformCrud.Tests`
  - Organization 模块 Person descriptor
- 边界在哪里：
  - 平台内核只做标准 CRUD 主干
  - Person 模块保留领域规则、跨模块摘要和特殊入口
  - 元数据不承载执行逻辑
- 如何验证：
  - 平台 CRUD 内核单测
  - Organization Person 试点测试
  - HostApi 构建
  - 必要时前端 Organization 构建
- 失败信号是什么：
  - 平台内核出现 Person 特判
  - 删除策略隐式化
  - summary block 失败拖垮详情主体
  - descriptor 变成规则脚本容器
- 如何回滚：
  - 逐阶段停用 descriptor 接管
  - 保留现有 Person 模块服务作为回滚锚点
  - 不影响已有元数据和前端页面运行时主线
