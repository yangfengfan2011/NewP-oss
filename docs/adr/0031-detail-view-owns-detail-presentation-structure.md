---
status: accepted
---

# Detail View 是标准 CRUD 详情展示结构的唯一事实源

标准 CRUD 曾同时从 Model 的 `form.view`、`view.visible`、`model.sections`、`form.edit` 和 Detail View 推断详情结构，导致人员查看 Drawer、完整详情页与 authoring metadata 表达不一致。平台决定由命名且完整的 Detail View 唯一拥有详情字段选择、顺序、分区、静态可见性和 renderer override；Model 只拥有字段结构、数据源以及 `form.create`、`form.edit`，现有创建和编辑行为不随本决策改变。本决策补充 ADR-0029 对 View、Model、Shell 和模块 binding 的职责划分。

## 决策

任何标准 CRUD 详情 capability 都必须显式引用一个 Detail View。一个 Model 可以有多个命名 Detail View，但每份 View 都必须完整，不允许 View 间继承、组合、增量覆盖，也不允许缺失时回退到 Model 或表单 schema。Detail View 中列入的字段即静态可见，未列入即不展示；本阶段不新增通用条件可见性表达式。

Detail View 使用 metadata 协议字段名，并只能引用详情响应实际提供的解析后字段目录。该目录包含 Model 显式字段、`EID`、`CreatedBy`、`CreatedAt`、`UpdatedBy`、`UpdatedAt` 五个 Entity 强制固定字段，以及 Model 选择的 `ENAME`、`ECODE` 可选 builtin。DTO alias、前端 mapper alias 和 `CompanyName` 一类展示投影不成为独立字段；展示投影只供其所属引用字段的 renderer 消费。字段未声明 `rendererKey` 时使用由 Model 字段类型决定的平台标准 renderer，显式 `rendererKey` 必须解析到模块注册的稳定 binding。

查看 Drawer 与完整详情页消费同一份 Resolved Detail Schema，并展示完全相同的普通字段、顺序、分区和 renderer。容器可以调整宽度、响应式列数、标题、关闭或返回交互；完整详情页还可以呈现同一 Detail View 显式声明的 `summaryBlocks` 和 `pageActions`，但这些扩展不能重新定义普通字段结构。View 可见性只控制呈现，不改变详情 API 数据契约，也不构成授权；服务端 endpoint 和业务权限判定仍是安全事实源。

人员中心的 `PersonDetail` 采用三个共享分区：`basic` 依次包含 `PersonNo`、`ENAME`；`organization` 依次包含 `CompanyId`、`DepartmentId`、`PositionId`、`EmploymentStatus`、`IsEnabled`；`audit` 依次包含 `CreatedBy`、`CreatedAt`、`UpdatedBy`、`UpdatedAt`。四个审计字段同时显示在 Drawer 与完整详情页，不另建完整页专属审计 summary。`EID`、`Revision` 和 `AccountEID`、`AccountBoundAt`、`AccountBoundBy`、`AccountBindingReason` 不进入普通字段结构；完整详情页继续通过 `userBinding`、`authorizationSummary` 展示业务摘要，并保留 `edit`、`disable` page action，其中停用必须拥有完整的原因输入契约。

`form.view`、`view.visible`、整体 `renderDetail`、`renderDetailHeaderActions` 和未被运行时消费的 `detailActions` 一次性退役，不保留兼容桥。字段级 renderer、summary renderer 和 metadata action handler 是标准 CRUD 允许的扩展点；需要完全自定义结构的业务详情必须使用自定义页面 capability。

外部 standard-data resource 必须在公共 Blueprint 契约中显式声明 `detailViewKey`，并引用插件包内完整的 `.view.json`。Runtime materializer 只校验、复制和关联显式 View，不再覆盖该文件或根据 Model 自动生成 Detail View；未声明 Detail View 的资源不得暴露标准详情 capability。

## 非目标

- 不重构或改变现有 `form.create`、`form.edit`、表单分区和交互。
- 不让 Detail View 决定后端查询投影、DTO alias 或服务端授权。
- 不为动态可见性建立通用表达式引擎。
- 不迁移自定义业务页面到标准 CRUD，也不改变数据库或业务数据。

## 验收与失败信号

缺失或空的 Detail View、字符串 section、重复字段、未知字段、详情响应未提供的字段、未知 renderer binding，以及详情 capability 缺失 View 引用，都必须在候选编译或前端原子注册阶段拒绝整份候选，不能带病激活后再局部报错。标准详情至少包含一个对象 section 和一个字段；纯 summary 页面应使用自定义页面 capability。

迁移验收必须证明创建和编辑行为保持不变，人员 Drawer 与完整详情页解析出相同的三个普通字段分区，并且四个 Entity 审计字段可由详情 API 和运行时 mapper 完整提供。完整详情页还须验证账号绑定、授权摘要以及 edit/disable 的权限、输入、成功、失败和并发冲突。真实浏览器 Gate 必须覆盖从 Drawer 进入完整详情、详情 URL 直达、刷新、前进后退和权限拒绝；standard-data 契约测试必须证明显式 View 可以装载，而缺失引用不会产生 Model fallback。

## 回滚

回滚必须把 metadata、Blueprint contracts、resolver、runtime materializer、详情消费者和相关验证作为同一边界恢复；由于本决策不改变数据库和业务数据，不需要数据回滚。不能只恢复旧属性或单个消费者而重新形成两套详情事实源。
