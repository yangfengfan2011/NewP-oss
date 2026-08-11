# Audit 详细设计

## 1. 背景

`Audit` 模块已经具备后端 API、前端页面和单元测试，当前能够记录并查询操作日志与访问日志。但现有文档还主要停留在“模块概览”和“边界说明”层，没有把日志模型、查询方式、宿主挂接点、与授权和其他模块的关系沉淀成一份稳定的详细设计。

这份设计的目的，是把 V1 阶段的审计方案固定为“最小但完整的基础审计链路”：先把日志写入、日志查询、菜单与权限接入讲清楚，再为后续中间件接线、归档、筛选增强和审计联动留出明确边界。

## 2. 目标

- 提供统一的操作日志记录与查询能力。
- 提供统一的访问日志查询能力。
- 支撑前端审计页面接入 Shell，按权限展示菜单和页面。
- 明确宿主、控制器和业务模块未来如何调用审计服务。
- 为后续接入 Host 中间件、配置变更审计和归档策略预留接口。

## 3. 非目标

- 不在本轮实现完整的访问日志中间件自动采集链路。
- 不实现日志归档、冷热分层、全文检索和复杂报表。
- 不实现跨模块统一审计事件总线。
- 不引入数据库持久化、消息队列投递或批量异步落库。
- 不实现细粒度的审计脱敏、留痕合规规则或保留策略配置。

## 4. 范围

涉及范围：

- 后端模块：`server/modules/Audit/`
- 前端模块：`frontend/modules/audit/`
- 测试项目：`tests/Audit.Tests/`
- 宿主未来挂接点：Host 中的控制器、过滤器或中间件

当前不覆盖：

- `Host` 级自动接入逻辑
- `Authorization` 之外的跨模块联动实现
- 配置化归档、导出和告警

## 5. 设计约束

- 当前审计能力必须保持最小闭环，优先保证“可写入、可查询、可展示”。
- 操作日志和访问日志使用两套清晰分离的数据模型，避免 V1 就做成大而全统一事件表。
- 控制器保持轻薄，日志写入和查询逻辑落在 `IAuditService`。
- 当前允许内存仓储，但仓储抽象必须足够稳定，后续能平滑替换为数据库实现。
- 菜单、路由和权限编码必须与后端模块声明保持一致。

## 6. 总体方案

采用“模块内聚合两类日志模型 + 服务层统一查询 + 前端按权限接入页面”的方案：

- `AuditModule` 负责声明权限、菜单和服务注册。
- `IAuditService` 提供记录操作日志、记录访问日志、查询操作日志、查询访问日志四类核心能力。
- `OperationLogRepository` 和 `AccessLogRepository` 分别承载两类日志存储和过滤逻辑。
- 控制器仅负责参数接收、调用服务并返回分页 DTO。
- 前端模块提供“操作日志”和“访问日志”两个页面，通过 Shell 注册到统一导航中。

选择这个方案的原因：

- 操作日志和访问日志模型语义不同，拆分后更容易理解和演进。
- 当前查询能力以分页和基础过滤为主，足以支撑 Phase 4 收口。
- 服务层和仓储边界清晰，为后续接中间件和持久化替换保留空间。

本轮不选这些方案：

- 不选一开始就做统一审计事件中心：范围过大，且当前需求并不要求。
- 不选只在控制器中直接写仓储：会让后续挂接点和测试变得分散。
- 不选立即引入复杂检索引擎：会偏离“最小可运行审计”的阶段目标。

## 7. 关键模型

### 操作日志模型

- `OperationLog`
  - `Id`
  - `OperationTime`
  - `UserId`
  - `UserName`
  - `ModuleId`
  - `OperationType`
  - `OperationTarget`
  - `TargetId`
  - `Description`
  - `Result`
  - `ErrorMessage`
  - `ClientIp`
  - `ExtensionData`

### 访问日志模型

- `AccessLog`
  - `Id`
  - `AccessTime`
  - `UserId`
  - `UserName`
  - `RequestPath`
  - `HttpMethod`
  - `StatusCode`
  - `ResponseTime`
  - `ClientIp`
  - `UserAgent`
  - `RequestParams`
  - `ExceptionMessage`

### DTO 与查询模型

- `CreateOperationLogRequest`
- `QueryOperationLogRequest`
- `OperationLogDto`
- `QueryAccessLogRequest`
- `AccessLogDto`
- `PagedResult<T>`

### 前端模型

- `FrontendModule.routes`
- `FrontendModule.menus`
- `FrontendModule.permissions`

