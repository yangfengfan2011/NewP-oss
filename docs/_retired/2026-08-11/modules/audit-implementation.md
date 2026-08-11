# Audit 模块实现文档

## 1. 模块概述

Audit 模块提供统一的审计日志记录与查询能力，支持操作日志和访问日志两类审计数据的管理。

- 模块 ID：`Audit`
- 版本：`1.0.0`
- 状态：已完成基础实现并通过验证

## 2. 架构设计

### 2.1 分层结构

```
AuditModule (模块入口)
├── Controllers (API 层)
│   ├── OperationLogsController
│   └── AccessLogsController
├── Services (服务层)
│   ├── IAuditService
│   └── AuditService
├── Repositories (仓储层)
│   ├── IOperationLogRepository / OperationLogRepository
│   └── IAccessLogRepository / AccessLogRepository
├── Models (领域模型)
│   ├── OperationLog
│   └── AccessLog
└── DTOs (数据传输对象)
    ├── OperationLogDto / CreateOperationLogRequest / QueryOperationLogRequest
    ├── AccessLogDto / QueryAccessLogRequest
    └── PagedResult<T>
```

### 2.2 核心组件

#### AuditModule
- 实现 `IPermissionProvider` 和 `IMenuProvider`
- 声明两个权限：`Audit.OperationLog.View`、`Audit.AccessLog.View`
- 注册三级菜单结构：审计日志 > 操作日志 / 访问日志
- 注册服务和仓储依赖

#### IAuditService
提供四个核心方法：
- `LogOperationAsync` - 记录操作日志
- `LogAccessAsync` - 记录访问日志
- `QueryOperationLogsAsync` - 查询操作日志（分页）
- `QueryAccessLogsAsync` - 查询访问日志（分页）

## 3. 数据模型

### 3.1 OperationLog（操作日志）

| 字段 | 类型 | 说明 |
|------|------|------|
| Id | long | 日志 ID |
| OperationTime | DateTime | 操作时间 |
| UserId | string? | 操作用户 ID |
| UserName | string? | 操作用户名 |
| ModuleId | string | 模块 ID |
| OperationType | string | 操作类型（Create/Update/Delete/Execute） |
| OperationTarget | string? | 操作目标 |
| TargetId | string? | 操作目标 ID |
| Description | string? | 操作描述 |
| Result | string | 操作结果（Success/Failed） |
| ErrorMessage | string? | 错误信息 |
| ClientIp | string? | 客户端 IP |
| ExtensionData | string? | 扩展数据（JSON） |

### 3.2 AccessLog（访问日志）

| 字段 | 类型 | 说明 |
|------|------|------|
| Id | long | 日志 ID |
| AccessTime | DateTime | 访问时间 |
| UserId | string? | 用户 ID |
| UserName | string? | 用户名 |
| RequestPath | string | 请求路径 |
| HttpMethod | string | HTTP 方法 |
| StatusCode | int | 状态码 |
| ResponseTime | long | 响应时间（毫秒） |
| ClientIp | string? | 客户端 IP |
| UserAgent | string? | User Agent |
| RequestParams | string? | 请求参数 |
| ExceptionMessage | string? | 异常信息 |

## 4. API 接口

### 4.1 操作日志接口

**查询操作日志**
```
GET /api/audit/operation-logs
Query Parameters:
  - userId?: string
  - moduleId?: string
  - operationType?: string
  - startTime?: DateTime
  - endTime?: DateTime
  - pageIndex: int (default: 1)
  - pageSize: int (default: 20)
Response: PagedResult<OperationLogDto>
```

**创建操作日志**
```
POST /api/audit/operation-logs
Body: CreateOperationLogRequest
Response: long (日志 ID)
```

### 4.2 访问日志接口

**查询访问日志**
```
GET /api/audit/access-logs
Query Parameters:
  - userId?: string
  - requestPath?: string
  - minStatusCode?: int
  - maxStatusCode?: int
  - startTime?: DateTime
  - endTime?: DateTime
  - pageIndex: int (default: 1)
  - pageSize: int (default: 20)
Response: PagedResult<AccessLogDto>
```

## 5. 前端实现

### 5.1 模块注册

位置：`frontend/modules/audit/src/index.ts`

- 注册两个路由：`/audit/operation-logs`、`/audit/access-logs`
- 声明三级菜单结构，与后端菜单声明保持一致
- 声明两个权限，与后端权限声明保持一致

### 5.2 页面组件

