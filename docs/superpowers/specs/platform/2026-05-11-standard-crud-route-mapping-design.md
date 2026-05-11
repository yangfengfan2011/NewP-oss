# 标准 CRUD 路由自动映射设计

## 1. 背景

人员中心后端已经把 `list / detail / create / update / disable / delete / options` 委托给 `StandardCrudEndpoint`，但标准入口仍由 `PersonsController` 手写暴露。这样虽然验证了统一 API 内核可用，但仍留下三类问题：

- 标准 CRUD 路由、字段映射和兼容 DTO 分散在 Controller 与 descriptor 之间，后续资源复制时容易重新生成样板代码。
- 如果完全改成 `/api/platform/crud/{resourceKey}` 这类抽象通用 URL，对外 API 会失去模块语义，前端、调试、文档和权限排查都会更难读。
- 如果每个资源都保留一个空壳 Controller，又会把平台化要消除的重复代码重新固化下来。

因此，本设计选择“对外保留模块语义路由，对内统一使用平台标准 CRUD 内核”的折中方案：标准 CRUD 路由由平台根据 `StandardCrudDescriptor.Route` 自动映射，模块只在存在非标准能力时编写专属 Controller。

## 2. 目标

- 新增标准 CRUD 资源注册表，统一收集模块声明的 `StandardCrudDescriptor`。
- 新增标准 CRUD 路由自动映射入口，按 descriptor 中的业务路由映射标准 API。
- 将 `StandardCrudDescriptor.Route` 定为标准 CRUD 路由唯一真源，不新增独立映射配置文件。
- 标准 CRUD 资源不再需要手写空壳 Controller。
- 非标准模块能力继续保留模块 Controller，例如人员账号绑定、用户候选项、组织树等。
- 为人员中心后端后续退场 `PersonsController` 中的标准 CRUD 方法提供设计依据。

## 3. 非目标

- 不把所有 API 都改成 `/api/platform/crud/{resourceKey}` 这种平台抽象 URL。
- 不在本轮设计中实现权限、对象授权、规则中心或导出任务中心。
- 不自动暴露所有模型；只有显式注册 descriptor 的资源才会映射标准 CRUD API。
- 不把人员 `user-link`、`user-options`、组织树等非标准能力塞入标准 CRUD。
- 不在本设计中改前端页面、DTO 生成器或数据库迁移。

## 4. 范围

涉及平台内核：

- `server/platform-crud/Descriptors/`
- `server/platform-crud/Runtime/`
- `server/platform-crud/Extensions/`
- `server/platform-crud/Abstractions/`
- `tests/PlatformCrud.Tests/`

涉及试点模块：

- `server/modules/Organization/Crud/PersonCrudDescriptorFactory.cs`
- `server/modules/Organization/OrganizationModule.cs`
- `server/modules/Organization/Controllers/PersonsController.cs`
- `tests/Organization.Tests/`

本设计只定义平台路由映射与人员中心迁移边界，不直接修改其他模块的 Controller。

### 4.1 人员中心删除与退场范围

自动路由映射落地后，人员中心不应继续保留标准 CRUD 样板代码。以下代码应在迁移任务中删除、拆分或改名，避免形成“看似有用但实际绕过平台内核”的第二入口。

后端明确删除或拆分：

