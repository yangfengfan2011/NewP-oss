---
status: accepted
---

# Detail View 是标准 CRUD 详情展示结构的唯一事实源

[ADR-0046](0046-view-composition-uses-tree-and-tab-hosts.md) 已确认将同一详情定义用于视图组合中的只读内容；该新增呈现面尚未实施，不改变本文的详情唯一事实源、字段结构及局部降级规则。

标准 CRUD 曾同时从 Model 的 `form.view`、`view.visible`、`model.sections`、`form.edit` 和 Detail View 推断详情结构，导致人员查看 Drawer、完整详情页与 authoring metadata 表达不一致。平台决定由命名且完整的 Detail View 唯一拥有详情字段选择、顺序、分区、静态可见性和 renderer override；Model 只拥有字段结构、数据源以及 `form.create`、`form.edit`，现有创建和编辑行为不随本决策改变。本决策补充 ADR-0029 对 View、Model、Shell 和模块 binding 的职责划分。

## 决策

所有基于 Model 的对象列表必须显式引用一个 Detail View，并在名称列提供受查看权限控制的 `view` 操作，点击名称默认打开只读 Drawer，操作菜单不重复提供“查看”；公司、部门、岗位与人员、账号遵循同一规则。无 Model 的统计列表不被补造对象详情。一个 Model 可以有多个命名 Detail View，但每份 View 都必须完整，不允许 View 间继承、组合、增量覆盖，也不允许缺失时回退到 Model 或表单 schema。Detail View 中列入的字段即静态可见，未列入即不展示；本阶段不新增通用条件可见性表达式。

Detail View 使用 metadata 协议字段名，并只能引用解析后字段目录中的规范名称。该目录包含 Model 显式字段、`EID`、`CreatedBy`、`CreatedAt`、`UpdatedBy`、`UpdatedAt` 五个 Entity 强制固定字段，以及 Model 选择的 `ENAME`、`ECODE` 可选 builtin。DTO alias、前端 mapper alias 和 `CompanyName` 一类展示投影不成为独立字段；展示投影只供其所属引用字段的 renderer 消费。字段未声明 `rendererKey` 时使用由 Model 字段类型决定的平台标准 renderer，显式 `rendererKey` 必须解析到模块注册的稳定 binding。响应数据可用性不参与该结构解析，由 ADR-0033 规定的运行时详情呈现 module 独立判断。

独立完整详情页是可选能力。惰性模块只有显式注册 `standard-crud-detail` capability 才派生完整页路由；只有 `detailViewKey` 时允许查看 Drawer，不生成完整页路由，也不显示“查看完整详情”。既有 eager resource 以显式 `detailRoutePath` 注册完整页，实际路径仍由列表导航统一派生。后端候选读取同样区分 Detail View 与行为引用：未声明完整页 binding 的详情视图只贡献展示定义及权限引用，不要求伪造行为 binding；普通读取与可信快照使用同一分类。

查看 Drawer 与已启用的完整详情页消费同一份 Resolved Detail Schema，并展示完全相同的普通字段、顺序、分区和 renderer。容器可以调整宽度、响应式列数、标题、关闭或返回交互；完整详情页还可以呈现同一 Detail View 显式声明的 `summaryBlocks` 和 `pageActions`，但这些扩展不能重新定义普通字段结构。View 可见性只控制呈现，不改变详情 API 数据契约，也不构成授权；服务端 endpoint 和业务权限判定仍是安全事实源。

所有 Entity Detail View 必须以独立的 `audit` 分区结束，且该分区只能按 `CreatedBy`、`CreatedAt`、`UpdatedBy`、`UpdatedAt` 的顺序声明四个审计字段。业务字段和完整页摘要位于审计信息之前。缺失、混入业务字段、顺序错误或不在末尾都由共享 schema resolver 拒绝；Shell 不自动补字段或推断详情。审计人员与时间继续消费 Model display 的 canonical companion。查看 Drawer 不要求写权限；完整页的业务摘要和操作继续遵循各自的权限与动作契约。

`form.view`、`view.visible`、整体 `renderDetail`、`renderDetailHeaderActions` 和未被运行时消费的 `detailActions` 一次性退役，不保留兼容桥。字段级 renderer、summary renderer 和 metadata action handler 是标准 CRUD 允许的扩展点；需要完全自定义结构的业务详情必须使用自定义页面 capability。

外部 standard-data resource 必须在公共 Blueprint 契约中显式声明 `detailViewKey`，并引用插件包内完整的 `.view.json`。Runtime materializer 只校验、复制和关联显式 View，不再覆盖该文件或根据 Model 自动生成 Detail View；未声明 Detail View 的资源不得暴露标准详情 capability。

## 非目标

- 不重构或改变现有 `form.create`、`form.edit`、表单分区和交互。
- 不让 Detail View 决定后端查询投影、DTO alias 或服务端授权。
- 不为动态可见性建立通用表达式引擎。
- 不迁移自定义业务页面到标准 CRUD，也不改变数据库或业务数据。

## 验收与失败信号

缺失或空的 Detail View、字符串 section、重复字段、未知字段、未知 renderer binding，详情 capability 缺失 View 引用，以及不符合固定末尾规则的审计分区，都必须在候选编译或前端注册阶段拒绝整份候选。标准详情至少包含一个对象 section 和一个字段；纯 summary 页面应使用自定义页面 capability。普通响应字段缺失不再属于候选失败，而按 ADR-0033 在响应到达后局部降级；关键标识失败与动作依赖不可用也由同一运行时 module 统一裁决和诊断。

迁移验收必须证明创建和编辑行为保持不变，人员 Drawer 与完整详情页解析出相同的普通字段分区与末尾审计分区，并且四个 Entity 审计字段可由详情 API 和运行时 mapper 完整提供。完整详情页还须验证账号绑定、授权摘要以及 edit/disable 的权限、输入、成功和失败。真实浏览器 Gate 必须覆盖从 Drawer 进入完整详情、详情 URL 直达、刷新、前进后退和权限拒绝；standard-data 契约测试必须证明显式 View 可以装载，而缺失引用不会产生 Model fallback。

## 回滚

回滚必须把 metadata、Blueprint contracts、resolver、runtime materializer、详情消费者和相关验证作为同一边界恢复；由于本决策不改变数据库和业务数据，不需要数据回滚。不能只恢复旧属性或单个消费者而重新形成两套详情事实源。
