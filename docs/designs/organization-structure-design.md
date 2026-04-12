# 组织结构模块详细设计

## 1. 背景

当前平台已经完成基础宿主、前后端模块接入、认证最小闭环、授权最小闭环、配置中心、审计、文件模块、插件运行时闭环和首批迁移 POC。接下来的验证重点，不再是“平台骨架能否跑起来”，而是“平台能否承接一个真实但边界清晰的业务模块”。

`组织结构模块` 选择作为首个真实业务模块，原因是它既具备明确的业务语义，也能覆盖真实平台验证所需的关键能力：树形数据、主数据维护、模块权限、前后端页面、跨模块关联、持久化承接和模块级 smoke 验证。

这份设计的目标，是把组织结构模块在 V1 的边界、模型、流程、集成点和验证标准固化下来，确保后续实现仍然服务于“验证标准业务模块接入闭环”这一主目标，而不是把范围扩展成完整 HR 或完整身份平台。

## 2. 目标

- 提供 `公司 / 部门 / 岗位 / 人员` 四类组织主数据的最小可运行承载能力。
- 以“人员中心”为主入口，提供统一的组织结构管理工作台。
- 支撑部门树、岗位归属、人员归属和人员状态等基础业务能力。
- 支撑 `人员 - 用户账号` 关联，验证组织模块与 `Identity Lite` 的受控衔接。
- 支撑在人员详情中查看账号绑定状态与授权摘要，验证组织模块与 `Authorization` 的受控衔接。
- 验证真实业务模块在当前平台中的标准接入路径，包括：权限声明、菜单注册、路由注册、后端接口、前端页面、持久化和测试。

## 3. 非目标

- 不构建完整的人力资源系统。
- 不接管登录认证、密码、账号可登录策略或用户生命周期管理。
- 不接管授权角色主数据、权限分配和复杂授权策略。
- 不实现汇报关系、编制、任职历史、生效时间、组织变更流程或审批流。
- 不支持一人多岗、多部门兼职、多公司任职等复杂任职模型。
- 不在本轮实现旧平台组织数据迁移、批量导入导出和复杂同步工具。
- 不把“岗位”与 `Authorization` 中的“授权角色”混为同一模型。

## 4. 范围

本轮计划覆盖：

- 后端模块：`server/modules/Organization/`
- 前端模块：`frontend/modules/organization/`
- 前端 Shell 的标准模块注册入口
- 与 `Identity Lite` 的用户查询或账号选择集成点
- 与 `Authorization` 的授权摘要读取集成点
- 组织结构模块的详细设计、实现说明、验证记录和状态收口

本轮不覆盖：

- `Identity Lite` 的账号创建、账号禁用、密码管理
- `Authorization` 的角色分配入口改造
- 旧平台组织资产迁移
- 复杂人事域能力

## 5. 设计约束

- 模块的首要目标是验证“标准业务模块接入闭环”，不是扩张平台基础模块职责。
- `Identity Lite` 继续负责“当前是谁”和账号认证主体。
- `Authorization` 继续负责“当前用户能做什么”和授权角色主数据。
- 组织结构模块只拥有 `公司 / 部门 / 岗位 / 人员` 及 `人员 - 用户` 关联关系，不拥有 `用户` 与 `授权角色` 主数据。
- 页面体验可以收口在“人员中心”工作台，但后端主数据边界必须保持清晰。
- 首版规则只做到最小必要集：树层级、启停、排序、唯一编码、关联校验；不提前引入复杂组织治理规则。
- 当前设计必须兼容现有平台的持久化底座和模块模板演进方式。

### 5.1 AI-first 最小检查补充