- 删除 `server/modules/Organization/Controllers/PersonsController.cs` 中的标准 CRUD actions：`GetList / Get / Create / Update / Disable / Delete / Options`。
- 删除 `PersonsController` 中只服务旧 DTO 兼容的私有映射方法：`MapListItem / MapDetail / MapCreateValues / MapUpdateValues / MapMutationDto / ReadString* / ReadBool / ReadDateTime / MutationDetailMissing`。
- 将 `SearchUserOptions` 与 `LinkUser` 从 `PersonsController` 拆到人员账号绑定专属 Controller，例如 `PersonUserLinksController`；该 Controller 只承载非标准动作。
- 删除 `server/modules/Organization/Services/IPersonService.cs` 中的标准 CRUD 方法：`GetByIdAsync / GetDetailAsync / GetListAsync / CreateAsync / UpdateAsync / DisableAsync`。
- 删除或重命名 `server/modules/Organization/Services/PersonService.cs`。迁移后只保留人员账号绑定服务，例如 `IPersonUserLinkService / PersonUserLinkService`，其职责限于 `LinkUserAsync`。
- 删除 `server/modules/Organization/Repositories/IPersonRepository.cs` 与 `server/modules/Organization/Persistence/Ef/EfPersonRepository.cs`。人员是否存在应通过标准 CRUD descriptor 对应的读取能力校验，不再维护 Person 专属仓储。
- 删除 `server/modules/Organization/DTOs/PersonListItemDto.cs`、`server/modules/Organization/DTOs/PersonDetailDto.cs` 和 `server/modules/Organization/DTOs/PersonSummaryBlockDto.cs`，前提是前端已切到 `StandardCrudListResult / StandardCrudDetail / StandardCrudSummaryBlock`。
- 删除 `server/modules/Organization/Models/Person.cs`，前提是人员账号绑定服务已不再构造或读取 `Person` 领域对象。Person 的运行时结构继续来自 `modules/Organization/metadata/models/Person.model.json` 和 `PersonCrudDescriptorFactory`。

后端保留：

- 保留 `PersonCrudDescriptorFactory`、`PersonCrudQueryCustomizer`、`PersonCrudDisplayValueResolver`、`PersonUserLinkSummaryProvider`、`PersonAuthorizationSummaryProvider`、`PersonCrudValidator`、`PersonCrudDeleteGuard`。
- 保留 `PersonUserLink` 模型、DTO、仓储和 EF 实现，因为账号绑定是人员中心专属关系，不属于标准 CRUD 主对象。
- 保留 `IdentityUserOptionDto` 与 `LinkPersonUserRequest`，它们属于人员账号绑定入口。
- 保留生成目录中的 `Generated/PersonDto.g.cs`，除非后续另有“未使用生成 DTO 清理”设计；它当前属于模型 DTO 生成主线，不在本路由映射设计中删除。

测试明确删除或改名：

- 删除 `PersonListContractTests` 中针对 `PersonsController` 标准 CRUD action 与旧 DTO 形状的测试。
- 将 descriptor、query customizer、delete guard、summary provider 相关测试迁移到新的 `PersonStandardCrudResourceTests` 或等价测试类。
- 删除 `PersonServiceTests` 中覆盖人员标准 CRUD service/repository 的测试。
- 将账号绑定相关测试迁移到 `PersonUserLinkServiceTests`。

前端明确收缩：

- `frontend/modules/organization/src/services/organizationService.ts` 中人员标准 CRUD 客户端应改为直接消费标准返回模型，删除旧数组兼容、旧 DTO 兼容读取和保存后再 `refreshPersonDetail` 的桥接逻辑。
- `PersonListItemDto / PersonDetailDto / PersonListResultDto` 等前端旧 DTO 类型应改名或替换为标准 CRUD 读取模型适配类型。
- `PersonCreatePage.tsx / PersonDetailPage.tsx` 是否删除取决于前端页面模式迁移：若人员维护统一收口到标准列表页 drawer/page runtime，则删除这些独立页面；若仍保留详情页体验，则只改其数据源为标准详情模型，不再依赖旧 DTO。

不删除：

- 不删除 `modules/Organization/metadata/models/Person.model.json`、`PersonList.view.json`、`PersonDetail.view.json` 和相关 dic/nav 元数据。
- 不删除 `PersonUserLink` 相关持久化代码。
- 不删除 Company / Department / Position 当前 service/controller/repository；它们是否迁移到标准 CRUD 是后续独立设计，不并入人员中心退场任务。

## 5. 设计约束

