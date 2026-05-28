# Organization 模块实现说明

## 实现范围

本轮已完成组织结构模块首版闭环，范围控制在以下 3 类能力：

- 组织主数据：`Company / Department / Position / Person`
- 人员与账号关联：`PersonUserLink`
- 只读展示能力：人员详情中的账号绑定状态与授权摘要

本轮没有扩到以下范围：

- 不在组织模块内创建账号、禁用账号、维护密码
- 不在组织模块内分配授权角色或维护权限
- 不实现复杂 HR 规则，如一人多岗、任职历史、审批流

## 后端实现

已落地：

- 独立后端模块 `server/modules/Organization`
- 模型定义与 EF 仓储：`Company / Department / Position / Person / PersonUserLink`
- 单模型 DTO：`CompanyDto / DepartmentDto / PositionDto / PersonDto / PersonUserLinkDto`
- 聚合 DTO 继续手工维护：`PersonDetailDto / DepartmentTreeNodeDto / IdentityUserOptionDto`
- 服务层规则：
  - 部门自环阻断
  - 同父部门编码唯一
  - 同公司人员工号唯一
  - 一人一账号 / 一账号一人
  - 绑定账号前先校验账号存在
- 可替换跨模块只读适配点：
  - `IIdentityUserDirectory`
  - `IAuthorizationSummaryReader`
- 组织模块 API：
  - `companies / departments / positions / persons` 最小读写接口
  - `persons/{id}/user-link` 绑定接口
  - `persons/user-options` 账号搜索接口
- 三套 provider 的组织表 migration：
  - `SqlServer`
  - `MySql`
  - `Oracle`

## 前端实现

已落地：

- 独立前端模块 `frontend/modules/organization`
- 模块注册：
  - 组织树页
  - 人员中心页
  - 人员详情页
  - 岗位管理页
- 组件：
  - `OrganizationTreePanel`
  - `PersonEditorForm`
  - `PersonUserLinkCard`
  - `AuthorizationSummaryCard`
  - `BindUserDialog`
- API 服务层：
  - 公司 / 部门 / 岗位查询
  - 人员详情查询
  - 可绑定账号搜索
  - 人员绑定账号

## 人员中心标准 CRUD 样板

人员中心当前作为标准 CRUD 样板维护，入口分为四层：

- `modules/Organization/metadata/views/PersonList.view.json`：声明查询、列表列、页面动作、行动作和行动作状态条件。metadata 只描述“动作适用于什么状态”，例如 `disable` 只适用于 `IsEnabled=true`，不描述执行流程。
- `frontend/packages/shell/src/pages/StandardCrudListPageHost.tsx`：负责列表状态机、操作列固定、行动作投影、危险动作更多菜单、抽屉打开和错误展示。
- `frontend/modules/organization/src/standard-crud/personStandardCrudResource.tsx`：保留人员资源的 provider、mapper、renderer 和 action handler，不承载 Shell 通用页面规则。
- `frontend/modules/organization/src/services/organizationService.ts`：负责 Organization API 调用和后端业务错误透传。

当前人员中心标准 CRUD 已接入 `/api/organization/persons` 列表与详情。

当前边界：

- 不在本样板中维护用户、角色、授权中心或动作级授权配置。
- 不在本样板中新增 `PersonNo` 数据库唯一索引。
- 不在公司、部门、岗位维护模块完成前继续扩展高级筛选体验。

验证入口：

```powershell
pnpm --dir frontend --filter @icp-platform/shell test -- standard-crud-schema-and-registry person-center-crud-pro-sample organization-person-center-list-layout
pnpm --dir frontend --filter @icp-platform/shell test -- organization-service-error-handling
pnpm --dir frontend --filter @icp-modules/organization build
dotnet test tests\Organization.Tests\Organization.Tests.csproj -nr:false -v minimal
```

失败信号包括：启用和停用同时出现在同一行、危险动作平铺在操作列、操作列未固定右侧、删除绑定账号人员只展示泛化错误。

回滚方式：回退 `PersonList.view.json` 的 `visibleWhen`、Shell Host 行动作投影、Organization 服务错误解析和本章节文档；本轮不涉及数据库迁移或后端契约变更。

## 边界与最小调整

实现过程中发现 4 个计划与现状差异，本轮均按“最小调整”处理：

1. `Task 2 / Task 3` 计划文件未列出 migration 项目，但仓库现状存在三套 `platform-persistence-efcore.migrations.*` 工程。由于组织表已经进入统一持久化目录，如果不补迁移，真实库装载会缺表，所以补齐了三套 migration 与 snapshot。
2. `Task 5` 计划中的定向构建命令使用 `@icp-platform/organization`，但仓库当前真实包名是 `@icp-modules/organization`。实现与验证都沿用仓库现状，没有扩大到全局包命名改造。
3. `Task 5` 的路由示例使用 `Organization.Department.View`，但当前后端实际只声明了 `Organization.Person.View`。为了不把前端任务扩成后端权限点重构，本轮前端路由统一沿用现有权限码。
4. 首版历史状态曾是后端尚未提供人员列表 API，因此人员中心页当时先固定“筛选区 + 列表容器 + 空态”，没有越界补后端列表接口；当前人员中心标准 CRUD 已接入 `/api/organization/persons` 列表与详情。

## 当前能力口径

### 已可用

- 组织树页面可展示公司/部门结构
- 人员详情页可展示基础信息、账号绑定状态和授权摘要
- 详情页可打开绑定账号对话框，搜索可绑定账号并调用绑定接口
- 宿主可装载 Organization 模块，前端 workspace 可整体构建
- `Company / Department / Position / Person / PersonUserLink` 已统一切到 `PascalCase` 表名与普通列名
- `PersonDto` 已补齐 `CompanyId_DisplayValue / DepartmentId_DisplayValue / PositionId_DisplayValue / EmploymentStatus_DisplayValue`
- `Companies / Departments / Positions / Persons` 控制器已直接使用单模型 DTO，不再拆分 `Create*Request / Update*Request`
- 人员中心标准 CRUD 已接入 `/api/organization/persons` 列表与详情

### 当前限制

- 默认 `NullIdentityUserDirectory` 仍只提供安全降级：
  - 搜索返回空列表
  - 真实绑定会报“未接入”错误
- 授权摘要读取失败时当前回退为空摘要，不区分“真的为空”与“依赖暂不可用”
- `PersonDetailDto` 等聚合读取模型仍保持手工维护，不纳入自动 DTO 覆盖范围

## 对应提交

- `9626c18b` 功能：新增组织结构模块骨架
- `7513534d` 测试：同步组织结构内建插件清单断言
- `1009402f` 功能：新增组织主数据模型与服务规则
- `16b31c03` / `5f82d27c` 修复：接入组织模块持久化模型目录
- `40732eaf` 功能：新增人员管理与账号绑定规则
- `9066c893` 修复：收口组织人员规则与持久化迁移
- `f278f11d` 功能：新增组织结构模块 API 与授权摘要读取
- `c79119f7` 功能：新增组织结构前端工作台
- `6c6af47e` 功能：打通组织结构模块绑定账号联通