- 入口在哪里：组织结构模块的后端入口规划为 `server/modules/Organization/`，对外接口入口为 `/api/organization/*`；前端入口规划为 `frontend/modules/organization/`，由 Shell 统一注册菜单和路由。
- 边界在哪里：模块只承载组织主数据和 `人员 - 用户` 关联，不承担认证账号主数据、授权角色主数据或复杂 HR 流程。
- 如何验证：通过后端模块测试、前端构建、模块菜单路由权限联通、组织树查询、人员详情、账号绑定和授权摘要展示完成验证。
- 失败信号是什么：如果实现过程中必须把用户主数据或授权角色主数据搬进组织模块，或必须引入复杂任职/流程模型才能跑通，则说明当前设计边界失效。
- 如何回滚：若后续发现组织结构模块范围过大，应只回退超出 `公司 / 部门 / 岗位 / 人员 / 人员账号关联` 的部分，不回退已稳定的标准接入链路。

## 6. 总体方案

采用“人员中心 + 组织主数据内聚 + 用户与授权能力受控引用”的方案：

- 组织结构模块拥有 `Company`、`Department`、`Position`、`Person` 和 `PersonUserLink` 五类核心模型。
- 前端以“人员中心”为主入口，同时提供组织树页、人员列表页、人员详情页和岗位管理页。
- `Identity Lite` 继续拥有用户账号主数据；组织结构模块只维护 `PersonUserLink`，并调用受控接口查询可绑定用户或已绑定用户摘要。
- `Authorization` 继续拥有授权角色、权限和用户角色；组织结构模块只在人员详情中展示账号的授权摘要，并提供跳转到授权管理的入口。
- 模块通过 `IPermissionProvider`、`IMenuProvider` 和前端模块注册机制接入平台，不通过修改宿主内部逻辑做特判。

选择这个方案的原因：

- 它能最大程度验证标准业务模块的真实接入能力，而不打乱现有平台基础边界。
- 组织结构自身拥有清晰的业务语义，适合作为首个真实业务模块。
- 前端可在“人员中心”工作台提供较好的操作体验，同时保持后端边界清晰。

本轮不选这些方案：

- 不选“组织模块直接拥有用户和授权角色主数据”：会直接改写 `Identity Lite` 和 `Authorization` 的职责。
- 不选“组织模块缓存一份用户影子副本”作为首版：会过早引入同步一致性问题。
- 不选“账号中心”作为首版入口：会削弱组织结构作为真实业务模块的验证价值。

## 7. 关键模型

### 7.1 组织主数据模型

- `Company`
  - `Id`
  - `Code`
  - `Name`
  - `ShortName`
  - `IsEnabled`
  - `SortOrder`

- `Department`
  - `Id`
  - `CompanyId`
  - `ParentDepartmentId`
  - `Code`
  - `Name`
  - `IsEnabled`
  - `SortOrder`

- `Position`
  - `Id`
  - `CompanyId`
  - `DepartmentId`
  - `Code`
  - `Name`
  - `IsEnabled`
  - `SortOrder`

- `Person`
  - `Id`
  - `PersonNo`
  - `Name`
  - `CompanyId`
  - `DepartmentId`
  - `PositionId`
  - `EmploymentStatus`
  - `IsEnabled`

- `PersonUserLink`
  - `PersonId`
  - `UserId`
  - `LinkedAt`
  - `LinkedBy`

### 7.2 关键关系

- `Company 1 -> N Department`
- `Department 1 -> N Department`
- `Department 1 -> N Position`
- `Department 1 -> N Person`
- `Position 1 -> N Person`
- `Person 0..1 <-> 0..1 User`

### 7.3 当前约束

- 部门必须隶属一个公司。
- 部门树不能成环。
- 同一父节点下部门编码唯一。
- 岗位首版固定隶属一个部门。
- 人员首版隶属一个公司和一个部门，可为空岗位。
- 一个人员最多绑定一个用户，一个用户最多绑定一个人员。
- `Person` 是业务人员档案，不等于 `User` 账号。
- `Position` 是组织岗位，不等于授权角色。

### 7.4 DTO 与前端模型建议

- `CompanyDto / CreateCompanyRequest / UpdateCompanyRequest`
- `DepartmentDto / DepartmentTreeNodeDto / CreateDepartmentRequest / UpdateDepartmentRequest`
- `PositionDto / CreatePositionRequest / UpdatePositionRequest`
- `PersonDto / PersonDetailDto / CreatePersonRequest / UpdatePersonRequest`
- `PersonUserLinkDto / LinkPersonUserRequest`
- 前端页面状态：组织树、人员列表、人员详情、绑定账号弹窗、岗位列表