- `StandardCrudDescriptor.Route` 是标准 CRUD 业务路由唯一真源。
- 不新增 JSON、XML 或额外代码映射表来重复声明路由。
- 平台 mapper 只映射标准动作：`list / detail / create / update / disable / delete / options`。
- 模块 Controller 只承载非标准动作，不再承载标准 CRUD 空壳方法。
- 路由冲突必须 fail fast，不能依赖 ASP.NET Core 最后注册者覆盖或隐式匹配顺序。
- 标准 CRUD API 的执行流程仍收口在 `StandardCrudEndpoint` 和 `StandardCrudService`，mapper 只负责路由到 endpoint，不承载业务分支。
- 旧接口迁移必须可回滚：人员中心标准 CRUD 方法退场应分阶段完成，保留特殊动作入口不受影响。

## 6. 总体方案

采用“资源注册表 + 自动路由映射 + 特殊 Controller”的方案。

模块通过 DI 注册标准 CRUD descriptor：

```csharp
services.AddStandardCrudResource(PersonCrudDescriptorFactory.Create());
```

Host 启动时调用平台扩展：

```csharp
app.MapStandardCrudResources();
```

平台从注册表读取所有 descriptor，并按 `descriptor.Route` 自动映射：

- `GET /{route}`
- `GET /{route}/{id}`
- `POST /{route}`
- `PUT /{route}/{id}`
- `POST /{route}/{id}/disable`
- `DELETE /{route}/{id}`
- `GET /{route}/options`

如果人员资源声明：

```csharp
Route = "api/organization/persons"
```

则对外 API 仍是：

```text
/api/organization/persons
/api/organization/persons/{id}
```

但这些标准路由不再由 `PersonsController` 手写，而是由平台自动映射到 `StandardCrudEndpoint`。

不选择“纯通用 URL”的原因：它会牺牲业务可读性，把模块边界隐藏在 `resourceKey` 参数里。  
不选择“每个资源空壳 Controller”的原因：它会增加重复代码和维护面，让后续 AI 更难判断标准能力真源。

## 7. 关键模型

### 7.1 标准 CRUD 资源注册表

新增 `IStandardCrudResourceRegistry`，用于运行时查找 descriptor。

建议能力：

- `IReadOnlyList<StandardCrudDescriptor> GetAll()`
- `StandardCrudDescriptor? FindByResourceKey(string resourceKey)`
- `StandardCrudDescriptor? FindByRoute(string route)`

注册表在构造或启动校验时检查：

- `ResourceKey` 不能重复。
- `Route` 规范化后不能重复。
- `Route` 不能为空。
- descriptor 必须通过 `StandardCrudDescriptorValidator`。

### 7.2 Descriptor 注册扩展

新增 DI 扩展：

```csharp
services.AddStandardCrudResource(StandardCrudDescriptor descriptor);
```

该扩展只注册 descriptor，不注册执行逻辑。执行逻辑仍由 `AddStandardCrudKernel()` 提供。

### 7.3 路由映射器

新增 `MapStandardCrudResources()`，它从 `IStandardCrudResourceRegistry` 获取资源并映射 Minimal API route handlers。

mapper 不直接访问数据库，不做字段转换，不知道 `Person` 含义，只负责把 HTTP 请求转换为标准 CRUD 调用。

### 7.4 标准 API 返回模型

自动映射后的标准 API 默认返回平台标准模型：

- 列表：`StandardCrudListResult`
- 详情：`StandardCrudDetail`
- 写入：`StandardCrudDetail`
- 删除成功：`204 No Content`
- 错误：`StandardCrudError`
- 选项：`IReadOnlyList<StandardCrudOptionItem>`

人员中心旧 DTO 兼容不再是平台 mapper 的责任。前端迁移时应改为消费标准返回模型。

## 8. 核心流程

### 8.1 模块注册流程

1. `OrganizationModule.ConfigureServices` 调用 `services.AddStandardCrudKernel()`。
2. 模块调用 `services.AddStandardCrudResource(PersonCrudDescriptorFactory.Create())`。
3. 模块继续注册人员专属扩展点：query customizer、display resolver、summary provider、validator、delete guard。
4. 模块只为非标准动作注册 Controller。

