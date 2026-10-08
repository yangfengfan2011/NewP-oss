# 通用 Link 查询接入

仅显式声明 `model.linkQuery` 的 Link 采用此能力；旧 Link、Entity 查询及标准关系树保持原协议。第一片真实采用者是 WorkspaceFolderContent 的文件夹直接内容。异构端点、反向界面及跨空间引用按父规格后续切片交付，不能从接口能表达双向推断这些消费者已完成。

## Authoring 与准备

`linkedTypes` 是允许父子模型组合的唯一来源，采用模块限定模型键。`linkQuery` 声明查询资源键、读取 behavior、可选源策略、两端引用字段与类型绑定、端点资源映射及公共列：

- 每端在 `fixedModelKey` 与 `typeField` 中选择一种；判别字段必须是必填普通文本。引用字段必须是非空 ObjectId，父子引用字段不能相同。
- `endpoints` 只覆盖 linkedTypes 的规范模型键，绑定真实 Entity 资源、类型标题词条及图标；可选 `constraintKey` 明确要求端点强制读取条件。
- `columns` 声明 field、valueType、nullable、sortable、operators 与按模型键的 mappings。映射只能是端点真实字段、`$modelKey` 或明确允许的 null；不取所有字段并集。
- 规范身份字段使用平台 `ordinalKey` 存储机制保留完整值。Link 的固定字段、审计物理语义和未采用资源不被改写。

真实模型进入 CompiledPersistencePlan。Host 在启动映射端点时整体编译 LinkQueryCatalog，校验模型 kind、模块依赖、资源身份、允许组合、字段类型/可空性及显示契约；必需策略/条件缺失或重复时拒绝启动。该步骤位于 activationSession.CompleteAsync 之前，不会把损坏候选迁移或激活后再等首个请求发现。

## 读取边界

公开 GET 路径为 `/api/links/{已登记资源键}`，输入为 `direction`（forward/reverse）、`anchorModelKey`、`anchorId`、可选 `types`，以及标准 fields/pageIndex/pageSize/sortBy/sortDirection/queryScope。源拥有者通过 ILinkQuerySourcePolicy 强制校验 queryScope；Workspace 正向源要求真实空间、根与有序 LID 链。通用内核不解释这些业务含义。

响应沿用 items、total、page、pageSize、sortBy、sortDirection。每行保留 resourceKey、linkId、parent、child、associatedObject 与公共 fields。反向只改变关联端角色，不交换 parent/child 的存储含义。当前 Workspace 策略仅开放正向位置内消费者，反向采用由后续片接入。

每个端点先交集正式 IDataScopeReadPlanProvider 与已声明 ILinkQueryEndpointConstraint；声明条件执行失败或不可下推时失败关闭。源若登记为真实数据范围资源则应用其读取计划；已有源 CRUD QueryCustomizer 仍在数据库中执行。未登记的 Link 不虚构授权资源。端点独立列表的临时用户筛选不继承到 Link 查询。

条件分组与参数翻译复用 EfPlatformQueryExecutor。各类型形成受控 SQL 分支，合并后统一计数、稳定排序和分页；缺失目标在此前排除，未知存储类型失败关闭。只物化本页，canonical raw/display 成对，非请求安全依赖不输出。用户与时间显示沿用真实 Model planner 和批量 resolver；类型标题来自活动词典。

受信任树通过 ReadDirectLayerAsync 读取一层完整同类型关系及 hasChildren，使用相同源/端点条件，不提供 HTTP 无限列表开关，不预载整树。层规模、实际数据库往返、物化量及 SQL IO/时间由真实数据验证；此契约不声明行数或深度 SLA。

## 标准列表与回退

List View 的 querySource 绑定上述 Link 模型/资源；公共列使用 projection，字段及排序继续按同一正式目录校验。共享列表请求 LID，Adapter 的 getRowId 提供关系身份，getDetailId 可提供当前目标资源真实 EID；目标详情重新执行自身准入。创建和写后回执继续沿共享 Mutation Execution，不以刷新失败重放写入。

撤回采用时关闭入口并撤销新增查询登记，保留实体、Link 和 migration history；不自动 Down、清表或让旧 Host 越过 migration frontier。新增目录不授权通用写入、实体删除、重命名或任意图查询。