**OperationLogs.tsx**
- 展示操作日志列表
- 支持分页查询
- 显示成功/失败统计
- 使用统一的 Shell 页面结构和样式

**AccessLogs.tsx**
- 展示访问日志列表
- 支持分页查询
- 显示成功/失败请求统计和平均响应时间
- 使用统一的 Shell 页面结构和样式

## 6. 仓储实现

### 6.1 当前实现方式

- 使用内存仓储（`List<T>`）
- 注册为 Singleton 生命周期
- 支持基础过滤和分页查询

### 6.2 查询能力

**OperationLogRepository**
- 按 userId、moduleId、operationType 过滤
- 按时间范围过滤
- 按操作时间倒序排列
- 支持 Skip/Take 分页

**AccessLogRepository**
- 按 userId、requestPath（Contains）过滤
- 按状态码范围过滤
- 按时间范围过滤
- 按访问时间倒序排列
- 支持 Skip/Take 分页

### 6.3 持久化说明

当前为内存实现，应用重启后数据丢失。仓储接口设计已为后续数据库持久化预留空间。

## 7. 测试覆盖

位置：`tests/Audit.Tests/AuditServiceTests.cs`

测试用例：
1. `LogOperationAsync_CreatesOperationLog` - 验证操作日志创建
2. `QueryOperationLogsAsync_ReturnsFilteredLogs` - 验证操作日志过滤查询
3. `LogAccessAsync_CreatesAccessLog` - 验证访问日志创建
4. `QueryAccessLogsAsync_ReturnsFilteredLogs` - 验证访问日志过滤查询
5. `QueryOperationLogsAsync_SupportsPagination` - 验证分页功能

测试状态：全部通过（5/5）

## 8. 权限与菜单

### 8.1 权限声明

| 权限代码 | 显示名称 | 分组 |
|---------|---------|------|
| Audit.OperationLog.View | 查看操作日志 | 操作日志 |
| Audit.AccessLog.View | 查看访问日志 | 访问日志 |

### 8.2 菜单结构

```
审计日志 (Audit)
├── 操作日志 (Audit.OperationLogs) - 需要 Audit.OperationLog.View
└── 访问日志 (Audit.AccessLogs) - 需要 Audit.AccessLog.View
```

前后端菜单声明完全一致。

## 9. 集成点

### 9.1 与 Host 的集成

- 控制器通过 `User.Identity?.Name` 获取当前用户信息
- 通过 `HttpContext.Connection.RemoteIpAddress` 获取客户端 IP
- 后续可在 Host 中间件中调用 `IAuditService.LogAccessAsync` 自动记录访问日志

### 9.2 与 Authorization 的集成

- 通过 `IPermissionProvider` 声明权限
- 通过 `IMenuProvider` 声明菜单
- 前端路由和菜单按权限控制可见性

### 9.3 与其他模块的集成

- 其他业务模块可注入 `IAuditService` 记录操作日志
- 通过 `CreateOperationLogRequest` 传递模块 ID 和操作信息

## 10. 实现特征

### 10.1 已实现能力

- ✅ 操作日志记录与查询
- ✅ 访问日志记录与查询
- ✅ 分页查询支持
- ✅ 基础过滤（用户、模块、类型、时间、状态码）
- ✅ 前端页面与路由
- ✅ 权限与菜单集成
- ✅ 单元测试覆盖

### 10.2 当前限制

- 使用内存仓储，应用重启后数据丢失
- 访问日志需要显式调用，未自动挂接到请求管道
- 查询能力为基础过滤，不支持全文检索和复杂分析
- 无日志归档、保留期和清理策略
- 无批量写入优化和异步队列

### 10.3 设计预留

- 仓储接口抽象，便于替换为数据库实现
- 服务层与控制器分离，便于在中间件中调用
- 扩展数据字段（ExtensionData），便于后续增强
- 清晰的模块边界，便于独立演进

## 11. 后续演进方向

1. **持久化增强**：替换为数据库仓储实现
2. **自动采集**：在 Host 中间件中自动记录访问日志
3. **查询增强**：支持更多过滤条件和排序方式
4. **归档策略**：实现日志保留期和自动归档
5. **导出能力**：支持日志导出为 CSV/Excel
6. **详情抽屉**：前端增加日志详情查看
7. **高级筛选**：前端增加组合筛选器
8. **审计联动**：与配置变更、文件操作等模块联动

## 12. 参考文档

- 设计文档：`docs/designs/audit-design.md`
- 边界说明：`docs/modules/audit-module-boundary-v1.md`
- 验证记录：`docs/verification/audit-module-verification.md`