## 8. 核心流程

### 主流程：记录操作日志

1. 调用方构造 `CreateOperationLogRequest`。
2. 控制器或其他调用方传入 `userId / userName / clientIp`。
3. `AuditService.LogOperationAsync(...)` 组装 `OperationLog` 实体。
4. `OperationLogRepository.AddAsync(...)` 写入仓储并返回日志 ID。

### 主流程：查询操作日志

1. 前端或调用方提交 `QueryOperationLogRequest`。
2. `AuditService` 计算分页偏移量。
3. 仓储按 `userId / moduleId / operationType / time range` 过滤。
4. 服务层映射为 `OperationLogDto` 并返回 `Items + Total`。

### 主流程：记录访问日志

1. 调用方构造 `AccessLog`。
2. `AuditService.LogAccessAsync(...)` 调用访问日志仓储保存。
3. 返回日志 ID。

### 主流程：查询访问日志

1. 前端或调用方提交 `QueryAccessLogRequest`。
2. `AuditService` 计算分页偏移量。
3. 仓储按 `userId / requestPath / statusCode range / time range` 过滤。
4. 服务层映射为 `AccessLogDto` 并返回分页结果。

### 前端展示流程

1. Shell 装配 `frontend/modules/audit/src/index.ts`。
2. 有 `Audit.OperationLog.View` 或 `Audit.AccessLog.View` 的用户看到对应菜单和页面。
3. 页面通过各自路由展示操作日志或访问日志列表。

## 9. 接口与集成点

### 后端接口

- `GET /api/audit/operation-logs`
- `POST /api/audit/operation-logs`
- `GET /api/audit/access-logs`

### 与宿主的集成

- 当前控制器可以直接写入操作日志。
- 后续 Host 可在中间件、过滤器或行为管道中调用 `IAuditService.LogAccessAsync(...)` 记录访问日志。
- 后续业务模块可以在关键动作完成后调用 `LogOperationAsync(...)` 记录审计事件。

### 与 Authorization 的衔接

- `AuditModule` 通过 `IPermissionProvider` 声明只读型权限。
- Shell 和前端页面依赖这些权限控制菜单与路由可见性。
- 当前查询控制是“模块级查看权限”，未细分到日志范围或数据域。

### 与前端的集成

- `frontend/modules/audit/src/index.ts` 注册两条路由、一个父菜单和两类权限。
- `OperationLogs.tsx` 与 `AccessLogs.tsx` 分别对应两个审计页面。

## 10. 数据与状态

- 当前仓储实现为进程内存储。
- 两类日志都支持分页查询与基础过滤。
- 当前未实现删除、归档、压缩和保留期策略。
- 当前没有审计事件幂等策略，也没有批量写入优化。
- 一致性要求：后端模块权限、前端权限声明、菜单和路由必须同步，否则会出现页面可见性偏差。

## 11. 错误处理与风险

- 当前访问日志仍以显式调用为主，尚未自动挂接到所有请求链路。
- 内存仓储在应用重启后会丢失日志数据。
- 当前日志查询只支持基础过滤，不适合高体量或复杂分析场景。
- `User.Identity?.Name` 同时作为 `userId` 和 `userName` 使用，后续接入真实身份系统后需要收敛。

风险控制方式：

- 通过仓储接口隔离持久化变化。
- 通过测试覆盖记录、过滤和分页等核心路径。
- 通过详细设计明确当前“已做/未做”边界，避免把后续能力误判为当前已完成。

## 12. 验证方案

- `tests/Audit.Tests` 通过。
- 前端 `pnpm -C frontend build` 通过。
- `GET /api/audit/operation-logs` 和 `GET /api/audit/access-logs` 可返回分页结果。
- `POST /api/audit/operation-logs` 可成功写入操作日志。
- 审计菜单与前端路由可在具备权限时访问。

## 13. 未决问题

- 访问日志自动采集是在下一阶段走中间件、过滤器还是统一网关挂接。
- 配置变更、文件操作等跨模块事件是否都应沉淀为统一操作日志规范。
- 日志保留期、归档和查询增强是由 Audit 自己承担，还是交由基础设施层统一处理。

## 14. 实施拆分建议

1. 固化操作日志与访问日志的模型、服务和基础查询。
2. 固化控制器、分页 DTO 与前端路由菜单接入。
3. 补齐验证记录，明确当前人工/半自动挂接点。
4. 后续再接入 Host 中间件、持久化、归档和高级筛选。
