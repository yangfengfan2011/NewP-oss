# 通用 Link 查询接入

仅显式声明 `model.linkQuery` 的 Link 采用此能力；旧 Link、Entity 查询及标准关系树保持原协议。真实采用者 WorkspaceFolderContent 支持三类混合内容、跨空间已有对象引用，以及从三类对象属性读取直接引用文件夹。

## Authoring 与准备

`linkedTypes` 是允许父子模型组合的唯一来源，采用模块限定模型键。`linkQuery` 声明查询资源键、读取 behavior、可选源策略、两端引用字段与类型绑定、端点资源映射及公共列：

- 每端在 `fixedModelKey` 与 `typeField` 中选择一种；判别字段必须是必填、ordinalKey 的普通 Scalar 文本，不能占用主键、审计或两端引用角色。引用字段必须是非空 ObjectId，父子引用字段不能相同。
- `endpoints` 只覆盖 linkedTypes 的规范模型键，绑定真实 Entity 资源、类型标题词条及图标；可选 `constraintKey` 明确要求端点强制读取条件。
- `columns` 声明 field、valueType、nullable、sortable、operators 与按模型键的 mappings。映射只能是端点真实字段、`$modelKey` 或明确允许的 null；不取所有字段并集。
- 规范身份字段使用平台 `ordinalKey` 存储机制保留完整值。Link 的固定字段、审计物理语义和未采用资源不被改写。

真实模型进入 CompiledPersistencePlan。Host 在启动映射端点时整体编译 LinkQueryCatalog，校验模型 kind、模块依赖、资源身份、允许组合、字段类型/可空性及显示契约；必需策略/条件缺失或重复时拒绝启动。该步骤位于 activationSession.CompleteAsync 之前，不会把损坏候选迁移或激活后再等首个请求发现。

`AddStandardCrudKernel` 登记查询目录与读取服务。仅采用原 Standard CRUD descriptor 和自有 `IStandardCrudService` 的应用，在没有正式 `LinkQuery` 声明时无需新增查询注册；空目录也不会装载查询策略。映射入口优先消费显式目录，目录缺失时核对正式 CompiledPersistencePlan：存在查询声明却缺目录以 `LINK_QUERY_CATALOG_NOT_REGISTERED` 拒绝启动，不静默遗漏新端点。

## 读取边界

公开 GET 路径为 `/api/links/{已登记资源键}`，输入为 `direction`（forward/reverse）、`anchorModelKey`、`anchorId`、可选 `types`，以及标准 fields/pageIndex/pageSize/sortBy/sortDirection/queryScope。源拥有者通过 ILinkQuerySourcePolicy 强制校验 queryScope；Workspace 正向源要求真实空间、根与有序 LID 链。通用内核不解释这些业务含义。

响应沿用 items、total、page、pageSize、sortBy、sortDirection。每行保留 resourceKey、linkId、parent、child、associatedObject 与公共 fields。反向只改变关联端角色，不交换 parent/child 的存储含义；公共列、显示、筛选及主排序来自关联父端。Workspace 反向不接受出现位置 queryScope，逐关系返回经过父文件夹有效范围与读取计划交集的结果，不返回根路径、空间身份或跳转许可。

每个端点先交集正式 IDataScopeReadPlanProvider 与已声明 ILinkQueryEndpointConstraint；声明条件执行失败或不可下推时失败关闭。源若登记为真实数据范围资源则应用其读取计划；已有源 CRUD QueryCustomizer 仍在数据库中执行。未登记的 Link 不虚构授权资源。端点独立列表的临时用户筛选不继承到 Link 查询。

约束继续用 `ReadPredicateAsync` 提供 EF 可翻译条件。确需数据库集合算法的可信模块可额外实现 `ReadSqlScopeAsync`，返回命令内准备 SQL、单列主键集合查询及独立 DbParameter；默认无额外 SQL，旧实现保持原行为。reader 分配唯一 scopeName，模块用它隔离局部表变量与参数名；准备 SQL 不得产生结果集。SQL/物理表列仅来自已编译可信映射，值保持参数化，HTTP 和 authoring 不接受 SQL。可选 ModelKey 限定约束归属，缺失、重复或模型不匹配的约束在激活完成前拒绝；运行期翻译/执行异常继续失败关闭。

所有端点 SQL scope 在单条 Link 读取命令内组合，并在锚点、关联端点、hasChildren、统一计数/排序/分页之前交集。命令内集合随成功、失败或取消销毁，不建立连接级残留、跨主体缓存或持久闭包。Workspace 用同一 scope 实现来源空间或可读根路径范围，且在数据库逐层维护去重 frontier/visited，而不枚举全部根路径。

条件分组与参数翻译复用 EfPlatformQueryExecutor。各类型形成受控 SQL 分支，合并后统一计数、稳定排序和分页；缺失目标在此前排除，未知存储类型失败关闭。内部只记录损坏类型诊断及按目标范围/筛选排除的聚合数量，不记录隐藏对象标识，不在读取时修复关系。只物化本页，canonical raw/display 成对，非请求安全依赖不输出。用户与时间显示沿用真实 Model planner 和批量 resolver；类型标题来自活动词典。

受信任树通过 ReadDirectLayerAsync 读取一层完整同类型关系及 hasChildren，使用相同源/端点条件，不提供 HTTP 无限列表开关，不预载整树。层规模、实际数据库往返、物化量及 SQL IO/时间由真实数据验证；此契约不声明行数或深度 SLA。

## 标准列表与回退

List View 的 querySource 绑定上述 Link 模型/资源；公共列使用 projection，字段及排序继续按同一正式目录校验。共享列表请求 LID，Adapter 的 getRowId 提供关系身份，getDetailId 可提供当前目标资源真实 EID；异构 Adapter 的 getDetailTarget 只返回已登记 detailViewKey 与真实主键，宿主验证完整详情能力并解析唯一正式路由；不接受任意 URL，目标详情重新执行自身准入。listFieldRenderers 仅呈现已声明公共字段，不放开请求白名单或 display 筛选排序。创建和写后回执继续沿共享 Mutation Execution，不以刷新失败重放写入。

撤回采用时关闭入口并撤销新增查询登记，保留实体、Link 和 migration history；不自动 Down、清表或让旧 Host 越过 migration frontier。新增目录不授权通用写入、实体删除、重命名或任意图查询。
