# Organization 模块验证记录

**验证日期**: 2026-04-13  
**验证人**: Codex  
**模块状态**: 首版闭环已完成

## 验证目标

验证组织结构模块的后端、前端、宿主装载与文档口径已经形成首版闭环，并明确记录实现过程中为适应仓库现状所做的最小调整。

## 关键差异说明

- 定向前端构建实际使用 `@icp-modules/organization`，因为仓库当前 `frontend/modules/organization/package.json` 的真实包名即为此值。
- 前端构建前执行过一次 `pnpm --dir frontend install --no-frozen-lockfile`，原因是本地缺 `node_modules`，且组织模块在 Task 5 中补入了 `core/ui/react` 依赖声明，需要同步 lockfile。
- 组织表 migration 为真实库可装载所必需，已补入三套 provider 工程。

## 分任务验证证据

### Task 1

- `dotnet test tests/Organization.Tests/Organization.Tests.csproj --filter OrganizationModuleTests -nr:false -v minimal`
- `dotnet build apps/host-api/HostApi.csproj -v minimal`

结果：

- 模块元数据测试通过
- HostApi 构建通过

### Task 2

- `dotnet test tests/Organization.Tests/Organization.Tests.csproj --filter "CompanyServiceTests|DepartmentServiceTests|EfOrganizationRepositoryTests" -nr:false -v minimal`
- `dotnet build apps/host-api/HostApi.csproj -v minimal`

结果：

- 组织主数据规则测试通过
- 统一持久化目录已包含 `OrganizationModelDefinitions`

### Task 3

- `dotnet test tests/Organization.Tests/Organization.Tests.csproj --filter "PersonServiceTests|EfOrganizationRepositoryTests" -nr:false -v minimal`
- `dotnet build server/platform-persistence-efcore.migrations.sqlserver/PlatformPersistence.EfCore.SqlServerMigrations.csproj -v minimal`
- `dotnet build server/platform-persistence-efcore.migrations.mysql/PlatformPersistence.EfCore.MySqlMigrations.csproj -v minimal`
- `dotnet build server/platform-persistence-efcore.migrations.oracle/PlatformPersistence.EfCore.OracleMigrations.csproj -v minimal`
- `dotnet build apps/host-api/HostApi.csproj -v minimal`

结果：

- 人员工号唯一性、账号绑定规则与详情查询测试通过
- 三套 migration 工程构建通过
- HostApi 构建通过

### Task 4

- `dotnet test tests/Organization.Tests/Organization.Tests.csproj --filter "OrganizationModuleTests|PersonServiceTests" -nr:false -v minimal`
- `dotnet build apps/host-api/HostApi.csproj -v minimal`

结果：

- 授权摘要读取与 OrganizationModule 注册测试通过
- HostApi 构建通过
- 规格复审通过：未越到 Authorization 写域、未提前实现 Task 6 接口

### Task 5

- `pnpm --dir frontend --filter @icp-modules/organization build`
- `pnpm --dir frontend build`

结果：

- 组织模块前端包可独立构建
- 前端 workspace 全量构建通过
- 页面骨架已形成：
  - `/organization/tree`
  - `/organization/persons`
  - `/organization/persons/:id`
  - `/organization/positions`

### Task 6

- `dotnet test tests/Organization.Tests/Organization.Tests.csproj --filter PersonServiceTests -nr:false -v minimal`
- `pnpm --dir frontend --filter @icp-modules/organization build`
- `dotnet build apps/host-api/HostApi.csproj -v minimal`
- `pnpm --dir frontend build`

结果：

- `IIdentityUserDirectory.SearchAsync` 默认行为测试通过
- 绑定账号前后端链路构建通过
- HostApi 与前端全量构建通过

## 最终验证

### 1. 后端模块测试

命令：

```bash
dotnet test tests/Organization.Tests/Organization.Tests.csproj -nr:false -v minimal
```

预期：

- Organization 模块相关测试全部通过

### 2. 宿主构建

命令：

```bash
dotnet build apps/host-api/HostApi.csproj -v minimal
```

预期：

- HostApi 构建成功，Organization 模块与三套 migration 工程均可参与装载

### 3. 前端全量构建

命令：

```bash
pnpm --dir frontend build
```

预期：

- 前端 workspace 全量构建成功，组织模块页面和 Shell 一起通过

## 已知限制