## 8. 核心流程

### 8.1 主流程：组织主数据维护

1. 管理员进入组织树页。
2. 查看公司与部门树。
3. 在当前节点下执行新增、编辑、启用、停用等操作。
4. 服务层校验层级关系、唯一编码和启停条件。
5. 仓储保存后返回最新组织结构视图。

### 8.2 主流程：人员维护

1. 管理员进入人员列表页并按公司、部门、在职状态筛选。
2. 新增或编辑人员档案。
3. 为人员选择所属公司、部门和岗位。
4. 服务层校验组织归属、岗位有效性和编码唯一性。
5. 保存后可在人员详情页查看完整信息。

### 8.3 主流程：人员绑定账号

1. 管理员在人员详情页看到账号绑定状态。
2. 点击“绑定账号”，打开账号选择对话框。
3. 组织模块调用 `Identity Lite` 提供的查询能力获取可绑定账号列表。
4. 管理员选择已有账号并提交绑定。
5. 服务层校验人员与账号是否都未被其他记录占用。
6. 保存 `PersonUserLink` 并返回最新绑定状态。

### 8.4 主流程：查看授权摘要

1. 人员详情页发现该人员已绑定用户账号。
2. 组织模块调用 `Authorization` 的只读能力，按 `UserId` 查询授权摘要。
3. 页面展示授权角色摘要或权限摘要。
4. 若需要修改授权，提供跳转到 `Authorization` 模块的入口。

### 8.5 异常流程

- 部门树成环或父子关系非法：返回业务错误。
- 停用部门时仍存在启用中的下级部门、岗位或人员：返回业务错误。
- 绑定账号时人员已绑定其他账号：返回业务错误。
- 绑定账号时目标账号已绑定其他人员：返回业务错误。
- 账号查询接口不可用：绑定流程提示依赖能力暂不可用，但不影响组织主数据维护。
- 授权摘要读取失败：人员页展示“摘要暂不可用”，不阻断人员主流程。

## 9. 页面与交互方案

### 9.1 页面最小集合

- 组织树页
- 人员列表页
- 人员详情/编辑页
- 岗位管理页
- 账号绑定对话框

### 9.2 交互原则

- 以前端“人员中心”为主入口，但保留组织树作为基础结构维护入口。
- 尽量让管理员在人员详情页完成主要操作，而不是在多个模块间来回切换。
- 真正属于其他模块的写操作不在本模块内复制实现，只做摘要展示或受控跳转。

### 9.3 人员详情工作台

建议在人员详情页集中展示：

- 基本信息
- 所属公司、部门、岗位
- 在职与启用状态
- 账号绑定状态
- 授权摘要
- 关联操作入口：绑定账号、解绑账号、跳转授权管理

## 10. 接口与集成点

### 10.1 后端接口建议

- `GET /api/organization/companies`
- `POST /api/organization/companies`
- `PUT /api/organization/companies/{id}`
- `POST /api/organization/companies/{id}/enable`
- `POST /api/organization/companies/{id}/disable`

- `GET /api/organization/departments/tree`
- `POST /api/organization/departments`
- `PUT /api/organization/departments/{id}`
- `POST /api/organization/departments/{id}/enable`
- `POST /api/organization/departments/{id}/disable`

- `GET /api/organization/positions`
- `POST /api/organization/positions`
- `PUT /api/organization/positions/{id}`
- `POST /api/organization/positions/{id}/enable`
- `POST /api/organization/positions/{id}/disable`

- `GET /api/organization/persons`
- `GET /api/organization/persons/{id}`
- `POST /api/organization/persons`
- `PUT /api/organization/persons/{id}`
- `POST /api/organization/persons/{id}/enable`
- `POST /api/organization/persons/{id}/disable`

- `PUT /api/organization/persons/{id}/user-link`
- `DELETE /api/organization/persons/{id}/user-link`

### 10.2 与 Identity Lite 的集成