### 8.2 Host 映射流程

1. Host 在 `Program.cs` 中调用 `app.MapStandardCrudResources()`。
2. mapper 从注册表读取所有 descriptor。
3. mapper 对每个 descriptor 执行静态校验和 route 冲突检查。
4. mapper 为每个 descriptor.Route 挂载标准 CRUD handlers。
5. handlers 调用 `StandardCrudEndpoint`。

### 8.3 人员中心迁移流程

1. 先让 `/api/organization/persons` 标准 CRUD 路由由 mapper 接管。
2. 前端人员中心改为消费 `StandardCrudListResult / StandardCrudDetail`。
3. `PersonsController` 中标准 CRUD 方法退场。
4. 保留或重命名人员专属 Controller，只承载 `user-options` 与 `{id}/user-link`。

### 8.4 人员中心代码退场流程

人员中心退场按“先替代、再删除”的顺序执行：

1. 平台 mapper 映射 `api/organization/persons` 标准 CRUD 路由，并有测试证明标准请求不经过 `PersonsController`。
2. 新增人员账号绑定专属 Controller 和 service，覆盖 `user-options` 与 `{id}/user-link`。
3. `PersonUserLinkService` 通过标准 CRUD 读取能力校验人员存在，不再依赖 `IPersonRepository`。
4. 删除 `PersonsController` 标准 CRUD actions 和私有 DTO 映射方法。
5. 删除 `IPersonService / PersonService / IPersonRepository / EfPersonRepository` 中的人员标准 CRUD 主干。
6. 删除后端旧 DTO 与对应测试，保留 descriptor 与扩展点测试。
7. 前端切换到标准返回模型后，删除旧兼容 normalizer 和旧 DTO 类型。

## 9. 接口与集成点

标准映射接口：

```text
GET    /{descriptor.Route}
GET    /{descriptor.Route}/{id}
POST   /{descriptor.Route}
PUT    /{descriptor.Route}/{id}
POST   /{descriptor.Route}/{id}/disable
DELETE /{descriptor.Route}/{id}
GET    /{descriptor.Route}/options
```

查询参数：

- `page`
- `pageSize`
- `sortBy`
- `sortDirection`
- 其他 query 参数进入 `StandardCrudListQuery.Filters`

写入请求：

- `POST` 和 `PUT` 接收 `Dictionary<string, object?>` 或等价 JSON 字段包。
- 字段白名单仍由 `StandardCrudService` 根据 descriptor 过滤。

人员中心专属接口仍由模块 Controller 承载：

```text
GET /api/organization/persons/user-options
PUT /api/organization/persons/{id}/user-link
```

后续如出现组织树、批量绑定、导入等能力，也应走模块专属 Controller 或单独平台能力设计。

## 10. 数据与状态

- descriptor 是标准 CRUD API 的 authoring 真源。
- 注册表是运行时只读视图，不作为长期 authoring 真源。
- 自动映射不新增持久化状态。
- 标准 CRUD 执行状态仍由数据库和 `StandardCrudService` 维护。
- 路由冲突、descriptor 无效、资源重复都应在启动阶段暴露，避免运行时隐式失败。

## 11. 错误处理与风险

错误处理：

- descriptor 无效：启动失败，输出明确资源 key 和 route。
- route 重复：启动失败，说明冲突资源。
- 资源未注册：不会映射对应标准路由。
- 目标不存在：返回 `StandardCrudError`，HTTP 404。
- 业务冲突：返回 `StandardCrudError`，HTTP 409。
- 删除策略不支持：返回标准删除错误。

主要风险：

- 前端旧代码仍依赖 `PersonListItemDto / PersonDetailDto`，迁到标准返回模型需要分阶段处理。
- `PersonsController` 与 mapper 同时占用同一路由时会产生冲突，实施时必须先拆出专属 Controller 或调整注册顺序并用测试锁定。
- 标准写入接收字段包后，前端字段命名必须与 descriptor 字段一致，不再依赖 Controller 做 DTO 兼容映射。