1. 默认 `NullIdentityUserDirectory` 仍是安全降级实现：搜索返回空，绑定报未接入，不会写入脏账号关联。
2. 人员列表页当前为首版骨架，尚未接真实列表 API。
3. 授权摘要读取失败时当前统一退化为空摘要，未进一步区分依赖不可用原因。

## 验证结论

**状态**: 通过

Organization 模块已经完成首版闭环：

- 后端模块可装载
- 前端模块可构建
- 组织主数据、人员详情、账号绑定入口和授权摘要只读展示均已落地
- 关键的计划/现状差异已被显式记录，而不是隐含在提交历史中

## 标准 CRUD 后端内核试点验证

验证日期：2026-05-04

本轮验证目标：

- `server/platform-crud` 已提供 descriptor、扩展点、EF 仓储、标准服务和 endpoint 门面。
- Person 已接入标准 CRUD descriptor、query customizer、display resolver、summary provider、validator 和 delete guard。
- 第一阶段读取主干覆盖 list/detail/options/summary。
- 第二阶段写操作覆盖 create/update/disable/delete，其中 delete 按 `conditional` 策略通过 delete guard 控制。
- 标准 update/disable 类更新行为由 CRUD 内核自动维护 `UpdatedAt`，客户端提交时间不作为真源。
- `user-link` 特殊入口仍保留在 Organization 模块内。

验证命令：

```powershell
dotnet test .\tests\PlatformCrud.Tests\PlatformCrud.Tests.csproj -nr:false -v minimal
dotnet test .\tests\Organization.Tests\Organization.Tests.csproj -nr:false -v minimal
dotnet build .\apps\host-api\HostApi.csproj -nr:false -v minimal
pnpm --dir .\frontend --filter @icp-modules/organization build
```

验证结果：

- `PlatformCrud.Tests`：通过，17/17。
- `Organization.Tests`：通过，59/59。
- `HostApi` 构建：通过，0 error。
- `@icp-modules/organization` 前端构建：通过。

验证说明：

- 并行执行多个 `dotnet` 命令时曾出现 `obj/bin` 文件锁，随后按计划命令单独重跑 `Organization.Tests` 通过。
- 构建仍保留既有 `NU1510` 与 `System.Security.Cryptography.Xml 9.0.0` 漏洞 warning，本轮未处理依赖治理。

验证结论：

- 标准 CRUD 后端内核与 Person 试点通过全量验证。
- 删除能力按 Person `conditional` 策略显式接入，不作为隐式通用删除暴露。
- 查询、显示值、摘要块、校验和删除守卫均通过代码扩展点接入，未下沉为元数据执行逻辑。
- 更新审计字段由内核统一维护，保持写行为可验证、可回滚。

## 标准 CRUD 写入接管与唯一约束验证

验证日期：2026-05-04

本轮验证目标：

- `Person create/update` 已由 `PersonsController` 委托 `StandardCrudEndpoint` 接管。
- `PersonNo` 全局唯一由 `StandardCrudUniqueConstraint` 声明并由 `StandardCrudService` 执行。
- `PersonCrudValidator` 不再承载人员工号唯一性，避免资源专属 service 样板继续扩散。
- `user-link` 特殊入口仍保留在 Organization 模块服务内。

验证命令：

```powershell
dotnet test .\tests\PlatformCrud.Tests\PlatformCrud.Tests.csproj -nr:false -v minimal
dotnet test .\tests\Organization.Tests\Organization.Tests.csproj -nr:false -v minimal
dotnet build .\apps\host-api\HostApi.csproj -nr:false -v minimal
pnpm --dir .\frontend --filter @icp-modules/organization build
```

验证结果：

- `PlatformCrud.Tests`：通过，31/31。
- `Organization.Tests`：通过，67/67。
- `HostApi` 构建：通过，0 warning，0 error。
- `@icp-modules/organization` 前端构建：通过。

验证结论：

- 标准 CRUD 唯一约束、仓储存在性检查和 Person 写入接管通过验证。
- `PersonNo` 按当前阶段要求为平台内全局唯一，不再按 `CompanyId` 分组。
- 本轮未引入数据库唯一索引；并发强一致性如需支持，应另开设计。
- 标准 CRUD 创建路径已由内核补齐 `CreatedAt`，更新路径继续由内核维护 `UpdatedAt`。

已知风险：

- `ENAME` 等字段必填校验仍应由后续标准 CRUD required field / 模型必填校验设计承接，本轮按计划不把资源必填规则塞回 `PersonCrudValidator`。