- `Identity Lite` 提供账号查询或选择能力。
- 组织模块只消费账号摘要与账号标识，不维护账号密码、登录策略和账号生命周期。
- 首版不在组织模块内创建账号。

### 10.3 与 Authorization 的集成

- `Authorization` 提供按 `UserId` 查询授权摘要的只读能力。
- 组织模块不在本模块内分配授权角色。
- 人员详情页提供跳转到授权模块的入口，而不是复制授权页面。

### 10.4 与 Shell 的集成

- 组织结构模块通过前端 SDK 注册菜单、路由和权限声明。
- Shell 只负责聚合和守卫，不为组织结构模块写死特判。

## 11. 权限设计

首版建议定义以下权限点：

- `Organization.Company.View`
- `Organization.Company.Manage`
- `Organization.Department.View`
- `Organization.Department.Manage`
- `Organization.Position.View`
- `Organization.Position.Manage`
- `Organization.Person.View`
- `Organization.Person.Manage`
- `Organization.Person.BindUser`

设计原则：

- 查看与管理分离。
- 账号绑定单独定义权限，避免把敏感关联操作混入普通编辑权限。
- 权限仍由模块自行声明，接入现有 `Authorization` 的同步机制。

## 12. 数据与状态

- 组织结构模块拥有自己的持久化模型和仓储。
- `PersonUserLink` 只保存跨模块关联标识，不保存用户主数据副本。
- 授权摘要属于只读投影，不作为组织模块内的权威数据保存。
- 前端页面状态以模块页面本地状态和受控查询为主，不引入额外全局可变状态。
- 一致性要求：
  - 组织树、人员归属、岗位归属和账号绑定状态必须保持同一套服务层校验口径。
  - 页面展示的账号与授权摘要允许短暂读取失败，但不允许回写其他模块主数据。

## 13. 错误处理与风险

### 13.1 主要风险

- `岗位` 与 `授权角色` 概念混淆。
- `人员` 与 `用户` 被误当成同一对象。
- 组织模块逐步侵入 `Identity Lite` 和 `Authorization` 的主边界。
- 树形数据规则过早复杂化，导致实现目标偏离“首个真实业务模块验证”。
- 前端体验因技术边界拆分而过度碎片化。

### 13.2 风险控制方式

- 文档、接口、页面命名统一使用“岗位”和“授权角色”两个术语。
- 模型分离 `Person` 与 `User`，用 `PersonUserLink` 显式表达绑定关系。
- 首版只允许“查和绑”，不允许在组织模块内创建账号或分配授权角色。
- 树规则只实现层级、排序、启停和唯一编码，不提前引入复杂人事域规则。
- 通过“人员详情工作台”统一主要操作体验。

## 14. 验证方案

### 14.1 后端验证

- 组织主数据 CRUD 测试通过。
- 部门树约束与启停校验测试通过。
- 人员绑定账号校验测试通过。
- 后端模块可被宿主正常装载。

### 14.2 前端验证

- 前端模块能正常注册菜单和路由。
- 权限守卫能按声明生效。
- 人员中心相关页面可正常构建并进入。

### 14.3 模块级 smoke

至少覆盖以下 5 条：

1. 管理员可看到“组织结构”菜单并进入页面。
2. 可新增公司、部门、岗位、人员并正常查询。
3. 可查看部门树和人员列表。
4. 可为一个人员绑定已有用户账号。
5. 绑定后可在人员详情中看到账号信息和授权摘要。

## 15. 实施拆分建议

1. 先落 `Company / Department / Position` 的模型、接口、仓储和组织树页面。
2. 再落 `Person` 的列表、详情和编辑。
3. 再落 `PersonUserLink` 与 `Identity Lite` 的账号绑定集成。
4. 最后补授权摘要读取、模块级 smoke 和文档收口。

## 16. 未决问题

- `Identity Lite` 应提供哪种最小账号查询接口更适合组织模块复用。
- 授权摘要首版展示“角色摘要”还是“权限摘要”更符合管理者认知。
- 岗位是否在首版就要求编码全局唯一，还是只要求部门内唯一。
- 人员是否需要首版支持离职但仍保留账号绑定记录的只读展示。