回滚思路：

- 如果 mapper 接管失败，移除 `app.MapStandardCrudResources()` 或取消 Person descriptor 注册，恢复原 `PersonsController` 标准方法。
- 如果前端标准模型迁移失败，保留旧 Controller adapter，延后人员中心前端切换。
- 如果注册表抽象不成立，只回退 `server/platform-crud` 新增注册和映射代码，不影响既有 `StandardCrudEndpoint`。

## 12. 验证方案

平台测试：

```powershell
dotnet test .\tests\PlatformCrud.Tests\PlatformCrud.Tests.csproj -nr:false -v minimal
```

Organization 测试：

```powershell
dotnet test .\tests\Organization.Tests\Organization.Tests.csproj -nr:false -v minimal
```

Host 构建：

```powershell
dotnet build .\apps\host-api\HostApi.csproj -nr:false -v minimal
```

前端迁移涉及人员中心时补充：

```powershell
pnpm --dir .\frontend --filter @icp-modules/organization build
```

设计文档校验：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\validate-ai-first-design.ps1 -Path .\docs\superpowers\specs\platform\2026-05-11-standard-crud-route-mapping-design.md
powershell -ExecutionPolicy Bypass -File .\scripts\check-ai-first-design-targets.ps1
```

## 13. 未决问题

当前无阻断性未决问题。

实施计划中需要明确迁移节奏：人员中心前端是否在第一批直接改为标准返回模型，还是先完成后端 mapper 与旧 Controller 兼容共存测试。该问题影响任务拆分，不影响本设计方向。

## 14. 实施拆分建议

1. 平台先新增 `IStandardCrudResourceRegistry`、descriptor 注册扩展和冲突校验测试。
2. 新增 `MapStandardCrudResources()`，用测试验证标准路由映射到 `StandardCrudEndpoint`。
3. Organization 注册 Person descriptor 到资源注册表。
4. 新增人员账号绑定专属 Controller 与 `PersonUserLinkService`，先接住 `user-options` 和 `{id}/user-link`。
5. 删除 `PersonsController` 标准 CRUD actions、私有 DTO 映射方法和对应 Controller 兼容测试。
6. 删除 `IPersonService / PersonService / IPersonRepository / EfPersonRepository` 的人员标准 CRUD 主干，保留或新建账号绑定专属服务。
7. 删除后端旧人员列表/详情/摘要 DTO，迁移测试到标准 CRUD 模型与人员账号绑定模型。
8. 人员中心前端迁移到标准返回模型，删除旧兼容 normalizer 和旧 DTO 类型。
9. 回填验证记录，说明删除清单、保留清单、标准 Controller 退场边界与特殊 Controller 保留规则。

## 15. AI-First 最小检查清单

- 入口在哪里：`server/platform-crud` 的资源注册表、DI 扩展和 `MapStandardCrudResources()`；试点入口是 `PersonCrudDescriptorFactory.Create()` 与 `OrganizationModule.ConfigureServices`。
- 边界在哪里：平台只映射标准 CRUD；人员账号绑定、用户候选项、组织树、批量导入导出等非标准能力继续属于模块 Controller 或单独平台能力。
- 如何验证：运行 `PlatformCrud.Tests`、`Organization.Tests`、`HostApi` 构建；涉及前端迁移时运行 `@icp-modules/organization` 构建；设计文档运行 AI-first 校验脚本。
- 失败信号是什么：标准 CRUD 仍需要手写空壳 Controller；人员标准 CRUD 仍依赖 `PersonService / IPersonRepository / EfPersonRepository`；同一路由存在 descriptor 与 Controller 双真源；mapper 出现 Person 特判；前端仍依赖旧 DTO 兼容层无法消费标准模型；route 冲突没有启动失败。
- 如何回滚：取消 Person descriptor 注册或移除 `MapStandardCrudResources()` 调用，恢复旧 `PersonsController` 标准方法；专属动作 Controller 独立保留，不随标准路由回滚。
